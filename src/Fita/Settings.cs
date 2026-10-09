using System.Text.Json;
using Fita.Audio;
using Fita.Library;

namespace Fita;

public enum VisMode { Spectrum, Scope, Off }

public enum ReplayGainMode { Off, Track, Album }

/// <summary>Escolhas que ficam de uma vez para a outra.</summary>
public sealed class AppSettings
{
    /// <summary>"en" (por omissão) ou "pt".</summary>
    public string Language { get; set; } = "en";
    public string Theme { get; set; } = "purple";
    /// <summary>Posição do controlo de volume, de 0 a 100.</summary>
    public double Volume { get; set; } = 80;
    public double Balance { get; set; }
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; }
    public EqSettings Eq { get; set; } = new();
    public List<EqUserPreset> EqPresets { get; set; } = [];
    public bool ShowEq { get; set; } = true;
    public bool ShowPlaylist { get; set; } = true;
    public bool Compact { get; set; }
    public bool AlwaysOnTop { get; set; }
    public VisMode Vis { get; set; } = VisMode.Spectrum;
    public bool TimeRemaining { get; set; }
    public ReplayGainMode ReplayGain { get; set; }
    /// <summary>Caminho escolhido à mão para o ffmpeg (vazio: procurar sozinho).</summary>
    public string FfmpegPath { get; set; } = "";
    /// <summary>Vazio: o dispositivo de som por omissão.</summary>
    public string OutputDevice { get; set; } = "";
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double WindowWidth { get; set; } = 500;
    public double PlaylistHeight { get; set; } = 260;
    /// <summary>Continuar a faixa onde ficou da última vez.</summary>
    public bool Resume { get; set; } = true;
    public string LastFolder { get; set; } = "";
}

public sealed class EqUserPreset
{
    public string Name { get; set; } = "";
    public double Preamp { get; set; }
    public List<EqBand> Bands { get; set; } = [];
}

/// <summary>
/// Guarda as definições em %APPDATA%\Fita (Windows) ou ~/.config/fita (Linux).
/// A variável FITA_DATA permite usar outra pasta (testes, modo portátil).
/// </summary>
public static class SettingsStore
{
    public static readonly string Folder =
        Environment.GetEnvironmentVariable("FITA_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                OperatingSystem.IsWindows() ? "Fita" : "fita");

    public static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
                s.Eq ??= new EqSettings();
                s.Eq.Sanitize();
                s.EqPresets ??= [];
                s.Volume = Math.Clamp(double.IsFinite(s.Volume) ? s.Volume : 80, 0, 100);
                s.Balance = Math.Clamp(double.IsFinite(s.Balance) ? s.Balance : 0, -1, 1);
                s.WindowWidth = Math.Clamp(double.IsFinite(s.WindowWidth) ? s.WindowWidth : 500, 420, 1600);
                s.PlaylistHeight = Math.Clamp(double.IsFinite(s.PlaylistHeight) ? s.PlaylistHeight : 260, 120, 2000);
                return s;
            }
        }
        catch (Exception ex)
        {
            Log.Write("Falha a ler as definições: " + ex.Message);
        }
        return new AppSettings();
    }

    private static string? _lastSaved;

    /// <summary>Grava só se mudou alguma coisa desde a última vez (é chamado de tempos a tempos).</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, Options);
            if (json == _lastSaved && File.Exists(FilePath))
                return;
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
            _lastSaved = json;
        }
        catch (Exception ex)
        {
            Log.Write("Falha a gravar as definições: " + ex.Message);
        }
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                var path = Path.Combine(SettingsStore.Folder, "log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
