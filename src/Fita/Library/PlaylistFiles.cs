using System.Globalization;
using System.Text;

namespace Fita.Library;

/// <summary>Que ficheiros entram na lista e como se leem e escrevem listas M3U/PLS.</summary>
public static class PlaylistFiles
{
    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".mp2", ".flac", ".ogg", ".oga", ".opus", ".wav", ".w64", ".aif", ".aiff", ".aifc",
        ".m4a", ".m4b", ".mp4", ".aac", ".wma", ".ape", ".wv", ".mka", ".webm", ".mpc", ".tta",
        ".ac3", ".eac3", ".dts", ".dsf", ".dff", ".caf", ".amr", ".au", ".tak", ".shn",
        // Tracker e consolas (o ffmpeg traz o libopenmpt e o libgme na maioria das versões).
        ".mod", ".xm", ".it", ".s3m", ".mptm", ".669", ".mtm", ".stm", ".nsf", ".spc", ".vgm", ".vgz", ".gbs",
    };

    public static readonly HashSet<string> PlaylistExtensions = new(StringComparer.OrdinalIgnoreCase) { ".m3u", ".m3u8", ".pls" };

    public static bool IsAudio(string path) => AudioExtensions.Contains(Path.GetExtension(path));
    public static bool IsPlaylist(string path) => PlaylistExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Transforma ficheiros, pastas (com subpastas) e listas em faixas, pela ordem natural dos
    /// caminhos dentro de cada pasta. Corre fora da thread da interface (pastas grandes demoram).
    /// </summary>
    public static List<Track> Expand(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var result = new List<Track>();
        foreach (var raw in paths)
        {
            ct.ThrowIfCancellationRequested();
            string path;
            try
            {
                path = Path.GetFullPath(raw);
            }
            catch
            {
                continue;
            }
            if (Directory.Exists(path))
            {
                foreach (var file in EnumerateFolder(path, ct))
                    result.Add(new Track(file));
            }
            else if (IsPlaylist(path) && File.Exists(path))
                result.AddRange(Read(path));
            else if (IsAudio(path))
                result.Add(new Track(path));
        }
        return result;
    }

    private static IEnumerable<string> EnumerateFolder(string root, CancellationToken ct)
    {
        var files = new List<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false,
        };
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (IsAudio(file))
                    files.Add(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        files.Sort(NaturalComparer.Instance);
        return files;
    }

    /// <summary>Lê uma lista M3U/M3U8/PLS. Caminhos relativos contam a partir da pasta da lista.</summary>
    public static List<Track> Read(string playlist)
    {
        var tracks = new List<Track>();
        string[] lines;
        try
        {
            // M3U antigo costuma vir em ANSI; o .m3u8 é sempre UTF-8. Na dúvida, UTF-8.
            lines = File.ReadAllLines(playlist, Encoding.UTF8);
        }
        catch
        {
            return tracks;
        }
        var folder = Path.GetDirectoryName(Path.GetFullPath(playlist)) ?? "";

        if (Path.GetExtension(playlist).Equals(".pls", StringComparison.OrdinalIgnoreCase))
        {
            var files = new SortedDictionary<int, string>();
            var titles = new Dictionary<int, string>();
            var lengths = new Dictionary<int, double>();
            foreach (var line in lines)
            {
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if (TryIndex(key, "File", out var n)) files[n] = value;
                else if (TryIndex(key, "Title", out n)) titles[n] = value;
                else if (TryIndex(key, "Length", out n) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var len)) lengths[n] = len;
            }
            foreach (var (n, file) in files)
            {
                var resolved = Resolve(folder, file);
                if (resolved is null)
                    continue;
                var t = new Track(resolved);
                if (titles.TryGetValue(n, out var title))
                    ApplyDisplayTitle(t, title);
                if (lengths.TryGetValue(n, out var len) && len > 0)
                    t.Duration = len;
                tracks.Add(t);
            }
            return tracks;
        }

        string? pendingTitle = null;
        double pendingLength = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0)
                continue;
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line[8..];
                var comma = rest.IndexOf(',');
                if (comma >= 0)
                {
                    double.TryParse(rest[..comma].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out pendingLength);
                    pendingTitle = rest[(comma + 1)..].Trim();
                }
                continue;
            }
            if (line.StartsWith('#'))
                continue;
            var path = Resolve(folder, line);
            if (path is not null)
            {
                var t = new Track(path);
                if (!string.IsNullOrEmpty(pendingTitle))
                    ApplyDisplayTitle(t, pendingTitle);
                if (pendingLength > 0)
                    t.Duration = pendingLength;
                tracks.Add(t);
            }
            pendingTitle = null;
            pendingLength = 0;
        }
        return tracks;
    }

    /// <summary>Escreve M3U8 (UTF-8) ou PLS, conforme a extensão. Caminhos relativos quando dá.</summary>
    public static void Write(string playlist, IEnumerable<Track> tracks)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(playlist)) ?? "";
        var sb = new StringBuilder();
        var list = tracks.ToList();
        if (Path.GetExtension(playlist).Equals(".pls", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("[playlist]\n");
            for (var i = 0; i < list.Count; i++)
            {
                var n = i + 1;
                sb.Append($"File{n}={Relative(folder, list[i].Path)}\n");
                sb.Append($"Title{n}={list[i].Display}\n");
                sb.Append(CultureInfo.InvariantCulture, $"Length{n}={(list[i].Duration > 0 ? (int)Math.Round(list[i].Duration) : -1)}\n");
            }
            sb.Append($"NumberOfEntries={list.Count}\nVersion=2\n");
        }
        else
        {
            sb.Append("#EXTM3U\n");
            foreach (var t in list)
            {
                sb.Append(CultureInfo.InvariantCulture, $"#EXTINF:{(t.Duration > 0 ? (int)Math.Round(t.Duration) : -1)},{t.Display}\n");
                sb.Append(Relative(folder, t.Path)).Append('\n');
            }
        }
        var tmp = playlist + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, playlist, overwrite: true);
    }

    private static bool TryIndex(string key, string prefix, out int n)
    {
        n = 0;
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
    }

    /// <summary>Só ficheiros locais: endereços (http:, ftp:…) ficam de fora.</summary>
    private static string? Resolve(string folder, string entry)
    {
        entry = entry.Trim().Trim('"');
        if (entry.Length == 0)
            return null;
        if (entry.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                entry = new Uri(entry).LocalPath;
            }
            catch
            {
                return null;
            }
        }
        else if (entry.Contains("://", StringComparison.Ordinal))
            return null;
        try
        {
            if (!OperatingSystem.IsWindows())
                entry = entry.Replace('\\', '/');
            var full = Path.IsPathFullyQualified(entry) ? entry : Path.GetFullPath(Path.Combine(folder, entry));
            return IsAudio(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Relative(string folder, string path)
    {
        var rel = Path.GetRelativePath(folder, path);
        return rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(rel) ? path : rel;
    }

    /// <summary>O título do #EXTINF costuma ser "Artista - Título".</summary>
    private static void ApplyDisplayTitle(Track track, string title)
    {
        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            track.Artist = title[..dash].Trim();
            track.Title = title[(dash + 3)..].Trim();
        }
        else
            track.Title = title;
    }
}
