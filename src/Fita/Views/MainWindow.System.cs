using Fita.Audio;
using Fita.Controls;
using Fita.Library;
using Fita.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Fita.Views;

/// <summary>
/// A Fita vista de fora: o painel multimédia do sistema (SMTC no Windows, MPRIS no Linux), as
/// teclas multimédia, o botão na barra de tarefas do Windows e a capa da faixa (no mostrador e
/// no painel).
/// </summary>
public partial class MainWindow
{
    private IMediaSession? _session;
    private WindowsMediaKeys? _mediaKeys;
    private WindowsTaskbar? _taskbar;
    private PlayState _sessionState = (PlayState)(-1);
    private int _sessionTicks;
    private Track? _coverTrack;
    private byte[]? _sessionCover;
    private CancellationTokenSource? _coverCancel;

    /// <summary>A posição vai para o sistema a cada tantos passos lentos (1 s).</summary>
    private const int PositionEvery = 4;
    /// <summary>Lado da capa no painel do sistema (o painel não precisa de mais).</summary>
    private const int SessionCoverSize = 300;

    private void StartSystemIntegration()
    {
        if (_demo)
            return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        _session = MediaSession.Create(handle,
            command => Dispatcher.UIThread.Post(() => OnMediaCommand(command)),
            seconds => Dispatcher.UIThread.Post(() => SeekTo(seconds)));
        // Sem o painel do Windows (versões antigas, ou se falhar), as teclas vão por RegisterHotKey.
        if (_session is null && OperatingSystem.IsWindows())
            _mediaKeys = WindowsMediaKeys.Start(key => Dispatcher.UIThread.Post(() => OnMediaCommand(key switch
            {
                WindowsMediaKeys.MediaKey.Next => MediaCommand.Next,
                WindowsMediaKeys.MediaKey.Previous => MediaCommand.Previous,
                WindowsMediaKeys.MediaKey.Stop => MediaCommand.Stop,
                _ => MediaCommand.PlayPause,
            })));
        if (OperatingSystem.IsWindows() && handle != IntPtr.Zero)
        {
            _taskbar = new WindowsTaskbar(handle, OnMediaCommand, TaskbarIcon) { ShowProgress = _settings.TaskbarProgress };
            Win32Properties.AddWndProcHookCallback(this, TaskbarHook);
        }
        _coverTrack = null;
        ShowCover(_shownTrack);
        SyncSystem(_engine.Snapshot(), force: true);
    }

    private void StopSystemIntegration()
    {
        if (_taskbar is not null)
        {
            Win32Properties.RemoveWndProcHookCallback(this, TaskbarHook);
            _taskbar.Dispose();
            _taskbar = null;
        }
        _coverCancel?.Cancel();
        _session?.Dispose();
        _session = null;
        _mediaKeys?.Dispose();
        _mediaKeys = null;
    }

    private void OnMediaCommand(MediaCommand command)
    {
        switch (command)
        {
            case MediaCommand.Play: Play(); break;
            case MediaCommand.Pause:
                if (_engine.Snapshot().State == PlayState.Playing)
                    PauseToggle();
                break;
            case MediaCommand.PlayPause: PauseOrPlay(); break;
            case MediaCommand.Stop: Stop(); break;
            case MediaCommand.Next: Next(); break;
            case MediaCommand.Previous: Previous(); break;
            case MediaCommand.Raise:
                if (WindowState == Avalonia.Controls.WindowState.Minimized)
                    WindowState = Avalonia.Controls.WindowState.Normal;
                Activate();
                break;
        }
    }

    private void SeekTo(double seconds)
    {
        var snap = _engine.Snapshot();
        if (snap.State == PlayState.Stopped || snap.Segment?.Item.Tag is not Track t)
            return;
        if (t.Duration > 0)
            seconds = Math.Min(seconds, t.Duration - 0.5);
        _engine.Seek(Math.Max(0, seconds));
        _sessionTicks = PositionEvery;
    }

    /// <summary>
    /// Estado e posição para o sistema. Chamado a cada passo lento (¼ s): o estado vai logo, a
    /// posição de segundo a segundo (o painel anima a barra sozinho entre atualizações).
    /// </summary>
    private void SyncSystem(EngineSnapshot snap, bool force = false)
    {
        _mediaKeys?.SetActive(snap.State != PlayState.Stopped);
        _taskbar?.SetState(snap.State, _shownTrack is not null);
        var duration = (snap.Segment?.Item.Tag as Track ?? _shownTrack)?.Duration ?? 0;
        var position = snap.State == PlayState.Stopped ? 0 : snap.Position;
        if (snap.State != _sessionState || force)
        {
            _sessionState = snap.State;
            _session?.SetState(snap.State);
            _sessionTicks = PositionEvery;
        }
        if (++_sessionTicks >= PositionEvery)
        {
            _sessionTicks = 0;
            _session?.SetPosition(position, duration);
            _taskbar?.SetProgress(position, duration);
        }
    }

