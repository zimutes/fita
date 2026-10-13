using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Avalonia.Threading;
using Fita.Audio;
using Fita.Library;
using PlayState = Fita.Audio.PlayState;

namespace Fita;

/// <summary>
/// Mantém a música a tocar com o ecrã desligado ou a app fechada: um serviço em primeiro plano
/// com a notificação (anterior, tocar/pausa, seguinte), a sessão multimédia (ecrã de bloqueio,
/// auscultadores, relógio, carro), o foco de áudio (pausa numa chamada) e a pausa quando se
/// desligam os auscultadores. Arranca quando a música começa e sai quando pára.
/// </summary>
[Service(Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public sealed class PlaybackService : Service
{
    private const string ChannelId = "fita.playback";
    private const int NotificationId = 1;
    private const string ActionPlayPause = "org.zimutek.fita.PLAY_PAUSE";
    private const string ActionNext = "org.zimutek.fita.NEXT";
    private const string ActionPrevious = "org.zimutek.fita.PREVIOUS";
    private const string ActionStop = "org.zimutek.fita.STOP";

    private static PlaybackService? _running;
    private static bool _watching;

    private MediaSession? _session;
    private PowerManager.WakeLock? _wake;
    private AudioManager? _audio;
    private AudioFocusRequestClass? _focus;
    private FocusListener? _focusListener;
    private NoisyReceiver? _noisy;
    private bool _foreground;
    private string? _artFor;
    private Bitmap? _art;

    /// <summary>Liga o serviço ao leitor: quando a música começa, o serviço arranca.</summary>
    public static void Watch(Context context)
    {
        if (_watching)
            return;
        _watching = true;
        var app = context.ApplicationContext ?? context;
        Player.Current.Changed += () =>
        {
            if (_running is null && Player.Current.State == PlayState.Playing)
            {
                var intent = new Intent(app, typeof(PlaybackService));
                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                    app.StartForegroundService(intent);
                else
                    app.StartService(intent);
            }
        };
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        _running = this;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && GetSystemService(NotificationService) is NotificationManager nm)
        {
            var channel = new NotificationChannel(ChannelId, Loc.T("M.Channel"), NotificationImportance.Low) { Description = "Fita" };
            channel.SetShowBadge(false);
            nm.CreateNotificationChannel(channel);
        }
        _session = new MediaSession(this, "Fita");
        _session.SetCallback(new SessionCallback());
        _session.Active = true;
        _audio = GetSystemService(AudioService) as AudioManager;
        _wake = (GetSystemService(PowerService) as PowerManager)?.NewWakeLock(WakeLockFlags.Partial, "fita:tocar");
        _wake?.SetReferenceCounted(false);
        _noisy = new NoisyReceiver();
        var filter = new IntentFilter(AudioManager.ActionAudioBecomingNoisy);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            RegisterReceiver(_noisy, filter, ReceiverFlags.NotExported);
        else
            RegisterReceiver(_noisy, filter);
        Player.Current.Changed += Update;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        switch (intent?.Action)
        {
            case ActionPlayPause: Run(p => p.PlayPause()); break;
            case ActionNext: Run(p => p.Next()); break;
            case ActionPrevious: Run(p => p.Previous()); break;
            case ActionStop: Run(p => p.Stop()); break;
        }
        // O Android exige a notificação logo à entrada de um serviço em primeiro plano.
        if (!_foreground)
        {
            var notification = BuildNotification();
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, notification, Android.Content.PM.ForegroundService.TypeMediaPlayback);
            else
                StartForeground(NotificationId, notification);
            _foreground = true;
        }
        Update();
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        Player.Current.Changed -= Update;
        if (_noisy is not null)
            UnregisterReceiver(_noisy);
        AbandonFocus();
        if (_wake is { IsHeld: true })
            _wake.Release();
        _session?.Release();
        _running = null;
        base.OnDestroy();
    }

    /// <summary>Tirar a app da lista das recentes não pára a música; só se estiver parada é que o serviço sai.</summary>
    public override void OnTaskRemoved(Intent? rootIntent)
    {
        if (Player.Current.State != PlayState.Playing)
            StopSelf();
        base.OnTaskRemoved(rootIntent);
    }

    private static void Run(Action<Player> action) => Dispatcher.UIThread.Post(() => action(Player.Current));

    // ── Estado → sessão, notificação, foco, processador ──────────────────────

    private void Update()
    {
        var player = Player.Current;
        var state = player.State;
        var track = player.PlayingTrack;

        if (_session is { } session)
        {
            var metadata = new MediaMetadata.Builder()
                .PutString(MediaMetadata.MetadataKeyTitle, track is null ? "Fita" : Title(track))!
                .PutString(MediaMetadata.MetadataKeyArtist, track?.Artist ?? "")!
                .PutString(MediaMetadata.MetadataKeyAlbum, track?.Album ?? "")!
                .PutLong(MediaMetadata.MetadataKeyDuration, (long)((track?.Duration ?? 0) * 1000))!;
            if (Art(track) is { } art)
                metadata.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, art);
            session.SetMetadata(metadata.Build());
            var playbackState = state switch
            {
                PlayState.Playing => PlaybackStateCode.Playing,
                PlayState.Paused => PlaybackStateCode.Paused,
                _ => PlaybackStateCode.Stopped,
            };
            session.SetPlaybackState(new PlaybackState.Builder()
                .SetActions(PlaybackState.ActionPlay | PlaybackState.ActionPause | PlaybackState.ActionPlayPause
                    | PlaybackState.ActionSkipToNext | PlaybackState.ActionSkipToPrevious | PlaybackState.ActionStop
                    | PlaybackState.ActionSeekTo)!
                .SetState(playbackState, (long)(player.Position * 1000), state == PlayState.Playing ? 1f : 0f)!
                .Build());
        }

        if (state == PlayState.Playing)
        {
            RequestFocus();
            if (_wake is { IsHeld: false })
                _wake.Acquire();
        }
        else if (_wake is { IsHeld: true })
            _wake.Release();

        if (state == PlayState.Stopped)
        {
            AbandonFocus();
            StopForegroundCompat(remove: true);
            StopSelf();
            return;
        }
        if (GetSystemService(NotificationService) is NotificationManager nm)
            nm.Notify(NotificationId, BuildNotification());
        // Em pausa a notificação fica, mas pode ser afastada (e o Android pode fechar o processo).
        if (state == PlayState.Paused)
            StopForegroundCompat(remove: false);
        else if (!_foreground)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, BuildNotification(), Android.Content.PM.ForegroundService.TypeMediaPlayback);
            else
                StartForeground(NotificationId, BuildNotification());
            _foreground = true;
        }
    }

    private void StopForegroundCompat(bool remove)
    {
        if (!_foreground)
            return;
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
            StopForeground(remove ? StopForegroundFlags.Remove : StopForegroundFlags.Detach);
        else
            StopForeground(remove);
        _foreground = false;
    }

    private static string Title(Track t) => string.IsNullOrWhiteSpace(t.Title) ? t.FileName : t.Title!;

    private Bitmap? Art(Track? track)
    {
        if (track is null)
            return null;
        if (_artFor == track.Path)
            return _art;
        _artFor = track.Path;
        _art = null;
        try
        {
            if (CoverArt.Find(track.Path) is { } bytes)
                _art = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        }
        catch
        {
        }
        return _art;
    }

    private Notification BuildNotification()
    {
        var player = Player.Current;
        var track = player.PlayingTrack;
        var playing = player.State == PlayState.Playing;
#pragma warning disable CA1422 // o construtor sem canal é só para o Android 7
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);
#pragma warning restore CA1422
        builder.SetSmallIcon(Resource.Drawable.ic_fita)
            .SetContentTitle(track is null ? "Fita" : Title(track))
            .SetContentText(track?.Artist ?? "")
            .SetShowWhen(false)
            .SetVisibility(NotificationVisibility.Public)
            .SetOngoing(playing)
            .SetDeleteIntent(Pending(ActionStop, 4));
        if (Art(track) is { } art)
            builder.SetLargeIcon(art);
        if (PackageManager?.GetLaunchIntentForPackage(PackageName!) is { } open)
            builder.SetContentIntent(PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
        builder.AddAction(Action(Android.Resource.Drawable.IcMediaPrevious, Loc.T("PrevTip"), ActionPrevious, 1));
        builder.AddAction(playing
            ? Action(Android.Resource.Drawable.IcMediaPause, Loc.T("PauseTip"), ActionPlayPause, 2)
            : Action(Android.Resource.Drawable.IcMediaPlay, Loc.T("PlayTip"), ActionPlayPause, 2));
        builder.AddAction(Action(Android.Resource.Drawable.IcMediaNext, Loc.T("NextTip"), ActionNext, 3));
        var style = new Notification.MediaStyle();
        style.SetShowActionsInCompactView(0, 1, 2);
        if (_session is not null)
            style.SetMediaSession(_session.SessionToken);
        builder.SetStyle(style);
        return builder.Build();
    }

    private Notification.Action Action(int icon, string title, string action, int code) =>
        new Notification.Action.Builder(Icon.CreateWithResource(this, icon), title, Pending(action, code)).Build();

    private PendingIntent Pending(string action, int code) =>
        PendingIntent.GetService(this, code, new Intent(this, typeof(PlaybackService)).SetAction(action),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

    // ── Foco de áudio: uma chamada ou outra app a tocar põe a Fita em pausa ──

    private bool _hasFocus;

    private void RequestFocus()
    {
        if (_hasFocus || _audio is null)
            return;
        _focusListener ??= new FocusListener();
        AudioFocusRequest result;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _focus ??= new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                .SetAudioAttributes(new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)!
                    .SetContentType(AudioContentType.Music)!
                    .Build()!)
                .SetOnAudioFocusChangeListener(_focusListener)
                .Build();
            result = _audio.RequestAudioFocus(_focus!);
        }
        else
        {
#pragma warning disable CA1422
            result = _audio.RequestAudioFocus(_focusListener, Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CA1422
        }
        _hasFocus = result == AudioFocusRequest.Granted;
    }

    private void AbandonFocus()
    {
        if (!_hasFocus || _audio is null)
            return;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && _focus is not null)
            _audio.AbandonAudioFocusRequest(_focus);
