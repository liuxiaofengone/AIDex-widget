using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CodexBarWidget;

/// <summary>
/// Builds the data JSON that the Adaptive Card template binds against.
/// Field names here must stay in sync with Templates/usage-template.json.
///
/// Compact card (2026-10-26): the tile clips anything taller than itself and
/// Adaptive Cards has no sticky footer, so a bottom "view all" link only
/// stays visible if the whole card fits. Each provider is therefore a single
/// summary line (name + per-window percentages, or the balance for
/// balance-style providers); per-window bars, reset times and the paid/
/// granted split live in the CodexBar desktop window behind the link.
/// The template renders one TextBlock per provider from "line".
/// </summary>
internal static class CardData
{
    private static bool _desktopLogged;
    private static string? _lastDesktop;

    public static string Build(UsageSnapshot snap)
    {
        var providers = new List<Dictionary<string, object?>>();
        foreach (var p in snap.Providers)
        {
            providers.Add(new Dictionary<string, object?>
            {
                ["line"] = BuildLine(p)
            });
        }

        string updatedAt;
        if (snap.FetchedAt == DateTimeOffset.MinValue)
        {
            updatedAt = "尚未更新";
        }
        else if (snap.IsStale)
        {
            updatedAt = $"更新于 {snap.FetchedAt:HH:mm}（可能已过期）";
        }
        else
        {
            updatedAt = $"更新于 {snap.FetchedAt:HH:mm}";
        }

        // The "查看全部用量" link sits at the bottom of the card (first-party
        // style). Resolve the desktop app here anyway and log it when it
        // changes, so the log shows whether discovery works; a click with no
        // exe just logs.
        var desktop = CodexBarClient.ResolveDesktopPath();
        if (!_desktopLogged || _lastDesktop != desktop)
        {
            _desktopLogged = true;
            _lastDesktop = desktop;
            Log.Write("Desktop app: " + (desktop ?? "not found"));
        }

        var root = new Dictionary<string, object?>
        {
            ["providers"] = providers,
            ["hasGlobalError"] = snap.GlobalError != null,
            ["globalError"] = snap.GlobalError ?? "",
            ["updatedAt"] = updatedAt
        };
        return JsonSerializer.Serialize(root);
    }

    private static string BuildLine(ProviderUsage p)
    {
        string head = string.IsNullOrEmpty(p.Plan) ? p.Name : $"{p.Name} {p.Plan}";
        if (p.ErrorText != null)
        {
            return $"{head} · ⚠ {p.ErrorText}";
        }
        var parts = new List<string>();
        // Same first-two-windows rule the old detailed card used; a third
        // window, if one ever appears, stays in the desktop window.
        for (int i = 0; i < p.Windows.Count && i < 2; i++)
        {
            var w = p.Windows[i];
            if (w.IsBalance)
            {
                parts.Add(string.IsNullOrEmpty(w.ResetText)
                    ? w.Label
                    : $"{w.Label} · {w.ResetText}");
            }
            else
            {
                parts.Add($"{w.Label} {(int)Math.Round(w.UsedPercent)}%");
            }
        }
        return parts.Count == 0 ? head : $"{head} · {string.Join(" · ", parts)}";
    }
}
