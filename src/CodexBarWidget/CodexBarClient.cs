using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CodexBarWidget;

public sealed class UsageWindow
{
    public string Label = "";
    public double UsedPercent;
    public string? ResetText;
}

public sealed class ProviderUsage
{
    public string Id = "";
    public string Name = "";
    public string? Plan;
    public List<UsageWindow> Windows = new();
    /// <summary>Localized, user-facing hint when this provider failed to fetch.</summary>
    public string? ErrorText;
}

public sealed class UsageSnapshot
{
    public DateTimeOffset FetchedAt;
    public List<ProviderUsage> Providers = new();
    /// <summary>Set when nothing at all could be shown (CLI missing, bad output...).</summary>
    public string? GlobalError;

    public bool IsStale => DateTimeOffset.Now - FetchedAt > TimeSpan.FromMinutes(5);
}

/// <summary>
/// Fetches usage by shelling out to Win-CodexBar's CLI:
///   codexbar-cli usage -f json
/// (no -p: returns the providers enabled in the user's CodexBar settings)
/// The CLI shares its settings/credentials with the CodexBar desktop app
/// (%APPDATA%\CodexBar), so this process never touches cookies or tokens.
///
/// The JSON parser is deliberately tolerant: the CLI docs pin down the error
/// row shape ({provider, error, errorKind}) but not every success field, so we
/// try the known key spellings and degrade to an error row instead of crashing.
/// The exact field names get pinned down from a real capture on the dev
/// machine (scripts/04-capture-usage-sample.ps1) before the MVP ships.
/// </summary>
internal static class CodexBarClient
{
    private static readonly object Gate = new();
    private static UsageSnapshot? _cache;
    private static Task? _inFlight;
    private static DateTimeOffset _lastFetch = DateTimeOffset.MinValue;
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(90);

    /// <summary>
    /// File the scheduled prefetch task refreshes every 3 minutes
    /// (scripts/05-register-prefetch-task.ps1). It is atomically replaced,
    /// so a reader never sees a half-written file.
    /// </summary>
    private static readonly string PrefetchPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexBarWidget", "latest.json");

    public static string? LastRawJson { get; private set; }

    /// <summary>Latest snapshot, or a "loading" placeholder before the first fetch.</summary>
    public static UsageSnapshot GetSnapshot()
    {
        lock (Gate)
        {
            return _cache ?? new UsageSnapshot
            {
                FetchedAt = DateTimeOffset.MinValue,
                GlobalError = "正在获取最新用量…"
            };
        }
    }

