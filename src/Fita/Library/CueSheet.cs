using System.Globalization;
using System.Text;

namespace Fita.Library;

/// <summary>
/// Folhas .cue: um disco inteiro num só ficheiro (FLAC, APE, WAV…) com as faixas marcadas por
/// tempos. Cada faixa vira um troço do ficheiro (<see cref="Track.Start"/> a <see cref="Track.End"/>).
/// A faixa começa no INDEX 01; o intervalo antes dela (INDEX 00) fica no fim da anterior, para o
/// disco correr sem cortes.
/// </summary>
public static class CueSheet
{
    /// <summary>Os tempos do .cue contam em setentas e cincos de segundo (os setores de um CD).</summary>
    private const double FramesPerSecond = 75;

    private sealed class CueTrack
    {
        public required string File { get; init; }
        public uint Number { get; init; }
        public string? Title { get; set; }
        public string? Performer { get; set; }
        public double? Start { get; set; }
        public double TrackGain { get; set; } = double.NaN;
        public double TrackPeak { get; set; } = double.NaN;
    }

    public static List<Track> Read(string cuePath)
    {
        string text;
        try
        {
            text = Decode(File.ReadAllBytes(cuePath));
        }
        catch
        {
            return [];
        }
        return Parse(text, Path.GetDirectoryName(Path.GetFullPath(cuePath)) ?? "");
    }

    public static List<Track> Parse(string text, string folder)
    {
        string? album = null, albumPerformer = null, genre = null, file = null;
        uint year = 0;
        double albumGain = double.NaN, albumPeak = double.NaN;
        var cueTracks = new List<CueTrack>();
        CueTrack? current = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0)
                continue;
            var (command, rest) = SplitCommand(line);
            switch (command)
            {
                case "FILE":
                    file = Resolve(folder, Unquote(LastWordOff(rest)));
                    current = null;
                    break;
                case "TRACK":
                    current = null;
                    // Só faixas de áudio, e só se o ficheiro existe.
                    if (file is null || !rest.EndsWith("AUDIO", StringComparison.OrdinalIgnoreCase))
                        break;
                    uint.TryParse(rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
                    current = new CueTrack { File = file, Number = number };
                    cueTracks.Add(current);
                    break;
                case "TITLE":
                    if (current is null)
                        album = Unquote(rest);
                    else
                        current.Title = Unquote(rest);
                    break;
                case "PERFORMER":
                    if (current is null)
                        albumPerformer = Unquote(rest);
                    else
                        current.Performer = Unquote(rest);
                    break;
                case "INDEX":
                {
                    var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (current is not null && parts.Length == 2 && parts[0] == "01" && ParseTime(parts[1]) is { } start)
                        current.Start = start;
                    break;
                }
                case "REM":
                {
                    var (key, value) = SplitCommand(rest);
                    value = Unquote(value);
                    switch (key)
                    {
                        case "DATE" when value.Length >= 4 && uint.TryParse(value.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y):
                            year = y;
                            break;
                        case "GENRE":
                            genre = value;
                            break;
                        case "REPLAYGAIN_ALBUM_GAIN": albumGain = ParseGain(value); break;
                        case "REPLAYGAIN_ALBUM_PEAK": albumPeak = ParseGain(value); break;
                        case "REPLAYGAIN_TRACK_GAIN" when current is not null: current.TrackGain = ParseGain(value); break;
                        case "REPLAYGAIN_TRACK_PEAK" when current is not null: current.TrackPeak = ParseGain(value); break;
                    }
                    break;
                }
            }
        }

        var result = new List<Track>();
        var valid = cueTracks.Where(t => t.Start is not null).ToList();
        for (var i = 0; i < valid.Count; i++)
        {
            var t = valid[i];
            // Acaba onde começa a seguinte do mesmo ficheiro; a última de cada ficheiro vai até ao fim.
            var next = i + 1 < valid.Count && Playlist.PathComparer.Equals(valid[i + 1].File, t.File) ? valid[i + 1] : null;
            var end = next?.Start ?? 0;
            if (next is not null && end <= t.Start)
                continue;
            result.Add(new Track(t.File)
            {
                Start = t.Start!.Value,
                End = end,
                Title = Clean(t.Title),
                Artist = Clean(t.Performer) ?? Clean(albumPerformer),
                Album = Clean(album),
                TrackNumber = t.Number,
                Year = year,
                Genre = Clean(genre),
                TrackGain = t.TrackGain,
                TrackPeak = t.TrackPeak,
                AlbumGain = albumGain,
                AlbumPeak = albumPeak,
                Duration = end > 0 ? end - t.Start!.Value : 0,
            });
        }
        return result;
    }

    /// <summary>"mm:ss:ff" (os minutos podem passar de 99) em segundos.</summary>
    public static double? ParseTime(string text)
    {
        var parts = text.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var f)
            || m < 0 || s is < 0 or > 59 || f is < 0 or >= 75)
            return null;
        return m * 60 + s + f / FramesPerSecond;
    }

    /// <summary>
    /// UTF-8 (com ou sem BOM) se for válido; senão Windows-1252 / Latin-1, que é como os
    /// programas de ripar antigos (EAC) gravavam.
    /// </summary>
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

    /// <summary>
    /// O ficheiro do FILE, só se for local e de áudio. Muitos .cue foram feitos para um .wav que
    /// depois foi convertido: se o nome não existir, serve o mesmo nome com outra extensão de áudio.
    /// </summary>
    private static string? Resolve(string folder, string entry)
    {
        if (entry.Length == 0 || entry.Contains("://", StringComparison.Ordinal))
            return null;
        try
        {
            if (!OperatingSystem.IsWindows())
                entry = entry.Replace('\\', '/');
            var full = Path.IsPathFullyQualified(entry) ? entry : Path.GetFullPath(Path.Combine(folder, entry));
            if (File.Exists(full) && PlaylistFiles.IsAudio(full))
                return full;
            var dir = Path.GetDirectoryName(full);
            var name = Path.GetFileNameWithoutExtension(full);
            if (dir is null || !Directory.Exists(dir))
                return null;
            return Directory.EnumerateFiles(dir, name + ".*")
                .Where(f => PlaylistFiles.IsAudio(f) && Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, NaturalComparer.Instance)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static (string Command, string Args) SplitCommand(string line)
    {
        var space = line.IndexOf(' ');
        return space < 0 ? (line.ToUpperInvariant(), "") : (line[..space].ToUpperInvariant(), line[(space + 1)..].Trim());
    }

    /// <summary>Tira a última palavra (o tipo do FILE: WAVE, MP3, BINARY…).</summary>
    private static string LastWordOff(string text)
    {
        var space = text.LastIndexOf(' ');
        return space > 0 && !text.EndsWith('"') ? text[..space].Trim() : text;
    }

    private static string Unquote(string text)
    {
        text = text.Trim();
        return text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
    }

    private static double ParseGain(string text)
    {
        var number = text.Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
