using Fita.Audio;
using Fita.Library;
using Fita.Platform;
using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Fita.Views;

public partial class MainWindow
{
    private bool _sizing;
    /// <summary>A janela já tem a altura das definições: a partir daqui, o que se vê é do utilizador.</summary>
    private bool _layoutReady;

    private void SetupWindow()
    {
        // Moldura própria. No Windows fica a borda do sistema (redimensionar, sombra, encaixe);
        // no Linux a janela fica sem decorações e redimensiona-se pela pega no canto.
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = 0;
        }
        else
            SystemDecorations = SystemDecorations.None;

        Topmost = _settings.AlwaysOnTop;
        Width = _settings.WindowWidth;

        TitleBar.PointerPressed += (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
                return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            if (e.ClickCount == 2)
            {
                ToggleCompact();
                e.Handled = true;
                return;
            }
            BeginMoveDrag(e);
            e.Handled = true;
        };
        // Como no Winamp, o mostrador também serve de pega para mover a janela.
        Lcd.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        MenuButton.Click += (_, _) => MainMenu().ShowAt(MenuButton);
        CompactButton.Click += (_, _) => ToggleCompact();
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Close();

        EqToggle.IsChecked = _settings.ShowEq;
        PlToggle.IsChecked = _settings.ShowPlaylist;
        EqToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.ShowEq = EqToggle.IsChecked == true;
            ApplyLayoutMode();
        };
        PlToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.ShowPlaylist = PlToggle.IsChecked == true;
            ApplyLayoutMode();
        };

        ResizeGrip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginResizeDrag(WindowEdge.SouthEast, e);
                e.Handled = true;
            }
        };
        Overlay.PointerPressed += (_, e) =>
        {
            if (e.Source == Overlay)
                HideOverlay();
        };

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);

        // Altura provisória até haver medidas (evita um salto ao abrir).
        Height = 820;
        Opened += (_, _) =>
        {
            RestorePosition();
            ApplyLayoutMode();
            // No X11 a primeira medida pode chegar antes de a janela estar mapeada: repete-se
            // depois do primeiro desenho.
            Dispatcher.UIThread.Post(() =>
            {
                ApplyLayoutMode();
                _layoutReady = true;
            }, DispatcherPriority.Loaded);
            ApplyWindowCorners();
            StartSystemIntegration();
        };
    }

    /// <summary>A altura que o utilizador deu à lista (lê-se antes de mexer na janela).</summary>
    private void RememberPlaylistHeight()
    {
        if (_layoutReady && !_sizing && PlPanel.IsVisible && SizeToContent == SizeToContent.Manual && PlPanel.Bounds.Height > 0)
            _settings.PlaylistHeight = PlPanel.Bounds.Height;
    }

    private void ToggleCompact()
    {
        _settings.Compact = !_settings.Compact;
        ApplyLayoutMode();
    }

    /// <summary>
    /// Mostra/esconde painéis e acerta a altura: sem lista (ou em modo compacto) a janela tem a
    /// altura do conteúdo; com lista, a lista fica com a altura que o utilizador lhe deu.
    /// </summary>
    private void ApplyLayoutMode()
    {
        RememberPlaylistHeight();
        var compact = _settings.Compact;
        Body.IsVisible = !compact;
        EqPanel.IsVisible = _settings.ShowEq;
        PlPanel.IsVisible = !compact && _settings.ShowPlaylist;
        MiniBar.IsVisible = compact;
        ByText.IsVisible = !compact;
        TitleText.IsVisible = !compact;
        if (EqToggle.IsChecked != _settings.ShowEq)
            EqToggle.IsChecked = _settings.ShowEq;
        if (PlToggle.IsChecked != _settings.ShowPlaylist)
            PlToggle.IsChecked = _settings.ShowPlaylist;
        Body.Margin = new Thickness(10, 6, 10, PlPanel.IsVisible ? 0 : 10);

        _sizing = true;
        try
        {
            if (!PlPanel.IsVisible)
            {
                MinHeight = 0;
                SizeToContent = SizeToContent.Height;
            }
            else
            {
                SizeToContent = SizeToContent.Manual;
                var top = TopHeight();
                MinHeight = top + 110;
                Height = top + Math.Max(110, _settings.PlaylistHeight);
            }
        }
        finally
        {
            Dispatcher.UIThread.Post(() => _sizing = false, DispatcherPriority.Background);
        }
        EnsureFrameLoop();
    }

    private double TopHeight()
    {
        var width = Bounds.Width > 0 ? Bounds.Width : Width;
        Body.Measure(new Size(width, double.PositiveInfinity));
        return TitleBar.Height + (Body.IsVisible ? Body.DesiredSize.Height : 0) + PlPanel.Margin.Top + PlPanel.Margin.Bottom;
    }

    private void RestorePosition()
    {
        if (_settings.WindowX is not { } x || _settings.WindowY is not { } y)
            return;
        var point = new PixelPoint((int)x, (int)y);
        // Só se ainda couber num ecrã (o portátil pode estar sem o monitor de ontem).
        if (Screens.All.Any(s => s.WorkingArea.Contains(point + new PixelPoint(40, 20))))
            Position = point;
    }

    private void RememberWindow()
    {
        if (WindowState != WindowState.Normal)
            return;
        RememberPlaylistHeight();
        _settings.WindowX = Position.X;
        _settings.WindowY = Position.Y;
        if (Bounds.Width > 0)
            _settings.WindowWidth = Bounds.Width;
    }

    private void ApplyWindowCorners()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
            WindowsDwm.Apply(handle, ThemeManager.Current.RoundWindow);
    }

    // ── Teclado ───────────────────────────────────────────────────────────────

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (Overlay.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                HideOverlay();
                e.Handled = true;
            }
            return;
        }
        if (MediaKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        var focused = FocusManager?.GetFocusedElement() as Visual;
        if (focused is TextBox || e.Source is TextBox)
            return;
        var inList = focused?.FindAncestorOfType<ListBox>(includeSelf: true) == PlaylistBox;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var plain = !ctrl && !alt;

        var handled = true;
        switch (e.Key)
        {
            case Key.Z when plain: Previous(); break;
            case Key.X when plain: Play(); break;
            case Key.C when plain: PauseToggle(); break;
            case Key.V when ctrl && !alt: ToggleStopAfter(); break;
            case Key.V when plain: Stop(); break;
            case Key.B when plain: Next(); break;
            case Key.Space: PauseOrPlay(); break;
            case Key.Left when !alt: SeekBy(ctrl ? -30 : -5); break;
            case Key.Right when !alt: SeekBy(ctrl ? 30 : 5); break;
            case Key.Up when alt: MoveSelected(-1); break;
            case Key.Down when alt: MoveSelected(1); break;
            case Key.Up when !inList: ChangeVolume(4); break;
            case Key.Down when !inList: ChangeVolume(-4); break;
            case Key.J when plain:
                if (!PlPanel.IsVisible)
                    PlToggle.IsChecked = true;
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;
            case Key.L when (shift || ctrl) && !alt: _ = AddFolderAsync(); break;
            case Key.L when plain: _ = AddFilesAsync(play: false); break;
            case Key.S when plain: SetShuffle(!_settings.Shuffle); break;
            case Key.R when plain: CycleRepeat(); break;
            case Key.G when alt: EqToggle.IsChecked = EqToggle.IsChecked != true; break;
            case Key.E when alt: PlToggle.IsChecked = PlToggle.IsChecked != true; break;
            case Key.L when alt: SetLibraryMode(!_libraryMode); break;
            case Key.W when ctrl: ToggleCompact(); break;
            case Key.T when ctrl: ToggleTimeMode(); break;
            case Key.T when plain: NextTheme(); break;
            case Key.A when plain: CycleLoop(); break;
            case Key.Delete when PlaylistBox.SelectedItems is { Count: > 0 }: RemoveSelected(); break;
            case Key.Enter when inList && PlaylistBox.SelectedItem is Track t: PlayTrack(t); break;
            case Key.Escape when _settings.Compact: ToggleCompact(); break;
            default: handled = false; break;
        }
        if (handled)
            e.Handled = true;
    }

    private bool MediaKey(Key key)
    {
        switch (key)
        {
            case Key.MediaPlayPause: PauseOrPlay(); return true;
            case Key.MediaNextTrack: Next(); return true;
            case Key.MediaPreviousTrack: Previous(); return true;
            case Key.MediaStop: Stop(); return true;
            default: return false;
        }
    }

    private void PauseOrPlay()
    {
        if (_engine.Snapshot().State == PlayState.Playing)
            PauseToggle();
        else
            Play();
    }

    private void ChangeVolume(double delta) => SetVolume(Volume.Value + delta);

    // ── Linha de comandos e segunda instância ─────────────────────────────────

    // Com vários ficheiros selecionados, o Explorador lança um processo por ficheiro e cada um
    // manda só o seu caminho. O que chega quase junto é uma seleção: junta-se, põe-se pela ordem
    // natural e entra na lista de uma vez, a tocar a partir do primeiro.
    private readonly List<string> _incomingPlay = [];
    private readonly List<string> _incomingEnqueue = [];
    private DispatcherTimer? _incomingTimer;
    private Task _incomingDone = Task.CompletedTask;
    private long? _lastPlayBatch;

    /// <summary>
    /// Ficheiros vindos da linha de comandos (ou de uma segunda instância): juntam-se à lista e
    /// tocam; com --enqueue só se juntam.
    /// </summary>
    public void HandleArgs(IReadOnlyList<string> args, bool startup)
    {
        var enqueue = args.Any(a => a is "--enqueue" or "-e");
        var paths = args.Where(a => !a.StartsWith('-')).ToList();
        if (!startup)
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }
        if (paths.Count == 0)
            return;
        (enqueue ? _incomingEnqueue : _incomingPlay).AddRange(paths);
        if (_incomingTimer is null)
        {
            _incomingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(startup ? 700 : 400) };
            _incomingTimer.Tick += (_, _) => FlushIncoming();
        }
        // Cada chegada adia a entrada: só se trata quando a seleção acabou de chegar.
        _incomingTimer.Stop();
        _incomingTimer.Start();
    }

    private void FlushIncoming()
    {
        _incomingTimer!.Stop();
        _incomingTimer.Interval = TimeSpan.FromMilliseconds(400);
        var play = TakeSorted(_incomingPlay);
        var enqueue = TakeSorted(_incomingEnqueue);
        // Um processo atrasado da mesma seleção junta-se ao fim, em vez de tirar a vez ao que já toca.
        var now = Environment.TickCount64;
        var startPlaying = play.Count > 0 && (_lastPlayBatch is not { } last || now - last > 3000);
        if (play.Count > 0)
            _lastPlayBatch = now;

        // Uma entrada de cada vez, para a ordem da lista ser a da chegada.
        var previous = _incomingDone;
        _incomingDone = AddInOrder();

        async Task AddInOrder()
        {
            try
            {
                await previous;
                if (play.Count > 0)
                    await AddPathsAsync(play, play: startPlaying);
                if (enqueue.Count > 0)
                    await AddPathsAsync(enqueue, play: false);
            }
            catch (Exception ex)
            {
                Log.Write("Ficheiros recebidos: " + ex.Message);
            }
        }
    }

    private static List<string> TakeSorted(List<string> pending)
    {
        var paths = pending.ToList();
        pending.Clear();
        paths.Sort(NaturalComparer.Instance);
        return paths;
    }
}
