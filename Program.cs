using System;
using System.Windows.Forms;
using PrinterTool.UI;

namespace PrinterTool;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is { Length: > 0 })
        {
            return Cli.Run(args);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { /* 清单已声明 DPI 时不处理 */ }
        Application.Run(new MainForm());
        return 0;
    }
}
