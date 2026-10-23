using System;
using System.IO;

namespace CodexBarWidget;

/// <summary>
/// Loads the Adaptive Card template from Templates\usage-template.json next to
/// the exe (it is copied into the package at build time), so card layout can
/// be tweaked without recompiling. Falls back to a minimal error card.
/// </summary>
internal static class CardTemplates
{
    private static string? _usageTemplate;

    public static string UsageTemplate => _usageTemplate ??= Load();

    private static string Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Templates", "usage-template.json");
        try
        {
            var json = File.ReadAllText(path);
            Log.Write($"Template loaded from {path} ({json.Length} chars).");
            return json;
        }
        catch (Exception ex)
        {
            Log.Write("Template load failed: " + ex.Message);
            return Fallback;
        }
    }

    private const string Fallback = """
        {
          "type": "AdaptiveCard",
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "version": "1.5",
          "body": [
            { "type": "TextBlock", "text": "AI 用量模板加载失败，请检查安装包。", "wrap": true }
          ]
        }
        """;
}
