using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
#if !ANDROID
using Fita.Platform;
using Fita.Views;
using Avalonia.Threading;
#endif

namespace Fita;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static string[] StartupArgs { get; set; } = [];

    /// <summary>No Android (uma só vista, sem janelas): o projeto Android diz aqui qual é a vista.</summary>
    public static Func<Control>? MainViewFactory { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Carrega definições, língua e tema antes de haver janela (os testes também usam).</summary>
    public static void Prepare()
    {
        Settings = SettingsStore.Load();
        Loc.Apply(Loc.Parse(Settings.Language), false);
        ThemeManager.Apply(Catalog.Get(Settings.Theme));
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if !ANDROID
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Prepare();
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.HandleArgs(StartupArgs, startup: true);
            SingleInstance.Listen(args => Dispatcher.UIThread.Post(() => window.HandleArgs(args, startup: false)));
        }
#endif
        if (ApplicationLifetime is ISingleViewApplicationLifetime single && MainViewFactory is { } view)
        {
            Prepare();
            single.MainView = view();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
