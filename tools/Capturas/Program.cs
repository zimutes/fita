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
if (only == "--ondas")
{
    Ondas(output);
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
    window.DemoCover(Capa());
    window.DemoWaveform();
    Pump();
    var frame = window.CaptureRenderedFrame();
    var file = Path.Combine(output, $"{theme.Key}.png");
    frame!.Save(file);
    Console.WriteLine($"{theme.Name}: {file} ({window.Bounds.Width}×{window.Bounds.Height})");
    window.Close();
    Pump();
}

// A onda da barra de posição em cada tema: o rato passa da esquerda para a direita e tira-se a
// fotografia a meio, com a barra ampliada 3×, numa só imagem (ondas.png).
static void Ondas(string output)
{
    const int zoom = 3;
    var tiras = new List<(string Nome, Avalonia.Media.Imaging.Bitmap Tira)>();
    foreach (var theme in Catalog.All)
    {
        ThemeManager.Apply(theme);
        var window = new MainWindow(demo: true);
        window.Show();
        Pump();
        window.DemoFrame(107);
        window.DemoWaveform();
        Pump();
        var seek = window.FindControl<Fita.Controls.LevelSlider>("Seek")!;
        var canto = seek.TranslatePoint(new Point(0, 0), window)!.Value;
        var meio = canto.Y + seek.Bounds.Height / 2;
        for (var x = 40.0; x <= 260; x += 8)
        {
            window.MouseMove(new Point(canto.X + x, meio));
            Thread.Sleep(16);
            Pump();
        }
        Thread.Sleep(90);
        Pump();
        var frame = window.CaptureRenderedFrame()!;
        var area = new Rect(canto.X - 4, canto.Y - 2, seek.Bounds.Width + 8, seek.Bounds.Height + 4);
        var tira = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(area.Width * zoom), (int)(area.Height * zoom)));
        using (var dc = tira.CreateDrawingContext())
        {
            using (dc.PushRenderOptions(new Avalonia.Media.RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.None }))
                dc.DrawImage(frame, area, new Rect(0, 0, area.Width * zoom, area.Height * zoom));
        }
        tiras.Add((theme.Name, tira));
        window.Close();
        Pump();
    }
    var largura = tiras.Max(t => t.Tira.PixelSize.Width);
    var altura = tiras.Sum(t => t.Tira.PixelSize.Height + 24);
    var folha = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(largura, altura));
    using (var dc = folha.CreateDrawingContext())
    {
        dc.FillRectangle(Avalonia.Media.Brushes.Black, new Rect(0, 0, largura, altura));
        var y = 0.0;
        foreach (var (nome, tira) in tiras)
        {
            var texto = new Avalonia.Media.FormattedText(nome, System.Globalization.CultureInfo.InvariantCulture,
                Avalonia.Media.FlowDirection.LeftToRight, Avalonia.Media.Typeface.Default, 14, Avalonia.Media.Brushes.White);
            dc.DrawText(texto, new Point(6, y + 3));
            y += 24;
            dc.DrawImage(tira, new Rect(0, y, tira.PixelSize.Width, tira.PixelSize.Height));
            y += tira.PixelSize.Height;
        }
    }
    var file = Path.Combine(output, "ondas.png");
    folha.Save(file);
    Console.WriteLine(file);
}

// Capa de exemplo desenhada aqui (sem imagens de terceiros): uma lua, para o Clair de lune.
static Avalonia.Media.Imaging.Bitmap Capa()
{
    var capa = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(220, 220));
    using (var dc = capa.CreateDrawingContext())
    {
        var ceu = new Avalonia.Media.LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new(Avalonia.Media.Color.Parse("#1b2a4a"), 0), new(Avalonia.Media.Color.Parse("#4a3a6a"), 1) },
        };
        dc.FillRectangle(ceu, new Rect(0, 0, 220, 220));
        var luz = new Avalonia.Media.RadialGradientBrush
        {
            GradientStops = { new(Avalonia.Media.Color.Parse("#80fff4d6"), 0), new(Avalonia.Media.Colors.Transparent, 1) },
        };
        dc.DrawEllipse(luz, null, new Point(140, 80), 80, 80);
        dc.DrawEllipse(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#fff4d6")), null, new Point(140, 80), 34, 34);
        dc.FillRectangle(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0f1830")), new Rect(0, 175, 220, 45));
    }
    return capa;
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

    // Sem som: M liga e desliga; mexer no volume devolve o som.
    var ind = window.FindControl<TextBlock>("IndMute")!;
    var vol = window.FindControl<TextBlock>("VolLabel")!;
    // Tira o foco da caixa de valor (lá as letras são texto, não atalhos).
    window.FindControl<Button>("PlayButton")!.Focus();
    Pump();
    void Tecla(Avalonia.Input.Key k, Avalonia.Input.PhysicalKey f)
    {
        window.KeyPress(k, Avalonia.Input.RawInputModifiers.None, f, null);
        window.KeyRelease(k, Avalonia.Input.RawInputModifiers.None, f, null);
        Pump();
    }
    Tecla(Avalonia.Input.Key.M, Avalonia.Input.PhysicalKey.M);
    Conferir("M tira o som (indicador)", 1, ind.Classes.Contains("on") ? 1 : 0);
    Conferir("M tira o som (VOL)", 1, vol.Classes.Contains("alert") ? 1 : 0);
    Tecla(Avalonia.Input.Key.M, Avalonia.Input.PhysicalKey.M);
    Conferir("M repõe o som", 0, ind.Classes.Contains("on") ? 1 : 0);
    var centro = vol.TranslatePoint(new Point(vol.Bounds.Width / 2, vol.Bounds.Height / 2), window)!.Value;
    window.MouseDown(centro, Avalonia.Input.MouseButton.Left);
    window.MouseUp(centro, Avalonia.Input.MouseButton.Left);
    Pump();
    Conferir("clique no VOL tira o som", 1, ind.Classes.Contains("on") ? 1 : 0);
    Tecla(Avalonia.Input.Key.Up, Avalonia.Input.PhysicalKey.ArrowUp);
    Conferir("subir o volume repõe o som", 0, ind.Classes.Contains("on") ? 1 : 0);

    // A barra de posição apanha o clique também na margem por cima da calha.
    window.DemoFrame(30);
    window.DemoWaveform();
    Pump();
    var seek = window.FindControl<Fita.Controls.LevelSlider>("Seek")!;
    var canto = seek.TranslatePoint(new Point(0, 0), window)!.Value;
    var alvo = new Point(canto.X + 7 + 0.75 * (seek.Bounds.Width - 14), canto.Y + 1);
    window.MouseDown(alvo, Avalonia.Input.MouseButton.Left);
    window.MouseUp(alvo, Avalonia.Input.MouseButton.Left);
    Pump();
    Conferir("clique 1 px dentro da margem salta para 75 %", 0.75, Math.Round(seek.Value, 2));
    Console.WriteLine(falhas == 0 ? "tudo certo" : $"{falhas} falha(s)");
    window.Close();
    Pump();
}
