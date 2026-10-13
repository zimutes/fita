using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Fita.Audio;

namespace Fita.Library;

/// <summary>
/// A forma de onda de uma faixa, para a barra de posição: o ffmpeg lê o ficheiro inteiro em
/// mono a 8 kHz (depressa: um disco inteiro em menos de um segundo) e cada barra fica com o
/// RMS do seu bocado, normalizado ao maior. Só para ver: não toca em nada do som.
/// </summary>
public static class Waveform
{
    public const int Bins = 600;
    private const int Rate = 8000;
    /// <summary>Blocos de 20 ms; no fim juntam-se nas <see cref="Bins"/> barras.</summary>
    private const int BlockSamples = Rate / 50;

    /// <summary>Valores de 0 a 1, ou null se não se conseguiu ler. Lê disco: fora da interface.</summary>
    public static float[]? Compute(string ffmpeg, string path, double start, double end, CancellationToken ct)
    {
        try
        {
            var info = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // As mesmas regras do leitor: lista de argumentos, só ficheiros locais e formatos de áudio.
            foreach (var a in (string[])["-hide_banner", "-nostdin", "-v", "error",
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
                         "-ac", "1", "-ar", Rate.ToString(CultureInfo.InvariantCulture), "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"])
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
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();

            var blocks = new List<float>();
            var stream = process.StandardOutput.BaseStream;
            var bytes = new byte[BlockSamples * 4 * 64];
            var pending = 0;
            double sum = 0;
            var count = 0;
            while (true)
            {
                var n = stream.Read(bytes, pending, bytes.Length - pending);
                if (n <= 0)
                    break;
                pending += n;
                var whole = pending / 4 * 4;
                foreach (var s in MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, whole)))
                {
                    sum += s * s;
                    if (++count == BlockSamples)
                    {
                        blocks.Add((float)Math.Sqrt(sum / count));
                        sum = 0;
                        count = 0;
                    }
                }
                Buffer.BlockCopy(bytes, whole, bytes, 0, pending - whole);
                pending -= whole;
            }
            process.WaitForExit();
            if (ct.IsCancellationRequested)
                return null;
            if (count > 0)
                blocks.Add((float)Math.Sqrt(sum / count));
            return blocks.Count < 2 ? null : Rebin(blocks);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Junta os blocos nas barras (o maior de cada grupo) e normaliza ao maior de todos.</summary>
    private static float[] Rebin(List<float> blocks)
    {
        var bins = new float[Math.Min(Bins, blocks.Count)];
        for (var i = 0; i < bins.Length; i++)
        {
            var from = (int)((long)i * blocks.Count / bins.Length);
            var to = Math.Max(from + 1, (int)((long)(i + 1) * blocks.Count / bins.Length));
            var max = 0f;
            for (var j = from; j < to; j++)
                max = Math.Max(max, blocks[j]);
            bins[i] = max;
        }
        var top = bins.Max();
        if (top <= 1e-6f)
            return bins;
        for (var i = 0; i < bins.Length; i++)
            bins[i] /= top;
        return bins;
    }
}
