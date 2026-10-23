using System;
using System.IO;

namespace CodexBarWidget;

/// <summary>
/// `CodexBarWidget.exe --selftest`
/// Runs one real fetch through codexbar-cli and writes three files to %TEMP%:
///   CodexBarWidget-raw.json   the CLI's raw stdout (pins down the real schema)
///   CodexBarWidget-data.json  the card data JSON we would send to the board
///   CodexBarWidget.log        the usual log (CLI path, stderr, parse notes)
/// This is how the data path gets verified over SSH, without a GUI.
/// </summary>
internal static class SelfTest
{
    public static void Run()
    {
        try
        {
            var snap = CodexBarClient.FetchFresh();
            var temp = Path.GetTempPath();

            if (CodexBarClient.LastRawJson != null)
            {
                File.WriteAllText(Path.Combine(temp, "CodexBarWidget-raw.json"), CodexBarClient.LastRawJson);
            }
            File.WriteAllText(Path.Combine(temp, "CodexBarWidget-data.json"), CardData.Build(snap));

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
