using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AIDexWidget;

/// <summary>
/// Reads usage straight from Nowdex's own snapshot files, no CLI, no
/// scheduled prefetch task: Nowdex's tray process keeps them fresh itself.
///   %LOCALAPPDATA%\Packages\RandyLu.Nowdex_*\LocalState\reports.json
///   %LOCALAPPDATA%\Packages\RandyLu.Nowdex_*\LocalState\reset-deadlines.json
///   %LOCALAPPDATA%\Packages\RandyLu.Nowdex_*\LocalState\connections.json
///
/// Privacy boundary: only these three snapshot files are ever read, and
/// only name-like fields are taken from connections.json. The LocalState
/// "credentials" folder is never opened, and nothing is sent anywhere.
/// reports.json is a Nowdex internal file, not a public API, so every
/// field read here is tolerant: missing or reshaped fields degrade to
/// "暂无用量数据" / "未命名服务" instead of crashing the widget.
/// </summary>
internal static class NowdexClient
{
    /// <summary>Raw reports.json text, for --selftest diagnostics.</summary>
    public static string? LastRawJson { get; private set; }

    private static string? _lastNamesLog;
    private static string? _lastStaleLog;

    /// <summary>Newest reports.json across all matching packages, or null.</summary>
    public static string? ResolveReportsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packages = Path.Combine(localAppData, "Packages");
        string? best = null;
        var bestTime = DateTime.MinValue;
        try
        {
            if (!Directory.Exists(packages)) return null;
            foreach (var dir in Directory.EnumerateDirectories(packages, "RandyLu.Nowdex_*"))
            {
                var candidate = Path.Combine(dir, "LocalState", "reports.json");
                if (!File.Exists(candidate)) continue;
                var time = File.GetLastWriteTime(candidate);
                if (best == null || time > bestTime)
                {
                    best = candidate;
                    bestTime = time;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Nowdex discovery failed: " + ex.Message);
        }
        return best;
    }

    public static UsageSnapshot GetSnapshot()
    {
        try
        {
            var path = ResolveReportsPath();
            if (path == null)
            {
                return new UsageSnapshot
                {
                    FetchedAt = DateTimeOffset.MinValue,
                    GlobalError = "未找到 Nowdex 数据文件，请确认 Nowdex 已安装并在运行"
                };
            }
            var json = File.ReadAllText(path);
            LastRawJson = json;
            var snap = Parse(json, path);
            LogNames(snap, path);
            return snap;
        }
        catch (Exception ex)
        {
            Log.Write("Nowdex read failed: " + ex.Message);
            return new UsageSnapshot
            {
                FetchedAt = DateTimeOffset.Now,
                GlobalError = "Nowdex 数据读取失败"
            };
        }
    }

    /// <summary>Open the Nowdex window via its app user model ID.</summary>
    public static void Launch()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var packages = Path.Combine(localAppData, "Packages");
            string? family = null;
            if (Directory.Exists(packages))
            {
                foreach (var dir in Directory.EnumerateDirectories(packages, "RandyLu.Nowdex_*"))
                {
                    if (File.Exists(Path.Combine(dir, "LocalState", "reports.json")))
                    {
                        family = Path.GetFileName(dir);
                        break;
                    }
                }
            }
            if (family == null)
            {
                Log.Write("Launch Nowdex: package not found.");
                return;
            }
            Log.Write("Launching Nowdex: " + family + "!App");
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:AppsFolder\\" + family + "!App",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Write("Launch Nowdex failed: " + ex.Message);
        }
    }

    private static UsageSnapshot Parse(string json, string reportsPath)
    {
        var localState = Path.GetDirectoryName(reportsPath)!;
        var names = ResolveNames(localState);
        var snap = new UsageSnapshot();
        DateTimeOffset latest = File.GetLastWriteTime(reportsPath);
        using var doc = JsonDocument.Parse(json);
        // EnumerateObject preserves document order: the card shows providers
        // in the same order Nowdex itself lists them.
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var guid = prop.Name;
            var el = prop.Value;
            var p = new ProviderUsage { Id = guid };
            var key = NormGuid(guid);
            p.Name = key != null && names.TryGetValue(key, out var n) ? n : "未命名服务";
            p.Plan = GetString(el, "Plan");

            var windows = new List<(UsageWindow w, string id)>();
            if (TryGet(el, "Windows", out var wins) && wins.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in wins.EnumerateArray())
                {
                    var used = GetDouble(w, "UsedPercent") ?? 0;
                    windows.Add((new UsageWindow
                    {
                        Label = GetString(w, "Title") ?? "",
                        UsedPercent = used,
                        RemainingPercent = GetDouble(w, "RemainingPercent") ?? (100 - used),
                        ResetText = FormatReset(GetString(w, "ResetsAt"))
                    }, GetString(w, "Id") ?? ""));
                }
            }
            // Honor Nowdex's own window order when it gives one (Codex:
            // session first, weekly second); otherwise keep file order.
            if (TryGet(el, "DisplayOrder", out var order) && order.ValueKind == JsonValueKind.Array)
            {
                var orderIds = new List<string>();
                foreach (var o in order.EnumerateArray())
                {
                    if (o.ValueKind == JsonValueKind.String) orderIds.Add(o.GetString()!);
                }
                var ordered = new List<(UsageWindow w, string id)>();
                foreach (var id in orderIds)
                {
                    foreach (var item in windows)
                    {
                        if (item.id == id) ordered.Add(item);
                    }
                }
                foreach (var item in windows)
                {
                    if (!orderIds.Contains(item.id)) ordered.Add(item);
                }
                windows = ordered;
            }
            foreach (var (w, _) in windows) p.Windows.Add(w);

            if (TryGet(el, "Balance", out var bal) && bal.ValueKind == JsonValueKind.Object)
            {
                var amount = GetString(bal, "Amount") ?? "";
                p.Windows.Add(new UsageWindow
                {
                    Label = "余额 " + amount,
                    UsedPercent = 0,
                    IsBalance = true,
                    ResetText = BalanceDetail(el)
                });
            }
            if (p.Windows.Count == 0) p.ErrorText = "暂无用量数据";

            if (TryGet(el, "UpdatedAt", out var ua) && ua.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(ua.GetString(), out var dto) && dto > latest)
            {
                latest = dto;
            }
            snap.Providers.Add(p);
        }
        // Normalize to machine-local: UpdatedAt carries +00:00, so without
        // this the footer flips between UTC and local depending on which
        // timestamp wins the max above (saw "07:04" for a 15:04 snapshot).
        snap.FetchedAt = latest.ToLocalTime();
        snap.StaleAfter = StaleAfter(localState);
        var staleLine = $"stale after {snap.StaleAfter.TotalMinutes:0} min";
        if (_lastStaleLog != staleLine)
        {
            _lastStaleLog = staleLine;
            Log.Write("Nowdex " + staleLine);
        }
        return snap;
    }

    /// <summary>Stale threshold from Nowdex's own refresh interval:
    /// at least 5 minutes and at least twice the interval, so a stuck
    /// tray is flagged promptly on fast intervals without false alarms
    /// on the 15-minute one. Unknown or missing setting keeps 30 minutes.</summary>
    private static TimeSpan StaleAfter(string localState)
    {
        try
        {
            var path = Path.Combine(localState, "settings.json");
            if (!File.Exists(path)) return TimeSpan.FromMinutes(30);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!TryGet(doc.RootElement, "RefreshInterval", out var v)) return TimeSpan.FromMinutes(30);
            double? minutes = null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) minutes = d;
            else if (v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString() ?? "";
                if (s.Contains("One", StringComparison.OrdinalIgnoreCase)) minutes = 1;
                else if (s.Contains("Five", StringComparison.OrdinalIgnoreCase)) minutes = 5;
                else if (s.Contains("Fifteen", StringComparison.OrdinalIgnoreCase)) minutes = 15;
                else if (double.TryParse(s, out var n)) minutes = n;
            }
            if (minutes == null || minutes <= 0) return TimeSpan.FromMinutes(30);
            return TimeSpan.FromMinutes(Math.Max(5, minutes.Value * 2));
        }
        catch (Exception ex)
        {
            Log.Write("Nowdex settings parse failed: " + ex.Message);
            return TimeSpan.FromMinutes(30);
        }
    }

    /// <summary>Reset time in tray language: relative when the reset is
    /// within 24 hours ("5 小时 0 分钟后重置", "45 分钟后重置"), absolute
    /// local date + time otherwise ("11月2日 14:21 重置").</summary>
    private static string? FormatReset(string? iso)
    {
        if (iso == null || !DateTimeOffset.TryParse(iso, out var reset)) return null;
        var local = reset.ToLocalTime();
        var diff = reset - DateTimeOffset.Now;
        if (diff <= TimeSpan.Zero) return local.ToString("M月d日 HH:mm") + " 重置";
        if (diff < TimeSpan.FromHours(1))
        {
            var m = (int)Math.Floor(diff.TotalMinutes);
            return m < 1 ? "即将重置" : m + " 分钟后重置";
        }
        if (diff < TimeSpan.FromDays(1))
        {
            return (int)diff.TotalHours + " 小时 " + diff.Minutes + " 分钟后重置";
        }
        return local.ToString("M月d日 HH:mm") + " 重置";
    }

    /// <summary>Paid/granted split for balance providers, from Details ids.</summary>
    private static string? BalanceDetail(JsonElement el)
    {
        string? topped = null, granted = null;
        if (TryGet(el, "Details", out var det) && det.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in det.EnumerateArray())
            {
                var id = GetString(d, "Id");
                if (id == "topped-up") topped = GetString(d, "Value");
                else if (id == "granted") granted = GetString(d, "Value");
            }
        }
        var parts = new List<string>();
        if (topped != null) parts.Add("已充值 " + topped);
        if (granted != null) parts.Add("赠送 " + granted);
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    /// <summary>
    /// GUID -&gt; display name. Sources, in order:
    /// 1. reset-deadlines.json keys ("Name.GUID.window") - covers every
    ///    provider that has a reset deadline.
    /// 2. connections.json, parsed tolerantly (dict keyed by GUID, list of
    ///    connection objects, or nested "connections") - the only source
    ///    for providers without deadlines, e.g. balance-only ones.
    /// Only name-like fields are read from connections.json; account
    /// identifiers, emails and anything else are ignored.
    /// Keys are normalized (dashes stripped, lowercased) because the two
    /// files spell GUIDs differently.
    /// </summary>
    private static Dictionary<string, string> ResolveNames(string localState)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var dlPath = Path.Combine(localState, "reset-deadlines.json");
            if (File.Exists(dlPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(dlPath));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var key = prop.Name;
                    var dot1 = key.IndexOf('.');
                    if (dot1 <= 0) continue;
                    var rest = key[(dot1 + 1)..];
                    var dot2 = rest.IndexOf('.');
                    if (dot2 <= 0) continue;
                    var guid = NormGuid(rest[..dot2]);
                    if (guid != null && !map.ContainsKey(guid)) map[guid] = key[..dot1];
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Nowdex deadlines parse failed: " + ex.Message);
        }
        try
        {
            var connPath = Path.Combine(localState, "connections.json");
            if (File.Exists(connPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(connPath));
                CollectNames(doc.RootElement, map);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Nowdex connections parse failed: " + ex.Message);
        }
        return map;
    }

    private static void CollectNames(JsonElement el, Dictionary<string, string> map)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                var guid = NormGuid(prop.Name);
                if (guid != null)
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        var n = PickName(prop.Value);
                        if (n != null && !map.ContainsKey(guid)) map[guid] = n;
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String
                        && !map.ContainsKey(guid))
                    {
                        map[guid] = prop.Value.GetString()!;
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.Object
                    || prop.Value.ValueKind == JsonValueKind.Array)
                {
                    CollectNames(prop.Value, map);
                }
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string? id = null;
                foreach (var idKey in new[] { "Id", "Guid", "ConnectionId", "SubscriptionId" })
                {
                    if (TryGet(item, idKey, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        id = NormGuid(v.GetString()!);
                        break;
                    }
                }
                if (id != null)
                {
                    var n = PickName(item);
                    if (n != null && !map.ContainsKey(id)) map[id] = n;
                }
            }
        }
    }

    private static string? PickName(JsonElement obj)
    {
        foreach (var key in new[] { "Provider", "ProviderName", "Service", "ServiceName", "Type", "Name", "DisplayName" })
        {
            if (TryGet(obj, key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    private static void LogNames(UsageSnapshot snap, string reportsPath)
    {
        var parts = new List<string>();
        var unresolved = new List<string>();
        foreach (var p in snap.Providers)
        {
            parts.Add(p.Name);
            if (p.Name == "未命名服务") unresolved.Add(p.Id);
        }
        var line = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(reportsPath))!)
            + " — " + string.Join(", ", parts);
        if (_lastNamesLog != line)
        {
            _lastNamesLog = line;
            Log.Write("Nowdex providers: " + line);
            if (unresolved.Count > 0)
            {
                Log.Write("Nowdex names unresolved for: " + string.Join(", ", unresolved));
            }
        }
    }

    /// <summary>GUID in either spelling -&gt; dashless lowercase, or null.</summary>
    private static string? NormGuid(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (Guid.TryParse(s, out var g)) return g.ToString("N");
        var t = s.Replace("-", "").ToLowerInvariant();
        if (t.Length != 32) return null;
        foreach (var c in t)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return null;
        }
        return t;
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement obj, string name)
    {
        return TryGet(obj, name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
    }

    private static double? GetDouble(JsonElement obj, string name)
    {
        return TryGet(obj, name, out var v) && v.TryGetDouble(out var d) ? d : null;
    }
}
