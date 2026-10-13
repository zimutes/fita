using System.Diagnostics;
using System.Globalization;
using Fita.Audio;
using Avalonia.Threading;

namespace Fita.Library;

/// <summary>
/// Mede o volume das faixas que não trazem ReplayGain (EBU R128, pelo filtro ebur128 do ffmpeg)
/// e guarda o resultado como ganho de faixa, à referência do ReplayGain 2.0 (−18 LUFS). Uma
/// faixa de cada vez, numa thread e num ffmpeg de prioridade baixa, a começar pelas que vão
/// tocar a seguir. O resultado fica na faixa (e na lista gravada): não se mede duas vezes.
/// </summary>
public sealed class LoudnessScanner : IDisposable
{
    /// <summary>Referência do ReplayGain 2.0.</summary>
    public const double ReferenceLufs = -18;

    private readonly Func<string?> _ffmpeg;
    private readonly object _lock = new();
    private readonly LinkedList<Track> _queue = new();
    private readonly HashSet<Track> _queued = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Uma faixa ficou medida (na thread da interface).</summary>
    public event Action<Track>? Scanned;

    public LoudnessScanner(Func<string?> ffmpeg)
    {
        _ffmpeg = ffmpeg;
        new Thread(Work) { IsBackground = true, Name = "fita-loudness", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public static bool NeedsScan(Track t) =>
        t.MetadataLoaded && !t.LoudnessScanned && double.IsNaN(t.TrackGain) && double.IsNaN(t.AlbumGain);

    /// <summary>Põe estas faixas à frente da fila, por esta ordem (a primeira é a mais urgente).</summary>
    public void Request(IEnumerable<Track> tracks)
    {
        var wanted = tracks.Where(NeedsScan).ToList();
        if (wanted.Count == 0)
            return;
        lock (_lock)
        {
            for (var i = wanted.Count - 1; i >= 0; i--)
            {
                var t = wanted[i];
                if (!_queued.Add(t))
                    _queue.Remove(t);
                _queue.AddFirst(t);
            }
        }
        _signal.Release();
    }

    private void Work()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            Track? track;
            lock (_lock)
            {
                track = _queue.First?.Value;
                if (track is not null)
                {
                    _queue.RemoveFirst();
                    _queued.Remove(track);
                }
            }
            if (track is null)
            {
                try
                {
                    _signal.Wait(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }
            if (!NeedsScan(track) || _ffmpeg() is not { } ffmpeg)
                continue;
            var result = Measure(ffmpeg, track.Path, track.Start, track.End, token);
            if (token.IsCancellationRequested)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                track.LoudnessScanned = true;
                if (result is { } r && double.IsNaN(track.TrackGain) && double.IsNaN(track.AlbumGain))
                {
                    track.TrackGain = Math.Round(ReferenceLufs - r.Lufs, 2);
                    track.TrackPeak = Math.Pow(10, r.PeakDb / 20);
                }
                Scanned?.Invoke(track);
            });
        }
    }

    /// <summary>Volume integrado (LUFS) e pico real (dBFS), ou null se não se conseguiu medir.</summary>
    public static (double Lufs, double PeakDb)? Measure(string ffmpeg, string path, double start, double end, CancellationToken ct)
    {
        try
        {
            var info = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // As mesmas regras do leitor: lista de argumentos, só ficheiros locais e formatos de áudio.
            foreach (var a in (string[])["-hide_banner", "-nostdin", "-nostats",
                         "-protocol_whitelist", "file", "-format_whitelist", FfmpegDecoder.FormatWhitelist])
                info.ArgumentList.Add(a);
            if (start > 0)
            {
                info.ArgumentList.Add("-ss");
                info.ArgumentList.Add(start.ToString("0.######", CultureInfo.InvariantCulture));
            }
            if (end > start)
            {
                info.ArgumentList.Add("-t");
                info.ArgumentList.Add((end - start).ToString("0.######", CultureInfo.InvariantCulture));
            }
            foreach (var a in (string[])["-i", "file:" + path, "-map", "0:a:0", "-vn", "-sn", "-dn",
                         "-af", "ebur128=peak=true:framelog=quiet", "-f", "null", "-"])
                info.ArgumentList.Add(a);

            using var process = Process.Start(info);
            if (process is null)
                return null;
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch { }
            using var _ = ct.Register(() =>
            {
                try { process.Kill(); } catch { }
            });
            var log = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? ParseSummary(log) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Do resumo do ebur128: "I: -14.3 LUFS" e, em "True peak", "Peak: -0.4 dBFS".</summary>
    public static (double Lufs, double PeakDb)? ParseSummary(string log)
    {
        var summary = log.LastIndexOf("Summary:", StringComparison.Ordinal);
        if (summary < 0)
            return null;
        double? lufs = null, peak = null;
        foreach (var raw in log[summary..].Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("I:", StringComparison.Ordinal))
                lufs = Number(line[2..], "LUFS");
            else if (line.StartsWith("Peak:", StringComparison.Ordinal))
                peak = Number(line[5..], "dBFS");
        }
        // Silêncio total dá -70 LUFS (ou -inf): não há ganho que faça sentido.
        if (lufs is not { } l || l <= -69.9)
            return null;
        return (l, peak ?? 0);

        static double? Number(string text, string unit)
        {
            var t = text.Replace(unit, "", StringComparison.Ordinal).Trim();
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
        }
    }

    public void Dispose() => _cts.Cancel();
}
