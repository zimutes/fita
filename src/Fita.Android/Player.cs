using Fita.Audio;
using Fita.Library;
using Avalonia.Threading;

namespace Fita;

/// <summary>
/// O leitor no telemóvel: o motor, a lista, as etiquetas e as definições. Há um só por processo,
/// partilhado pela vista (que pode ser destruída e recriada pelo Android) e pelo serviço que
/// mantém a música a tocar com o ecrã desligado. Tudo o que mexe na lista corre na thread da
/// interface.
/// </summary>
public sealed class Player
{
    private static Player? _current;

    public static Player Current => _current ?? throw new InvalidOperationException("Player not started.");

    public static bool Started => _current is not null;

    public AudioEngine Engine { get; }
    public Playlist Playlist { get; } = new();
    public MetadataLoader Tags { get; }
    public AppSettings Settings => App.Settings;
    public string? Ffmpeg { get; }

    /// <summary>Mudou o estado (tocar, pausa, faixa): para a notificação e para a vista.</summary>
    public event Action? Changed;

    public string? LastError { get; private set; }

    private double _resumeAt;
    private int _lastEnded;
    private PlayState _lastState;
    private Track? _lastTrack;
    private readonly DispatcherTimer _timer;
    private int _ticks;

    private Player(string? ffmpeg, string? ffprobe)
    {
        Ffmpeg = ffmpeg;
        AudioEngine.OutputFactory = _ => new AudioTrackOutput();
        Engine = new AudioEngine
        {
            FfmpegPath = ffmpeg,
            Volume = VolumeGain(Settings.Volume),
            Balance = (float)Settings.Balance,
            CrossfadeSeconds = Settings.Crossfade,
        };
        Engine.NextItem = NextFromDecoder;
        Engine.ConfigureEq(Settings.Eq);
        Tags = new MetadataLoader(() => ffprobe);
        Playlist.Shuffle = Settings.Shuffle;
        Playlist.Repeat = Settings.Repeat;

        var (tracks, current, position) = PlaylistStore.Load();
        Playlist.Items.ReplaceAll(tracks);
        if (current >= 0 && current < tracks.Count)
        {
            Playlist.SetCurrent(tracks[current]);
            _resumeAt = position;
        }
        Tags.Enqueue(tracks);
        Playlist.Changed += () => _saveSoon = true;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
    }

    public static Player Start(string? ffmpeg, string? ffprobe) => _current ??= new Player(ffmpeg, ffprobe);

    /// <summary>Controlo de 0 a 100 → ganho (ao quadrado, como no desktop).</summary>
    public static float VolumeGain(double position) => (float)Math.Pow(Math.Clamp(position, 0, 100) / 100, 2);

    public Track? PlayingTrack => Engine.Snapshot().Segment?.Item.Tag as Track ?? Playlist.Current;

    public PlayState State => Engine.Snapshot().State;

    public PlayItem ToPlayItem(Track track)
    {
        var db = Settings.ReplayGain switch
        {
            ReplayGainMode.Track => double.IsNaN(track.TrackGain) ? track.AlbumGain : track.TrackGain,
            ReplayGainMode.Album => double.IsNaN(track.AlbumGain) ? track.TrackGain : track.AlbumGain,
            _ => double.NaN,
        };
        var gain = double.IsNaN(db) ? 1f : (float)Math.Clamp(Math.Pow(10, db / 20), 0.05, 4);
        return new PlayItem(track, track.Path, gain, track.Start, track.End);
    }

    private static bool SameAlbum(Track? a, Track? b) =>
        a is not null && b is not null && !string.IsNullOrWhiteSpace(a.Album)
        && string.Equals(a.Album, b.Album, StringComparison.CurrentCultureIgnoreCase)
        && Library.Playlist.PathComparer.Equals(Path.GetDirectoryName(a.Path), Path.GetDirectoryName(b.Path));

