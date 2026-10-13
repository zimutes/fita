using System.Runtime.Versioning;
using System.Text;
using Fita.Audio;
using Tmds.DBus.Protocol;

namespace Fita.Platform;

/// <summary>
/// MPRIS (org.mpris.MediaPlayer2) no bus da sessão: é por aqui que o GNOME, o KDE, o
/// playerctl e as teclas multimédia veem e comandam a Fita no Linux. Usa o Tmds.DBus.Protocol,
/// que o Avalonia já traz. Os pedidos chegam na thread do D-Bus; o estado guarda-se com um lock.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class Mpris : IMediaSession, IMethodHandler
{
    private const string ObjectPath = "/org/mpris/MediaPlayer2";
    private const string RootInterface = "org.mpris.MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string NoTrack = "/org/mpris/MediaPlayer2/TrackList/NoTrack";

    private static readonly byte[] Introspection = Encoding.UTF8.GetBytes("""
        <interface name="org.mpris.MediaPlayer2">
          <method name="Raise"/>
          <method name="Quit"/>
          <property name="CanQuit" type="b" access="read"/>
          <property name="CanRaise" type="b" access="read"/>
          <property name="HasTrackList" type="b" access="read"/>
          <property name="Identity" type="s" access="read"/>
          <property name="DesktopEntry" type="s" access="read"/>
          <property name="SupportedUriSchemes" type="as" access="read"/>
          <property name="SupportedMimeTypes" type="as" access="read"/>
        </interface>
        <interface name="org.mpris.MediaPlayer2.Player">
          <method name="Next"/>
          <method name="Previous"/>
          <method name="Pause"/>
          <method name="PlayPause"/>
          <method name="Stop"/>
          <method name="Play"/>
          <method name="Seek"><arg name="Offset" type="x" direction="in"/></method>
          <method name="SetPosition"><arg name="TrackId" type="o" direction="in"/><arg name="Position" type="x" direction="in"/></method>
          <method name="OpenUri"><arg name="Uri" type="s" direction="in"/></method>
          <signal name="Seeked"><arg name="Position" type="x"/></signal>
          <property name="PlaybackStatus" type="s" access="read"/>
          <property name="Rate" type="d" access="read"/>
          <property name="Metadata" type="a{sv}" access="read"/>
          <property name="Volume" type="d" access="read"/>
          <property name="Position" type="x" access="read"/>
          <property name="MinimumRate" type="d" access="read"/>
          <property name="MaximumRate" type="d" access="read"/>
          <property name="CanGoNext" type="b" access="read"/>
          <property name="CanGoPrevious" type="b" access="read"/>
          <property name="CanPlay" type="b" access="read"/>
          <property name="CanPause" type="b" access="read"/>
          <property name="CanSeek" type="b" access="read"/>
          <property name="CanControl" type="b" access="read"/>
        </interface>
        """);

    private static readonly string[] PlayerProperties =
    [
        "PlaybackStatus", "Rate", "Metadata", "Volume", "Position", "MinimumRate", "MaximumRate",
        "CanGoNext", "CanGoPrevious", "CanPlay", "CanPause", "CanSeek", "CanControl",
    ];
    private static readonly string[] RootProperties =
        ["CanQuit", "CanRaise", "HasTrackList", "Identity", "DesktopEntry", "SupportedUriSchemes", "SupportedMimeTypes"];

    private readonly Action<MediaCommand> _onCommand;
    private readonly Action<double> _onSeek;
    private readonly object _lock = new();
    private Connection? _connection;
    private bool _disposed;

    // Estado (com _lock).
    private PlayState _state = PlayState.Stopped;
    private NowPlaying? _track;
    private int _trackSerial;
    private string? _artUrl;
    private double _position;
    private double _duration;
    private long _positionAt = Environment.TickCount64;

    private Mpris(Action<MediaCommand> onCommand, Action<double> onSeek)
    {
        _onCommand = onCommand;
        _onSeek = onSeek;
    }

    public static Mpris? Create(Action<MediaCommand> onCommand, Action<double> onSeek)
    {
        // Sem esta variável não há sessão de ambiente de trabalho (e o Address.Session ia
        // procurar o endereço ao X11, com erros feios no registo).
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")) || string.IsNullOrEmpty(Address.Session))
            return null;
        var mpris = new Mpris(onCommand, onSeek);
        _ = mpris.ConnectAsync();
        return mpris;
    }

    public string Path => ObjectPath;

    private async Task ConnectAsync()
    {
        try
        {
            var connection = new Connection(Address.Session!);
            await connection.ConnectAsync();
            connection.AddMethodHandler(this);
            // Uma segunda Fita (pasta de dados de testes) fica com um nome próprio.
            if (await RequestNameAsync(connection, "org.mpris.MediaPlayer2.fita") != 1)
                await RequestNameAsync(connection, $"org.mpris.MediaPlayer2.fita.instance{Environment.ProcessId}");
            lock (_lock)
            {
                if (_disposed)
                {
                    connection.Dispose();
                    return;
                }
                _connection = connection;
            }
        }
        catch (Exception ex)
        {
            Log.Write("MPRIS: " + ex.Message);
        }
    }

    private static Task<uint> RequestNameAsync(Connection connection, string name)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su");
        writer.WriteString(name);
        writer.WriteUInt32(4); // DBUS_NAME_FLAG_DO_NOT_QUEUE
        return connection.CallMethodAsync(writer.CreateMessage(), (message, _) => message.GetBodyReader().ReadUInt32());
    }

    // ── O que a Fita diz ──────────────────────────────────────────────────────

    public void SetTrack(NowPlaying? track)
    {
        lock (_lock)
        {
            // A mesma faixa (etiquetas ou capa a chegar) fica com o mesmo identificador.
            if (track?.Id != _track?.Id)
                _trackSerial++;
            _track = track;
            _artUrl = track?.Cover is { Length: > 0 } cover ? WriteCover(cover) : null;
            _duration = track?.Duration ?? 0;
        }
        PropertiesChanged("Metadata", "CanGoNext", "CanGoPrevious", "CanPlay", "CanSeek");
    }

    public void SetState(PlayState state)
    {
        lock (_lock)
        {
            if (state == _state)
                return;
            _position = CurrentPosition();
            _positionAt = Environment.TickCount64;
            _state = state;
        }
        PropertiesChanged("PlaybackStatus");
    }

    public void SetPosition(double position, double duration)
    {
        bool jumped, durationChanged;
        lock (_lock)
        {
            // Um salto (e não o andar normal do tempo) avisa-se com o sinal Seeked.
            jumped = Math.Abs(CurrentPosition() - position) > 1.5;
            _position = position;
            _positionAt = Environment.TickCount64;
            // A duração pode só chegar depois (etiquetas lidas em segundo plano).
            durationChanged = duration > 0 && Math.Abs(duration - _duration) > 0.5;
            if (durationChanged)
                _duration = duration;
        }
        if (durationChanged)
            PropertiesChanged("Metadata", "CanSeek");
        else if (jumped)
            Signal("Seeked", (long)(position * 1e6));
    }

    /// <summary>Segundos, a contar com o tempo que passou desde a última atualização (com _lock).</summary>
    private double CurrentPosition()
    {
        var position = _position;
        if (_state == PlayState.Playing)
            position += (Environment.TickCount64 - _positionAt) / 1000.0;
        return _duration > 0 ? Math.Min(position, _duration) : position;
    }

    /// <summary>
    /// A capa vai para um ficheiro (o MPRIS só aceita um endereço), na pasta privada da sessão
    /// (ou na cache do utilizador), nunca no /tmp partilhado. Nome pelo conteúdo, para os painéis
    /// não mostrarem a capa anterior da cache deles.
    /// </summary>
    private static string? WriteCover(byte[] cover)
    {
        try
        {
            var folder = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run
                ? System.IO.Path.Combine(run, "fita")
                : System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } cache
                    ? cache
                    : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "fita");
            Directory.CreateDirectory(folder);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cover))[..16].ToLowerInvariant();
            var file = System.IO.Path.Combine(folder, $"cover-{hash}");
            if (!File.Exists(file))
            {
                foreach (var old in Directory.GetFiles(folder, "cover-*"))
                    File.Delete(old);
                File.WriteAllBytes(file, cover);
            }
            return new Uri(file).AbsoluteUri;
        }
        catch (Exception ex)
        {
            Log.Write("MPRIS, capa: " + ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        Connection? connection;
        lock (_lock)
        {
            _disposed = true;
            connection = _connection;
            _connection = null;
        }
        connection?.Dispose();
    }

    // ── Pedidos que chegam ────────────────────────────────────────────────────

    public bool RunMethodHandlerSynchronously(Message message) => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        try
        {
            Handle(context);
        }
        catch (Exception ex)
        {
            Log.Write("MPRIS: " + ex.Message);
            if (!context.ReplySent && !context.NoReplyExpected)
                context.ReplyError("org.freedesktop.DBus.Error.Failed", ex.Message);
        }
        return ValueTask.CompletedTask;
    }

    private void Handle(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([Introspection]);
            return;
        }
        var request = context.Request;
        var member = request.MemberAsString;
        switch (request.InterfaceAsString)
        {
            case PropertiesInterface:
                HandleProperties(context, member);
                return;
            case RootInterface:
                if (member == "Raise")
                    _onCommand(MediaCommand.Raise);
                else if (member != "Quit")
                    break;
                ReplyEmpty(context);
                return;
            case PlayerInterface:
                if (HandlePlayer(context, member))
                    return;
                break;
        }
        context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", $"Unknown method {member}");
    }

    private bool HandlePlayer(MethodContext context, string? member)
    {
        switch (member)
        {
            case "Next": _onCommand(MediaCommand.Next); break;
            case "Previous": _onCommand(MediaCommand.Previous); break;
            case "Pause": _onCommand(MediaCommand.Pause); break;
            case "PlayPause": _onCommand(MediaCommand.PlayPause); break;
            case "Stop": _onCommand(MediaCommand.Stop); break;
            case "Play": _onCommand(MediaCommand.Play); break;
            case "OpenUri": break;
            case "Seek":
            {
                var offset = context.Request.GetBodyReader().ReadInt64() / 1e6;
                double target;
                lock (_lock)
                    target = CurrentPosition() + offset;
                SeekTo(target);
                break;
            }
            case "SetPosition":
            {
                var reader = context.Request.GetBodyReader();
                var trackId = reader.ReadObjectPathAsString();
                var position = reader.ReadInt64() / 1e6;
                // A especificação manda ignorar pedidos para outra faixa (já mudou entretanto).
                if (trackId == TrackId())
                    SeekTo(position);
                break;
            }
            default:
                return false;
        }
        ReplyEmpty(context);
        return true;
    }

    private void SeekTo(double seconds)
    {
        double duration;
        lock (_lock)
            duration = _duration;
        if (seconds < 0)
            seconds = 0;
        // Para lá do fim: segue para a faixa seguinte, como pede a especificação.
        if (duration > 0 && seconds >= duration)
            _onCommand(MediaCommand.Next);
        else
            _onSeek(seconds);
    }

    private void HandleProperties(MethodContext context, string? member)
    {
        var reader = context.Request.GetBodyReader();
        var iface = reader.ReadString();
        switch (member)
        {
            case "Get":
            {
                var name = reader.ReadString();
                var writer = context.CreateReplyWriter("v");
                try
                {
                    if (WriteProperty(ref writer, iface, name))
                        context.Reply(writer.CreateMessage());
                    else
                        context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"Unknown property {name}");
                }
                finally
                {
                    writer.Dispose();
                }
                return;
            }
            case "GetAll":
            {
                var writer = context.CreateReplyWriter("a{sv}");
                try
                {
                    var names = iface == RootInterface ? RootProperties : iface == PlayerInterface ? PlayerProperties : [];
                    WriteProperties(ref writer, iface, names);
                    context.Reply(writer.CreateMessage());
                }
                finally
                {
                    writer.Dispose();
                }
                return;
            }
            case "Set":
                // Tudo é só de leitura (o volume do sistema é que manda).
                context.ReplyError("org.freedesktop.DBus.Error.PropertyReadOnly", "Read-only");
                return;
        }
        context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", $"Unknown method {member}");
    }

    private static void ReplyEmpty(MethodContext context)
    {
        if (context.NoReplyExpected)
            return;
        using var writer = context.CreateReplyWriter(null!);
        context.Reply(writer.CreateMessage());
    }

    // ── Propriedades ──────────────────────────────────────────────────────────

    private void WriteProperties(ref MessageWriter writer, string iface, string[] names)
    {
        var dict = writer.WriteDictionaryStart();
        foreach (var name in names)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(name);
            WriteProperty(ref writer, iface, name);
        }
        writer.WriteDictionaryEnd(dict);
    }

    /// <summary>Escreve o valor como variante; falso se não houver tal propriedade.</summary>
    private bool WriteProperty(ref MessageWriter writer, string iface, string name)
    {
        lock (_lock)
        {
            var hasTrack = _track is not null;
            if (iface == RootInterface)
            {
                switch (name)
                {
                    case "CanQuit": writer.WriteVariantBool(false); return true;
                    case "CanRaise": writer.WriteVariantBool(true); return true;
                    case "HasTrackList": writer.WriteVariantBool(false); return true;
                    case "Identity": writer.WriteVariantString("Fita"); return true;
                    case "DesktopEntry": writer.WriteVariantString("fita"); return true;
                    case "SupportedUriSchemes": writer.WriteSignature("as"); writer.WriteArray(Array.Empty<string>()); return true;
                    case "SupportedMimeTypes": writer.WriteSignature("as"); writer.WriteArray(Array.Empty<string>()); return true;
                }
                return false;
            }
            if (iface != PlayerInterface)
                return false;
            switch (name)
            {
                case "PlaybackStatus":
                    writer.WriteVariantString(_state switch { PlayState.Playing => "Playing", PlayState.Paused => "Paused", _ => "Stopped" });
                    return true;
                case "Rate" or "MinimumRate" or "MaximumRate" or "Volume": writer.WriteVariantDouble(1.0); return true;
                case "Position": writer.WriteVariantInt64((long)(CurrentPosition() * 1e6)); return true;
                case "CanGoNext" or "CanGoPrevious" or "CanPlay" or "CanPause": writer.WriteVariantBool(hasTrack); return true;
                case "CanSeek": writer.WriteVariantBool(hasTrack && _duration > 0); return true;
                case "CanControl": writer.WriteVariantBool(true); return true;
                case "Metadata":
                    writer.WriteSignature("a{sv}");
                    WriteMetadata(ref writer);
                    return true;
            }
            return false;
        }
    }

    private string TrackId() => _track is null ? NoTrack : $"/org/zimutek/fita/track/{_trackSerial}";

    private void WriteMetadata(ref MessageWriter writer)
    {
        var dict = writer.WriteDictionaryStart();
        Entry(ref writer, "mpris:trackid");
        writer.WriteVariantObjectPath(TrackId());
        if (_track is { } t)
        {
            if (_duration > 0)
            {
                Entry(ref writer, "mpris:length");
                writer.WriteVariantInt64((long)(_duration * 1e6));
            }
            Entry(ref writer, "xesam:title");
            writer.WriteVariantString(t.Title);
            if (!string.IsNullOrWhiteSpace(t.Artist))
            {
                Entry(ref writer, "xesam:artist");
                writer.WriteSignature("as");
                writer.WriteArray(new[] { t.Artist! });
            }
            if (!string.IsNullOrWhiteSpace(t.Album))
            {
                Entry(ref writer, "xesam:album");
                writer.WriteVariantString(t.Album);
            }
            if (t.TrackNumber > 0)
            {
                Entry(ref writer, "xesam:trackNumber");
                writer.WriteVariantInt32((int)t.TrackNumber);
            }
            if (_artUrl is not null)
            {
                Entry(ref writer, "mpris:artUrl");
                writer.WriteVariantString(_artUrl);
            }
        }
        writer.WriteDictionaryEnd(dict);

        static void Entry(ref MessageWriter writer, string key)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(key);
        }
    }

    // ── Sinais ────────────────────────────────────────────────────────────────

    private void PropertiesChanged(params string[] names)
    {
        var connection = _connection;
        if (connection is null)
            return;
        try
        {
            var writer = connection.GetMessageWriter();
            try
            {
                writer.WriteSignalHeader(null, ObjectPath, PropertiesInterface, "PropertiesChanged", "sa{sv}as");
                writer.WriteString(PlayerInterface);
                WriteProperties(ref writer, PlayerInterface, names);
                writer.WriteArray(Array.Empty<string>());
                connection.TrySendMessage(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Write("MPRIS: " + ex.Message);
        }
    }

    private void Signal(string member, long value)
    {
        var connection = _connection;
        if (connection is null)
            return;
        try
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(null, ObjectPath, PlayerInterface, member, "x");
            writer.WriteInt64(value);
            connection.TrySendMessage(writer.CreateMessage());
        }
        catch (Exception ex)
        {
            Log.Write("MPRIS: " + ex.Message);
        }
    }
}
