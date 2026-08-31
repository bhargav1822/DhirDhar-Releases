using System;

namespace DhirDhar.LicenseGenerator;

internal static class Program
{
    /// <summary>
    /// The main entry point for the DhirDhar License Generator GUI application.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.SystemAware);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        System.Windows.Forms.Application.Run(new MainForm());
    }
}
