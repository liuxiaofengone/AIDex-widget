using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace CodexBarWidget;

/// <summary>Bookkeeping for one pinned widget instance.</summary>
public sealed class CompactWidgetInfo
{
    public string WidgetId { get; set; } = "";
    public string WidgetName { get; set; } = "";
    public bool IsActive { get; set; }
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
                            WidgetName = ctx.DefinitionId
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
            WidgetName = widgetContext.DefinitionId
        };
        lock (Gate) RunningWidgets[info.WidgetId] = info;
        Log.Write($"CreateWidget {info.WidgetId} ({info.WidgetName})");

        UpdateWidget(info);
        CodexBarClient.EnsureFresh(RefreshAllWidgets);
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
            CodexBarClient.Invalidate();
            if (RunningWidgets.TryGetValue(actionInvokedArgs.WidgetContext.Id, out var info))
            {
                UpdateWidget(info); // immediate repaint (cached data), fresh data follows
            }
            CodexBarClient.EnsureFresh(RefreshAllWidgets);
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        // Size changed: same template handles all sizes, just repaint.
        if (RunningWidgets.TryGetValue(contextChangedArgs.WidgetContext.Id, out var info))
        {
            UpdateWidget(info);
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        // The board is looking at us: repaint with cache, refresh in background.
        if (RunningWidgets.TryGetValue(widgetContext.Id, out var info))
        {
            info.IsActive = true;
            UpdateWidget(info);
        }
        CodexBarClient.EnsureFresh(RefreshAllWidgets);
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

    private static void UpdateWidget(CompactWidgetInfo info)
    {
        try
        {
            var snapshot = CodexBarClient.GetSnapshot();
            var options = new WidgetUpdateRequestOptions(info.WidgetId)
            {
                Template = CardTemplates.UsageTemplate,
                Data = CardData.Build(snapshot),
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
