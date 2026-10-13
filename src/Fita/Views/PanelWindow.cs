using Fita.Controls;
using Fita.Platform;
using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace Fita.Views;

/// <summary>
/// No modo de janelas separadas (o Winamp clássico), o equalizador e a lista vivem cada um na
/// sua janela pequena: uma barra de título fina e o painel. Pertencem à janela do leitor (ficam
/// à frente dela e minimizam com ela) e colam-se a ela e umas às outras (<see cref="WindowDock"/>).
/// </summary>
public sealed class PanelWindow : Window
{
    public Border TitleBarArea { get; }
    public Button CloseButton { get; }
    private readonly Border _host;

    public PanelWindow(string titleKey)
    {
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Fita/Assets/fita.ico")));
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        FontSize = 13;
        this[!FontFamilyProperty] = this.GetResourceObservable("FontSans").ToBinding();
        this[!ForegroundProperty] = this.GetResourceObservable("Text").ToBinding();
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = 0;
        }
        else
            SystemDecorations = SystemDecorations.None;

        var title = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        title[!TextBlock.TextProperty] = this.GetResourceObservable("LU." + titleKey).ToBinding();
        title[!TextBlock.ForegroundProperty] = this.GetResourceObservable("TitleFg").ToBinding();
        title[!TextBlock.FontFamilyProperty] = this.GetResourceObservable("FontDisplay").ToBinding();
        title[!TextBlock.LetterSpacingProperty] = this.GetResourceObservable("TitleSpacing").ToBinding();
        CloseButton = new Button { Classes = { "chrome", "close" }, Content = new Glyph { Kind = GlyphKind.Close } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(title);
        Grid.SetColumn(CloseButton, 1);
        grid.Children.Add(CloseButton);
        TitleBarArea = new Border { Height = 24, Padding = new Thickness(12, 0, 4, 0), Child = grid, Background = Brushes.Transparent };
        TitleBarArea[!Border.BackgroundProperty] = this.GetResourceObservable("TitleBg").ToBinding();
        DockPanel.SetDock(TitleBarArea, Dock.Top);

        _host = new Border();
        var dock = new DockPanel { Children = { TitleBarArea, _host } };
        Content = new Panel { Children = { new Backdrop(), dock } };
    }

    /// <summary>O painel que a janela mostra (tirado da janela do leitor).</summary>
    public Control? Panel
    {
        get => _host.Child;
        set => _host.Child = value;
    }

    /// <summary>As classes de tema que os estilos procuram na janela, e os cantos no Windows 11.</summary>
    public void ApplyTheme(ThemeDef t)
    {
        Classes.Set("sink", t.PressSinks);
        Classes.Set("lift", t.HoverLifts);
        Classes.Set("motion", t.Motion);
        Classes.Set("bevel", t.Slider == SliderLook.Bevel);
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle()?.Handle is { } handle && handle != IntPtr.Zero)
            WindowsDwm.Apply(handle, t.RoundWindow);
    }
}
