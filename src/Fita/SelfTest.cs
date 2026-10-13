using System.Diagnostics;
using Fita.Audio;
using Fita.Library;

namespace Fita;

/// <summary>
/// "Fita --selftest ficheiro…": passa os ficheiros pelo motor inteiro (ffmpeg, EQ, limitador,
/// OpenAL), em silêncio e sem janela, e confirma que a posição anda, que o salto funciona e que
/// as faixas passam de uma para a outra. Aceita também pastas e listas (M3U, PLS, .cue). Serve
/// para testar numa máquina sem interface.
/// </summary>
internal static class SelfTest
{
    public static int Run(string[] files)
    {
        var ffmpeg = FfmpegLocator.FindFfmpeg(Environment.GetEnvironmentVariable("FITA_FFMPEG"));
        Console.WriteLine($"ffmpeg: {ffmpeg ?? "não encontrado"}");
        if (ffmpeg is null || files.Length == 0)
            return 2;

        var tracks = PlaylistFiles.Expand(files);
        foreach (var t in tracks)
        {
            var meta = MetadataReader.Read(t.Path, FfmpegLocator.FindFfprobe(ffmpeg));
            meta.ApplyTo(t);
            Console.WriteLine($"  {Name(t)}: {t.Codec} {t.SampleRate} Hz {t.Channels} ch {t.Duration:0.00} s"
                + (t.IsPart ? $" (troço {t.Start:0.000}–{(t.End > 0 ? t.End.ToString("0.000") : "fim")} s)" : ""));
        }
        if (tracks.Count == 0)
            return 2;

        var items = tracks.Select(t => new PlayItem(t, t.Path, 1f, t.Start, t.End)).ToList();
        using var engine = new AudioEngine { Silent = true, FfmpegPath = ffmpeg };
        engine.NextItem = (current, _) =>
        {
            var i = items.FindIndex(x => ReferenceEquals(x.Tag, current.Tag));
            return i >= 0 && i + 1 < items.Count ? items[i + 1] : null;
        };
        var eq = new EqSettings();
        EqPresets.Apply(eq, EqPresets.BuiltIn.First(p => p.Name == "Rock"));
        engine.ConfigureEq(eq);
        // FITA_CROSSFADE=segundos: o mesmo teste com crossfade.
        if (double.TryParse(Environment.GetEnvironmentVariable("FITA_CROSSFADE"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var crossfade))
            engine.CrossfadeSeconds = crossfade;
        // FITA_SPEED=1.5 (ou 1.5t, como uma fita): o mesmo teste a outra velocidade.
        if (Environment.GetEnvironmentVariable("FITA_SPEED") is { Length: > 0 } speedText
            && double.TryParse(speedText.TrimEnd('t'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed))
            engine.Speed = new PlaybackSpeed(speed, KeepPitch: !speedText.EndsWith('t'));
        engine.Play(items[0]);

        var clock = Stopwatch.StartNew();
        var seen = new List<Track>();
        Track? last = null;
        var seeked = false;
        var limit = TimeSpan.FromSeconds(Math.Max(20, tracks.Sum(t => t.Duration > 0 ? t.Duration : 15) + 10));
        var nextPrint = 0.0;
        (double A, double B)? loop = null;
        if (Environment.GetEnvironmentVariable("FITA_LOOP")?.Split(',') is [var la, var lb]
            && double.TryParse(la, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var loopA)
            && double.TryParse(lb, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var loopB))
            loop = (loopA, loopB);
        var lastPosition = 0.0;
        var loops = 0;
        var loopStarted = false;
        while (clock.Elapsed < limit)
        {
            var s = engine.Snapshot();
            if (s.Segment?.Item.Tag is Track current && current != last)
            {
                last = current;
                seen.Add(current);
                Console.WriteLine($"{clock.Elapsed.TotalSeconds,6:0.00}s  faixa {seen.Count}: {Name(current)}  (pos {s.Position:0.00} s)");
            }
            if (clock.Elapsed.TotalSeconds >= nextPrint)
            {
                nextPrint += 1;
                Console.WriteLine($"{clock.Elapsed.TotalSeconds,6:0.00}s  {s.State} pos {s.Position:0.00} s  {engine.SampleRate} Hz{(engine.LimiterActive ? "  [limitador]" : "")}");
            }
            // FITA_LOOP=a,b: depois do salto, liga o ciclo A-B e confirma que volta ao A.
            if (seeked && loop is { } ab && !loopStarted && clock.Elapsed.TotalSeconds > 2)
            {
                loopStarted = true;
                engine.SetLoop(ab.A, ab.B);
                Console.WriteLine($"         ciclo A-B {ab.A:0.0}–{ab.B:0.0} s");
            }
            if (engine.Loop is not null && s.Position < lastPosition - 0.5)
            {
                loops++;
                Console.WriteLine($"         voltou ao A: {lastPosition:0.00} → {s.Position:0.00} s");
                if (loops >= 2)
                {
                    engine.ClearLoop();
                    Console.WriteLine("         ciclo desligado");
                }
            }
            lastPosition = s.Position;
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
        if (last is not null && last.Duration > 0)
            Console.WriteLine($"         última faixa: {last.Duration:0.00} s");
        if (engine.OutputError is { } error)
            Console.WriteLine($"Saída de som: {error}");
        var ok = (loop is null || loops >= 2) && engine.EndedCount > 0 && failures.Count == 0 && seen.Count == items.Count && engine.OutputError is null;
        Console.WriteLine(ok ? $"OK: {seen.Count} faixas em {clock.Elapsed.TotalSeconds:0.0} s" : "FALHOU");
        return ok ? 0 : 1;
    }

    private static string Name(Track t) => t.Title ?? t.FileName;
}
