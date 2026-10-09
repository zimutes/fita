namespace Fita.Audio;

/// <summary>
/// Procura o ffmpeg (e o ffprobe ao lado dele), só em sítios que um pacote de instalação cria:
/// nunca na pasta atual nem solto ao lado da app (que pode estar nas Transferências).
/// </summary>
public static class FfmpegLocator
{
    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    public static string? FindFfmpeg(string? chosen)
    {
        foreach (var candidate in Candidates(chosen))
        {
            try
            {
                // Só caminhos absolutos: um relativo seria resolvido contra a pasta atual.
                if (!string.IsNullOrWhiteSpace(candidate) && Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch { }
        }
        return null;
    }

    /// <summary>O ffprobe serve de recurso para ler duração e etiquetas que o TagLib não conhece.</summary>
    public static string? FindFfprobe(string? ffmpeg)
    {
        if (ffmpeg is null)
            return null;
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", Exe("ffprobe"));
        return File.Exists(probe) ? probe : null;
    }

    private static IEnumerable<string?> Candidates(string? chosen)
    {
        yield return chosen;

        if (OperatingSystem.IsWindows())
        {
            // winget (Gyan.FFmpeg e afins), na pasta do próprio utilizador.
            var winget = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
            foreach (var exe in SafeFind(winget, "*FFmpeg*"))
                yield return exe;

            // Chocolatey: o bin\ffmpeg.exe é um intermediário ("shim"); o executável real está em lib.
            // Só pela variável ChocolateyInstall: um caminho fixo em C:\ProgramData podia ser criado
            // por qualquer utilizador num PC sem Chocolatey.
            if (Environment.GetEnvironmentVariable("ChocolateyInstall") is { Length: > 0 } choco)
                foreach (var package in new[] { "ffmpeg", "ffmpeg-full", "ffmpeg-shared" })
                    yield return Path.Combine(choco, "lib", package, "tools", "ffmpeg", "bin", "ffmpeg.exe");

            // Scoop.
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe");
        }

        // Só entradas absolutas: uma entrada relativa (".") apontaria para a pasta atual, que pode
        // ser a dos ficheiros abertos (por exemplo, as Transferências).
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dir = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
            if (Path.IsPathFullyQualified(dir))
                yield return Path.Combine(dir, Exe("ffmpeg"));
        }

        if (!OperatingSystem.IsWindows())
            foreach (var dir in new[] { "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin", "/snap/bin" })
                yield return Path.Combine(dir, "ffmpeg");

        // Por último, uma cópia portátil numa subpasta da app (uma transferência solta não cria subpastas).
        yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin", Exe("ffmpeg"));
    }

    private static List<string> SafeFind(string root, string folderPattern)
    {
        var found = new List<string>();
        try
        {
            if (!Directory.Exists(root))
                return found;
            foreach (var dir in Directory.EnumerateDirectories(root, folderPattern))
                found.AddRange(Directory.EnumerateFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories));
        }
        catch { }
        return found;
    }
}
