using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Fita.Views;

/// <summary>
/// Modo de janelas separadas (desligado por omissão): o equalizador e a lista saem para janelas
/// pequenas suas, que se colam ao leitor e umas às outras e andam com ele, como no Winamp
/// clássico. Os painéis são os mesmos controlos, mudados de janela.
/// </summary>
public partial class MainWindow
{
    private WindowDock? _dock;
    private PanelWindow? _eqWindow;
    private PanelWindow? _plWindow;

    private bool Separate => _eqWindow is not null;

    private void SetSeparateWindows(bool on)
    {
        _settings.SeparateWindows = on;
        if (on && !Separate)
            EnterSeparate();
        else if (!on && Separate)
            LeaveSeparate();
    }

    private void EnterSeparate()
    {
        RememberPlaylistHeight();
        if (_dock is null)
        {
            _dock = new WindowDock(this);
            _dock.Grip(this, TitleBar, e => (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null);
            _dock.Grip(this, Lcd);
        }

        Body.Children.Remove(EqPanel);
        Root.Children.Remove(PlPanel);
        EqPanel.Margin = new Thickness(10, 6, 10, 10);
        PlPanel.Margin = new Thickness(10, 6, 10, 10);

        _eqWindow = MakePanelWindow("EqTitle", EqPanel, () => EqToggle.IsChecked = false);
        _eqWindow.SizeToContent = SizeToContent.Height;
        _eqWindow.CanResize = false;
        _eqWindow.Width = Bounds.Width;

        _plWindow = MakePanelWindow("PlTitle", PlPanel, () => PlToggle.IsChecked = false);
        _plWindow.Width = _settings.PlWindowWidth ?? Bounds.Width;
        _plWindow.Height = _settings.PlWindowHeight ?? 34 + _settings.PlaylistHeight;
        _plWindow.MinWidth = 300;
        _plWindow.MinHeight = 140;
        _plWindow.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        _plWindow.AddHandler(DragDrop.DropEvent, OnDrop);
        _plWindow.AddHandler(DragDrop.DragLeaveEvent, (_, _) => MarkDrop(null));
        DragDrop.SetAllowDrop(_plWindow, true);

        ApplyLayoutMode();
        // Depois de as janelas terem tamanho: no sítio gravado ou empilhadas por baixo do leitor.
        Dispatcher.UIThread.Post(PlacePanels, DispatcherPriority.Loaded);
    }

    private PanelWindow MakePanelWindow(string titleKey, Control panel, Action close)
    {
        var window = new PanelWindow(titleKey) { Panel = panel, Topmost = Topmost };
        window.CloseButton.Click += (_, _) => close();
        window.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        window.Opened += (_, _) => window.ApplyTheme(ThemeManager.Current);
        // Fechar a janela do painel (Alt+F4) só a esconde: volta com o botão EQ/PL. Quando é a
        // Fita toda a fechar, fecha mesmo.
        window.Closing += (_, e) =>
        {
            if (!_closingAll && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                close();
            }
        };
        _dock!.Add(window);
        _dock.Grip(window, window.TitleBarArea, e => (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null);
        return window;
    }

    private void PlacePanels()
    {
        if (_eqWindow is null || _plWindow is null)
            return;
        bool OnScreen(double? x, double? y) =>
            x is { } px && y is { } py && Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint((int)px + 40, (int)py + 10)));
        var main = WindowDock.RectOf(this);
        var eqHeight = _eqWindow.IsVisible ? WindowDock.SizeOf(_eqWindow).Height : 0;
        _eqWindow.Position = OnScreen(_settings.EqWindowX, _settings.EqWindowY)
            ? new PixelPoint((int)_settings.EqWindowX!.Value, (int)_settings.EqWindowY!.Value)
            : new PixelPoint(main.X, main.Bottom);
        _plWindow.Position = OnScreen(_settings.PlWindowX, _settings.PlWindowY)
            ? new PixelPoint((int)_settings.PlWindowX!.Value, (int)_settings.PlWindowY!.Value)
            : new PixelPoint(main.X, main.Bottom + eqHeight);
    }

    private void LeaveSeparate()
    {
        RememberPanels();
        _closingAll = true;
        foreach (var w in new[] { _eqWindow, _plWindow })
        {
            if (w is null)
                continue;
            w.Panel = null;
            w.Close();
        }
        _closingAll = false;
        _eqWindow = null;
        _plWindow = null;
        _dock = null;
        EqPanel.Margin = new Thickness(0);
        PlPanel.Margin = new Thickness(10, 8, 10, 10);
        Body.Children.Add(EqPanel);
        Root.Children.Add(PlPanel);
        ApplyLayoutMode();
    }

    private bool _closingAll;

    /// <summary>Mostra/esconde as janelas dos painéis conforme os botões EQ e PL.</summary>
    private void ApplySeparateLayout()
    {
        var compact = _settings.Compact;
        Body.IsVisible = !compact;
        MiniBar.IsVisible = compact;
        ByText.IsVisible = !compact;
        TitleText.IsVisible = !compact;
        if (EqToggle.IsChecked != _settings.ShowEq)
            EqToggle.IsChecked = _settings.ShowEq;
        if (PlToggle.IsChecked != _settings.ShowPlaylist)
            PlToggle.IsChecked = _settings.ShowPlaylist;
        Body.Margin = new Thickness(10, 6, 10, 10);
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        EqPanel.IsVisible = true;
        PlPanel.IsVisible = true;
        ShowPanel(_eqWindow!, _settings.ShowEq);
        ShowPanel(_plWindow!, _settings.ShowPlaylist);
        EnsureFrameLoop();
    }

    private void ShowPanel(Window window, bool show)
    {
        if (show && !window.IsVisible)
            window.Show(this);
        else if (!show && window.IsVisible)
            window.Hide();
    }

    /// <summary>Onde estavam as janelas dos painéis (para voltarem ao mesmo sítio).</summary>
    private void RememberPanels()
    {
        if (_eqWindow is { } eq && eq.IsVisible)
        {
            _settings.EqWindowX = eq.Position.X;
            _settings.EqWindowY = eq.Position.Y;
        }
        if (_plWindow is { } pl && pl.IsVisible && pl.WindowState == WindowState.Normal)
        {
            _settings.PlWindowX = pl.Position.X;
            _settings.PlWindowY = pl.Position.Y;
            _settings.PlWindowWidth = pl.Bounds.Width;
            _settings.PlWindowHeight = pl.Bounds.Height;
        }
    }

    private void ClosePanels()
    {
        RememberPanels();
        _closingAll = true;
        _eqWindow?.Close();
        _plWindow?.Close();
    }

    private void ThemePanels(ThemeDef t)
    {
        _eqWindow?.ApplyTheme(t);
        _plWindow?.ApplyTheme(t);
    }
}
