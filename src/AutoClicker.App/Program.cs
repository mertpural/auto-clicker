using Avalonia;
using System.Diagnostics;

namespace AutoClicker.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception exception)
        {
            Trace.WriteLine(exception);
            Console.Error.WriteLine("AutoClicker could not start. " + exception.Message);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
