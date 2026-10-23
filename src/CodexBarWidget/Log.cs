using System;
using System.IO;

namespace CodexBarWidget;

/// <summary>
/// Minimal file logger. The widget provider runs windowless (WinExe) inside a
/// package, so %TEMP%\CodexBarWidget.log is our only diagnostic channel.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string _path = "";

    public static void Init()
    {
        try
        {
            // Fully qualified: this class has a property named "Path", which
            // would shadow System.IO.Path here (caused CS1061 in the first build).
            _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CodexBarWidget.log");
        }
        catch
        {
            _path = "";
        }
        Write("---- log init ----");
    }

    public static string Path => _path;

    public static void Write(string message)
    {
        try
        {
            if (_path.Length == 0) return;
            lock (Gate)
            {
                File.AppendAllText(_path,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the provider down.
        }
    }
}