    /// <summary>Na thread de descodificação: pede à interface a faixa que se segue (sem silêncio entre elas).</summary>
    private PlayItem? NextFromDecoder(PlayItem current, CancellationToken ct)
    {
        try
        {
            var task = Dispatcher.UIThread.InvokeAsync(() =>
            {
                var previous = current.Tag as Track;
                var next = Playlist.After(previous, automatic: true);
                if (next is null)
                    return null;
                Tags.Prioritize(next);
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

    // ── Comandos ──────────────────────────────────────────────────────────────

    public void PlayTrack(Track track, double start = 0)
    {
        if (Ffmpeg is null)
        {
            LastError = Loc.T("NoFfmpeg");
            return;
        }
        Tags.Prioritize(track);
        track.Failed = false;
        Playlist.SetCurrent(track);
        Engine.Play(ToPlayItem(track), start);
        _resumeAt = 0;
        LastError = null;
        Notify();
    }

    public void Play()
    {
        var snap = Engine.Snapshot();
        if (snap.State == PlayState.Paused)
        {
            Engine.Resume();
            Notify();
            return;
        }
        if (snap.State == PlayState.Playing)
            return;
        var track = Playlist.Current ?? Playlist.After(null, automatic: false);
        if (track is not null)
            PlayTrack(track, track == Playlist.Current && Settings.Resume ? _resumeAt : 0);
    }

    public void PlayPause()
    {
        if (State == PlayState.Playing)
            Pause();
        else
            Play();
    }

    public void Pause()
    {
        if (State != PlayState.Playing)
            return;
        Engine.Pause();
        _resumeAt = Engine.Snapshot().Position;
        Notify();
    }

    public void Stop()
    {
        Engine.Stop();
        _resumeAt = 0;
        Notify();
    }

    public void Next()
    {
        var from = PlayingTrack;
        if (Playlist.After(from, automatic: false) is not { } next)
            return;
        if (State == PlayState.Stopped)
        {
            Playlist.SetCurrent(next);
            _resumeAt = 0;
            Notify();
        }
        else
            PlayTrack(next);
    }

    public void Previous()
    {
        var snap = Engine.Snapshot();
        // Como nos leitores de telemóvel: a meio da faixa, "anterior" volta ao começo dela.
        if (snap.State != PlayState.Stopped && snap.Position > 4 && snap.Segment?.Item.Tag is Track playing)
        {
            Engine.Seek(0);
            Notify();
            return;
        }
        if (Playlist.Before(PlayingTrack) is not { } previous)
            return;
        if (snap.State == PlayState.Stopped)
        {
            Playlist.SetCurrent(previous);
            _resumeAt = 0;
            Notify();
        }
        else
            PlayTrack(previous);
    }

    public void Seek(double seconds)
    {
        var snap = Engine.Snapshot();
        if (snap.State == PlayState.Stopped)
        {
            _resumeAt = Math.Max(0, seconds);
            return;
        }
        Engine.Seek(Math.Max(0, seconds));
        Notify();
    }

    /// <summary>Onde está a faixa (ou onde vai recomeçar, parada).</summary>
    public double Position
    {
        get
        {
            var snap = Engine.Snapshot();
            return snap.State == PlayState.Stopped ? (Settings.Resume ? _resumeAt : 0) : snap.Position;
        }
    }

    public void SetVolume(double value)
    {
        value = Math.Clamp(value, 0, 100);
        Settings.Volume = value;
        Engine.Volume = VolumeGain(value);
    }

    public void SetShuffle(bool on)
    {
        Settings.Shuffle = on;
        Playlist.Shuffle = on;
    }

    public RepeatMode CycleRepeat()
    {
        var next = Settings.Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
        Settings.Repeat = next;
        Playlist.Repeat = next;
        return next;
    }

    /// <summary>Substitui a lista e toca a partir de <paramref name="first"/>.</summary>
    public void PlayList(IReadOnlyList<Track> tracks, Track first)
    {
        Playlist.Items.ReplaceAll(tracks);
        Tags.Enqueue(tracks);
        PlayTrack(first);
    }

    /// <summary>Junta ao fim da lista (ou logo a seguir à que toca).</summary>
    public void Enqueue(IReadOnlyList<Track> tracks, bool next = false)
    {
        var fresh = tracks.Select(t => new Track(t.Path)
        {
            Title = t.Title, Artist = t.Artist, Album = t.Album, TrackNumber = t.TrackNumber, Duration = t.Duration,
        }).ToList();
        int? at = next && PlayingTrack is { } playing ? Playlist.IndexOf(playing) + 1 : null;
        Playlist.Add(fresh, at);
        Tags.Enqueue(fresh);
    }

    // ── Estado ────────────────────────────────────────────────────────────────

    private bool _saveSoon;

    private void Notify() => Changed?.Invoke();

    private void Tick()
    {
        _ticks++;
        while (Engine.Failures.TryDequeue(out var failure))
        {
            if (failure.Item.Tag is Track t)
            {
                t.Failed = true;
                LastError = Loc.F("CantRead", t.FileName);
                Log.Write($"Não consegui ler {t.Path}: {failure.Message}");
            }
        }
        var ended = Engine.EndedCount;
        if (ended != _lastEnded)
        {
            _lastEnded = ended;
            _resumeAt = 0;
            var last = Playlist.Current;
            if (!Engine.EndedByStopAfter && last is { Failed: false } && Playlist.After(last, automatic: true) is { } late && late != last)
                PlayTrack(late);
        }
        if (Engine.ConsumeRestartRequest())
        {
            var snap = Engine.Snapshot();
            if (snap.Segment is not null)
                Engine.Play(snap.Segment.Item, snap.Position, snap.State == PlayState.Paused);
        }
        // A lista segue o que se ouve (passagem sem silêncio para a seguinte).
        var playing = Engine.Snapshot();
        if (playing.Segment?.Item.Tag is Track now && now != Playlist.Current)
            Playlist.SetCurrent(now);
        if (playing.State != _lastState || PlayingTrack != _lastTrack)
        {
            _lastState = playing.State;
            _lastTrack = PlayingTrack;
            Notify();
        }
        // Grava de tempos a tempos: o Android pode matar o processo sem avisar.
        if (_saveSoon && _ticks % 8 == 0 || _ticks % 40 == 0)
        {
            _saveSoon = false;
            Save();
        }
    }

    public void Save()
    {
        var snap = Engine.Snapshot();
        var position = snap.State == PlayState.Stopped ? _resumeAt : snap.Position;
        PlaylistStore.Save(Playlist.Items, Playlist.Current, position);
        SettingsStore.Save(Settings);
    }
}
