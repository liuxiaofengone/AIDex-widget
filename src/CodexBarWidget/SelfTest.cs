using System;
using System.IO;

namespace CodexBarWidget;

/// <summary>
/// `CodexBarWidget.exe --selftest`
/// Reads one real Nowdex snapshot and writes three files to %TEMP%:
///   NowdexWidget-raw.json   the raw reports.json (pins down the real schema)
///   NowdexWidget-data.json  the card data JSON we would send to the board
///   CodexBarWidget.log      the usual log (package, resolved names)
/// This is how the data path gets verified over SSH, without a GUI.
/// </summary>
internal static class SelfTest
{
    public static void Run()
    {
        try
        {
            var snap = NowdexClient.GetSnapshot();
            var temp = Path.GetTempPath();

            if (NowdexClient.LastRawJson != null)
            {
                File.WriteAllText(Path.Combine(temp, "NowdexWidget-raw.json"), NowdexClient.LastRawJson);
            }
            File.WriteAllText(Path.Combine(temp, "NowdexWidget-data.json"), CardData.Build(snap));

            Log.Write($"SelfTest done. Providers: {snap.Providers.Count}, GlobalError: {snap.GlobalError ?? "(none)"}");
            foreach (var p in snap.Providers)
            {
                Log.Write($"  {p.Name}: windows={p.Windows.Count}, error={p.ErrorText ?? "(none)"}");
                foreach (var w in p.Windows)
                {
                    Log.Write($"    {w.Label}: {w.UsedPercent:0}% reset={w.ResetText ?? "(none)"}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("SelfTest failed: " + ex);
        }
    }
}
