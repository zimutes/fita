using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Fita.Audio;

namespace Fita.Producer;

/// <summary>
/// Analisa uma faixa: um ffmpeg só, que divide o áudio em dois. Um ramo passa pelo ebur128
/// (volume integrado, LRA e pico real, a medida de referência, no resumo do stderr); o outro
/// sai em float estéreo para o <see cref="SignalStats"/>, à frequência original (os picos e as
/// saturações contam-se nas amostras verdadeiras, sem reamostragem).
/// </summary>
public static class Analyzer
{
    public static TrackAnalysis? Analyze(string ffmpeg, string path, double start, double end, int sampleRate, int channels,
        CancellationToken ct)
    {
        var rate = sampleRate is >= 8000 and <= 384000 ? sampleRate : 48000;
        var info = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // As mesmas regras do leitor: lista de argumentos, só ficheiros locais e formatos de áudio.
        foreach (var a in (string[])["-hide_banner", "-nostdin", "-nostats",
                     "-protocol_whitelist", "file", "-format_whitelist", FfmpegDecoder.FormatWhitelist])
            info.ArgumentList.Add(a);
        var invariant = CultureInfo.InvariantCulture;
        if (start > 0)
        {
            info.ArgumentList.Add("-ss");
            info.ArgumentList.Add(start.ToString("0.######", invariant));
        }
        if (end > start)
        {
            info.ArgumentList.Add("-t");
            info.ArgumentList.Add((end - start).ToString("0.######", invariant));
        }
        foreach (var a in (string[])["-i", "file:" + path,
                     "-filter_complex", "[0:a:0]asplit=2[m][o];[m]ebur128=peak=true:framelog=quiet[mo]",
                     "-map", "[mo]", "-f", "null", "-",
                     "-map", "[o]", "-ac", "2", "-ar", rate.ToString(invariant), "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"])
            info.ArgumentList.Add(a);

        var log = new StringBuilder();
        try
        {
            using var process = Process.Start(info);
            if (process is null)
                return null;
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch { }
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                    return;
                lock (log)
                {
                    // Só interessa o fim (o resumo); o começo pode ser comprido.
                    if (log.Length > 64_000)
                        log.Remove(0, 32_000);
                    log.AppendLine(e.Data);
                }
            };
            process.BeginErrorReadLine();
            using var _ = ct.Register(() =>
            {
                try { process.Kill(); } catch { }
            });

            var stats = new SignalStats(rate, mono: channels == 1);
            var stdout = process.StandardOutput.BaseStream;
            var bytes = new byte[65536];
            var pending = 0;
            while (true)
            {
                var n = stdout.Read(bytes, pending, bytes.Length - pending);
                if (n <= 0)
                    break;
                pending += n;
                var whole = pending / 8 * 8;
                stats.Feed(MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, whole)));
                var rest = pending - whole;
                if (rest > 0)
                    Buffer.BlockCopy(bytes, whole, bytes, 0, rest);
                pending = rest;
            }
            process.WaitForExit();
            if (ct.IsCancellationRequested || process.ExitCode != 0)
                return null;

            var analysis = new TrackAnalysis { Channels = channels };
            stats.Finish(analysis);
            string text;
            lock (log)
                text = log.ToString();
            var summary = ParseSummary(text);
            analysis.Integrated = summary.Integrated ?? analysis.OwnIntegrated;
            analysis.Range = summary.Range ?? double.NaN;
            analysis.TruePeak = summary.TruePeak ?? analysis.SamplePeak;
            if (analysis.Integrated <= -69.9)
                analysis.Integrated = double.NaN;
            return analysis;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Write($"Análise falhou ({path}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Do resumo do ebur128: "I: -14.3 LUFS", "LRA: 5.2 LU" e, em "True peak", "Peak: -0.4 dBFS".
    /// </summary>
    public static (double? Integrated, double? Range, double? TruePeak) ParseSummary(string log)
    {
        var at = log.LastIndexOf("Summary:", StringComparison.Ordinal);
        if (at < 0)
            return (null, null, null);
        double? i = null, lra = null, peak = null;
        foreach (var raw in log[at..].Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("I:", StringComparison.Ordinal))
                i = Number(line[2..], "LUFS");
            else if (line.StartsWith("LRA:", StringComparison.Ordinal))
                lra = Number(line[4..], "LU");
            else if (line.StartsWith("Peak:", StringComparison.Ordinal))
                peak = Number(line[5..], "dBFS");
        }
        return (i, lra, peak);

        static double? Number(string text, string unit)
        {
            var t = text.Replace(unit, "", StringComparison.Ordinal).Trim();
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
        }
    }
}
