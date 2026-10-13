using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Fita.Library;

/// <summary>Uma linha da lista de reprodução.</summary>
public sealed class Track : INotifyPropertyChanged
{
    private string? _title;
    private string? _artist;
    private string? _album;
    private double _duration;
    private string _info = "";
    private bool _isCurrent;
    private bool _failed;
    private int _number;
    private bool _dropTarget;

    public Track(string path)
    {
        Path = path;
    }

    public string Path { get; }

    /// <summary>Faixa de um .cue: começa neste segundo do ficheiro…</summary>
    public double Start { get; init; }

    /// <summary>…e acaba neste (0: no fim do ficheiro).</summary>
    public double End { get; init; }

    /// <summary>Só um troço do ficheiro (faixa de um .cue).</summary>
    public bool IsPart => Start > 0 || End > 0;

    /// <summary>O que distingue a faixa na lista: o caminho e, num troço, onde começa.</summary>
    public string Key => IsPart ? Path + "#" + Start.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : Path;

    public string FileName => IsPart && TrackNumber > 0
        ? $"{System.IO.Path.GetFileNameWithoutExtension(Path)} #{TrackNumber}"
        : System.IO.Path.GetFileNameWithoutExtension(Path);

    public string? Title { get => _title; set => SetDisplay(ref _title, value); }
    public string? Artist { get => _artist; set => SetDisplay(ref _artist, value); }
    public string? Album { get => _album; set => Set(ref _album, value); }
    public uint TrackNumber { get; set; }
    public uint Year { get; set; }
    public string? Genre { get; set; }

    /// <summary>Segundos; 0 se ainda não se sabe.</summary>
    public double Duration
    {
        get => _duration;
        set
        {
            if (Set(ref _duration, value))
                OnPropertyChanged(nameof(DurationText));
        }
    }

    /// <summary>Linha técnica: formato, frequência, bits, canais, débito.</summary>
    public string Info
    {
        get
        {
            if (_info.Length == 0 && MetadataLoaded)
                _info = BuildInfo();
            return _info;
        }
    }

    /// <summary>Volta a montar a linha técnica (depois de ler etiquetas ou de mudar de língua).</summary>
    public void RefreshInfo()
    {
        _info = "";
        OnPropertyChanged(nameof(Info));
    }

    private string BuildInfo()
    {
        var parts = new List<string>();
        if (Codec.Length > 0)
            parts.Add(Codec);
        if (SampleRate > 0)
            parts.Add((SampleRate / 1000.0).ToString(SampleRate % 1000 == 0 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz");
        if (BitsPerSample > 0)
            parts.Add(BitsPerSample + " bit");
        if (Channels > 0)
            parts.Add(Channels switch { 1 => Loc.T("Mono"), 2 => Loc.T("Stereo"), _ => Channels + " ch" });
        if (Bitrate > 0)
            parts.Add(Bitrate + " kbps");
        return string.Join(" · ", parts);
    }

    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public string Codec { get; set; } = "";

    /// <summary>O volume já foi medido (EBU R128) por não haver ReplayGain nas etiquetas.</summary>
    public bool LoudnessScanned { get; set; }

    /// <summary>ReplayGain em dB (NaN se não houver).</summary>
    public double TrackGain { get; set; } = double.NaN;
    public double AlbumGain { get; set; } = double.NaN;
    public double TrackPeak { get; set; } = double.NaN;
    public double AlbumPeak { get; set; } = double.NaN;

    public bool MetadataLoaded { get; set; }
    public long FileSize { get; set; }
    public DateTime Modified { get; set; }

    /// <summary>"Artista – Título", ou o nome do ficheiro se não houver etiquetas.</summary>
    public string Display =>
        string.IsNullOrWhiteSpace(_title) ? FileName
        : string.IsNullOrWhiteSpace(_artist) ? _title!
        : $"{_artist} – {_title}";

    public string DurationText => Duration > 0 ? Format.Time(Duration) : "";

    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    public bool Failed { get => _failed; set => Set(ref _failed, value); }
    public int Number { get => _number; set => Set(ref _number, value); }
    /// <summary>Marca de "largar aqui" ao arrastar dentro da lista.</summary>
    public bool DropTarget { get => _dropTarget; set => Set(ref _dropTarget, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void SetDisplay(ref string? field, string? value, [CallerMemberName] string? name = null)
    {
        if (Set(ref field, value, name))
            OnPropertyChanged(nameof(Display));
    }
}

public static class Format
{
    public static string Time(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>Tempo do mostrador: sempre mm:ss (ou h:mm:ss), com o zero à esquerda.</summary>
    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
