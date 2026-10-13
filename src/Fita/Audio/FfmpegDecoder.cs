using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Fita.Audio;

/// <summary>
/// Descodifica um ficheiro com o ffmpeg para float estéreo intercalado, à frequência da saída.
/// Um processo por faixa (e por salto): o ffmpeg lê praticamente tudo e o pipe é simples.
/// </summary>
internal sealed class FfmpegDecoder : IDisposable
{
    /// <summary>
    /// Só estes leitores de formato (demuxers). Um ficheiro que finja ser outra coisa (uma lista
    /// HLS, um "concat" que aponta para outros ficheiros) é recusado em vez de interpretado.
    /// </summary>
    public const string FormatWhitelist =
        "mp3,flac,ogg,wav,w64,aiff,mov,asf,matroska,ape,wv,aac,mpc,mpc8,tta,ac3,eac3,dts,dtshd,dsf,iff,caf," +
        "amr,amrnb,amrwb,au,tak,shn,mlp,truehd,xwma,voc,libopenmpt,libgme";

    private readonly Process _process;
    private readonly Stream _stdout;
    private readonly byte[] _bytes;
    private int _pending;
    private readonly Queue<string> _errors = new();

    public FfmpegDecoder(string ffmpeg, string file, double startSeconds, int sampleRate, PlaybackSpeed speed = default)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // ArgumentList, nunca uma string de argumentos: um nome de ficheiro não pode virar opção.
        foreach (var a in (string[])["-hide_banner", "-nostdin", "-v", "error",
                     "-protocol_whitelist", "file", "-format_whitelist", FormatWhitelist])
            start.ArgumentList.Add(a);
        if (startSeconds > 0.000001)
        {
            // Ao microssegundo: as faixas de um .cue encostam-se umas às outras a partir daqui.
            start.ArgumentList.Add("-ss");
            start.ArgumentList.Add(startSeconds.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        }
        // O prefixo "file:" impede que um nome seja lido como protocolo ou como opção.
        foreach (var a in (string[])["-i", "file:" + file, "-map", "0:a:0", "-vn", "-sn", "-dn"])
            start.ArgumentList.Add(a);
        if (speed.Factor > 0 && !speed.IsNormal)
        {
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            // atempo estica o tempo sem mexer no tom; asetrate muda os dois, como uma fita mais
            // depressa (e o -ar volta a pôr tudo à frequência da saída).
            start.ArgumentList.Add("-af");
            start.ArgumentList.Add(speed.KeepPitch
                ? "atempo=" + speed.Factor.ToString("0.####", invariant)
                : $"aresample={sampleRate.ToString(invariant)},asetrate={((int)Math.Round(sampleRate * speed.Factor)).ToString(invariant)}");
        }
        foreach (var a in (string[])["-ac", "2", "-ar", sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"])
            start.ArgumentList.Add(a);

        _process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
        _process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;
            lock (_errors)
            {
                _errors.Enqueue(e.Data.Trim());
                while (_errors.Count > 6)
                    _errors.Dequeue();
            }
        };
        _process.BeginErrorReadLine();
        _stdout = _process.StandardOutput.BaseStream;
        _bytes = new byte[8192 * 8];
    }

    /// <summary>
    /// Lê amostras estéreo intercaladas para <paramref name="destination"/> (no máximo metade do
    /// seu tamanho em pares L/R). Devolve quantos pares leu; 0 no fim do ficheiro ou se o processo
    /// foi morto.
    /// </summary>
    public int Read(Span<float> destination)
    {
        var maxFrames = destination.Length / 2;
        var wantBytes = Math.Min(maxFrames * 8, _bytes.Length);
        while (_pending < 8)
        {
            var n = _stdout.Read(_bytes, _pending, wantBytes - _pending);
            if (n <= 0)
                return 0;
            _pending += n;
        }
        var frames = _pending / 8;
        var used = frames * 8;
        MemoryMarshal.Cast<byte, float>(_bytes.AsSpan(0, used)).CopyTo(destination);
        // Sobra de uma amostra incompleta: passa para o início.
        var rest = _pending - used;
        if (rest > 0)
            Buffer.BlockCopy(_bytes, used, _bytes, 0, rest);
        _pending = rest;
        return frames;
    }

    /// <summary>Última mensagem de erro do ffmpeg, se houve alguma.</summary>
    public string? LastError
    {
        get
        {
            try
            {
                _process.WaitForExit(500);
            }
            catch { }
            lock (_errors)
                return _errors.Count > 0 ? string.Join(" · ", _errors) : null;
        }
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill();
        }
        catch { }
    }

    public void Dispose()
    {
        Kill();
        try { _process.Dispose(); } catch { }
    }
}
