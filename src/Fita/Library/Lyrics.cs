using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Fita.Library;

/// <summary>
/// Letras com tempos (LRC): um .lrc ao lado do ficheiro, com o mesmo nome, ou a letra dentro
/// da etiqueta quando traz tempos. Só as sincronizadas interessam: a linha do momento aparece
/// no mostrador, no lugar do álbum.
/// </summary>
public sealed partial class Lyrics
{
    private readonly (double Time, string Text)[] _lines;

    private Lyrics((double, string)[] lines) => _lines = lines;

    public int Count => _lines.Length;

    /// <summary>A linha que se ouve no segundo <paramref name="seconds"/> (vazia antes da primeira).</summary>
    public string LineAt(double seconds)
    {
        var lo = 0;
        var hi = _lines.Length - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_lines[mid].Time <= seconds)
            {
                found = mid;
                lo = mid + 1;
            }
            else
                hi = mid - 1;
        }
        return found < 0 ? "" : _lines[found].Text;
    }

    /// <summary>A letra de uma faixa, ou null. Lê disco: fora da thread da interface.</summary>
    public static Lyrics? Find(Track track)
    {
        // Num disco com .cue, um .lrc com o nome do ficheiro seria do disco todo.
        if (track.IsPart)
            return null;
        try
        {
            var lrc = Path.ChangeExtension(track.Path, ".lrc");
            if (File.Exists(lrc) && new FileInfo(lrc).Length < 1024 * 1024 && Parse(Decode(File.ReadAllBytes(lrc))) is { } fromFile)
                return fromFile;
        }
        catch { }
        try
        {
            using var file = TagLib.File.Create(track.Path);
            return string.IsNullOrWhiteSpace(file.Tag.Lyrics) ? null : Parse(file.Tag.Lyrics);
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    /// <summary>
    /// "[mm:ss.xx]texto", com uma ou mais marcas por linha, e o [offset:±ms] do formato. Sem
    /// marcas de tempo (letra simples), null.
    /// </summary>
    public static Lyrics? Parse(string text)
    {
        var lines = new List<(double, string)>();
        var offset = 0.0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (OffsetTag().Match(line) is { Success: true } o)
            {
                // Um offset positivo adianta a letra.
                offset = -int.Parse(o.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0;
                continue;
            }
            var times = new List<double>();
            var at = 0;
            Match m;
            while ((m = TimeTag().Match(line, at)).Success && m.Index == at)
            {
                double seconds = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                    + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (m.Groups[3].Success)
                {
                    var fraction = m.Groups[3].Value;
                    seconds += int.Parse(fraction, CultureInfo.InvariantCulture) / Math.Pow(10, fraction.Length);
                }
                times.Add(seconds);
                at = m.Index + m.Length;
            }
            if (times.Count == 0)
                continue;
            // Marcas de palavra (<mm:ss.xx>) do LRC "melhorado" ficam de fora.
            var words = Regex.Replace(line[at..], @"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", "").Trim();
            foreach (var t in times)
                lines.Add((t, words));
        }
        if (lines.Count == 0)
            return null;
        return new Lyrics(lines.Select(l => (Math.Max(0, l.Item1 + offset), l.Item2)).OrderBy(l => l.Item1).ToArray());
    }

    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
