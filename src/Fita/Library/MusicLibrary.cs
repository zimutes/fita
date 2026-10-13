using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;

namespace Fita.Library;

/// <summary>Um disco da biblioteca: as faixas de um álbum numa pasta.</summary>
public sealed class LibraryAlbum
{
    public required string Folder { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public uint Year { get; init; }
    public required List<PlaylistStore.Entry> Tracks { get; init; }
    public double Duration => Tracks.Sum(t => t.Duration);
    /// <summary>Texto onde a procura procura (minúsculas, sem acentos).</summary>
    public required string SearchText { get; init; }
}

/// <summary>
/// A biblioteca: as faixas das pastas de música escolhidas, com as etiquetas lidas, guardadas
/// em library.json. Ao abrir lê o índice e volta a ver as pastas em segundo plano (só lê
/// etiquetas de ficheiros novos ou mudados); depois fica atenta às mudanças nas pastas.
/// </summary>
public sealed class MusicLibrary : IDisposable
{
    private sealed class Data
    {
        public int Version { get; set; } = 1;
        public List<PlaylistStore.Entry> Tracks { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly Func<string?> _ffprobe;
    private readonly List<FileSystemWatcher> _watchers = [];
    private List<string> _folders = [];
    private List<PlaylistStore.Entry> _tracks = [];
    private CancellationTokenSource? _scan;
    private DispatcherTimer? _rescanSoon;

    public static string FilePath => Path.Combine(SettingsStore.Folder, "library.json");

    /// <summary>Os discos, por artista e álbum. Muda inteira quando a biblioteca muda.</summary>
    public IReadOnlyList<LibraryAlbum> Albums { get; private set; } = [];
    public int TrackCount => _tracks.Count;
    private int _pendingTags;

    /// <summary>Ficheiros ainda por ler numa varredura (0: parada). A thread de fundo é que o mexe.</summary>
    public int Scanning => Volatile.Read(ref _pendingTags);

    /// <summary>A biblioteca mudou (na thread da interface).</summary>
    public event Action? Changed;

    public MusicLibrary(Func<string?> ffprobe) => _ffprobe = ffprobe;

    public IReadOnlyList<string> Folders => _folders;

    /// <summary>Abre o índice gravado e volta a ver as pastas.</summary>
    public void Start(IEnumerable<string> folders)
    {
        _folders = folders.Where(Directory.Exists).Distinct(Playlist.PathComparer).ToList();
        try
        {
            if (File.Exists(FilePath))
                _tracks = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath), Options)?.Tracks ?? [];
        }
        catch (Exception ex)
        {
            Log.Write("Falha a ler a biblioteca: " + ex.Message);
        }
        Rebuild();
        Rescan();
    }

    public void SetFolders(IEnumerable<string> folders)
    {
        _folders = folders.Where(Directory.Exists).Distinct(Playlist.PathComparer).ToList();
        Rescan();
    }

