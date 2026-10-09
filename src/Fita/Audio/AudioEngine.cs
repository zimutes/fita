using System.Collections.Concurrent;

namespace Fita.Audio;

public enum PlayState { Stopped, Playing, Paused }

/// <summary>O que tocar: o item da lista (opaco para o motor), o ficheiro e o ganho (ReplayGain).</summary>
public sealed record PlayItem(object? Tag, string Path, float Gain = 1f);

/// <summary>Um troço contínuo de som: uma faixa a partir de um certo segundo.</summary>
public sealed class Segment(PlayItem item, double startSeconds)
{
    public PlayItem Item { get; } = item;
    public double StartSeconds { get; } = startSeconds;
}

/// <summary>Fotografia do motor, tirada de uma vez para o estado, a faixa e a posição baterem certo.</summary>
public readonly record struct EngineSnapshot(PlayState State, Segment? Segment, double Position);

/// <summary>
/// Motor de reprodução. Uma thread de descodificação (ffmpeg) enche uma fila com ~2 s de som;
/// a thread de saída tira blocos dessa fila, passa-os pelo EQ, volume e limitador, e entrega-os
/// ao OpenAL. Quando uma faixa acaba, a descodificação pede logo a seguinte e encosta-a à
/// anterior na mesma fila: as passagens são sem silêncio (gapless).
/// A interface nunca espera pelas threads: os comandos só mudam estado e acordam a saída.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public const int BlockFrames = 1024;
    private const int ChunkFrames = 4096;
    private const double BufferAheadSeconds = 2.0;

    private sealed class Chunk(int gen, float[] data, int frames)
    {
        public int Gen { get; } = gen;
        public float[] Data { get; } = data;
        public int Frames { get; } = frames;
        public Segment? Start { get; init; }
        public bool End { get; init; }
    }

    // ── Estado partilhado (sempre sob _sync) ─────────────────────────────────
    private readonly object _sync = new();
    private readonly Queue<Chunk> _queue = new();
    private long _queuedFrames;
    private int _gen;
    private bool _flush;
    private PlayState _state;
    private Segment? _pending;
    private readonly List<(long Frame, Segment Segment)> _boundaries = [];
    private CancellationTokenSource? _decoderCts;
    private bool _stopAfterCurrent;
    private int _endedCount;
    private bool _endedByStopAfter;
    private string? _deviceRequest;
    private bool _deviceChangePending;
    private bool _restartRequested;

    // ── Thread de saída ───────────────────────────────────────────────────────
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private volatile bool _disposed;
    private AlOutput? _out;
    private volatile int _rate = 48000;
    private long _consumed;
    private long _completed;
    private long _played;
    private bool _endReached;
    private Chunk? _carry;
    private int _carryPos;
    private readonly Queue<int> _framesPerBuffer = new();
    private readonly EqProcessor _eq = new();
    private EqSettings _eqSettings = new();
    private float _gainL = 1, _gainR = 1;
    private float _limiter = 1;
    private long _limitUntil;
    private string? _deviceName;

    // ── Histórico para o visualizador (mono, depois do EQ e antes do volume) ──
    private readonly object _visLock = new();
    private readonly float[] _vis = new float[1 << 15];
    private long _visWritten;

    private volatile float _volume = 0.8f;
    private volatile float _balance;

    /// <summary>Para os testes: tudo funciona, mas o OpenAL toca com ganho zero.</summary>
    public bool Silent { get; init; }

    public string? FfmpegPath { get; set; }

    /// <summary>
    /// Chamado na thread de descodificação quando uma faixa acaba de ser lida: devolve a que se
    /// segue (ou null para parar). Quem implementa trata de passar para a thread da interface.
    /// </summary>
    public Func<PlayItem, CancellationToken, PlayItem?>? NextItem { get; set; }

    /// <summary>Faixas que não se conseguiram ler (o motor salta para a seguinte).</summary>
    public ConcurrentQueue<(PlayItem Item, string Message)> Failures { get; } = new();

    public string? OutputError { get; private set; }
    public int SampleRate => _rate;

    public AudioEngine(string? deviceName = null)
    {
        _deviceName = deviceName;
        _thread = new Thread(Run) { IsBackground = true, Name = "fita-output", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Ganho linear do volume (0 a 1); a curva do controlo fica do lado da interface.</summary>
    public float Volume { get => _volume; set => _volume = Math.Clamp(value, 0f, 1f); }

    /// <summary>-1 (só esquerda) a 1 (só direita).</summary>
    public float Balance { get => _balance; set => _balance = Math.Clamp(value, -1f, 1f); }

    /// <summary>O limitador está a baixar o som para não saturar (nos últimos ~300 ms).</summary>
    public bool LimiterActive => Environment.TickCount64 < Interlocked.Read(ref _limitUntil);

    /// <summary>Parar no fim da faixa atual. Desliga-se sozinho quando atua.</summary>
    public bool StopAfterCurrent
    {
        get { lock (_sync) return _stopAfterCurrent; }
        set { lock (_sync) _stopAfterCurrent = value; }
    }

    /// <summary>Sobe sempre que a reprodução chega ao fim sozinha (fim da lista ou "parar depois").</summary>
    public int EndedCount { get { lock (_sync) return _endedCount; } }

    /// <summary>O último fim foi pedido com "parar depois desta" (e não por a lista ter acabado).</summary>
    public bool EndedByStopAfter { get { lock (_sync) return _endedByStopAfter; } }

    public void ConfigureEq(EqSettings settings)
    {
        var copy = settings.Clone();
        lock (_sync)
            _eqSettings = copy;
        if (_ready.IsSet)
            _eq.Configure(copy, _rate);
    }

    public static List<string> OutputDevices() => AlOutput.ListDevices();

    /// <summary>null: o dispositivo por omissão.</summary>
    public void SetOutputDevice(string? name)
    {
        lock (_sync)
        {
            _deviceRequest = name;
            _deviceChangePending = true;
        }
        _wake.Set();
    }

    /// <summary>
    /// O dispositivo mudou de frequência ou foi reaberto e a fila perdeu-se: quem chama deve
    /// voltar a pedir a faixa na posição atual. Verdadeiro uma vez por ocorrência.
    /// </summary>
    public bool ConsumeRestartRequest()
    {
        lock (_sync)
        {
            var r = _restartRequested;
            _restartRequested = false;
            return r;
        }
    }

    // ── Comandos ──────────────────────────────────────────────────────────────

    public void Play(PlayItem item, double startSeconds = 0, bool paused = false)
    {
        int gen;
        CancellationTokenSource cts;
        var start = Math.Max(0, startSeconds);
        lock (_sync)
        {
            _decoderCts?.Cancel();
            gen = ++_gen;
            _flush = true;
            _queue.Clear();
            _queuedFrames = 0;
            _boundaries.Clear();
            _pending = new Segment(item, start);
            _state = paused ? PlayState.Paused : PlayState.Playing;
            _endedByStopAfter = false;
            cts = _decoderCts = new CancellationTokenSource();
            Monitor.PulseAll(_sync);
        }
        new Thread(() => Decode(gen, item, start, cts.Token)) { IsBackground = true, Name = "fita-decoder" }.Start();
        _wake.Set();
    }

    public void Pause()
    {
        lock (_sync)
            if (_state == PlayState.Playing)
                _state = PlayState.Paused;
        _wake.Set();
    }

    public void Resume()
    {
        lock (_sync)
            if (_state == PlayState.Paused)
                _state = PlayState.Playing;
        _wake.Set();
    }

    public void Stop()
    {
        lock (_sync)
        {
            _decoderCts?.Cancel();
            _decoderCts = null;
            _gen++;
            _flush = true;
            _queue.Clear();
            _queuedFrames = 0;
            _boundaries.Clear();
            _pending = null;
            _state = PlayState.Stopped;
            Monitor.PulseAll(_sync);
        }
        _wake.Set();
    }

    /// <summary>Salta para um segundo da faixa atual, mantendo a pausa se estiver em pausa.</summary>
    public void Seek(double seconds)
    {
        var snap = Snapshot();
        if (snap.Segment is null || snap.State == PlayState.Stopped)
            return;
        Play(snap.Segment.Item, seconds, snap.State == PlayState.Paused);
    }

    public EngineSnapshot Snapshot()
    {
        lock (_sync)
        {
            var played = Interlocked.Read(ref _played);
            Segment? segment = _pending;
            var position = segment?.StartSeconds ?? 0;
            for (var i = _boundaries.Count - 1; i >= 0; i--)
            {
                var (frame, seg) = _boundaries[i];
                if (frame > played)
                    continue;
                segment = seg;
                position = seg.StartSeconds + (played - frame) / (double)_rate;
                // As fronteiras para trás já não interessam.
                if (i > 0)
                    _boundaries.RemoveRange(0, i);
                break;
            }
            return new EngineSnapshot(_state, _state == PlayState.Stopped ? null : segment, position);
        }
    }

    /// <summary>Copia as últimas amostras tocadas (mono) para o visualizador.</summary>
    public void CopyRecent(float[] destination)
    {
        lock (_visLock)
        {
            var end = Math.Min(Interlocked.Read(ref _played), _visWritten);
            var start = end - destination.Length;
            var oldest = _visWritten - _vis.Length;
            var mask = _vis.Length - 1;
            for (var i = 0; i < destination.Length; i++)
            {
                var index = start + i;
                destination[i] = index < 0 || index < oldest ? 0 : _vis[index & mask];
            }
        }
    }

    // ── Descodificação ────────────────────────────────────────────────────────

    private void Decode(int gen, PlayItem first, double start, CancellationToken ct)
    {
        try
        {
            if (WaitHandle.WaitAny([_ready.WaitHandle, ct.WaitHandle]) != 0)
                return;
            var item = first;
            var offset = start;
            var failures = 0;
            while (!ct.IsCancellationRequested)
            {
                if (DecodeOne(gen, new Segment(item, offset), ct))
                    failures = 0;
                else if (++failures >= 8)
                    break;
                if (ct.IsCancellationRequested)
                    return;
                PlayItem? next;
                try
                {
                    next = NextItem?.Invoke(item, ct);
                }
                catch
                {
                    next = null;
                }
                if (next is null)
                    break;
                item = next;
                offset = 0;
            }
            if (!ct.IsCancellationRequested)
                Push(new Chunk(gen, [], 0) { End = true }, ct);
        }
        catch (Exception ex)
        {
            Log.Write("Descodificação: " + ex);
        }
    }

    /// <summary>Verdadeiro se a faixa deu som (ou se foi interrompida a meio).</summary>
    private bool DecodeOne(int gen, Segment segment, CancellationToken ct)
    {
        var ffmpeg = FfmpegPath;
        if (string.IsNullOrEmpty(ffmpeg))
        {
            Failures.Enqueue((segment.Item, "ffmpeg"));
            return false;
        }

        FfmpegDecoder decoder;
        try
        {
            decoder = new FfmpegDecoder(ffmpeg, segment.Item.Path, segment.StartSeconds, _rate);
        }
        catch (Exception ex)
        {
            Failures.Enqueue((segment.Item, ex.Message));
            return false;
        }

        using (decoder)
        using (ct.Register(decoder.Kill))
        {
            var first = true;
            var gain = segment.Item.Gain;
            while (!ct.IsCancellationRequested)
            {
                var data = new float[ChunkFrames * 2];
                var frames = 0;
                try
                {
                    while (frames < ChunkFrames)
                    {
                        var n = decoder.Read(data.AsSpan(frames * 2));
                        if (n == 0)
                            break;
                        frames += n;
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                }
                if (frames == 0)
                    break;
                if (gain != 1f)
                    for (var i = 0; i < frames * 2; i++)
                        data[i] *= gain;
                if (!Push(new Chunk(gen, data, frames) { Start = first ? segment : null }, ct))
                    return true;
                first = false;
            }
            if (first && !ct.IsCancellationRequested)
            {
                var error = decoder.LastError;
                Failures.Enqueue((segment.Item, string.IsNullOrWhiteSpace(error) ? "no audio" : error));
                return false;
            }
            return true;
        }
    }

    private bool Push(Chunk chunk, CancellationToken ct)
    {
        var max = (long)(_rate * BufferAheadSeconds);
        lock (_sync)
        {
            while (_queuedFrames >= max && chunk.Gen == _gen && !ct.IsCancellationRequested)
                Monitor.Wait(_sync, 100);
            if (chunk.Gen != _gen || ct.IsCancellationRequested)
                return false;
            _queue.Enqueue(chunk);
            _queuedFrames += chunk.Frames;
        }
        _wake.Set();
        return true;
    }

    // ── Saída ─────────────────────────────────────────────────────────────────

    private void Run()
    {
        var block = new float[BlockFrames * 2];
        var paused = false;
        var nextHealthCheck = 0L;
        var nextOpenAttempt = 0L;

        while (!_disposed)
        {
            try
            {
                if (_out is null)
                {
                    if (Environment.TickCount64 >= nextOpenAttempt)
                    {
                        TryOpen();
                        nextOpenAttempt = Environment.TickCount64 + 2000;
                    }
                    if (_out is null)
                    {
                        _wake.WaitOne(500);
                        continue;
                    }
                }

                bool flush, deviceChange;
                int gen;
                PlayState state;
                string? device;
                lock (_sync)
                {
                    flush = _flush;
                    _flush = false;
                    gen = _gen;
                    state = _state;
                    deviceChange = _deviceChangePending;
                    _deviceChangePending = false;
                    device = _deviceRequest;
                }

                if (deviceChange)
                    ChangeDevice(device);

                if (flush)
                {
                    _out!.StopAndClear();
                    _framesPerBuffer.Clear();
                    _carry = null;
                    _consumed = 0;
                    _completed = 0;
                    Interlocked.Exchange(ref _played, 0);
                    _endReached = false;
                    _eq.Reset();
                    _limiter = 1;
                    paused = false;
                    lock (_visLock)
                    {
                        Array.Clear(_vis);
                        _visWritten = 0;
                    }
                }

                var output = _out!;
                if (state == PlayState.Playing)
                {
                    paused = false;
                    _completed += output.ReclaimProcessed(_framesPerBuffer);
                    while (output.HasFreeBuffer)
                    {
                        var n = Fill(block, gen);
                        if (n == 0)
                            break;
                        Process(block, n);
                        output.Queue(block, n);
                        _framesPerBuffer.Enqueue(n);
                        _consumed += n;
                    }
                    if (_framesPerBuffer.Count > 0)
                    {
                        output.EnsurePlaying();
                        var offset = Math.Min(output.SampleOffset, _framesPerBuffer.Peek());
                        Interlocked.Exchange(ref _played, _completed + offset);
                    }
                    else
                        Interlocked.Exchange(ref _played, _completed);

                    if (_endReached && _framesPerBuffer.Count == 0)
                    {
                        lock (_sync)
                        {
                            if (gen == _gen && _state == PlayState.Playing)
                            {
                                _state = PlayState.Stopped;
                                _pending = null;
                                _boundaries.Clear();
                                _endedCount++;
                            }
                        }
                        _endReached = false;
                    }
                }
                else if (state == PlayState.Paused)
                {
                    if (!paused)
                    {
                        output.Pause();
                        paused = true;
                    }
                }
                else if (_framesPerBuffer.Count > 0)
                {
                    output.StopAndClear();
                    _framesPerBuffer.Clear();
                }

                if (Environment.TickCount64 >= nextHealthCheck)
                {
                    nextHealthCheck = Environment.TickCount64 + 1000;
                    CheckDevice();
                }

                _wake.WaitOne(state == PlayState.Playing ? 4 : 60);
            }
            catch (Exception ex)
            {
                Log.Write("Saída de som: " + ex);
                Thread.Sleep(200);
            }
        }
        _out?.Dispose();
    }

    private void TryOpen()
    {
        try
        {
            _out = new AlOutput(_deviceName);
            if (Silent)
                _out.Gain = 0;
            _rate = _out.SampleRate;
            EqSettings eq;
            lock (_sync)
                eq = _eqSettings;
            _eq.Configure(eq, _rate);
            OutputError = null;
            _ready.Set();
        }
        catch (DllNotFoundException)
        {
            OutputError = "openal";
            _out = null;
        }
        catch (Exception ex)
        {
            OutputError = ex.Message;
            _out = null;
        }
    }

    private void ChangeDevice(string? name)
    {
        _deviceName = name;
        var rate = _rate;
        if (_out is not null && _out.Reopen(name))
        {
            if (_out.SampleRate != rate)
                AfterRateChange();
            return;
        }
        _out?.Dispose();
        _out = null;
        TryOpen();
        RequestRestart();
    }

    /// <summary>Auscultadores USB desligados e afins: tenta o dispositivo por omissão.</summary>
    private void CheckDevice()
    {
        if (_out is null || _out.IsConnected)
            return;
        Log.Write("Dispositivo de som desligado; a passar para o por omissão.");
        var rate = _rate;
        if (_out.Reopen(null))
        {
            if (_out.SampleRate != rate)
                AfterRateChange();
            else
                RequestRestart();
            return;
        }
        _out.Dispose();
        _out = null;
        TryOpen();
        RequestRestart();
    }

    private void AfterRateChange()
    {
        _rate = _out!.SampleRate;
        EqSettings eq;
        lock (_sync)
            eq = _eqSettings;
        _eq.Configure(eq, _rate);
        RequestRestart();
    }

    private void RequestRestart()
    {
        lock (_sync)
            _restartRequested = _state != PlayState.Stopped;
    }

    private int Fill(float[] block, int gen)
    {
        var filled = 0;
        while (filled < BlockFrames)
        {
            if (_carry is null)
            {
                Chunk next;
                lock (_sync)
                {
                    if (_queue.Count == 0)
                        break;
                    next = _queue.Dequeue();
                    _queuedFrames -= next.Frames;
                    Monitor.PulseAll(_sync);

                    if (next.Gen != gen)
                        continue;
                    if (next.Start is not null)
                    {
                        if (_stopAfterCurrent && _boundaries.Count > 0)
                        {
                            // "Parar depois desta": a faixa seguinte já estava na fila; corta aqui.
                            _stopAfterCurrent = false;
                            _endedByStopAfter = true;
                            _decoderCts?.Cancel();
                            _queue.Clear();
                            _queuedFrames = 0;
                            _endReached = true;
                            break;
                        }
                        _boundaries.Add((_consumed + filled, next.Start));
                    }
                }
                if (next.End)
                {
                    _endReached = true;
                    break;
                }
                _carry = next;
                _carryPos = 0;
            }

            var take = Math.Min(BlockFrames - filled, _carry.Frames - _carryPos);
            Array.Copy(_carry.Data, _carryPos * 2, block, filled * 2, take * 2);
            filled += take;
            _carryPos += take;
            if (_carryPos >= _carry.Frames)
                _carry = null;
        }
        return filled;
    }

    private void Process(float[] block, int frames)
    {
        _eq.Process(block, frames);

        lock (_visLock)
        {
            var mask = _vis.Length - 1;
            for (var i = 0; i < frames; i++)
                _vis[(_consumed + i) & mask] = (block[2 * i] + block[2 * i + 1]) * 0.5f;
            _visWritten = _consumed + frames;
        }

        // Volume e balanço com rampa ao longo do bloco: mexer no controlo não dá estalidos.
        var volume = _volume;
        var balance = _balance;
        var targetL = volume * Math.Min(1f, 1f - balance);
        var targetR = volume * Math.Min(1f, 1f + balance);
        float gl = _gainL, gr = _gainR;
        var dl = (targetL - gl) / frames;
        var dr = (targetR - gr) / frames;

        // Limitador de picos: ataque imediato, recuperação em ~120 ms. Só atua acima de -0,2 dB,
        // ou seja, quando o EQ ou o ReplayGain iam fazer saturar.
        const float threshold = 0.977f;
        var release = MathF.Exp(-1f / (0.12f * _rate));
        var env = _limiter;
        var minEnv = 1f;
        for (var i = 0; i < frames; i++)
        {
            gl += dl;
            gr += dr;
            var l = block[2 * i] * gl;
            var r = block[2 * i + 1] * gr;
            var peak = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            var want = peak > threshold ? threshold / peak : 1f;
            env = want < env ? want : want - (want - env) * release;
            if (env < minEnv)
                minEnv = env;
            block[2 * i] = l * env;
            block[2 * i + 1] = r * env;
        }
        _gainL = targetL;
        _gainR = targetR;
        _limiter = env;
        if (minEnv < 0.95f)
            Interlocked.Exchange(ref _limitUntil, Environment.TickCount64 + 300);
    }

    public void Dispose()
    {
        lock (_sync)
            _decoderCts?.Cancel();
        _disposed = true;
        _wake.Set();
        _thread.Join(1500);
    }
}
