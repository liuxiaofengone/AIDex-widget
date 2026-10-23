using System;
using System.Runtime.InteropServices;
using COM;
using Microsoft.Windows.Widgets.Providers;

namespace CodexBarWidget;

internal static class Program
{
    // CLSID of the widget provider COM class.
    // MUST match Package.appxmanifest: <com:Class Id="..."> and
    // <CreateInstance ClassId="...">.
    private const string ClsidFactory = "3F5A6FFB-F501-46FD-A614-AE8F40F3B00E";

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    private static void Main(string[] args)
    {
        Log.Init();
        Log.Write("Program start. Args: " + string.Join(" ", args));

        // Headless verification path (used over SSH on the dev machine):
        // fetches usage through the CLI once and dumps the rendered card data
        // plus the raw CLI JSON into %TEMP%.
        if (Array.Exists(args, a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            SelfTest.Run();
            return;
        }

        uint cookie;
        var clsid = Guid.Parse(ClsidFactory);
        int hr = CoRegisterClassObject(
            clsid,
            new WidgetProviderFactory<WidgetProvider>(),
            0x4, // CLSCTX_LOCAL_SERVER
            0x1, // REGCLS_MULTIPLEUSE
            out cookie);
        if (hr < 0)
        {
            Log.Write($"CoRegisterClassObject failed: 0x{hr:X8}");
            return;
        }
        Log.Write("Widget provider registered with OLE.");

        if (GetConsoleWindow() != IntPtr.Zero)
        {
            // Launched manually from a console: stay up until ENTER (debugging).
            Console.WriteLine("CodexBarWidget provider running. Press ENTER to exit.");
            Console.ReadLine();
        }
        else
        {
            // Launched by the widget host: stay alive until the last pinned
            // widget is removed (DeleteWidget sets this event).
            WidgetProvider.GetEmptyWidgetListEvent().WaitOne();
        }

        CoRevokeClassObject(cookie);
        Log.Write("Program exit.");
    }
}
