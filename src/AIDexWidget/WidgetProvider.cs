using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace AIDexWidget;

/// <summary>Bookkeeping for one pinned widget instance.</summary>
public sealed class CompactWidgetInfo
{
    public string WidgetId { get; set; } = "";
    public string WidgetName { get; set; } = "";
    public bool IsActive { get; set; }
    // Tile size drives how many provider rows the card shows (small 1,
    // medium 3, large 5); the board reports it on every activation and on
    // size changes, so this is kept current, not read once at pin time.
    public WidgetSize Size { get; set; } = WidgetSize.Medium;
}

/// <summary>
/// The widget provider. The widget host activates this COM class, tells us
/// about pinned widgets, and we push Adaptive Card template + data back
/// through WidgetManager.UpdateWidget.
///
/// Data flow: callbacks never block on the network. They render whatever
/// snapshot is cached (possibly "loading" on first paint) and kick off a
/// background refresh; when the fetch lands, every pinned widget is updated
/// again. This matters because `codexbar-cli usage` can take several seconds.
/// </summary>
internal sealed class WidgetProvider : IWidgetProvider
{
    public static Dictionary<string, CompactWidgetInfo> RunningWidgets = new();
    private static readonly object Gate = new();

    private static readonly ManualResetEvent EmptyWidgetListEvent = new(false);

    public static ManualResetEvent GetEmptyWidgetListEvent() => EmptyWidgetListEvent;

    public WidgetProvider()
    {
        // Recover widgets that were pinned before a restart / provider crash.
        try
        {
            var runningWidgets = WidgetManager.GetDefault().GetWidgetInfos();
            lock (Gate)
            {
                foreach (var widgetInfo in runningWidgets)
                {
                    var ctx = widgetInfo.WidgetContext;
                    if (!RunningWidgets.ContainsKey(ctx.Id))
                    {
                        RunningWidgets[ctx.Id] = new CompactWidgetInfo
                        {
                            WidgetId = ctx.Id,
                            WidgetName = ctx.DefinitionId,
                            Size = ctx.Size
                        };
                    }
                }
            }
            Log.Write($"WidgetProvider created. Recovered {RunningWidgets.Count} widget(s).");
        }
        catch (Exception ex)
        {
            Log.Write("WidgetProvider recovery failed: " + ex.Message);
        }
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        var info = new CompactWidgetInfo
        {
            WidgetId = widgetContext.Id,
            WidgetName = widgetContext.DefinitionId,
            Size = widgetContext.Size
        };
        lock (Gate) RunningWidgets[info.WidgetId] = info;
        Log.Write($"CreateWidget {info.WidgetId} ({info.WidgetName})");

        // Nowdex files are re-read on every update, so no prefetch/cache step.
        UpdateWidget(info);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        lock (Gate)
        {
            RunningWidgets.Remove(widgetId);
            Log.Write($"DeleteWidget {widgetId}. Remaining: {RunningWidgets.Count}");
            if (RunningWidgets.Count == 0)
            {
                EmptyWidgetListEvent.Set();
            }
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        Log.Write($"OnActionInvoked verb={actionInvokedArgs.Verb}");
        if (actionInvokedArgs.Verb == "refresh")
        {
            if (RunningWidgets.TryGetValue(actionInvokedArgs.WidgetContext.Id, out var info))
            {
                UpdateWidget(info); // re-reads the Nowdex files
            }
        }
        if (actionInvokedArgs.Verb == "viewAll")
        {
            // "查看全部用量": hand off to the Nowdex window (single-instance
            // tray app, so launching its AUMID brings it forward, or starts
            // it if it is not running). No card repaint needed.
            NowdexClient.Launch();
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        // Size changed: same template handles all sizes, just repaint.
        if (RunningWidgets.TryGetValue(contextChangedArgs.WidgetContext.Id, out var info))
        {
            info.Size = contextChangedArgs.WidgetContext.Size;
            UpdateWidget(info);
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        // The board is looking at us: repaint from the Nowdex files.
        if (RunningWidgets.TryGetValue(widgetContext.Id, out var info))
        {
            info.IsActive = true;
            info.Size = widgetContext.Size;
            UpdateWidget(info);
        }
    }

    public void Deactivate(string widgetId)
    {
        if (RunningWidgets.TryGetValue(widgetId, out var info))
        {
            info.IsActive = false;
        }
    }

    private static void RefreshAllWidgets()
    {
        List<CompactWidgetInfo> copy;
        lock (Gate) copy = RunningWidgets.Values.ToList();
        foreach (var info in copy)
        {
            UpdateWidget(info);
        }
    }

    /// <summary>Provider rows per tile size, capped by the provider count
    /// in CardData. Small shows 1 row, medium 3, large 5.</summary>
    private static int MaxRows(WidgetSize size) => size switch
    {
        WidgetSize.Small => 1,
        WidgetSize.Large => 5,
        _ => 3
    };

    private static void UpdateWidget(CompactWidgetInfo info)
    {
        try
        {
            var snapshot = NowdexClient.GetSnapshot();
            var options = new WidgetUpdateRequestOptions(info.WidgetId)
            {
                Template = CardTemplates.UsageTemplate,
                Data = CardData.Build(snapshot, MaxRows(info.Size)),
                CustomState = "1"
            };
            WidgetManager.GetDefault().UpdateWidget(options);
        }
        catch (Exception ex)
        {
            Log.Write("UpdateWidget failed: " + ex);
        }
    }
}