    public static void Invalidate()
    {
        lock (Gate) _lastFetch = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Seed/refresh the in-memory cache from the scheduled-prefetch file,
    /// if it is newer than the current cache. Called on Activate/CreateWidget
    /// so opening the board paints prefetch data instantly (≤3 min old)
    /// instead of waiting for a live CLI run; the background refresh in
    /// EnsureFresh still runs afterwards as a backstop.
    ///
    /// A prefetch snapshot never clobbers a good cache with an error: if the
    /// file fails to parse, the old snapshot stays and the live path will
    /// surface the error instead. A missing/stale file just means the board
    /// shows the last-known data, which is the intended "follow CodexBar"
    /// semantic.
    /// </summary>
    public static bool RefreshFromPrefetchFile()
    {
        try
        {
            if (!File.Exists(PrefetchPath)) return false;
            // Local time, not UTC: CardData renders FetchedAt as HH:mm in
            // the snapshot's own offset, so UTC here would display 8 hours
            // early in China.
            var mtime = new DateTimeOffset(File.GetLastWriteTime(PrefetchPath));
            lock (Gate)
            {
                if (_cache != null && mtime <= _lastFetch) return false;
            }
            var json = File.ReadAllText(PrefetchPath);
            if (string.IsNullOrWhiteSpace(json)) return false;
            var snap = Parse(json, mtime);
            if (snap.GlobalError != null)
            {
                Log.Write("Prefetch file ignored (parse error): " + snap.GlobalError);
                return false;
            }
            lock (Gate)
            {
                // Re-check under the lock: a live fetch may have landed meanwhile.
                if (_cache != null && mtime <= _lastFetch) return false;
                _cache = snap;
                _lastFetch = mtime;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Prefetch file load failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>Start a background fetch if the cache is stale; invoke onUpdated when done.</summary>
    public static void EnsureFresh(Action onUpdated)
    {
        lock (Gate)
        {
            if (_inFlight is { IsCompleted: false }) return;
            if (_cache != null && DateTimeOffset.Now - _lastFetch < FreshFor) return;

            _inFlight = Task.Run(() =>
            {
                var snap = Fetch();
                lock (Gate)
                {
                    _cache = snap;
                    _lastFetch = DateTimeOffset.Now;
                }
                try { onUpdated(); }
                catch (Exception ex) { Log.Write("Refresh callback failed: " + ex.Message); }
            });
        }
    }

    /// <summary>Synchronous fetch (self-test path).</summary>
    public static UsageSnapshot FetchFresh()
    {
        var snap = Fetch();
        lock (Gate)
        {
            _cache = snap;
            _lastFetch = DateTimeOffset.Now;
        }
        return snap;
    }

    private static UsageSnapshot ErrorSnapshot(string message) =>
        new() { FetchedAt = DateTimeOffset.Now, GlobalError = message };

    private static UsageSnapshot Fetch()
    {
        var cli = ResolveCliPath();
        if (cli == null)
        {
            Log.Write("codexbar-cli.exe not found.");
            return ErrorSnapshot("找不到 codexbar-cli.exe。请先安装 Win-CodexBar；或把 CLI 的完整路径写进 %APPDATA%\\CodexBarWidget\\cli-path.txt。");
        }

        try
        {
            Log.Write("Running CLI: " + cli);
            var psi = new ProcessStartInfo
            {
                FileName = cli,
                // No -p on purpose: the CLI then fetches exactly the providers
                // the user enabled in CodexBar settings (docs/CLI.md), in their
                // display order. Hardcoding "-p both" here was a bug: it forced
                // Claude + Codex onto users who only enabled e.g. DeepSeek.
                Arguments = "usage -f json --web-timeout 25",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // The CLI writes UTF-8; without this, .NET decodes stdout with
                // the system code page and currency symbols (¥) turn to mush.
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(45_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return ErrorSnapshot("获取用量超时（45 秒），稍后点刷新重试。");
            }

            string json = stdoutTask.Result;
            string stderr = stderrTask.Result;
            LastRawJson = json;
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Log.Write("CLI stderr: " + Truncate(stderr, 500));
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                return ErrorSnapshot("CodexBar CLI 没有返回数据。");
            }
            return Parse(json);
        }
        catch (Exception ex)
        {
            Log.Write("Fetch failed: " + ex);
            return ErrorSnapshot("获取用量失败：" + ex.Message);
        }
    }

    /// <summary>
    /// Locate codexbar-cli.exe. NOTE: the installed desktop app is named
    /// codexbar.exe — never launch that with usage arguments (it opens the
    /// GUI). Only the explicit config file may point at a differently named exe
    /// (e.g. a from-source build where the CLI is codexbar.exe).
    /// </summary>
    private static string? ResolveCliPath()
    {
        // 1) Explicit config file: %APPDATA%\CodexBarWidget\cli-path.txt
        try
        {
            var cfg = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CodexBarWidget", "cli-path.txt");
            if (File.Exists(cfg))
            {
                var p = File.ReadAllText(cfg).Trim();
                if (p.Length > 0 && File.Exists(p)) return p;
            }
        }
        catch { /* fall through */ }

        // 2) Environment override
        var env = Environment.GetEnvironmentVariable("CODEXBAR_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        // 3) PATH (a packaged process may not inherit the user's PATH additions,
        //    which is why the config file above is the reliable route)
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(';'))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "codexbar-cli.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry */ }
        }

        // 4) Common install locations
        string[] guesses =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "CodexBar", "codexbar-cli.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "CodexBar", "codexbar-cli.exe")
        };
        foreach (var g in guesses)
        {
            if (File.Exists(g)) return g;
        }
        return null;
    }

    // ---------- JSON parsing (tolerant) ----------

    private static UsageSnapshot Parse(string json, DateTimeOffset? fetchedAt = null)
    {
        var snap = new UsageSnapshot { FetchedAt = fetchedAt ?? DateTimeOffset.Now };
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            Log.Write("JSON parse failed: " + ex.Message + " | " + Truncate(json, 300));
            snap.GlobalError = "CodexBar CLI 输出不是有效 JSON，原始输出已写入日志。";
            return snap;
        }

        using (doc)
        {
            var rows = FindRows(doc.RootElement);
            if (rows == null)
            {
                Log.Write("Unexpected JSON root: " + Truncate(json, 800));
                snap.GlobalError = "无法识别 CodexBar CLI 的输出结构，原始输出已写入日志。";
                return snap;
            }
            foreach (var row in rows.Value.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Object)
                {
                    snap.Providers.Add(ParseRow(row));
                }
            }
        }

        if (snap.Providers.Count == 0)
        {
            snap.GlobalError = "没有启用的服务商。请在 CodexBar 里启用 Claude / Codex。";
        }
        return snap;
    }

