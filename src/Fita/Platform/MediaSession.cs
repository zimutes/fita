using Fita.Audio;

namespace Fita.Platform;

public enum MediaCommand { Play, Pause, PlayPause, Stop, Next, Previous, Raise }

/// <summary>
/// O que está a tocar, como o sistema o mostra (painel multimédia, ecrã de bloqueio). Path
/// identifica a faixa: a mesma faixa volta a ser enviada quando chegam as etiquetas ou a capa.
/// </summary>
public sealed record NowPlaying(string Path, string Title, string? Artist, string? Album, uint TrackNumber, double Duration, byte[]? Cover);

/// <summary>
/// A Fita vista pelo sistema: no Windows o painel multimédia (SMTC), no Linux o MPRIS. É também
/// por aqui que chegam as teclas multimédia, os botões dos auscultadores e os comandos do painel.
/// Os comandos chegam numa thread qualquer: quem os recebe passa-os à interface.
/// </summary>
public interface IMediaSession : IDisposable
{
    void SetTrack(NowPlaying? track);
    void SetState(PlayState state);
    /// <summary>Posição e duração em segundos (o sistema anima a barra entre chamadas).</summary>
    void SetPosition(double position, double duration);
}

public static class MediaSession
{
    /// <summary>A sessão do sistema, ou null se não houver (ou falhar).</summary>
    public static IMediaSession? Create(IntPtr window, Action<MediaCommand> onCommand, Action<double> onSeek)
    {
        try
        {
            if (OperatingSystem.IsWindows() && window != IntPtr.Zero)
                return WindowsMediaControls.Create(window, onCommand, onSeek);
            if (OperatingSystem.IsLinux())
                return Mpris.Create(onCommand, onSeek);
        }
        catch (Exception ex)
        {
            Log.Write("Sessão multimédia do sistema: " + ex.Message);
        }
        return null;
    }
}
