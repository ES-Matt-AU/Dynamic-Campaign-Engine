using System.IO;
using System.Windows;

namespace CampaignDesktop;

public partial class App : Application
{
    /// <summary>Any unhandled error is written to CampaignDesktop-error.txt next to the program and shown, instead of the window just closing.</summary>
    public static string ErrorLogPath => Path.Combine(AppContext.BaseDirectory, "CampaignDesktop-error.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) =>
        {
            File.AppendAllText(ErrorLogPath, $"{DateTime.Now:u}\n{args.Exception}\n\n");
            MessageBox.Show($"Something went wrong:\n{args.Exception.Message}\n\nDetails were written to:\n{ErrorLogPath}", "Error");
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}
