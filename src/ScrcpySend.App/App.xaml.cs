using Microsoft.UI.Xaml;

namespace ScrcpySend.App;

public partial class App : Application
{
    private MainWindow? window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => AppLog.Write($"Unhandled failure: {args.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
        window.Log($"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} WinUI window activated.");
        if (Environment.GetCommandLineArgs().Contains("--ui-self-test"))
            window.RunUiSelfTest();
        else if (Environment.GetCommandLineArgs().Contains("--capture-self-test"))
            window.RunCaptureSelfTest();
        else if (Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            var timer = window.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
            timer.Start();
        }
    }
}
