using System.Globalization;
using Fita.Audio;
using Fita.Controls;
using Fita.Library;
using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Track = Fita.Library.Track;

namespace Fita.Views;

/// <summary>Uma linha da biblioteca: um álbum (que abre e fecha) ou uma faixa dele.</summary>
public sealed class LibraryEntry
{
    public required string Text { get; init; }
    public string Detail { get; init; } = "";
    public bool IsAlbum { get; init; }
    public bool IsTrack => !IsAlbum;
    public required MusicAlbum Album { get; init; }
    public Track? Track { get; init; }
}

/// <summary>Um álbum da música do telemóvel.</summary>
public sealed class MusicAlbum
{
    public required string Title { get; init; }
    public string? Artist { get; init; }
    public List<Track> Tracks { get; } = [];
    public bool Open { get; set; }
}

/// <summary>
/// A Fita no telemóvel: o mostrador, a barra de posição, o transporte e o volume em cima; por
/// baixo, três separadores (a lista, a biblioteca do telemóvel e o equalizador). Os controlos e
/// os temas são os do desktop; o leitor (<see cref="Player"/>) vive à parte, para a música não
/// parar quando o Android destrói a vista.
/// </summary>
public partial class MobileView : UserControl
{
    private readonly Player _player = Player.Current;
    private readonly SpectrumAnalyzer _analyzer = new();
    private readonly List<LevelSlider> _bandSliders = [];
    private readonly List<TextBlock> _bandLabels = [];
    private LevelSlider _preamp = null!;
    private List<MusicAlbum> _albums = [];
    private bool _frameLoop;
    private TimeSpan _lastFrame;
    private Track? _shownTrack;
    private PlayState _shownState = (PlayState)(-1);
    private string? _transient;
    private long _transientUntil;
    private int _blink;

    /// <summary>A música do telemóvel (o Android põe aqui a leitura do MediaStore).</summary>
    public static Func<List<Track>?>? ScanMusic { get; set; }

    /// <summary>Pedir a permissão de ler a música (o Android liga isto ao pedido do sistema).</summary>
    public static Action? RequestMusicPermission { get; set; }

    /// <summary>A permissão chegou (ou foi recusada): volta a ler a biblioteca.</summary>
    public static event Action? PermissionChanged;

    public static void RaisePermissionChanged() => Dispatcher.UIThread.Post(() => PermissionChanged?.Invoke());

