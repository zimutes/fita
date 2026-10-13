using Fita.Audio;
using Fita.Controls;
using Fita.Library;
using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Fita.Views;

/// <summary>
/// A janela do leitor. Este ficheiro trata da reprodução e do mostrador; a lista, o equalizador,
/// os menus e a moldura da janela estão nos outros ficheiros parciais.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AudioEngine _engine;
    private readonly Playlist _playlist = new();
    private readonly MetadataLoader _tags;
    private readonly LoudnessScanner _loudness;
    private readonly SpectrumAnalyzer _analyzer = new();
    private readonly DispatcherTimer _slowTimer;
    private string? _ffmpeg;
    private string? _ffprobe;

    private Track? _shownTrack;
    /// <summary>A faixa onde se marcou o A (mudar de faixa esquece-o).</summary>
    private Track? _loopTrack;
    private PlayState _shownState = (PlayState)(-1);
    private int _lastEnded;
    private bool _frameLoop;
    private TimeSpan _lastFrame;
    private int _blink;
    private string? _lastError;
    /// <summary>Segundo onde a faixa atual ficou da última vez (para continuar).</summary>
    private double _resumeAt;
    private bool _demo;

    public MainWindow() : this(demo: false)
    {
    }

    public MainWindow(bool demo)
    {
        _demo = demo;
        _settings = App.Settings;
        InitializeComponent();

        LocateFfmpeg();
        _engine = new AudioEngine(string.IsNullOrEmpty(_settings.OutputDevice) ? null : _settings.OutputDevice)
        {
            Silent = demo || Environment.GetEnvironmentVariable("FITA_SILENT") == "1",
            FfmpegPath = _ffmpeg,
            Volume = VolumeGain(_settings.Volume),
            Balance = (float)_settings.Balance,
            CrossfadeSeconds = _settings.Crossfade,
            Speed = new PlaybackSpeed(_settings.Speed, _settings.KeepPitch),
        };
        _engine.NextItem = NextFromDecoder;
        _engine.ConfigureEq(_settings.Eq);
        _tags = new MetadataLoader(() => _ffprobe);
        _tags.Progress += UpdateFooterSoon;
        _loudness = new LoudnessScanner(() => _ffmpeg);
        _loudness.Scanned += t =>
        {
            if (t == _shownTrack)
                Refresh(force: true);
        };

        Vis.Analyzer = _analyzer;
        Graph.Analyzer = _analyzer;

        SetupPlayback();
        SetupPlaylist();
        SetupEq();
        SetupWindow();

        if (demo)
            LoadDemo();
        else
            LoadPlaylist();

        _slowTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => SlowTick());
        _slowTimer.Start();
        ThemeManager.Changed += OnThemeChanged;
        Loc.Changed += OnLanguageChanged;
        OnThemeChanged();
        Refresh(force: true);
    }

    private void LocateFfmpeg()
    {
        _ffmpeg = FfmpegLocator.FindFfmpeg(string.IsNullOrWhiteSpace(_settings.FfmpegPath) ? null : _settings.FfmpegPath);
        _ffprobe = FfmpegLocator.FindFfprobe(_ffmpeg);
    }

    /// <summary>Controlo de 0 a 100 → ganho. Ao quadrado: a meio fica a −12 dB, como o ouvido espera.</summary>
    private static float VolumeGain(double position) => (float)Math.Pow(Math.Clamp(position, 0, 100) / 100, 2);

    // ── Reprodução ────────────────────────────────────────────────────────────

    private void SetupPlayback()
    {
        PrevButton.Click += (_, _) => Previous();
        PlayButton.Click += (_, _) => Play();
        PauseButton.Click += (_, _) => PauseToggle();
        StopButton.Click += (_, _) => Stop();
        NextButton.Click += (_, _) => Next();
        OpenButton.Click += async (_, _) => await AddFilesAsync(play: true);
        MiniPrev.Click += (_, _) => Previous();
        MiniNext.Click += (_, _) => Next();
        MiniPlay.Click += (_, _) =>
        {
            if (_engine.Snapshot().State == PlayState.Playing)
                PauseToggle();
            else
                Play();
        };

        Seek.Committed += (_, value) =>
        {
            var snap = _engine.Snapshot();
            var duration = (snap.Segment?.Item.Tag as Track)?.Duration ?? 0;
            if (duration > 0 && snap.State != PlayState.Stopped)
                _engine.Seek(value * duration);
        };

        Volume.DefaultValue = 80;
        Volume.UserChanged += (_, v) => SetVolume(v);
        VolumeBox.Maximum = 100;
        VolumeBox.DefaultValue = 80;
        VolumeBox.Committed += (_, v) => SetVolume(v);

        Balance.UserChanged += (_, v) => SetBalance(Math.Abs(v) < 0.06 ? 0 : v);
        BalanceBox.Minimum = -100;
        BalanceBox.Maximum = 100;
        BalanceBox.Formatter = BalanceText;
        BalanceBox.Parser = ParseBalance;
        BalanceBox.Committed += (_, v) => SetBalance(v / 100);
        SetVolume(_settings.Volume, quiet: true);
        SetBalance(_settings.Balance, quiet: true);

        ShuffleToggle.IsChecked = _settings.Shuffle;
        _playlist.Shuffle = _settings.Shuffle;
        ShuffleToggle.IsCheckedChanged += (_, _) => SetShuffle(ShuffleToggle.IsChecked == true);
        _playlist.Repeat = _settings.Repeat;
        RepeatToggle.Click += (_, _) => CycleRepeat();
        ShowRepeat();

        TimeArea.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(TimeArea).Properties.IsLeftButtonPressed)
                return;
            ToggleTimeMode();
            e.Handled = true;
        };
        Vis.Mode = _settings.Vis;
        Vis.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(Vis).Properties.IsLeftButtonPressed)
                return;
            SetVis(_settings.Vis switch { VisMode.Spectrum => VisMode.Scope, VisMode.Scope => VisMode.Off, _ => VisMode.Spectrum });
            e.Handled = true;
        };
        StatusText.PointerPressed += (_, e) =>
        {
            if (_ffmpeg is null)
            {
                ShowFfmpegMissing();
                e.Handled = true;
            }
        };
    }

    private void SetVolume(double value, bool quiet = false)
    {
        value = Math.Clamp(value, 0, 100);
        Volume.Value = value;
        VolumeBox.Value = Math.Round(value);
        _settings.Volume = value;
        _engine.Volume = VolumeGain(value);
        if (!quiet)
            ShowStatus($"{Loc.T("Vol")} {value:0}%");
    }

    /// <summary>-1 (esquerda) a 1 (direita).</summary>
    private void SetBalance(double value, bool quiet = false)
    {
        value = Math.Clamp(value, -1, 1);
        Balance.Value = value;
        BalanceBox.Value = Math.Round(value * 100);
        _settings.Balance = value;
        _engine.Balance = (float)value;
        if (!quiet)
            ShowStatus($"{Loc.T("Bal")} {BalanceText(Math.Round(value * 100))}");
    }

    /// <summary>"C" ao centro, "L25"/"R25" (ou "E25"/"D25" em português).</summary>
    private static string BalanceText(double v) =>
        v == 0 ? "C"
        : v < 0 ? (Loc.Language == UiLanguage.Pt ? "E" : "L") + (-v).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
        : (Loc.Language == UiLanguage.Pt ? "D" : "R") + v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Aceita "C", "L25"/"E25", "R25"/"D25" ou um número de −100 a 100.</summary>
    private static double? ParseBalance(string text)
    {
        var t = text.Trim().ToUpperInvariant();
        if (t is "C" or "0")
            return 0;
        if (t.Length > 1 && (t[0] is 'L' or 'E' or 'R' or 'D'))
            return ValueBox.ParseNumber(t[1..]) is { } n ? (t[0] is 'L' or 'E' ? -Math.Abs(n) : Math.Abs(n)) : null;
        return ValueBox.ParseNumber(t);
    }

    private Track? PlayingTrack => _engine.Snapshot().Segment?.Item.Tag as Track;

    private PlayItem ToPlayItem(Track track)
    {
        var db = _settings.ReplayGain switch
        {
            ReplayGainMode.Track => double.IsNaN(track.TrackGain) ? track.AlbumGain : track.TrackGain,
            ReplayGainMode.Album => double.IsNaN(track.AlbumGain) ? track.TrackGain : track.AlbumGain,
            _ => double.NaN,
        };
        var gain = double.IsNaN(db) ? 1f : (float)Math.Clamp(Math.Pow(10, db / 20), 0.05, 4);
        return new PlayItem(track, track.Path, gain, track.Start, track.End);
    }

    /// <summary>Chamado na thread de descodificação: pede à interface a faixa que se segue.</summary>
    private PlayItem? NextFromDecoder(PlayItem current, CancellationToken ct)
    {
        try
        {
            var task = Dispatcher.UIThread.InvokeAsync(() =>
            {
                var previous = current.Tag as Track;
                var next = _playlist.After(previous, automatic: true);
                if (next is null)
                    return null;
                _tags.Prioritize(next);
                // Dentro de um disco não há crossfade: os discos ao vivo e as misturas correm inteiros.
                return ToPlayItem(next) with { Gapless = SameAlbum(previous, next) };
            }).GetTask();
            task.Wait(ct);
            return task.Result;
        }
        catch
        {
            return null;
        }
    }

    private static bool SameAlbum(Track? a, Track? b) =>
        a is not null && b is not null && !string.IsNullOrWhiteSpace(a.Album)
        && string.Equals(a.Album, b.Album, StringComparison.CurrentCultureIgnoreCase)
        && Playlist.PathComparer.Equals(Path.GetDirectoryName(a.Path), Path.GetDirectoryName(b.Path));

    private void PlayTrack(Track track, double start = 0)
    {
        if (_ffmpeg is null)
        {
            _playlist.SetCurrent(track);
            Refresh(force: true);
            ShowFfmpegMissing();
            return;
        }
        _tags.Prioritize(track);
        track.Failed = false;
        _playlist.SetCurrent(track);
        _engine.Play(ToPlayItem(track), start);
        _resumeAt = 0;
        _shownTrack = null;
        ScanUpcoming();
        EnsureFrameLoop();
        Refresh(force: true);
    }

    /// <summary>Como no Winamp: em pausa continua; a tocar recomeça a faixa; parado toca a atual.</summary>
    private void Play()
    {
        var snap = _engine.Snapshot();
        if (snap.State == PlayState.Paused)
        {
            _engine.Resume();
            EnsureFrameLoop();
            return;
        }
        if (snap.State == PlayState.Playing && snap.Segment?.Item.Tag is Track playing)
        {
            PlayTrack(playing);
            return;
        }
        var selected = PlaylistBox.SelectedItem as Track;
        var track = _playlist.Current ?? selected ?? _playlist.After(null, automatic: false);
        if (track is null)
        {
            _ = AddFilesAsync(play: true);
            return;
        }
        var start = _settings.Resume && track == _playlist.Current ? _resumeAt : 0;
        PlayTrack(track, start);
    }

    private void PauseToggle()
    {
        var state = _engine.Snapshot().State;
        if (state == PlayState.Playing)
            _engine.Pause();
        else if (state == PlayState.Paused)
        {
            _engine.Resume();
            EnsureFrameLoop();
        }
        Refresh(force: true);
    }

    private void Stop()
    {
        _engine.Stop();
        _resumeAt = 0;
        Refresh(force: true);
    }

    private void Next()
    {
        var snap = _engine.Snapshot();
        var from = snap.Segment?.Item.Tag as Track ?? _playlist.Current;
        var next = _playlist.After(from, automatic: false);
        if (next is null)
            return;
        if (snap.State == PlayState.Stopped)
        {
            _playlist.SetCurrent(next);
            _resumeAt = 0;
            ScrollTo(next);
            Refresh(force: true);
        }
        else
            PlayTrack(next);
    }

    private void Previous()
    {
        var snap = _engine.Snapshot();
        var from = snap.Segment?.Item.Tag as Track ?? _playlist.Current;
        var previous = _playlist.Before(from);
        if (previous is null)
            return;
        if (snap.State == PlayState.Stopped)
        {
            _playlist.SetCurrent(previous);
            _resumeAt = 0;
            ScrollTo(previous);
            Refresh(force: true);
        }
        else
            PlayTrack(previous);
    }

    private void SeekBy(double seconds)
    {
        var snap = _engine.Snapshot();
        if (snap.State == PlayState.Stopped || snap.Segment?.Item.Tag is not Track t)
            return;
        var target = snap.Position + seconds;
        if (t.Duration > 0)
            target = Math.Min(target, t.Duration - 0.5);
        _engine.Seek(Math.Max(0, target));
    }

    private void SetShuffle(bool on)
    {
        _settings.Shuffle = on;
        _playlist.Shuffle = on;
        if (ShuffleToggle.IsChecked != on)
            ShuffleToggle.IsChecked = on;
        Refresh(force: true);
    }

    private void CycleRepeat()
    {
        _settings.Repeat = _settings.Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
        _playlist.Repeat = _settings.Repeat;
        ShowRepeat();
        Refresh(force: true);
    }

    private void ShowRepeat()
    {
        RepeatToggle.IsChecked = _settings.Repeat != RepeatMode.Off;
        RepeatToggle.Content = Loc.Upper
            ? Loc.T(_settings.Repeat == RepeatMode.One ? "RepeatOne" : "Repeat").ToUpper(Loc.Culture)
            : Loc.T(_settings.Repeat == RepeatMode.One ? "RepeatOne" : "Repeat");
    }

    private void ToggleTimeMode()
    {
        _settings.TimeRemaining = !_settings.TimeRemaining;
        Refresh(force: true);
    }

    private void SetVis(VisMode mode)
    {
        _settings.Vis = mode;
        Vis.Mode = mode;
        Vis.InvalidateVisual();
    }

    /// <summary>
    /// Com o ReplayGain ligado, mede a faixa atual e as que vêm a seguir, se não trouxerem
    /// etiquetas. Só essas: medir a lista toda punha o computador a trabalhar sem pedir.
    /// </summary>
    private void ScanUpcoming()
    {
        if (_demo || _settings.ReplayGain == ReplayGainMode.Off || !_settings.LoudnessScan)
            return;
        var current = PlayingTrack ?? _playlist.Current;
        var tracks = new List<Track>();
        if (current is not null)
            tracks.Add(current);
        tracks.AddRange(_playlist.Upcoming(current, 5));
        _loudness.Request(tracks);
    }

    private void ToggleStopAfter()
    {
        _engine.StopAfterCurrent = !_engine.StopAfterCurrent;
        ShowStatus(_engine.StopAfterCurrent ? Loc.T("StopAfterOn") : "");
        Refresh(force: true);
    }

    // ── Mostrador ─────────────────────────────────────────────────────────────

    private string? _transientStatus;
    private long _transientUntil;

    /// <summary>Mensagem curta no mostrador (volume, balanço…), que some sozinha.</summary>
    private void ShowStatus(string text)
    {
        _transientStatus = text;
        _transientUntil = Environment.TickCount64 + 1500;
        Refresh();
    }

    private void EnsureFrameLoop()
    {
        if (_frameLoop)
            return;
        _frameLoop = true;
        _lastFrame = TimeSpan.Zero;
        RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : (time - _lastFrame).TotalSeconds;
        _lastFrame = time;
        var snap = _engine.Snapshot();
        var active = snap.State == PlayState.Playing;

        if (active && !_demo)
            _engine.CopyRecent(_analyzer.Samples);
        if (Vis.Mode != VisMode.Off || EqPanel.IsVisible)
        {
            _analyzer.SetBarCount(Vis.WantedBars());
            _analyzer.Update(_engine.SampleRate, dt, active);
            Vis.InvalidateVisual();
            if (EqPanel.IsVisible)
                Graph.InvalidateVisual();
        }
        if (active)
        {
            TitleMarquee.Tick(dt);
            MiniMarquee.Tick(dt);
        }
        Refresh(snap);

        if (active || !_analyzer.IsSilent)
            RequestAnimationFrame(OnFrame);
        else
            _frameLoop = false;
    }

    private void Refresh(bool force = false)
    {
        if (force)
            _shownState = (PlayState)(-1);
        Refresh(_engine.Snapshot());
    }

    private void Refresh(EngineSnapshot snap)
    {
        // Passagem sem silêncio para a faixa seguinte: a lista segue o que se ouve.
        if (snap.Segment?.Item.Tag is Track playing && playing != _playlist.Current)
        {
            _playlist.SetCurrent(playing);
            if (!PlaylistBox.IsPointerOver)
                ScrollTo(playing);
        }
        var track = snap.Segment?.Item.Tag as Track ?? _playlist.Current;

        if (track != _shownTrack || snap.State != _shownState)
        {
            if (_shownTrack is not null)
                _shownTrack.PropertyChanged -= ShownTrackChanged;
            _shownTrack = track;
            if (track is not null)
                track.PropertyChanged += ShownTrackChanged;
            _shownState = snap.State;
            ShowTrackText();
            ShowCover(track);
            ShowWaveform(track);
            LoadLyrics(track);
            if (track != _loopTrack)
            {
                _loopTrack = track;
                _loopA = null;
            }
            ShowLoopMarks();
        }

        var position = snap.State == PlayState.Stopped ? (_settings.Resume ? _resumeAt : 0) : snap.Position;
        var duration = track?.Duration ?? 0;
        if (Seek.IsDragging && duration > 0)
            position = Seek.Value * duration;
        if (duration > 0)
            position = Math.Min(position, duration);
        var time = _settings.TimeRemaining && duration > 0
            ? "-" + Format.Clock(duration - position)
            : Format.Clock(position);
        if (TimeText.Text != time)
        {
            TimeText.Text = time;
            MiniTime.Text = time;
        }
        if (!Seek.IsDragging)
            Seek.Value = duration > 0 ? position / duration : 0;
        Seek.IsEnabled = duration > 0 && snap.State != PlayState.Stopped;

        var glyph = snap.State switch { PlayState.Playing => GlyphKind.Play, PlayState.Paused => GlyphKind.Pause, _ => GlyphKind.Stop };
        StateGlyph.Kind = glyph;
        MiniState.Kind = glyph;
        MiniPlayGlyph.Kind = snap.State == PlayState.Playing ? GlyphKind.Pause : GlyphKind.Play;

        string status;
        if (_transientStatus is not null && Environment.TickCount64 < _transientUntil)
            status = _transientStatus;
        else if (_ffmpeg is null)
            status = Loc.T("NoFfmpeg");
        else if (_engine.OutputError is { } outputError)
            status = outputError == "openal" ? Loc.T("NoOpenAl") : $"{Loc.T("NoOutput")}: {outputError}";
        else if (_lastError is not null && snap.State == PlayState.Stopped)
            status = _lastError;
        else if (snap.State == PlayState.Paused)
            status = Loc.T("Paused");
        else if (snap.State == PlayState.Stopped)
            status = track is null ? Loc.T("Idle") : Loc.T("Stopped");
        else
            status = LyricLine(track, position) ?? AlbumLine(track);
        if (StatusText.Text != status)
            StatusText.Text = status;

        SetIndicator(IndEq, _settings.Eq.Enabled);
        SetIndicator(IndShuffle, _settings.Shuffle);
        SetIndicator(IndRepeat, _settings.Repeat != RepeatMode.Off);
        IndRepeat.Text = _settings.Repeat == RepeatMode.One ? "REP 1" : "REP";
        SetIndicator(IndRg, _settings.ReplayGain != ReplayGainMode.Off && track is not null
            && !(double.IsNaN(track.TrackGain) && double.IsNaN(track.AlbumGain)));
        SetIndicator(IndStopAfter, _engine.StopAfterCurrent);
        SetIndicator(IndLimit, _engine.LimiterActive && snap.State == PlayState.Playing);
    }

    private static void SetIndicator(Control indicator, bool on) => indicator.Classes.Set("on", on);

    private static string AlbumLine(Track? track)
    {
        if (track is null)
            return "";
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(track.Album))
            parts.Add(track.Album!);
        if (track.Year > 0)
            parts.Add(track.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(track.Genre))
            parts.Add(track.Genre!);
        return string.Join(" · ", parts);
    }

    private void ShownTrackChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Track.Display) or nameof(Track.Info) or nameof(Track.Duration) or nameof(Track.Album))
        {
            ShowTrackText();
            UpdateSessionTrack();
        }
    }

    private void ShowTrackText()
    {
        var track = _shownTrack;
        if (track is null)
        {
            TitleMarquee.Text = "Fita · " + Loc.T("Tagline");
            MiniMarquee.Text = "Fita";
            InfoText.Text = "";
            Title = "Fita";
            return;
        }
        var duration = track.Duration > 0 ? $" ({Format.Time(track.Duration)})" : "";
        TitleMarquee.Text = $"{track.Number}. {track.Display}{duration}";
        MiniMarquee.Text = track.Display;
        var extras = PlaybackExtras();
        // À frente, para não ficar cortado quando a linha não cabe.
        InfoText.Text = extras.Length == 0 ? track.Info : track.Info.Length == 0 ? extras : $"{extras} · {track.Info}";
        Title = $"{track.Display} · Fita";
    }

    private void SlowTick()
    {
        // Faixas que não se leram: marcadas na lista, e a mensagem fica no mostrador.
        while (_engine.Failures.TryDequeue(out var failure))
        {
            if (failure.Item.Tag is Track t)
            {
                t.Failed = true;
                _lastError = Loc.F("CantRead", t.FileName);
                Log.Write($"Não consegui ler {t.Path}: {failure.Message}");
            }
        }

        var ended = _engine.EndedCount;
        if (ended != _lastEnded)
        {
            _lastEnded = ended;
            _resumeAt = 0;
            // A leitura vai ~2 s à frente: uma faixa juntada nesses últimos segundos chegou
            // depois de a lista ter sido dada por acabada. Se for o caso, segue-se para ela.
            var last = _playlist.Current;
            if (!_engine.EndedByStopAfter && last is { Failed: false }
                && _playlist.After(last, automatic: true) is { } late && late != last)
                PlayTrack(late);
            else
                Refresh(force: true);
        }

        if (_engine.ConsumeRestartRequest())
        {
            var snap = _engine.Snapshot();
            if (snap.Segment is not null)
                _engine.Play(snap.Segment.Item, snap.Position, snap.State == PlayState.Paused);
        }

        var state = _engine.Snapshot().State;
        // No Winamp o tempo pisca em pausa.
        _blink++;
        TimeText.Opacity = state == PlayState.Paused && _blink % 4 >= 2 ? 0.25 : 1;
        if (state == PlayState.Playing)
        {
            _lastError = null;
            EnsureFrameLoop();
        }
        SyncSystem(_engine.Snapshot());
        SleepTick();
        if (_blink % 8 == 0)
            ScanUpcoming();
        // As definições gravam-se também a meio (a cada ~10 s, se mudaram): se a app for fechada
        // à força, não se perde o tema, o volume nem o EQ.
        if (_blink % 40 == 0 && !_demo)
            SettingsStore.Save(_settings);
        Refresh();
    }

    private void OnThemeChanged()
    {
        var t = ThemeManager.Current;
        Classes.Set("sink", t.PressSinks);
        Classes.Set("lift", t.HoverLifts);
        Classes.Set("motion", t.Motion);
        Classes.Set("bevel", t.Slider == SliderLook.Bevel);
        ShowRepeat();
        ApplyWindowCorners();
        Vis.InvalidateVisual();
        Graph.InvalidateVisual();
        EnsureFrameLoop();
    }

    private void OnLanguageChanged()
    {
        foreach (var t in _playlist.Items)
            if (t.MetadataLoaded)
                t.RefreshInfo();
        ShowRepeat();
        ShowTrackText();
        UpdateFooter();
        BalanceBox.Value = BalanceBox.Value;
        RefreshEqTexts();
        Refresh(force: true);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        SaveAll();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _slowTimer.Stop();
        _engine.Dispose();
        _tags.Dispose();
        _loudness.Dispose();
        StopSystemIntegration();
    }

    private void SaveAll()
    {
        if (_demo)
            return;
        RememberWindow();
        var snap = _engine.Snapshot();
        var position = snap.State == PlayState.Stopped ? _resumeAt : snap.Position;
        PlaylistStore.Save(_playlist.Items, _playlist.Current, position);
        SettingsStore.Save(_settings);
    }

    private void LoadPlaylist()
    {
        var (tracks, current, position) = PlaylistStore.Load();
        _playlist.Items.ReplaceAll(tracks);
        if (current >= 0 && current < tracks.Count)
        {
            _playlist.SetCurrent(tracks[current]);
            _resumeAt = position;
        }
        _tags.Enqueue(tracks);
        UpdateFooter();
        if (_playlist.Current is { } t)
            Dispatcher.UIThread.Post(() => ScrollTo(t), DispatcherPriority.Background);
    }

    /// <summary>Lista e mostrador de exemplo, para as capturas do README.</summary>
    private void LoadDemo()
    {
        string[][] demo =
        [
            ["Erik Satie", "Gymnopédie No. 1", "Trois Gymnopédies", "212"],
            ["Claude Debussy", "Clair de lune", "Suite bergamasque", "301"],
            ["Johann Sebastian Bach", "Prelude in C major, BWV 846", "The Well-Tempered Clavier", "139"],
            ["Frédéric Chopin", "Nocturne Op. 9 No. 2", "Nocturnes", "272"],
            ["Maurice Ravel", "Pavane pour une infante défunte", "Pavane", "389"],
            ["Ludwig van Beethoven", "Für Elise", "Bagatelle No. 25", "175"],
            ["Wolfgang Amadeus Mozart", "Piano Sonata No. 16, I. Allegro", "Sonata facile", "263"],
            ["Antonio Vivaldi", "Spring, I. Allegro", "The Four Seasons", "205"],
            ["Gustav Holst", "Jupiter, the Bringer of Jollity", "The Planets", "468"],
            ["Edvard Grieg", "In the Hall of the Mountain King", "Peer Gynt", "152"],
            ["Camille Saint-Saëns", "The Swan", "Le carnaval des animaux", "184"],
            ["Pyotr Ilyich Tchaikovsky", "Dance of the Sugar Plum Fairy", "The Nutcracker", "116"],
        ];
        var tracks = demo.Select((d, i) =>
        {
            var t = new Track($"/music/{d[0]}/{d[2]}/{i + 1:00} {d[1]}.flac")
            {
                Artist = d[0], Title = d[1], Album = d[2], Duration = double.Parse(d[3], System.Globalization.CultureInfo.InvariantCulture),
                Codec = "FLAC", SampleRate = 44100, BitsPerSample = 16, Channels = 2, Bitrate = 912 - i * 17, MetadataLoaded = true,
            };
            return t;
        }).ToList();
        _playlist.Items.ReplaceAll(tracks);
        _playlist.SetCurrent(tracks[1]);
        _resumeAt = 107;
        UpdateFooter();
    }

    /// <summary>Para as capturas: um espectro plausível e o estado "a tocar" no mostrador.</summary>
    public void DemoFrame(double seconds)
    {
        var rnd = new Random(7);
        _analyzer.SetBarCount(Vis.WantedBars());
        for (var i = 0; i < SpectrumAnalyzer.Size; i++)
        {
            var x = i / 48000.0;
            _analyzer.Samples[i] = (float)(0.35 * Math.Sin(2 * Math.PI * 110 * x) + 0.2 * Math.Sin(2 * Math.PI * 220 * x + 1)
                + 0.12 * Math.Sin(2 * Math.PI * 660 * x) + 0.07 * Math.Sin(2 * Math.PI * 2400 * x) + 0.04 * Math.Sin(2 * Math.PI * 7000 * x)
                + 0.02 * (rnd.NextDouble() - 0.5));
        }
        for (var k = 0; k < 30; k++)
            _analyzer.Update(48000, 1 / 60.0, true);
        Vis.InvalidateVisual();
        Graph.InvalidateVisual();
        TimeText.Text = Format.Clock(seconds);
        StateGlyph.Kind = GlyphKind.Play;
        StatusText.Text = AlbumLine(_playlist.Current);
        Seek.IsEnabled = true;
        Seek.Value = seconds / (_playlist.Current?.Duration ?? 300);
        SetIndicator(IndEq, _settings.Eq.Enabled);
    }
}