    /// <summary>Volta a ver as pastas todas (o que não mudou não se volta a ler).</summary>
    public void Rescan()
    {
        _scan?.Cancel();
        var cancel = _scan = new CancellationTokenSource();
        var folders = _folders.ToList();
        var known = _tracks.ToDictionary(t => Key(t), Playlist.PathComparer);
        Watch(folders);
        _ = Task.Run(() => Scan(folders, known, cancel.Token)).ContinueWith(t =>
        {
            if (cancel.IsCancellationRequested || !t.IsCompletedSuccessfully)
                return;
            _tracks = t.Result;
            Rebuild();
            Save();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private List<PlaylistStore.Entry> Scan(List<string> folders, Dictionary<string, PlaylistStore.Entry> known, CancellationToken ct)
    {
        try
        {
            return ScanFolders(folders, known, ct);
        }
        finally
        {
            Interlocked.Exchange(ref _pendingTags, 0);
        }
    }

    private List<PlaylistStore.Entry> ScanFolders(List<string> folders, Dictionary<string, PlaylistStore.Entry> known, CancellationToken ct)
    {
        var found = PlaylistFiles.Expand(folders, ct);
        var result = new List<PlaylistStore.Entry>(found.Count);
        var toRead = new List<Track>();
        foreach (var track in found)
        {
            long size = 0;
            var modified = DateTime.MinValue;
            try
            {
                var info = new FileInfo(track.Path);
                size = info.Length;
                modified = info.LastWriteTimeUtc;
            }
            catch { }
            if (known.TryGetValue(track.Key, out var old) && old.Size == size && old.Modified == modified && old.Loaded)
                result.Add(old);
            else
                toRead.Add(track);
        }
        Interlocked.Exchange(ref _pendingTags, toRead.Count);
        if (toRead.Count > 0)
            Dispatcher.UIThread.Post(() => Changed?.Invoke());
        // Etiquetas em paralelo, mas pouco: é um trabalho de fundo.
        var read = new PlaylistStore.Entry[toRead.Count];
        var done = 0;
        Parallel.For(0, toRead.Count, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, i =>
        {
            var t = toRead[i];
            var meta = MetadataReader.Read(t.Path, _ffprobe());
            meta.ApplyTo(t);
            read[i] = PlaylistStore.ToEntry(t);
            Interlocked.Decrement(ref _pendingTags);
            if (Interlocked.Increment(ref done) % 50 == 0)
                Dispatcher.UIThread.Post(() => Changed?.Invoke());
        });
        result.AddRange(read);
        return result;
    }

    private static string Key(PlaylistStore.Entry e) =>
        e.Start is > 0 ? e.Path + "#" + e.Start.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : e.Path;

    /// <summary>Agrupa as faixas em discos: o mesmo álbum na mesma pasta (assim as compilações não se partem).</summary>
    private void Rebuild()
    {
        Albums = _tracks
            .GroupBy(t => (Folder: Path.GetDirectoryName(t.Path) ?? "", Album: (t.Album ?? "").Trim().ToLowerInvariant()))
            .Select(g =>
            {
                var tracks = g.OrderBy(t => t.TrackNumber == 0 ? uint.MaxValue : t.TrackNumber)
                    .ThenBy(t => t.Path, NaturalComparer.Instance).ThenBy(t => t.Start ?? 0).ToList();
                var artists = tracks.Select(t => t.Artist).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
                var title = tracks.Select(t => t.Album).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                    ?? Path.GetFileName(g.Key.Folder);
                var artist = artists.Count switch { 0 => "", 1 => artists[0]!, _ => Loc.T("VariousArtists") };
                return new LibraryAlbum
                {
                    Folder = g.Key.Folder,
                    Title = title,
                    Artist = artist,
                    Year = tracks.Select(t => t.Year).FirstOrDefault(y => y > 0),
                    Tracks = tracks,
                    SearchText = Fold(string.Join(" ", [artist, title, .. tracks.Select(t => t.Title ?? ""), .. artists!])),
                };
            })
            .OrderBy(a => a.Artist == Loc.T("VariousArtists") ? 1 : 0)
            .ThenBy(a => a.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Year)
            .ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Changed?.Invoke();
    }

    /// <summary>Minúsculas e sem acentos, para "cancao" encontrar "Canção".</summary>
    public static string Fold(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Data { Tracks = _tracks }, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write("Falha a gravar a biblioteca: " + ex.Message);
        }
    }

    /// <summary>Uma mudança nas pastas volta a ver tudo, mas só passado um bocado (as cópias vêm aos poucos).</summary>
    private void Watch(List<string> folders)
    {
        foreach (var w in _watchers)
            w.Dispose();
        _watchers.Clear();
        foreach (var folder in folders)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
                FileSystemEventHandler changed = (_, e) =>
                {
                    if (PlaylistFiles.IsAudio(e.FullPath) || Path.GetExtension(e.FullPath).Equals(".cue", StringComparison.OrdinalIgnoreCase)
                        || Directory.Exists(e.FullPath) || e.ChangeType == WatcherChangeTypes.Deleted)
                        Dispatcher.UIThread.Post(RescanSoon);
                };
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Changed += changed;
                watcher.Renamed += (s, e) => changed(s, e);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Log.Write($"Biblioteca, não consigo vigiar {folder}: {ex.Message}");
            }
        }
    }

    private void RescanSoon()
    {
        if (_rescanSoon is null)
        {
            _rescanSoon = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _rescanSoon.Tick += (_, _) =>
            {
                _rescanSoon.Stop();
                Rescan();
            };
        }
        _rescanSoon.Stop();
        _rescanSoon.Start();
    }

    public void Dispose()
    {
        _scan?.Cancel();
        _rescanSoon?.Stop();
        foreach (var w in _watchers)
            w.Dispose();
    }
}
