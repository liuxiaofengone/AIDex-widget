using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CodexBarWidget;

/// <summary>
/// Builds the data JSON that the Adaptive Card template binds against.
/// Field names here must stay in sync with Templates/usage-template.json.
///
/// Row card (2026-10-27): each provider renders as one stock-style row
/// block (left: name + secondary window remaining, right: primary
/// remaining + reset time, remaining tinted by threshold: <=10% red,
/// <=30% yellow, else blue), because the tile clips
/// anything taller than itself and Adaptive Cards has no sticky footer.
/// The bottom "view all" link only stays visible if the whole card fits,
/// so the row count follows the tile size: small 1, medium 3, large 5
/// (capped by the provider count; extra providers stay behind the link).
/// Per-window bars live in the Nowdex window; only the primary window's
/// reset time is on the card.
/// The template renders one row Container per provider from these fields.
/// </summary>
internal static class CardData
{

    /// <summary>Self-test path: medium tile (3 rows).</summary>
    public static string Build(UsageSnapshot snap) => Build(snap, 3);

    public static string Build(UsageSnapshot snap, int maxRows)
    {
        var providers = new List<Dictionary<string, object?>>();
        for (int i = 0; i < snap.Providers.Count && i < maxRows; i++)
        {
            providers.Add(BuildRow(snap.Providers[i]));
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
        // style) and the whole card is clickable via the root selectAction.
        // The Nowdex package discovery is logged by NowdexClient itself.
        var root = new Dictionary<string, object?>
        {
            ["providers"] = providers,
            ["hasGlobalError"] = snap.GlobalError != null,
            ["globalError"] = snap.GlobalError ?? "",
            ["updatedAt"] = updatedAt
        };
        return JsonSerializer.Serialize(root);
    }

    private static Dictionary<string, object?> BuildRow(ProviderUsage p)
    {
        string title = string.IsNullOrEmpty(p.Plan) ? p.Name : $"{p.Name} {p.Plan}";
        if (p.ErrorText != null)
        {
            return new Dictionary<string, object?>
            {
                ["title"] = title,
                ["subtitle"] = $"⚠ {p.ErrorText}",
                ["subtitleColor"] = "attention",
                ["subtitleSubtle"] = false,
                ["value"] = "",
                ["valueColor"] = "default",
                ["sub"] = ""
            };
        }

        if (p.Windows.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["title"] = title,
                ["subtitle"] = "",
                ["subtitleColor"] = "default",
                ["subtitleSubtle"] = true,
                ["value"] = "",
                ["valueColor"] = "default",
                ["sub"] = ""
            };
        }

        var primary = p.Windows[0];
        if (primary.IsBalance)
        {
            // Label is "余额 ¥29.99" (or bare "余额"); the amount goes on the
            // right like a stock price, the paid/granted split underneath
            // the name like a stock's subtitle.
            string amount = primary.Label.StartsWith("余额 ", StringComparison.Ordinal)
                ? primary.Label["余额 ".Length..]
                : primary.Label;
            if (amount.Length == 0) amount = primary.Label;
            // Bare "余额" (no amount parsed): don't print the word twice.
            string balanceSub = amount == "余额" ? "" : "余额";
            return new Dictionary<string, object?>
            {
                ["title"] = title,
                ["subtitle"] = primary.ResetText ?? "",
                ["subtitleColor"] = "default",
                ["subtitleSubtle"] = true,
                ["value"] = amount,
                ["valueColor"] = "default",
                ["sub"] = balanceSub
            };
        }

        // Remaining language, like the Nowdex tray: the big number is what
        // is left, tinted by the same thresholds inverted (<=10% red,
        // <=30% yellow, else blue). The primary window's label gives way
        // to its reset time (the tray's most actionable line); the
        // secondary window keeps its label + remaining.
        int remaining = (int)Math.Round(primary.RemainingPercent);
        string subtitle = "";
        if (p.Windows.Count > 1)
        {
            var second = p.Windows[1];
            subtitle = second.IsBalance
                ? second.Label
                : $"{second.Label} 剩余 {(int)Math.Round(second.RemainingPercent)}%";
        }
        return new Dictionary<string, object?>
        {
            ["title"] = title,
            ["subtitle"] = subtitle,
            ["subtitleColor"] = "default",
            ["subtitleSubtle"] = true,
            ["value"] = "剩余 " + remaining + "%",
            ["valueColor"] = remaining <= 10 ? "attention" : remaining <= 30 ? "warning" : "accent",
            ["sub"] = primary.ResetText ?? ""
        };
    }
}