    private static JsonElement? FindRows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "providers", "results", "data", "usage" })
            {
                if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Array)
                {
                    return el;
                }
            }
        }
        return null;
    }

    private static ProviderUsage ParseRow(JsonElement row)
    {
        var p = new ProviderUsage
        {
            Id = GetString(row, "provider") ?? "unknown"
        };
        p.Name = DisplayName(p.Id);

        var error = GetString(row, "error");
        if (error != null)
        {
            var kind = GetString(row, "errorKind") ?? "unknown";
            p.ErrorText = ErrorHint(kind, error);
            return p;
        }

        JsonElement usage;
        if (!TryGetObject(row, "usage", out usage))
        {
            // claude-swap rows carry the account shape instead of a top-level
            // usage object; look one level down before giving up.
            if (TryGetObject(row, "account", out var account) &&
                TryGetObject(account, "usage", out usage))
            {
                // use this usage element
            }
            else
            {
                p.ErrorText = "暂无用量数据";
                return p;
            }
        }

        p.Plan = GetString(usage, "plan") ?? GetString(row, "plan");

        AddWindow(p, usage, "primary", 0);
        AddWindow(p, usage, "secondary", 1);
        if (p.Windows.Count == 0)
        {
            AddWindow(p, usage, "session", 0);
            AddWindow(p, usage, "weekly", 1);
        }
        if (p.Windows.Count == 0)
        {
            p.ErrorText = "暂无用量数据";
        }
        return p;
    }

    private static void AddWindow(ProviderUsage p, JsonElement usage, string key, int index)
    {
        if (!TryGetObject(usage, key, out var win)) return;

        double? used = GetNumber(win, "usedPercent")
                       ?? GetNumber(win, "used_percent")
                       ?? GetNumber(win, "percentUsed")
                       ?? GetNumber(win, "used");
        if (used == null) return;

        var w = new UsageWindow
        {
            UsedPercent = Math.Clamp(used.Value, 0, 100)
        };

        // Real CLI output is snake_case (verified against a captured sample,
        // 2026-10-22); the camelCase spellings are kept as fallbacks.
        var minutes = GetNumber(win, "windowMinutes") ?? GetNumber(win, "window_minutes");
        var desc = GetString(win, "resetDescription") ?? GetString(win, "reset_description");
        var resetsAt = GetString(win, "resetsAt") ?? GetString(win, "resets_at") ?? GetString(win, "resetAt");

        // Balance-style providers (DeepSeek) have no time window at all:
        // no window_minutes, no resets_at. The money sits in
        // reset_description, e.g. "¥29.99 (Paid: ¥29.99 / Granted: ¥0.00)",
        // and usage.login_method reads "CNY balance: ¥29.99". Render the
        // amount as the label and the paid/granted split as the side text.
        if (minutes == null && resetsAt == null && desc != null)
        {
            w.Label = BalanceLabel(desc,
                GetString(usage, "login_method") ?? GetString(usage, "loginMethod"));
            w.ResetText = BalanceDetail(desc);
            p.Windows.Add(w);
            return;
        }

        w.Label = minutes switch
        {
            300 => "5 小时窗口",
            1440 => "今日",
            10080 => "每周窗口",
            43200 => "每月窗口",
            _ => index == 0 ? "会话窗口" : "每周窗口"
        };

        if (!string.IsNullOrWhiteSpace(desc))
        {
            w.ResetText = desc;
        }
        else if (resetsAt != null && DateTimeOffset.TryParse(resetsAt, out var dto))
        {
            w.ResetText = dto.LocalDateTime.ToString("M月d日 HH:mm") + " 重置";
        }

        p.Windows.Add(w);
    }

    private static string BalanceLabel(string desc, string? loginMethod)
    {
        string headline = desc;
        int paren = desc.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0) headline = desc[..paren];
        if (headline.Length == 0 && loginMethod != null)
        {
            int colon = loginMethod.IndexOf(": ", StringComparison.Ordinal);
            headline = colon >= 0 ? loginMethod[(colon + 2)..] : loginMethod;
        }
        return headline.Length > 0 ? "余额 " + headline : "余额";
    }

    private static string? BalanceDetail(string desc)
    {
        var paid = Regex.Match(desc, @"Paid:\s*([^/\)]+)");
        var granted = Regex.Match(desc, @"Granted:\s*([^\)]+)");
        if (paid.Success && granted.Success)
        {
            return $"已充值 {paid.Groups[1].Value.Trim()} · 赠送 {granted.Groups[1].Value.Trim()}";
        }
        int paren = desc.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? desc[(paren + 1)..].Trim(' ', '(', ')') : null;
    }

    // ---------- small helpers ----------

    private static bool TryGetObject(JsonElement parent, string key, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(key, out value)
               && value.ValueKind == JsonValueKind.Object;
    }

    private static string? GetString(JsonElement parent, string key)
    {
        return parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(key, out var el)
               && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
    }

    private static double? GetNumber(JsonElement parent, string key)
    {
        return parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(key, out var el)
               && el.ValueKind == JsonValueKind.Number
               && el.TryGetDouble(out var d)
            ? d
            : null;
    }

    private static string DisplayName(string id) => id.ToLowerInvariant() switch
    {
        "claude" => "Claude",
        "codex" => "Codex",
        "cursor" => "Cursor",
        "copilot" => "Copilot",
        "gemini" => "Gemini",
        "deepseek" => "DeepSeek",
        "kimi" => "Kimi",
        _ => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..]
    };

    /// <summary>errorKind values are documented in Win-CodexBar docs/CLI.md.</summary>
    private static string ErrorHint(string kind, string rawError) => kind switch
    {
        "needsAuthentication" => "需要重新登录（打开 CodexBar 刷新凭据）",
        "expiredSession" => "登录已过期（打开 CodexBar 重新登录）",
        "browserSignInRequired" => "需要在浏览器登录后刷新",
        "localRuntimeOffline" => "本地 CLI 未运行或未安装",
        "timeout" => "获取超时，稍后点刷新重试",
        _ => Truncate(rawError, 60)
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
