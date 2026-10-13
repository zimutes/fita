namespace Fita.Audio;

/// <summary>Um bloco de som da fila do motor (estéreo intercalado).</summary>
internal sealed class Chunk(int gen, float[] data, int frames)
{
    public int Gen { get; } = gen;
    public float[] Data { get; } = data;
    public int Frames { get; } = frames;
    /// <summary>Começa aqui uma faixa (ou um salto).</summary>
    public Segment? Start { get; init; }
    /// <summary>Fim da reprodução (bloco vazio).</summary>
    public bool End { get; init; }
}

/// <summary>
/// Crossfade, do lado da thread de descodificação: os últimos <see cref="Hold"/> frames de cada
/// faixa ficam retidos e, se a seguinte também se fundir, misturam-se com o começo dela, com
/// curvas de potência igual (o volume não afunda a meio). A faixa seguinte começa (para a
/// posição e a lista) no início da mistura. Uma por descodificação; não é partilhada.
/// </summary>
internal sealed class Crossfade(int gen, int hold, Func<Chunk, bool> push)
{
    private readonly Queue<Chunk> _held = new();
    private long _heldFrames;
    private float[]? _mixIn;
    private int _mixInPos;
    /// <summary>Frames da faixa atual (as muito curtas não se fundem).</summary>
    private long _trackFrames;

    public int Hold { get; } = hold;

    /// <summary>Envia um bloco da faixa, misturado com o fim da anterior se for o caso, e retém os últimos.</summary>
    public bool Emit(Chunk chunk)
    {
        _trackFrames += chunk.Frames;
        if (_mixIn is { } mix)
        {
            var total = mix.Length / 2;
            var n = Math.Min(chunk.Frames, total - _mixInPos);
            for (var i = 0; i < n; i++)
            {
                var t = (_mixInPos + i + 0.5) / total;
                var gin = (float)Math.Sin(t * Math.PI / 2);
                var gout = (float)Math.Cos(t * Math.PI / 2);
                var a = (_mixInPos + i) * 2;
                chunk.Data[i * 2] = chunk.Data[i * 2] * gin + mix[a] * gout;
                chunk.Data[i * 2 + 1] = chunk.Data[i * 2 + 1] * gin + mix[a + 1] * gout;
            }
            _mixInPos += n;
            if (_mixInPos >= total)
                _mixIn = null;
        }
        _held.Enqueue(chunk);
        _heldFrames += chunk.Frames;
        while (_held.Count > 0 && _heldFrames - _held.Peek().Frames >= Hold)
        {
            var c = _held.Dequeue();
            _heldFrames -= c.Frames;
            if (!push(c))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Fim de uma faixa: com <paramref name="crossfade"/>, os últimos Hold frames ficam para
    /// misturar com a seguinte; sem, sai tudo como está. Uma faixa com menos do dobro do
    /// crossfade não se funde. Falso se a reprodução foi interrompida.
    /// </summary>
    public bool EndTrack(bool crossfade)
    {
        // A faixa acabou antes de a mistura com a anterior acabar: o resto da anterior sai a desvanecer.
        if (_mixIn is { } mix)
        {
            var left = mix.Length / 2 - _mixInPos;
            if (left > 0 && !Emit(new Chunk(gen, new float[left * 2], left)))
                return false;
            _mixIn = null;
        }
        var trackFrames = _trackFrames;
        _trackFrames = 0;
        if (!crossfade || trackFrames < 2L * Hold || _heldFrames < Hold)
        {
            while (_held.Count > 0)
                if (!push(_held.Dequeue()))
                    return false;
            _heldFrames = 0;
            return true;
        }
        // Sai o que vem antes da cauda; o primeiro bloco da cauda pode ter de ser partido.
        var excess = (int)(_heldFrames - Hold);
        var tail = new float[Hold * 2];
        var at = 0;
        while (_held.Count > 0)
        {
            var c = _held.Dequeue();
            var from = 0;
            if (excess > 0)
            {
                var head = Math.Min(excess, c.Frames);
                if (!push(new Chunk(c.Gen, c.Data.AsSpan(0, head * 2).ToArray(), head) { Start = c.Start }))
                    return false;
                excess -= head;
                from = head;
            }
            var rest = c.Frames - from;
            if (rest > 0)
            {
                Array.Copy(c.Data, from * 2, tail, at * 2, rest * 2);
                at += rest;
            }
        }
        _heldFrames = 0;
        _mixIn = tail;
        _mixInPos = 0;
        return true;
    }
}
