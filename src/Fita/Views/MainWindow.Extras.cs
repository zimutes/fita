using System.Globalization;
using Fita.Audio;
using Fita.Library;
using Avalonia.Threading;

namespace Fita.Views;

/// <summary>
/// Extras da reprodução que não ocupam espaço: velocidade, ciclo A-B (marcas na barra de
/// posição), temporizador para dormir e letra sincronizada (na linha do álbum). O que está
/// ligado aparece no começo da linha do formato, no mostrador.
/// </summary>
public partial class MainWindow
{
    // ── Velocidade ────────────────────────────────────────────────────────────

    public static readonly double[] Speeds = [0.5, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 2];

    private void SetSpeed(double factor, bool keepPitch)
    {
        _settings.Speed = factor;
        _settings.KeepPitch = keepPitch;
        _engine.Speed = new PlaybackSpeed(factor, keepPitch);
        ShowTrackText();
        ShowStatus(Math.Abs(factor - 1) < 0.0005 ? Loc.T("SpeedNormal") : $"{Loc.T("Speed")} {SpeedText(factor)}");
    }

    private static string SpeedText(double factor) => factor.ToString("0.##", Loc.Culture) + "×";

    // ── Ciclo A-B ─────────────────────────────────────────────────────────────

    private double? _loopA;

    /// <summary>Tecla A: a primeira marca o A, a segunda o B (e o ciclo começa), a terceira desliga.</summary>
    private void CycleLoop()
    {
        var snap = _engine.Snapshot();
        if (_engine.Loop is not null)
        {
            _engine.ClearLoop();
            _loopA = null;
            ShowStatus(Loc.T("LoopOff"));
        }
        else if (snap.State == PlayState.Stopped || snap.Segment is null)
            return;
        else if (_loopA is not { } a)
        {
            _loopA = snap.Position;
            ShowStatus($"A {Format.Clock(snap.Position)}");
        }
        else if (snap.Position - a >= 0.5)
        {
            _engine.SetLoop(a, snap.Position);
            ShowStatus($"A–B {Format.Clock(a)}–{Format.Clock(snap.Position)}");
        }
        else
        {
            // B antes do A (ou colado a ele): recomeça a marcação.
            _loopA = snap.Position;
            ShowStatus($"A {Format.Clock(snap.Position)}");
        }
        ShowLoopMarks();
    }

    private void ShowLoopMarks()
    {
        var duration = _shownTrack?.Duration ?? 0;
        var loop = _engine.Loop;
        if (loop is null && _loopA is not null && _engine.Snapshot().State == PlayState.Stopped)
            _loopA = null;
        double? Fraction(double? seconds) => seconds is { } s && duration > 0 ? Math.Clamp(s / duration, 0, 1) : null;
        Seek.MarkA = Fraction(loop?.A ?? _loopA);
        Seek.MarkB = Fraction(loop?.B);
    }

    private string LoopMenuText() =>
        _engine.Loop is not null ? Loc.T("LoopClear") : _loopA is null ? Loc.T("LoopSetA") : Loc.T("LoopSetB");

    // ── Temporizador para dormir ──────────────────────────────────────────────

    public static readonly int[] SleepMinutes = [15, 30, 45, 60, 90];
    private const double SleepFadeSeconds = 10;
    private long? _sleepAt;
    private int? _sleepChoice;

    private void SetSleep(int? minutes)
    {
        _sleepChoice = minutes;
        _sleepAt = minutes is { } m ? Environment.TickCount64 + m * 60_000L : null;
        _engine.Volume = OutputGain;
        ShowTrackText();
        ShowStatus(minutes is { } min ? Loc.F("SleepIn", min) : Loc.T("SleepOff"));
    }

    /// <summary>A cada passo lento: perto da hora o volume desce devagar, e à hora para.</summary>
    private void SleepTick()
    {
        if (_sleepAt is not { } at)
            return;
        var left = (at - Environment.TickCount64) / 1000.0;
        if (left <= 0)
        {
            Stop();
            _sleepAt = null;
            _sleepChoice = null;
            _engine.Volume = OutputGain;
            ShowTrackText();
            ShowStatus(Loc.T("SleepDone"));
            return;
        }
        if (left < SleepFadeSeconds)
            _engine.Volume = OutputGain * (float)(left / SleepFadeSeconds);
        // O que falta, de minuto a minuto, na linha do formato.
        if (_blink % 20 == 0)
            ShowTrackText();
    }

    /// <summary>O que está ligado e muda o que se ouve: vai no fim da linha do formato.</summary>
    private string PlaybackExtras()
    {
        var parts = new List<string>();
        if (Math.Abs(_settings.Speed - 1) > 0.0005)
            parts.Add(SpeedText(_settings.Speed) + (_settings.KeepPitch ? "" : " " + Loc.T("Tape")));
        if (_sleepAt is { } at)
            parts.Add("zZ " + Math.Max(1, (int)Math.Ceiling((at - Environment.TickCount64) / 60000.0)).ToString(CultureInfo.InvariantCulture) + " min");
        return parts.Count == 0 ? "" : string.Join(" · ", parts);
    }

    // ── Letra ─────────────────────────────────────────────────────────────────

    private Track? _lyricsTrack;
    private Lyrics? _lyrics;
    private CancellationTokenSource? _lyricsCancel;

    private void LoadLyrics(Track? track)
    {
        if (track == _lyricsTrack)
            return;
        _lyricsTrack = track;
        _lyrics = null;
        _lyricsCancel?.Cancel();
        _lyricsCancel = null;
        if (track is null || _demo || !_settings.ShowLyrics)
            return;
        var cancel = _lyricsCancel = new CancellationTokenSource();
        _ = Task.Run(() => Lyrics.Find(track)).ContinueWith(t =>
        {
            if (!cancel.IsCancellationRequested && track == _lyricsTrack && t.IsCompletedSuccessfully)
                _lyrics = t.Result;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void SetShowLyrics(bool on)
    {
        _settings.ShowLyrics = on;
        var track = _lyricsTrack;
        _lyricsTrack = null;
        LoadLyrics(on ? track : null);
        if (!on)
            _lyricsTrack = track;
        Refresh(force: true);
    }

    /// <summary>A linha da letra neste momento, ou null se não houver letra (fica o álbum).</summary>
    private string? LyricLine(Track? track, double position)
    {
        if (_lyrics is not { } lyrics || track != _lyricsTrack || !_settings.ShowLyrics)
            return null;
        var line = lyrics.LineAt(position);
        return line.Length > 0 ? "♪ " + line : null;
    }
}
