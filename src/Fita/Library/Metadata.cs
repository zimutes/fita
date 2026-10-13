using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Fita.Audio;
using Avalonia.Threading;

namespace Fita.Library;

/// <summary>O que se sabe de um ficheiro: etiquetas e dados técnicos.</summary>
public sealed record TrackMetadata
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public uint TrackNumber { get; init; }
    public uint Year { get; init; }
    public string? Genre { get; init; }
    public double Duration { get; init; }
    public int Bitrate { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int BitsPerSample { get; init; }
    public string Codec { get; init; } = "";
    public double TrackGain { get; init; } = double.NaN;
    public double AlbumGain { get; init; } = double.NaN;
    public double TrackPeak { get; init; } = double.NaN;
    public double AlbumPeak { get; init; } = double.NaN;
    public long FileSize { get; init; }
    public DateTime Modified { get; init; }

    public void ApplyTo(Track track)
    {
        // Num troço de um .cue, os nomes vêm do .cue; o ficheiro só completa o que faltar.
        var part = track.IsPart;
        track.Title = part ? track.Title ?? Title : Title;
        track.Artist = part ? track.Artist ?? Artist : Artist;
        track.Album = part ? track.Album ?? Album : Album;
        track.TrackNumber = part && track.TrackNumber > 0 ? track.TrackNumber : TrackNumber;
        track.Year = part && track.Year > 0 ? track.Year : Year;
        track.Genre = part ? track.Genre ?? Genre : Genre;
        track.Bitrate = Bitrate;
        track.SampleRate = SampleRate;
        track.Channels = Channels;
        track.BitsPerSample = BitsPerSample;
        track.Codec = Codec;
        if (!part || double.IsNaN(track.AlbumGain))
        {
            // O ganho "de faixa" do ficheiro é, num .cue, o do disco inteiro.
            track.TrackGain = part ? double.NaN : TrackGain;
            track.TrackPeak = part ? double.NaN : TrackPeak;
            track.AlbumGain = part && double.IsNaN(AlbumGain) ? TrackGain : AlbumGain;
            track.AlbumPeak = part && double.IsNaN(AlbumPeak) ? TrackPeak : AlbumPeak;
        }
        track.FileSize = FileSize;
        track.Modified = Modified;
        track.Duration = !part ? Duration
            : track.End > 0 ? track.End - track.Start
            : Math.Max(0, Duration - track.Start);
        track.MetadataLoaded = true;
        track.RefreshInfo();
    }
}

public static class MetadataReader
{
    /// <summary>Lê com o TagLib#; se não conhecer o formato, pergunta ao ffprobe.</summary>
    public static TrackMetadata Read(string path, string? ffprobe)
    {
        long size = 0;
        var modified = DateTime.MinValue;
        try
        {
            var info = new FileInfo(path);
            size = info.Length;
            modified = info.LastWriteTimeUtc;
        }
        catch { }

        TrackMetadata? meta = null;
        try
        {
            meta = ReadTagLib(path);
        }
        catch { }
        if (meta is null || meta.Duration <= 0)
        {
            var probed = ffprobe is null ? null : ReadFfprobe(ffprobe, path);
            if (probed is not null)
                meta = meta is null ? probed : meta with
                {
                    Duration = probed.Duration,
                    SampleRate = meta.SampleRate > 0 ? meta.SampleRate : probed.SampleRate,
                    Channels = meta.Channels > 0 ? meta.Channels : probed.Channels,
                    Codec = meta.Codec.Length > 0 ? meta.Codec : probed.Codec,
                };
        }
        meta ??= new TrackMetadata();
        return meta with { FileSize = size, Modified = modified };
    }

