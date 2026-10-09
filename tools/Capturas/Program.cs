using Fita;
using Fita.Themes;
using Fita.Views;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;

// As definições de teste ficam numa pasta à parte (nunca as do utilizador).
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "capturas");
Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("FITA_DATA", Path.Combine(output, "dados"));
var only = args.Length > 1 ? args[1] : null;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

App.Prepare();
if (only == "--verificar")
{
    Verificar();
    return;
}
// Uma curva com forma, para o gráfico do EQ mostrar alguma coisa.
Fita.Audio.EqPresets.Apply(App.Settings.Eq, Fita.Audio.EqPresets.BuiltIn.First(p => p.Name == "Rock"));
foreach (var theme in Catalog.All)
{
    if (only is not null && theme.Key != only)
        continue;
    ThemeManager.Apply(theme);
    var window = new MainWindow(demo: true);
    window.Show();
    Pump();
    window.DemoFrame(107);
    Pump();
    var frame = window.CaptureRenderedFrame();
    var file = Path.Combine(output, $"{theme.Key}.png");
    frame!.Save(file);
    Console.WriteLine($"{theme.Name}: {file} ({window.Bounds.Width}×{window.Bounds.Height})");
    window.Close();
    Pump();
}

static void Pump()
{
    for (var i = 0; i < 5; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}

// Escreve nas caixas de valor como um utilizador (texto + Enter, vazio = repor) e confere.
static void Verificar()
{
    var window = new MainWindow(demo: true);
    window.Show();
    Pump();
    var falhas = 0;
    void Escrever(string nome, string texto)
    {
        var box = window.FindControl<Fita.Controls.ValueBox>(nome)!;
        box.Focus();
        Pump();
        box.SelectAll();
        if (texto.Length > 0)
            window.KeyTextInput(texto);
        else
            window.KeyPress(Avalonia.Input.Key.Back, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Backspace, null);
        window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        Pump();
    }
    void Conferir(string o, double esperado, double obtido)
    {
        var ok = Math.Abs(esperado - obtido) < 1e-6;
        if (!ok) falhas++;
        Console.WriteLine($"{(ok ? "ok   " : "FALHA")} {o}: {obtido} (esperado {esperado})");
    }
    Escrever("VolumeBox", "35");
    Conferir("volume 35", 35, App.Settings.Volume);
    Escrever("VolumeBox", "");
    Conferir("volume vazio repõe", 80, App.Settings.Volume);
    Escrever("BalanceBox", "L30");
    Conferir("pan L30", -0.3, Math.Round(App.Settings.Balance, 6));
    Escrever("BalanceBox", "c");
    Conferir("pan C", 0, App.Settings.Balance);
    Escrever("BalanceBox", "-120");
    Conferir("pan limitado", -1, App.Settings.Balance);
    Console.WriteLine(falhas == 0 ? "tudo certo" : $"{falhas} falha(s)");
    window.Close();
    Pump();
}