    private IntPtr TaskbarHook(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbar?.HandleMessage(message, wParam) == true)
            handled = true;
        return IntPtr.Zero;
    }

    /// <summary>Os ícones da miniatura são os glifos dos botões de transporte, a branco.</summary>
    private unsafe IntPtr TaskbarIcon(WindowsTaskbar.GlyphIcon icon)
    {
        var size = WindowsTaskbar.IconSize(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        var glyph = new Glyph
        {
            Kind = icon switch
            {
                WindowsTaskbar.GlyphIcon.Previous => GlyphKind.Prev,
                WindowsTaskbar.GlyphIcon.Pause => GlyphKind.Pause,
                WindowsTaskbar.GlyphIcon.Next => GlyphKind.Next,
                _ => GlyphKind.Play,
            },
            Foreground = Brushes.White,
        };
        glyph.Measure(new Size(size, size));
        glyph.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        bitmap.Render(glyph);
        var pixels = new byte[size * size * 4];
        fixed (byte* p = pixels)
            bitmap.CopyPixels(new PixelRect(0, 0, size, size), (IntPtr)p, pixels.Length, size * 4);
        // Vem com o alfa pré-multiplicado; o ícone quer branco com alfa.
        for (var i = 0; i < pixels.Length; i += 4)
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
        return WindowsTaskbar.CreateIcon(size, pixels);
    }

    private void SetTaskbarProgress(bool on)
    {
        _settings.TaskbarProgress = on;
        if (_taskbar is null)
            return;
        _taskbar.ShowProgress = on;
        _sessionTicks = PositionEvery;
    }

    // ── Capa ──────────────────────────────────────────────────────────────────

    /// <summary>A faixa mostrada mudou: procura a capa em segundo plano e avisa o sistema.</summary>
    private void ShowCover(Track? track)
    {
        if (track == _coverTrack)
            return;
        _coverTrack = track;
        _sessionCover = null;
        _coverCancel?.Cancel();
        _coverCancel = null;
        SetCoverImage(null);
        // O texto vai logo; a capa chega quando estiver lida.
        UpdateSessionTrack();
        if (track is null || _demo)
            return;
        var cancel = _coverCancel = new CancellationTokenSource();
        var path = track.Path;
        var wantSession = _session is not null;
        _ = Task.Run(() => LoadCover(path, wantSession)).ContinueWith(t =>
        {
            if (t.Exception is { } ex)
                Log.Write($"Capa de {path}: {ex.InnerException?.Message}");
            var cover = t.IsCompletedSuccessfully ? t.Result : null;
            if (cancel.IsCancellationRequested || track != _coverTrack)
            {
                cover?.Display.Dispose();
                return;
            }
            if (cover is null)
                return;
            SetCoverImage(cover.Display);
            _sessionCover = cover.ForSession;
            UpdateSessionTrack();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private sealed record LoadedCover(Bitmap Display, byte[]? ForSession);

    private static LoadedCover? LoadCover(string path, bool forSession)
    {
        var bytes = CoverArt.Find(path);
        if (bytes is null)
            return null;
        using var full = new Bitmap(new MemoryStream(bytes));
        // Pequena para o mostrador (o dobro, para ecrãs com escala), média para o painel.
        var display = full.CreateScaledBitmap(Fit(full.PixelSize, 220));
        byte[]? png = null;
        if (forSession)
        {
            using var scaled = full.CreateScaledBitmap(Fit(full.PixelSize, SessionCoverSize));
            using var stream = new MemoryStream();
            scaled.Save(stream);
            png = stream.ToArray();
        }
        return new LoadedCover(display, png);
    }

    /// <summary>Título, artista e capa para o painel do sistema (também quando as etiquetas chegam).</summary>
    private void UpdateSessionTrack() =>
        _session?.SetTrack(_coverTrack is { } t ? NowPlayingOf(t, _sessionCover) : null);

    private static Avalonia.PixelSize Fit(Avalonia.PixelSize size, int side)
    {
        var scale = Math.Min(1.0, side / (double)Math.Max(size.Width, size.Height));
        return new Avalonia.PixelSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale)));
    }

    private void SetCoverImage(Bitmap? image)
    {
        var old = CoverImage.Source as Bitmap;
        CoverImage.Source = image;
        CoverImage.IsVisible = image is not null && _settings.ShowCover;
        if (old != image)
            old?.Dispose();
    }

    private void SetShowCover(bool on)
    {
        _settings.ShowCover = on;
        CoverImage.IsVisible = on && CoverImage.Source is not null;
    }

    /// <summary>Para as capturas: uma capa desenhada, sem ler ficheiros.</summary>
    public void DemoCover(Bitmap cover) => SetCoverImage(cover);

    private static NowPlaying NowPlayingOf(Track track, byte[]? cover) =>
        new(track.Path, string.IsNullOrWhiteSpace(track.Title) ? track.FileName : track.Title!, track.Artist, track.Album,
            track.TrackNumber, track.Duration, cover);
}
