using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CodexBarWidget;

/// <summary>
/// Builds the data JSON that the Adaptive Card template binds against.
/// Field names here must stay in sync with Templates/usage-template.json.
///
/// Per provider we flatten the first two usage windows into fixed slots
/// (prefix "s" = primary/session, "w" = secondary/weekly) instead of nesting
/// a second $data loop — simpler and safer with the widget host's templating.
/// </summary>
internal static class CardData
{
    public static string Build(UsageSnapshot snap)
    {
        var providers = new List<Dictionary<string, object?>>();
        foreach (var p in snap.Providers)
        {
            var d = new Dictionary<string, object?>
            {
                ["name"] = p.Name,
                ["hasPlan"] = !string.IsNullOrEmpty(p.Plan),
                ["plan"] = p.Plan ?? "",
                ["hasError"] = p.ErrorText != null,
                ["errorText"] = p.ErrorText ?? ""
            };
            FillWindow(d, "s", p.Windows.Count > 0 ? p.Windows[0] : null);
            FillWindow(d, "w", p.Windows.Count > 1 ? p.Windows[1] : null);
            providers.Add(d);
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

        var root = new Dictionary<string, object?>
        {
            ["providers"] = providers,
            ["hasGlobalError"] = snap.GlobalError != null,
            ["globalError"] = snap.GlobalError ?? "",
            ["updatedAt"] = updatedAt
        };
        return JsonSerializer.Serialize(root);
    }

    private static void FillWindow(Dictionary<string, object?> d, string prefix, UsageWindow? win)
    {
        string cap = prefix.ToUpperInvariant(); // "S" / "W"
        d["has" + cap] = win != null;
        if (win == null)
        {
            d[prefix + "Label"] = "";
            d[prefix + "UsedText"] = "";
            d[prefix + "BarUsed"] = 1;
            d[prefix + "BarLeft"] = 99;
            d[prefix + "BarStyle"] = "accent";
            d["has" + cap + "Reset"] = false;
            d[prefix + "Reset"] = "";
            return;
        }

        int used = (int)Math.Round(win.UsedPercent);
        // Column widths are weights; keep both segments visible at extremes.
        int barUsed = used <= 0 ? 1 : used >= 100 ? 99 : used;

        d[prefix + "Label"] = win.Label;
        d[prefix + "UsedText"] = used + "%";
        d[prefix + "BarUsed"] = barUsed;
        d[prefix + "BarLeft"] = 100 - barUsed;
        d[prefix + "BarStyle"] = used >= 90 ? "attention" : used >= 70 ? "warning" : "accent";
        d["has" + cap + "Reset"] = !string.IsNullOrEmpty(win.ResetText);
        d[prefix + "Reset"] = win.ResetText ?? "";
    }
}