    private static TrackMetadata ReadTagLib(string path)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        var p = file.Properties;
        var description = p?.Codecs?.FirstOrDefault(c => c is not null && (c.MediaTypes & TagLib.MediaTypes.Audio) != 0)?.Description ?? "";
        return new TrackMetadata
        {
            Title = Clean(tag.Title),
            Artist = Clean(tag.FirstPerformer) ?? Clean(tag.FirstAlbumArtist),
            Album = Clean(tag.Album),
            TrackNumber = tag.Track,
            Year = tag.Year,
            Genre = Clean(tag.FirstGenre),
            Duration = p?.Duration.TotalSeconds ?? 0,
            Bitrate = p?.AudioBitrate ?? 0,
            SampleRate = p?.AudioSampleRate ?? 0,
            Channels = p?.AudioChannels ?? 0,
            BitsPerSample = p?.BitsPerSample ?? 0,
            Codec = CodecName(path, description),
            TrackGain = tag.ReplayGainTrackGain,
            AlbumGain = tag.ReplayGainAlbumGain,
            TrackPeak = tag.ReplayGainTrackPeak,
            AlbumPeak = tag.ReplayGainAlbumPeak,
        };
    }

    private static TrackMetadata? ReadFfprobe(string ffprobe, string path)
    {
        try
        {
            var start = new ProcessStartInfo(ffprobe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in (string[])["-v", "error", "-protocol_whitelist", "file",
                         "-format_whitelist", FfmpegDecoder.FormatWhitelist, "-select_streams", "a:0",
                         "-show_entries", "format=duration,bit_rate:format_tags=title,artist,album:stream=codec_name,sample_rate,channels",
                         "-of", "json", "file:" + path])
                start.ArgumentList.Add(a);
            using var process = Process.Start(start);
            if (process is null)
                return null;
            var json = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return null;
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            double duration = 0;
            int bitrate = 0, rate = 0, channels = 0;
            string? title = null, artist = null, album = null, codec = null;
            if (root.TryGetProperty("format", out var format))
            {
                duration = Number(format, "duration");
                bitrate = (int)(Number(format, "bit_rate") / 1000);
                if (format.TryGetProperty("tags", out var tags))
                    foreach (var t in tags.EnumerateObject())
                    {
                        var v = Clean(t.Value.GetString());
                        switch (t.Name.ToLowerInvariant())
                        {
                            case "title": title = v; break;
                            case "artist": artist = v; break;
                            case "album": album = v; break;
                        }
                    }
            }
            if (root.TryGetProperty("streams", out var streams) && streams.GetArrayLength() > 0)
            {
                var s = streams[0];
                rate = (int)Number(s, "sample_rate");
                channels = (int)Number(s, "channels");
                if (s.TryGetProperty("codec_name", out var c))
                    codec = c.GetString();
            }
            return new TrackMetadata
            {
                Title = title, Artist = artist, Album = album, Duration = duration, Bitrate = bitrate,
                SampleRate = rate, Channels = channels, Codec = CodecName(path, codec ?? ""),
            };
        }
        catch
        {
            return null;
        }
    }

    private static double Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var v))
            return 0;
        if (v.ValueKind == JsonValueKind.Number)
            return v.GetDouble();
        return double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim().Replace('\0', ' ');

    /// <summary>Nome curto do formato, para o mostrador ("FLAC", "Opus", "AAC"…).</summary>
    public static string CodecName(string path, string description)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var d = description.ToLowerInvariant();
        if (d.Contains("alac")) return "ALAC";
        if (d.Contains("opus")) return "Opus";
        if (d.Contains("vorbis")) return "Vorbis";
        if (d.Contains("flac") && ext != "flac") return "FLAC";
        return ext switch
        {
            "mp3" => "MP3",
            "mp2" => "MP2",
            "flac" => "FLAC",
            "ogg" or "oga" => d.Length > 0 ? description : "Ogg",
            "opus" => "Opus",
            "m4a" or "m4b" or "mp4" or "aac" => d.Contains("alac") ? "ALAC" : "AAC",
            "wav" or "w64" => "WAV",
            "aif" or "aiff" or "aifc" => "AIFF",
            "wma" => "WMA",
            "ape" => "APE",
            "wv" => "WavPack",
            "mpc" => "Musepack",
            "dsf" or "dff" => "DSD",
            "mka" or "webm" => d.Length > 0 ? description.ToUpperInvariant() : "MKA",
            _ => ext.ToUpperInvariant(),
        };
    }
}

/// <summary>
/// Lê etiquetas em segundo plano, duas de cada vez, e aplica-as na thread da interface.
/// A faixa que vai tocar passa à frente.
/// </summary>
public sealed class MetadataLoader : IDisposable
{
    private readonly ConcurrentQueue<Track> _urgent = new();
    private readonly ConcurrentQueue<Track> _normal = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<string?> _ffprobe;
    private int _pending;

    public event Action? Progress;

    public MetadataLoader(Func<string?> ffprobe)
    {
        _ffprobe = ffprobe;
        for (var i = 0; i < 2; i++)
            new Thread(Work) { IsBackground = true, Name = "fita-tags", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public int Pending => Volatile.Read(ref _pending);

    public void Enqueue(IEnumerable<Track> tracks)
    {
        foreach (var t in tracks)
        {
            if (t.MetadataLoaded)
                continue;
            _normal.Enqueue(t);
            Interlocked.Increment(ref _pending);
            _signal.Release();
        }
    }

    public void Prioritize(Track track)
    {
        if (track.MetadataLoaded)
            return;
        _urgent.Enqueue(track);
        Interlocked.Increment(ref _pending);
        _signal.Release();
    }

    private void Work()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                _signal.Wait(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!_urgent.TryDequeue(out var track) && !_normal.TryDequeue(out track))
                continue;
            Interlocked.Decrement(ref _pending);
            if (track.MetadataLoaded)
                continue;
            TrackMetadata meta;
            try
            {
                meta = MetadataReader.Read(track.Path, _ffprobe());
            }
            catch
            {
                meta = new TrackMetadata();
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (!track.MetadataLoaded)
                    meta.ApplyTo(track);
                Progress?.Invoke();
            }, DispatcherPriority.Background);
        }
    }

    public void Dispose() => _cts.Cancel();
}
