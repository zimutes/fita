using Fita.Platform;
using Avalonia;

namespace Fita;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest")
            return SelfTest.Run(args.Skip(1).ToArray());

        // Já há uma Fita aberta (ou a abrir): entrega-lhe os ficheiros e sai.
        if (SingleInstance.HandOff(args))
            return 0;

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Erro: " + e.ExceptionObject);
        App.StartupArgs = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
