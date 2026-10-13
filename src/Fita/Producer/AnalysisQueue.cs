using System.Text.Json;
using System.Text.Json.Serialization;
using Fita.Library;
using Avalonia.Threading;

namespace Fita.Producer;

/// <summary>
/// As análises já feitas, em analysis.json (na pasta das definições), pela chave da faixa. Uma
/// análise só vale enquanto o ficheiro tiver o mesmo tamanho e a mesma data.
/// </summary>
public sealed class AnalysisStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly object _lock = new();
    private readonly Dictionary<string, TrackAnalysis> _items;
    private readonly string _file;

    private AnalysisStore(string file, Dictionary<string, TrackAnalysis> items)
    {
        _file = file;
        _items = items;
    }

    public static string FilePath => Path.Combine(SettingsStore.Folder, "analysis.json");

    public static AnalysisStore Load(string? file = null)
    {
        file ??= FilePath;
        try
        {
            if (File.Exists(file))
            {
                var items = JsonSerializer.Deserialize<Dictionary<string, TrackAnalysis>>(File.ReadAllText(file), Options);
                if (items is not null)
                    return new AnalysisStore(file, items);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Falha a ler as análises: " + ex.Message);
        }
        return new AnalysisStore(file, []);
    }

    /// <summary>A análise guardada, se ainda serve para o ficheiro como está agora.</summary>
    public TrackAnalysis? Get(Track track)
    {
        TrackAnalysis? a;
        lock (_lock)
            if (!_items.TryGetValue(track.Key, out a))
                return null;
        return a.Version == TrackAnalysis.CurrentVersion && Stamp(track.Path) is { } s && s.Size == a.Size && s.Modified == a.Modified
            ? a
            : null;
    }

    public void Put(Track track, TrackAnalysis analysis)
    {
        lock (_lock)
            _items[track.Key] = analysis;
    }

    public void Save()
    {
        try
        {
            string json;
            lock (_lock)
                json = JsonSerializer.Serialize(_items, Options);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write("Falha a gravar as análises: " + ex.Message);
        }
    }

    public static (long Size, DateTime Modified)? Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Analisa as faixas em segundo plano, uma de cada vez (o ffmpeg com prioridade baixa), pela ordem
/// em que se pedem; as que se pedem com <c>urgent</c> passam para a frente. O resultado chega à
/// interface por <see cref="Analyzed"/> e fica na cache.
/// </summary>
public sealed class AnalysisQueue : IDisposable
{
    private readonly Func<string?> _ffmpeg;
    private readonly AnalysisStore _store;
    private readonly object _lock = new();
    private readonly LinkedList<Track> _queue = new();
    private readonly HashSet<Track> _queued = [];
    private readonly Dictionary<Track, TaskCompletionSource<TrackAnalysis?>> _waiting = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private Track? _working;
    private long _lastSave;

    /// <summary>Uma faixa ficou analisada (ou não se conseguiu: null), na thread da interface.</summary>
    public event Action<Track, TrackAnalysis?>? Analyzed;

    public AnalysisQueue(Func<string?> ffmpeg, AnalysisStore store)
    {
        _ffmpeg = ffmpeg;
        _store = store;
        new Thread(Work) { IsBackground = true, Name = "fita-analysis", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public AnalysisStore Store => _store;

    /// <summary>Quantas faixas faltam (a que está a ser analisada incluída).</summary>
    public int Pending
    {
        get
        {
            lock (_lock)
                return _queue.Count + (_working is null ? 0 : 1);
        }
    }

    /// <summary>A análise já feita (da cache), ou null.</summary>
    public TrackAnalysis? Cached(Track track) => _store.Get(track);

    /// <summary>
    /// Põe faixas na fila. A cache vê-se na thread de trabalho (cada faixa é um acesso ao disco):
    /// as que já estão analisadas saem logo, sem ffmpeg.
    /// </summary>
    public void Request(IEnumerable<Track> tracks, bool urgent = false)
    {
        var wanted = tracks.ToList();
        if (wanted.Count == 0)
            return;
        lock (_lock)
        {
            if (urgent)
            {
                for (var i = wanted.Count - 1; i >= 0; i--)
                {
                    var t = wanted[i];
                    if (!_queued.Add(t))
                        _queue.Remove(t);
                    _queue.AddFirst(t);
                }
            }
            else
            {
                foreach (var t in wanted)
                    if (_queued.Add(t))
                        _queue.AddLast(t);
            }
        }
        _signal.Release();
    }

    /// <summary>Espera pela análise desta faixa (da cache, se já houver).</summary>
    public Task<TrackAnalysis?> GetAsync(Track track)
    {
        if (_store.Get(track) is { } done)
            return Task.FromResult<TrackAnalysis?>(done);
        TaskCompletionSource<TrackAnalysis?> tcs;
        lock (_lock)
        {
            if (!_waiting.TryGetValue(track, out tcs!))
            {
                tcs = new TaskCompletionSource<TrackAnalysis?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting[track] = tcs;
            }
        }
        Request([track]);
        return tcs.Task;
    }

    /// <summary>Esquece o que está na fila (a que está a meio acaba).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
            _queued.Clear();
        }
    }

    private void Work()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            Track? track;
            lock (_lock)
            {
                track = _queue.First?.Value;
                if (track is not null)
                {
                    _queue.RemoveFirst();
                    _queued.Remove(track);
                    _working = track;
                }
            }
            if (track is null)
            {
                if (_lastSave != 0)
                {
                    _store.Save();
                    _lastSave = 0;
                }
                try
                {
                    _signal.Wait(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            var result = _store.Get(track);
            if (result is null && _ffmpeg() is { } ffmpeg && AnalysisStore.Stamp(track.Path) is { } stamp)
            {
                result = Analyzer.Analyze(ffmpeg, track.Path, track.Start, track.End, track.SampleRate, track.Channels, token);
                if (token.IsCancellationRequested)
                    return;
                if (result is not null)
                {
                    result.Size = stamp.Size;
                    result.Modified = stamp.Modified;
                    _store.Put(track, result);
                    // Grava de tempos a tempos durante uma lista grande, e sempre no fim.
                    if (_lastSave == 0)
                        _lastSave = Environment.TickCount64;
                    else if (Environment.TickCount64 - _lastSave > 15_000)
                    {
                        _store.Save();
                        _lastSave = Environment.TickCount64;
                    }
                }
            }
            TaskCompletionSource<TrackAnalysis?>? waiter;
            lock (_lock)
            {
                _working = null;
                if (_waiting.Remove(track, out waiter)) { }
            }
            waiter?.TrySetResult(result);
            var done = result;
            Dispatcher.UIThread.Post(() => Analyzed?.Invoke(track, done));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_lastSave != 0)
            _store.Save();
    }
}