    public MobileView()
    {
        InitializeComponent();
        Vis.Analyzer = _analyzer;
        Graph.Analyzer = _analyzer;
        Vis.Mode = _player.Settings.Vis;

        SetupPlayback();
        SetupTabs();
        SetupList();
        SetupLibrary();
        SetupEq();

        _player.Changed += OnPlayerChanged;
        ThemeManager.Changed += OnThemeChanged;
        Loc.Changed += OnLanguageChanged;
        PermissionChanged += LoadLibrary;
        new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => SlowTick()).Start();
        OnThemeChanged();
        Refresh(force: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            _insets = top.InsetsManager;
            PaintSystemBars();
            top.BackRequested += OnBack;
        }
        EnsureFrameLoop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            top.BackRequested -= OnBack;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Voltar: do EQ ou da biblioteca para a lista; da lista, a app vai para trás (a música continua).</summary>
    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (!ListPanel.IsVisible)
        {
            ShowTab(TabList);
            e.Handled = true;
        }
        else if (BackToSystem is { } back)
        {
            back();
            e.Handled = true;
        }
    }

    private Avalonia.Controls.Platform.IInsetsManager? _insets;

    /// <summary>As barras do sistema (estado e navegação) na cor de fundo do tema.</summary>
    private void PaintSystemBars()
    {
        if (_insets is null)
            return;
        var t = ThemeManager.Current;
        _insets.SystemBarColor = t.Base;
    }

    /// <summary>A atividade manda a app para trás em vez de a fechar.</summary>
    public static Action? BackToSystem { get; set; }

    // ── Reprodução ────────────────────────────────────────────────────────────

    private void SetupPlayback()
    {
        PrevButton.Click += (_, _) => _player.Previous();
        NextButton.Click += (_, _) => _player.Next();
        PlayButton.Click += (_, _) =>
        {
            _player.PlayPause();
            EnsureFrameLoop();
        };
        Seek.Committed += (_, value) =>
        {
            var duration = (_player.PlayingTrack?.Duration) ?? 0;
            if (duration > 0)
                _player.Seek(value * duration);
        };
        Volume.UserChanged += (_, v) => SetVolume(v);
        Volume.Committed += (_, v) => SetVolume(v);
        SetVolume(_player.Settings.Volume, quiet: true);

        ShuffleToggle.IsChecked = _player.Settings.Shuffle;
        ShuffleToggle.IsCheckedChanged += (_, _) => _player.SetShuffle(ShuffleToggle.IsChecked == true);
        RepeatToggle.Click += (_, _) =>
        {
            var mode = _player.CycleRepeat();
            ShowRepeat();
            Status(Loc.T(mode switch { RepeatMode.All => "M.RepeatAll", RepeatMode.One => "M.RepeatOne", _ => "M.RepeatOff" }));
        };
        ShowRepeat();

        TimeArea.PointerPressed += (_, _) =>
        {
            _player.Settings.TimeRemaining = !_player.Settings.TimeRemaining;
            Refresh(force: true);
        };
        Vis.PointerPressed += (_, e) =>
        {
            var next = _player.Settings.Vis switch { VisMode.Spectrum => VisMode.Scope, VisMode.Scope => VisMode.Off, _ => VisMode.Spectrum };
            _player.Settings.Vis = next;
            Vis.Mode = next;
            e.Handled = true;
        };
        MenuButton.Click += (_, _) => MainMenu().ShowAt(MenuButton);
    }

    private void SetVolume(double value, bool quiet = false)
    {
        _player.SetVolume(value);
        Volume.Value = _player.Settings.Volume;
        VolumeText.Text = Math.Round(_player.Settings.Volume).ToString(CultureInfo.InvariantCulture);
        if (!quiet)
            Status($"{Loc.T("Vol")} {_player.Settings.Volume:0}%");
    }

    private void ShowRepeat()
    {
        var mode = _player.Settings.Repeat;
        RepeatToggle.IsChecked = mode != RepeatMode.Off;
        RepeatGlyph.Kind = mode == RepeatMode.One ? GlyphKind.RepeatOne : GlyphKind.Repeat;
    }

    private void OnPlayerChanged()
    {
        Refresh(force: true);
        EnsureFrameLoop();
    }

    private void Status(string text)
    {
        _transient = text;
        _transientUntil = Environment.TickCount64 + 1800;
        Refresh();
    }

    private void EnsureFrameLoop()
    {
        if (_frameLoop || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _frameLoop = true;
        _lastFrame = TimeSpan.Zero;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : (time - _lastFrame).TotalSeconds;
        _lastFrame = time;
        var snap = _player.Engine.Snapshot();
        var active = snap.State == PlayState.Playing;
        if (active)
            _player.Engine.CopyRecent(_analyzer.Samples);
        if (Vis.Mode != VisMode.Off || EqPanel.IsVisible)
        {
            _analyzer.SetBarCount(Vis.WantedBars());
            _analyzer.Update(_player.Engine.SampleRate, dt, active);
            Vis.InvalidateVisual();
            if (EqPanel.IsVisible)
                Graph.InvalidateVisual();
        }
        if (active)
            TitleMarquee.Tick(dt);
        Refresh();
        if ((active || !_analyzer.IsSilent) && TopLevel.GetTopLevel(this) is { } top)
            top.RequestAnimationFrame(OnFrame);
        else
            _frameLoop = false;
    }

    private void SlowTick()
    {
        _blink++;
        var state = _player.State;
        TimeText.Opacity = state == PlayState.Paused && _blink % 4 >= 2 ? 0.25 : 1;
        if (state == PlayState.Playing)
            EnsureFrameLoop();
        UpdateFooter();
        Refresh();
    }

    private void Refresh(bool force = false)
    {
        if (force)
            _shownState = (PlayState)(-1);
        var snap = _player.Engine.Snapshot();
        var track = snap.Segment?.Item.Tag as Track ?? _player.Playlist.Current;
        if (track != _shownTrack || snap.State != _shownState)
        {
            if (_shownTrack is not null)
                _shownTrack.PropertyChanged -= ShownTrackChanged;
            _shownTrack = track;
            if (track is not null)
                track.PropertyChanged += ShownTrackChanged;
            _shownState = snap.State;
            ShowTrackText(track);
            ShowCover(track);
            ShowWaveform(track);
            if (track is not null && !PlaylistBox.IsPointerOver)
                Dispatcher.UIThread.Post(() =>
                {
                    if (_player.Playlist.IndexOf(track) >= 0)
                        PlaylistBox.ScrollIntoView(track);
                }, DispatcherPriority.Background);
        }

        var position = _player.Position;
        var duration = track?.Duration ?? 0;
        if (Seek.IsDragging && duration > 0)
            position = Seek.Value * duration;
        if (duration > 0)
            position = Math.Min(position, duration);
        var time = _player.Settings.TimeRemaining && duration > 0 ? "-" + Format.Clock(duration - position) : Format.Clock(position);
        if (TimeText.Text != time)
            TimeText.Text = time;
        if (!Seek.IsDragging)
            Seek.Value = duration > 0 ? position / duration : 0;
        Seek.IsEnabled = duration > 0 && track is not null;

        var glyph = snap.State switch { PlayState.Playing => GlyphKind.Play, PlayState.Paused => GlyphKind.Pause, _ => GlyphKind.Stop };
        StateGlyph.Kind = glyph;
        PlayGlyph.Kind = snap.State == PlayState.Playing ? GlyphKind.Pause : GlyphKind.Play;

        string status;
        if (_transient is not null && Environment.TickCount64 < _transientUntil)
            status = _transient;
        else if (_player.Ffmpeg is null)
            status = Loc.T("NoFfmpeg");
        else if (_player.Engine.OutputError is { } error)
            status = $"{Loc.T("NoOutput")}: {error}";
        else if (_player.LastError is { } last && snap.State == PlayState.Stopped)
            status = last;
        else if (snap.State == PlayState.Paused)
            status = Loc.T("Paused");
        else if (snap.State == PlayState.Stopped)
            status = track is null ? Loc.T("M.Idle") : Loc.T("Stopped");
        else
            status = AlbumLine(track);
        if (StatusText.Text != status)
            StatusText.Text = status;

        IndEq.Classes.Set("on", _player.Settings.Eq.Enabled);
        IndShuffle.Classes.Set("on", _player.Settings.Shuffle);
        IndRepeat.Classes.Set("on", _player.Settings.Repeat != RepeatMode.Off);
        IndRepeat.Text = _player.Settings.Repeat == RepeatMode.One ? "REP 1" : "REP";
        IndRg.Classes.Set("on", _player.Settings.ReplayGain != ReplayGainMode.Off && track is not null
            && !(double.IsNaN(track.TrackGain) && double.IsNaN(track.AlbumGain)));
        IndLimit.Classes.Set("on", _player.Engine.LimiterActive && snap.State == PlayState.Playing);
    }

    /// <summary>As etiquetas chegam depois (lêem-se em segundo plano): o mostrador acompanha.</summary>
    private void ShownTrackChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Track.Display) or nameof(Track.Info) or nameof(Track.Duration) or nameof(Track.Album))
            ShowTrackText(_shownTrack);
    }

    private void ShowTrackText(Track? track)
    {
        if (track is null)
        {
            TitleMarquee.Text = "Fita · " + Loc.T("Tagline");
            InfoText.Text = "";
            return;
        }
        var duration = track.Duration > 0 ? $" ({Format.Time(track.Duration)})" : "";
        TitleMarquee.Text = $"{track.Number}. {track.Display}{duration}";
        InfoText.Text = track.Info;
    }

    private static string AlbumLine(Track? track)
    {
        if (track is null)
            return "";
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(track.Album))
            parts.Add(track.Album!);
        if (track.Year > 0)
            parts.Add(track.Year.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(track.Genre))
            parts.Add(track.Genre!);
        return string.Join(" · ", parts);
    }

    private string? _coverFor;

    private void ShowCover(Track? track)
    {
        if (!_player.Settings.ShowCover || track is null)
        {
            CoverImage.IsVisible = false;
            _coverFor = null;
            return;
        }
        if (_coverFor == track.Path)
            return;
        _coverFor = track.Path;
        var path = track.Path;
        _ = Task.Run(() => CoverArt.Find(path)).ContinueWith(t =>
        {
            if (_coverFor != path)
                return;
            var bytes = t.IsCompletedSuccessfully ? t.Result : null;
            Bitmap? bitmap = null;
            if (bytes is not null)
                try
                {
                    using var stream = new MemoryStream(bytes);
                    bitmap = Bitmap.DecodeToWidth(stream, 320);
                }
                catch
                {
                }
            CoverImage.Source = bitmap;
            CoverImage.IsVisible = bitmap is not null;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private readonly Dictionary<string, float[]> _waveCache = [];
    private readonly Queue<string> _waveOrder = new();

    private void ShowWaveform(Track? track)
    {
        Seek.Waveform = null;
        if (track is null || !_player.Settings.ShowWaveform || _player.Ffmpeg is not { } ffmpeg)
            return;
        if (_waveCache.TryGetValue(track.Key, out var cached))
        {
            Seek.Waveform = cached;
            return;
        }
        var wanted = track;
        _ = Task.Run(() => Waveform.Compute(ffmpeg, wanted.Path, wanted.Start, wanted.End, CancellationToken.None)).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || t.Result is not { } wave)
                return;
            _waveCache[wanted.Key] = wave;
            _waveOrder.Enqueue(wanted.Key);
            while (_waveOrder.Count > 32)
                _waveCache.Remove(_waveOrder.Dequeue());
            if (_shownTrack == wanted)
                Seek.Waveform = wave;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ── Separadores ───────────────────────────────────────────────────────────

    private void SetupTabs()
    {
        TabList.Click += (_, _) => ShowTab(TabList);
        TabLibrary.Click += (_, _) => ShowTab(TabLibrary);
        TabEq.Click += (_, _) => ShowTab(TabEq);
        ShowTab(TabList);
    }

    private void ShowTab(ToggleButton tab)
    {
        TabList.IsChecked = tab == TabList;
        TabLibrary.IsChecked = tab == TabLibrary;
        TabEq.IsChecked = tab == TabEq;
        ListPanel.IsVisible = tab == TabList;
        LibraryPanel.IsVisible = tab == TabLibrary;
        EqPanel.IsVisible = tab == TabEq;
        if (tab == TabLibrary && _albums.Count == 0)
            LoadLibrary();
        if (tab == TabEq)
            EnsureFrameLoop();
    }

    // ── Lista ─────────────────────────────────────────────────────────────────

    private void SetupList()
    {
        PlaylistBox.ItemsSource = _player.Playlist.Items;
        PlaylistBox.Tapped += (_, e) =>
        {
            if (ItemAt(e.Source) is Track t)
            {
                _player.PlayTrack(t);
                EnsureFrameLoop();
            }
            // No telemóvel não há seleção: a linha que conta é a que toca.
            PlaylistBox.SelectedItem = null;
        };
        PlaylistBox.ContextRequested += (_, e) =>
        {
            if (ItemAt(e.Source) is not Track t)
                return;
            Menu(
                (Loc.T("Play"), () => _player.PlayTrack(t)),
                (Loc.T("PlayNext"), () => _player.Playlist.Move([t], NextIndex(t))),
                (Loc.T("Remove"), () => _player.Playlist.Remove([t])),
                ("-", null),
                (Loc.T("M.ClearList"), () => _player.Playlist.Clear()))
                .ShowAt(PlaylistBox, showAtPointer: true);
            e.Handled = true;
        };
        _player.Playlist.Changed += UpdateFooter;
        UpdateFooter();
    }

    private int NextIndex(Track moving)
    {
        var playing = _player.PlayingTrack;
        return playing is null ? 0 : _player.Playlist.IndexOf(playing) + 1;
    }

    private static object? ItemAt(object? source) =>
        (source as StyledElement)?.DataContext is { } d && d is not MobileView ? d : null;

    private void UpdateFooter()
    {
        var items = _player.Playlist.Items;
        ListEmpty.IsVisible = items.Count == 0;
        var text = items.Count == 0 ? "" : $"{Loc.Count(items.Count)} · {Format.Time(_player.Playlist.TotalDuration)}";
        if (ListFooter.Text != text)
            ListFooter.Text = text;
    }

    private static MenuFlyout Menu(params (string Text, Action? Action)[] items)
    {
        var menu = new MenuFlyout();
        foreach (var (text, action) in items)
        {
            if (text == "-")
            {
                menu.Items.Add(new Separator());
                continue;
            }
            var item = new MenuItem { Header = text };
            if (action is not null)
                item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        return menu;
    }

    // ── Biblioteca ────────────────────────────────────────────────────────────

    private void SetupLibrary()
    {
        GrantButton.Click += (_, _) => RequestMusicPermission?.Invoke();
        SearchBox.TextChanged += (_, _) => FillLibrary();
        LibraryBox.Tapped += (_, e) =>
        {
            if (ItemAt(e.Source) is not LibraryEntry row)
                return;
            if (row.IsAlbum)
            {
                row.Album.Open = !row.Album.Open;
                FillLibrary(keep: row.Album);
            }
            else if (row.Track is { } t)
            {
                // Toca o álbum a partir desta faixa: a lista passa a ser o álbum.
                var tracks = row.Album.Tracks.Select(Copy).ToList();
                _player.PlayList(tracks, tracks[row.Album.Tracks.IndexOf(t)]);
                ShowTab(TabList);
                EnsureFrameLoop();
            }
        };
        LibraryBox.ContextRequested += (_, e) =>
        {
            if (ItemAt(e.Source) is not LibraryEntry row)
                return;
            IReadOnlyList<Track> tracks = row.IsAlbum ? row.Album.Tracks : [row.Track!];
            Menu(
                (Loc.T("Play"), () =>
                {
                    var copies = tracks.Select(Copy).ToList();
                    _player.PlayList(copies, copies[0]);
                    ShowTab(TabList);
                }),
                (Loc.T("PlayNext"), () => _player.Enqueue(tracks, next: true)),
                (Loc.T("M.AddToList"), () => _player.Enqueue(tracks)))
                .ShowAt(LibraryBox, showAtPointer: true);
            e.Handled = true;
        };
    }

    private static Track Copy(Track t) => new(t.Path)
    {
        Title = t.Title, Artist = t.Artist, Album = t.Album, TrackNumber = t.TrackNumber, Duration = t.Duration, Year = t.Year,
    };

    private void LoadLibrary()
    {
        if (ScanMusic is null)
            return;
        LibraryEmptyText.Text = Loc.T("M.Scanning");
        LibraryEmpty.IsVisible = true;
        GrantButton.IsVisible = false;
        _ = Task.Run(() =>
        {
            try
            {
                return ScanMusic();
            }
            catch (Exception ex)
            {
                Log.Write("Falha a ler a música do telemóvel: " + ex.Message);
                return null;
            }
        }).ContinueWith(t =>
        {
            var tracks = t.Result;
            if (tracks is null)
            {
                // Sem permissão (ou recusada): o botão para a pedir.
                LibraryEmptyText.Text = Loc.T("M.NeedPermission");
                GrantButton.IsVisible = true;
                _albums = [];
                FillLibrary();
                return;
            }
            _albums = tracks
                .GroupBy(x => (x.Album ?? "", Path.GetDirectoryName(x.Path) ?? ""))
                .Select(g =>
                {
                    var first = g.First();
                    var album = new MusicAlbum
                    {
                        Title = string.IsNullOrWhiteSpace(first.Album) ? Path.GetFileName(g.Key.Item2) : first.Album!,
                        Artist = g.Select(x => x.Artist).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
                    };
                    album.Tracks.AddRange(g.OrderBy(x => x.TrackNumber == 0 ? uint.MaxValue : x.TrackNumber)
                        .ThenBy(x => x.FileName, NaturalComparer.Instance));
                    return album;
                })
                .OrderBy(a => a.Artist ?? "~", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            LibraryEmptyText.Text = Loc.T("M.NoMusic");
            FillLibrary();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void FillLibrary(MusicAlbum? keep = null)
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var rows = new List<LibraryEntry>();
        foreach (var album in _albums)
        {
            var match = query.Length == 0
                || Contains(album.Title, query) || Contains(album.Artist, query)
                || album.Tracks.Any(t => Contains(t.Title, query) || Contains(t.FileName, query));
            if (!match)
                continue;
            var name = album.Artist is { Length: > 0 } artist ? $"{artist} – {album.Title}" : album.Title;
            rows.Add(new LibraryEntry
            {
                Text = name, IsAlbum = true, Album = album,
                Detail = $"{album.Tracks.Count} · {Format.Time(album.Tracks.Sum(t => t.Duration))}",
            });
            if (album.Open || query.Length > 0)
                foreach (var t in album.Tracks)
                    if (query.Length == 0 || album.Open || Contains(t.Title, query) || Contains(t.FileName, query))
                        rows.Add(new LibraryEntry { Text = t.Display, Detail = t.DurationText, Album = album, Track = t });
        }
        LibraryBox.ItemsSource = rows;
        LibraryEmpty.IsVisible = rows.Count == 0;
        if (keep is not null && rows.FirstOrDefault(r => r.IsAlbum && r.Album == keep) is { } row)
            LibraryBox.ScrollIntoView(row);
    }

    private static bool Contains(string? text, string query) =>
        text is not null && CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, query,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    // ── Equalizador ───────────────────────────────────────────────────────────

    private EqSettings Eq => _player.Settings.Eq;

    private void SetupEq()
    {
        EqOn.IsChecked = Eq.Enabled;
        EqOn.IsCheckedChanged += (_, _) =>
        {
            Eq.Enabled = EqOn.IsChecked == true;
            ApplyEq();
        };
        EqAuto.IsChecked = Eq.AutoPreamp;
        EqAuto.IsCheckedChanged += (_, _) =>
        {
            Eq.AutoPreamp = EqAuto.IsChecked == true;
            ApplyEq();
        };
        PresetButton.Click += (_, _) => PresetMenu().ShowAt(PresetButton);
        ResetEqButton.Click += (_, _) =>
        {
            EqPresets.Apply(Eq, EqPresets.BuiltIn[0]);
            Eq.Preamp = 0;
            ApplyEq();
        };
        Graph.BandChanged += _ =>
        {
            Eq.Preset = "";
            ApplyEq();
        };
        BuildBandSliders();
        ApplyEq();
    }

    private void BuildBandSliders()
    {
        BandGrid.Children.Clear();
        BandGrid.ColumnDefinitions.Clear();
        _bandSliders.Clear();
        _bandLabels.Clear();
        BandGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)));
        BandGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(6)));
        for (var i = 0; i < Eq.Bands.Count; i++)
            BandGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        (_preamp, var pre) = Column(0, Loc.T("Preamp"));
        pre.Bind(TextBlock.TextProperty, this.GetResourceObservable("LU.Preamp"));
        _preamp.UserChanged += (_, v) =>
        {
            Eq.Preamp = Math.Round(v * 2) / 2;
            ApplyEq();
        };
        for (var i = 0; i < Eq.Bands.Count; i++)
        {
            var band = i;
            var (slider, label) = Column(i + 2, "");
            slider.UserChanged += (_, v) =>
            {
                Eq.Bands[band].Gain = Math.Round(v * 2) / 2;
                Eq.Preset = "";
                ApplyEq();
            };
            _bandSliders.Add(slider);
            _bandLabels.Add(label);
        }
    }

    private (LevelSlider Slider, TextBlock Label) Column(int column, string text)
    {
        var slider = new LevelSlider
        {
            Orientation = Orientation.Vertical,
            Minimum = -EqSettings.MaxGain,
            Maximum = EqSettings.MaxGain,
            Origin = 0,
            DefaultValue = 0,
            Width = 28,
            Height = 140,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var label = new TextBlock { Text = text, Classes = { "label" }, FontSize = 9.5, HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { slider, label } };
        Grid.SetColumn(stack, column);
        BandGrid.Children.Add(stack);
        return (slider, label);
    }

    private void ApplyEq()
    {
        Eq.Sanitize();
        _player.Engine.ConfigureEq(Eq);
        Graph.SetSettings(Eq, _player.Engine.SampleRate);
        if (_bandSliders.Count != Eq.Bands.Count)
            BuildBandSliders();
        for (var i = 0; i < _bandSliders.Count; i++)
        {
            var b = Eq.Bands[i];
            if (!_bandSliders[i].IsDragging)
                _bandSliders[i].Value = b.UsesGain ? b.Gain : 0;
            _bandSliders[i].IsEnabled = b.UsesGain;
            _bandLabels[i].Text = b.Frequency >= 1000
                ? (b.Frequency / 1000).ToString(b.Frequency % 1000 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + "k"
                : b.Frequency.ToString("0", CultureInfo.InvariantCulture);
        }
        if (!_preamp.IsDragging)
            _preamp.Value = Eq.Preamp;
        if (EqOn.IsChecked != Eq.Enabled)
            EqOn.IsChecked = Eq.Enabled;
        if (EqAuto.IsChecked != Eq.AutoPreamp)
            EqAuto.IsChecked = Eq.AutoPreamp;
        var auto = EqMath.AutoPreampDb(Eq, _player.Engine.SampleRate);
        var preset = Eq.Preset.Length == 0 ? Loc.T("Custom") : Loc.PresetName(Eq.Preset);
        PreampInfo.Text = Eq.AutoPreamp && auto < -0.05
            ? $"{preset} · {Loc.T("Auto")} {auto.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)} dB"
            : preset;
        Refresh();
    }

    private MenuFlyout PresetMenu()
    {
        var menu = new MenuFlyout();
        foreach (var preset in EqPresets.BuiltIn)
        {
            var item = new MenuItem { Header = Loc.PresetName(preset.Name), ToggleType = MenuItemToggleType.Radio, IsChecked = Eq.Preset == preset.Name };
            item.Click += (_, _) =>
            {
                EqPresets.Apply(Eq, preset);
                ApplyEq();
            };
            menu.Items.Add(item);
        }
        if (_player.Settings.EqPresets.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var user in _player.Settings.EqPresets.ToList())
            {
                var item = new MenuItem { Header = user.Name, ToggleType = MenuItemToggleType.Radio, IsChecked = Eq.Preset == user.Name };
                item.Click += (_, _) =>
                {
                    Eq.Bands = user.Bands.Select(b => b.Clone()).ToList();
                    Eq.Preamp = user.Preamp;
                    Eq.Preset = user.Name;
                    ApplyEq();
                };
                menu.Items.Add(item);
            }
        }
        return menu;
    }

    // ── Menu, tema e língua ───────────────────────────────────────────────────

    private MenuFlyout MainMenu()
    {
        var menu = new MenuFlyout();
        MenuItem Radio(string header, bool on, Action click)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = on };
            item.Click += (_, _) => click();
            return item;
        }
        MenuItem Check(string header, bool on, Action click)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
            item.Click += (_, _) => click();
            return item;
        }

        var theme = new MenuItem { Header = Loc.T("Theme") };
        foreach (var t in Catalog.All)
            theme.Items.Add(Radio(t.Name, ThemeManager.Current == t, () =>
            {
                _player.Settings.Theme = t.Key;
                ThemeManager.Apply(t);
            }));
        menu.Items.Add(theme);
        var language = new MenuItem { Header = Loc.T("Language") };
        language.Items.Add(Radio("English", Loc.Language == UiLanguage.En, () => SetLanguage(UiLanguage.En)));
        language.Items.Add(Radio("Português", Loc.Language == UiLanguage.Pt, () => SetLanguage(UiLanguage.Pt)));
        menu.Items.Add(language);

        var rg = new MenuItem { Header = Loc.T("ReplayGain") };
        foreach (var mode in Enum.GetValues<ReplayGainMode>())
            rg.Items.Add(Radio(Loc.T("Rg" + mode), _player.Settings.ReplayGain == mode, () =>
            {
                _player.Settings.ReplayGain = mode;
                Refresh(force: true);
            }));
        menu.Items.Add(rg);
        menu.Items.Add(Check(Loc.T("ShowCover"), _player.Settings.ShowCover, () =>
        {
            _player.Settings.ShowCover = !_player.Settings.ShowCover;
            _coverFor = null;
            ShowCover(_shownTrack);
        }));
        menu.Items.Add(Check(Loc.T("ShowWaveform"), _player.Settings.ShowWaveform, () =>
        {
            _player.Settings.ShowWaveform = !_player.Settings.ShowWaveform;
            ShowWaveform(_shownTrack);
        }));
        menu.Items.Add(Check(Loc.T("ResumeOption"), _player.Settings.Resume, () => _player.Settings.Resume = !_player.Settings.Resume));
        menu.Items.Add(new Separator());
        var reload = new MenuItem { Header = Loc.T("M.Rescan") };
        reload.Click += (_, _) =>
        {
            ShowTab(TabLibrary);
            LoadLibrary();
        };
        menu.Items.Add(reload);
        var about = new MenuItem { Header = Loc.T("AboutMenu") };
        about.Click += (_, _) => Status($"Fita {typeof(MobileView).Assembly.GetName().Version?.ToString(3)} · {Loc.T("By")} · {Loc.T("Tagline")}");
        menu.Items.Add(about);
        return menu;
    }

    private void SetLanguage(UiLanguage language)
    {
        _player.Settings.Language = Loc.Code(language);
        Loc.Apply(language, ThemeManager.Current.Uppercase);
    }

    /// <summary>Os temas mudam a forma dos botões pelas classes do topo (como na janela do desktop).</summary>
    private void OnThemeChanged()
    {
        var t = ThemeManager.Current;
        Classes.Set("sink", t.PressSinks);
        Classes.Set("lift", t.HoverLifts);
        Classes.Set("motion", t.Motion);
        Classes.Set("bevel", t.Slider == SliderLook.Bevel);
        PaintSystemBars();
        ShowRepeat();
        Vis.InvalidateVisual();
        Graph.InvalidateVisual();
    }

    private void OnLanguageChanged()
    {
        ShowRepeat();
        ShowTrackText(_shownTrack);
        UpdateFooter();
        if (_albums.Count > 0)
            FillLibrary();
        Refresh(force: true);
    }
}
