using static Fita.Audio.OpenAl;

namespace Fita.Audio;

/// <summary>
/// Saída de som por OpenAL Soft, em fila de buffers curtos (streaming). Só é usada pela thread
/// de saída do <see cref="AudioEngine"/>, exceto <see cref="ListDevices"/>.
/// </summary>
internal sealed unsafe class AlOutput : IDisposable
{
    public const int BufferCount = 6;

    private readonly IntPtr _device;
    private readonly IntPtr _context;
    private readonly uint _source;
    private readonly uint[] _buffers = new uint[BufferCount];
    private readonly Stack<uint> _free = new();
    private readonly bool _float;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, byte*, int*, byte> _reopen;
    private short[] _pcm16 = [];
    private float _gain = 1f;

    public int SampleRate { get; private set; }
    public int Queued => BufferCount - _free.Count;
    public bool HasFreeBuffer => _free.Count > 0;

    /// <param name="deviceName">null: o dispositivo por omissão do sistema.</param>
    public AlOutput(string? deviceName)
    {
        EnsureLoaded();
        var name = Utf8z(deviceName);
        fixed (byte* p = name)
            _device = alcOpenDevice(p);
        if (_device == IntPtr.Zero && deviceName != null)
            _device = alcOpenDevice(null);
        if (_device == IntPtr.Zero)
            throw new InvalidOperationException("No audio output device.");

        // HRTF desligado: é música em estéreo, não um jogo em 3D. Com auscultadores o OpenAL Soft
        // podia ligá-lo sozinho e "espacializar" o disco.
        var attributes = Attributes();
        fixed (int* a = attributes)
            _context = alcCreateContext(_device, a);
        if (_context == IntPtr.Zero || alcMakeContextCurrent(_context) == 0)
        {
            alcCloseDevice(_device);
            throw new InvalidOperationException("Could not start the audio output.");
        }

        if (AlcExtension(_device, "ALC_SOFT_reopen_device"))
            _reopen = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int*, byte>)ProcAddress(_device, "alcReopenDeviceSOFT");

        _float = AlExtension("AL_EXT_FLOAT32");
        ReadFrequency();

        uint source;
        alGenSources(1, &source);
        _source = source;
        // Canais diretos: o L vai para o L e o R para o R, sem passar pelo "espaço" do OpenAL.
        alSourcei(_source, AL_SOURCE_RELATIVE, 1);
        alSourcef(_source, AL_ROLLOFF_FACTOR, 0f);
        if (AlExtension("AL_SOFT_direct_channels"))
            alSourcei(_source, AL_DIRECT_CHANNELS_SOFT, 1);

        fixed (uint* b = _buffers)
            alGenBuffers(BufferCount, b);
        foreach (var buffer in _buffers)
            _free.Push(buffer);
    }

    private static int[] Attributes() => [ALC_HRTF_SOFT, 0, 0];

    private void ReadFrequency()
    {
        int frequency;
        alcGetIntegerv(_device, ALC_FREQUENCY, 1, &frequency);
        SampleRate = frequency is >= 8000 and <= 768000 ? frequency : 48000;
    }

    /// <summary>Nomes dos dispositivos de saída, como o OpenAL os mostra.</summary>
    public static List<string> ListDevices()
    {
        try
        {
            EnsureLoaded();
            var all = AlcExtension(IntPtr.Zero, "ALC_ENUMERATE_ALL_EXT");
            return ReadList(alcGetString(IntPtr.Zero, all ? ALC_ALL_DEVICES_SPECIFIER : ALC_DEVICE_SPECIFIER));
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Falso quando o dispositivo desapareceu (auscultadores USB desligados, por exemplo).</summary>
    public bool IsConnected
    {
        get
        {
            int connected = 1;
            alcGetIntegerv(_device, ALC_CONNECTED, 1, &connected);
            return connected != 0;
        }
    }

    /// <summary>Passa o mesmo contexto para outro dispositivo, sem perder a fila. Falso se não der.</summary>
    public bool Reopen(string? deviceName)
    {
        if (_reopen == null)
            return false;
        var name = Utf8z(deviceName);
        var attributes = Attributes();
        bool ok;
        fixed (byte* p = name)
        fixed (int* a = attributes)
            ok = _reopen(_device, p, a) != 0;
        if (ok)
            ReadFrequency();
        return ok;
    }

    /// <summary>Recolhe os buffers que já tocaram e devolve quantas amostras (por canal) tinham.</summary>
    public int ReclaimProcessed(Queue<int> framesPerBuffer)
    {
        alGetSourcei(_source, AL_BUFFERS_PROCESSED, out var processed);
        var frames = 0;
        for (var i = 0; i < processed; i++)
        {
            uint buffer;
            alSourceUnqueueBuffers(_source, 1, &buffer);
            _free.Push(buffer);
            if (framesPerBuffer.Count > 0)
                frames += framesPerBuffer.Dequeue();
        }
        return frames;
    }

    /// <summary>Põe um bloco estéreo intercalado (float) na fila.</summary>
    public void Queue(float[] interleaved, int frames)
    {
        var buffer = _free.Pop();
        var samples = frames * 2;
        if (_float)
        {
            fixed (float* p = interleaved)
                alBufferData(buffer, AL_FORMAT_STEREO_FLOAT32, p, samples * sizeof(float), SampleRate);
        }
        else
        {
            if (_pcm16.Length < samples)
                _pcm16 = new short[samples];
            for (var i = 0; i < samples; i++)
                _pcm16[i] = (short)Math.Clamp((int)MathF.Round(interleaved[i] * 32767f), -32768, 32767);
            fixed (short* p = _pcm16)
                alBufferData(buffer, AL_FORMAT_STEREO16, p, samples * sizeof(short), SampleRate);
        }
        alSourceQueueBuffers(_source, 1, &buffer);
    }

    public void EnsurePlaying()
    {
        alGetSourcei(_source, AL_SOURCE_STATE, out var state);
        if (state != AL_PLAYING)
            alSourcePlay(_source);
    }

    public void Pause()
    {
        alGetSourcei(_source, AL_SOURCE_STATE, out var state);
        if (state == AL_PLAYING)
            alSourcePause(_source);
    }

    /// <summary>Pára e esvazia a fila: todos os buffers voltam a estar livres.</summary>
    public void StopAndClear()
    {
        alSourceStop(_source);
        alSourcei(_source, AL_BUFFER, 0);
        _free.Clear();
        foreach (var buffer in _buffers)
            _free.Push(buffer);
    }

    /// <summary>Amostras já tocadas do buffer que está a tocar.</summary>
    public int SampleOffset
    {
        get
        {
            alGetSourcei(_source, AL_SAMPLE_OFFSET, out var offset);
            return Math.Max(0, offset);
        }
    }

    /// <summary>Ganho final da fonte (usado nos testes para correr em silêncio).</summary>
    public float Gain
    {
        get => _gain;
        set
        {
            _gain = value;
            alSourcef(_source, AL_GAIN, value);
        }
    }

    public void Dispose()
    {
        try
        {
            alSourceStop(_source);
            alSourcei(_source, AL_BUFFER, 0);
            var source = _source;
            alDeleteSources(1, &source);
            fixed (uint* b = _buffers)
                alDeleteBuffers(BufferCount, b);
            alcMakeContextCurrent(IntPtr.Zero);
            alcDestroyContext(_context);
            alcCloseDevice(_device);
        }
        catch { }
    }
}
