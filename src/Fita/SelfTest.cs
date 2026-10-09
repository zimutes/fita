using System.Diagnostics;
using Fita.Audio;
using Fita.Library;

namespace Fita;

/// <summary>
/// "Fita --selftest ficheiro…": passa os ficheiros pelo motor inteiro (ffmpeg, EQ, limitador,
/// OpenAL), em silêncio e sem janela, e confirma que a posição anda, que o salto funciona e que
/// as faixas passam de uma para a outra. Serve para testar numa máquina sem interface.
/// </summary>
internal static class SelfTest
{
    public static int Run(string[] files)
    {
        var ffmpeg = FfmpegLocator.FindFfmpeg(Environment.GetEnvironmentVariable("FITA_FFMPEG"));
        Console.WriteLine($"ffmpeg: {ffmpeg ?? "não encontrado"}");
        if (ffmpeg is null || files.Length == 0)
            return 2;

        foreach (var f in files)
        {
            var meta = MetadataReader.Read(f, FfmpegLocator.FindFfprobe(ffmpeg));
            Console.WriteLine($"  {Path.GetFileName(f)}: {meta.Codec} {meta.SampleRate} Hz {meta.Channels} ch {meta.Duration:0.00} s «{meta.Title}»");
        }

        var items = files.Select(f => new PlayItem(f, Path.GetFullPath(f))).ToList();
        using var engine = new AudioEngine { Silent = true, FfmpegPath = ffmpeg };
        engine.NextItem = (current, _) =>
        {
            var i = items.FindIndex(x => x.Path == current.Path);
            return i >= 0 && i + 1 < items.Count ? items[i + 1] : null;
        };
        var eq = new EqSettings();
        EqPresets.Apply(eq, EqPresets.BuiltIn.First(p => p.Name == "Rock"));
        engine.ConfigureEq(eq);
        engine.Play(items[0]);

        var clock = Stopwatch.StartNew();
        var seen = new List<string>();
        string? last = null;
        var seeked = false;
        var limit = TimeSpan.FromSeconds(Math.Max(20, files.Length * 15));
        var nextPrint = 0.0;
        while (clock.Elapsed < limit)
        {
            var s = engine.Snapshot();
            if (s.Segment is not null && s.Segment.Item.Path != last)
            {
                last = s.Segment.Item.Path;
                seen.Add(last);
                Console.WriteLine($"{clock.Elapsed.TotalSeconds,6:0.00}s  faixa {seen.Count}: {Path.GetFileName(last)}");
            }
            if (clock.Elapsed.TotalSeconds >= nextPrint)
            {
                nextPrint += 1;
                Console.WriteLine($"{clock.Elapsed.TotalSeconds,6:0.00}s  {s.State} pos {s.Position:0.00} s  {engine.SampleRate} Hz{(engine.LimiterActive ? "  [limitador]" : "")}");
            }
            if (!seeked && seen.Count == 1 && s.Position > 1.2)
            {
                seeked = true;
                engine.Seek(2.5);
                Thread.Sleep(150);
                var after = engine.Snapshot().Position;
                Console.WriteLine($"         salto para 2.5 s → {after:0.00} s {(Math.Abs(after - 2.5) < 0.6 ? "ok" : "FALHOU")}");
            }
            if (engine.EndedCount > 0)
                break;
            Thread.Sleep(50);
        }

        var failures = engine.Failures.ToList();
        foreach (var (item, message) in failures)
            Console.WriteLine($"FALHA {Path.GetFileName(item.Path)}: {message}");
        if (engine.OutputError is { } error)
            Console.WriteLine($"Saída de som: {error}");
        var ok = engine.EndedCount > 0 && failures.Count == 0 && seen.Count == items.Count && engine.OutputError is null;
        Console.WriteLine(ok ? $"OK: {seen.Count} faixas em {clock.Elapsed.TotalSeconds:0.0} s" : "FALHOU");
        return ok ? 0 : 1;
    }
}