#pragma warning disable CA1422
        else if (_focusListener is not null)
            _audio.AbandonAudioFocus(_focusListener);
#pragma warning restore CA1422
        _hasFocus = false;
    }

    private sealed class FocusListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        private bool _resume;

        public void OnAudioFocusChange(AudioFocus focusChange)
        {
            switch (focusChange)
            {
                case AudioFocus.Loss:
                    _resume = false;
                    if (_running is { } s)
                        s._hasFocus = false;
                    Run(p => p.Pause());
                    break;
                case AudioFocus.LossTransient:
                    _resume = Player.Current.State == PlayState.Playing;
                    Run(p => p.Pause());
                    break;
                case AudioFocus.Gain:
                    if (_resume)
                        Run(p => p.Play());
                    _resume = false;
                    break;
            }
        }
    }

    /// <summary>Auscultadores desligados (ou o Bluetooth foi-se): pausa, como em qualquer leitor.</summary>
    private sealed class NoisyReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == AudioManager.ActionAudioBecomingNoisy)
                Run(p => p.Pause());
        }
    }

    /// <summary>Os comandos que chegam pela sessão: ecrã de bloqueio, botões dos auscultadores, relógio, carro.</summary>
    private sealed class SessionCallback : MediaSession.Callback
    {
        public override void OnPlay() => Run(p => p.Play());
        public override void OnPause() => Run(p => p.Pause());
        public override void OnSkipToNext() => Run(p => p.Next());
        public override void OnSkipToPrevious() => Run(p => p.Previous());
        public override void OnStop() => Run(p => p.Stop());
        public override void OnSeekTo(long pos) => Run(p => p.Seek(pos / 1000.0));
    }
}
