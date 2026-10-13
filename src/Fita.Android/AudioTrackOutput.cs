using Android.Media;
using Fita.Audio;

namespace Fita;

/// <summary>
/// A saída de som no Android: um AudioTrack em streaming, em float estéreo, à frequência nativa
/// do telemóvel (para o sistema não reamostrar). O motor pensa numa fila de blocos; aqui a fila é
/// a conta entre o que se escreveu e a cabeça de leitura do AudioTrack.
/// </summary>
internal sealed class AudioTrackOutput : IAudioOutput
{
    /// <summary>Quanto cabe à frente da cabeça de leitura (~170 ms a 48 kHz), como os 6 buffers do OpenAL.</summary>
    private const int CapacityFrames = 8192;
    private const int BlockFrames = AudioEngine.BlockFrames;

    private readonly AudioTrack _track;
    private long _written;
    private long _reclaimed;
    private long _headBase;
    private long _lastHead;
    private float _gain = 1f;

    public AudioTrackOutput()
    {
        var rate = AudioTrack.GetNativeOutputSampleRate(global::Android.Media.Stream.Music);
        SampleRate = rate is >= 8000 and <= 192000 ? rate : 48000;
        var min = AudioTrack.GetMinBufferSize(SampleRate, ChannelOut.Stereo, Encoding.PcmFloat);
        var bytes = Math.Max(min, CapacityFrames * 8);
        _track = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!)
            .SetAudioFormat(new AudioFormat.Builder()
                .SetEncoding(Encoding.PcmFloat)!
                .SetSampleRate(SampleRate)!
                .SetChannelMask(ChannelOut.Stereo)!
                .Build()!)
            .SetBufferSizeInBytes(bytes)
            .SetTransferMode(AudioTrackMode.Stream)
            .Build();
    }

    public int SampleRate { get; }

    /// <summary>A cabeça de leitura em amostras, sem as voltas do int de 32 bits.</summary>
    private long Head()
    {
        var raw = (long)(uint)_track.PlaybackHeadPosition;
        var head = raw - _headBase;
        // Depois de esvaziar, a cabeça volta a zero (às vezes um pouco mais tarde).
        if (head < 0)
        {
            _headBase = 0;
            head = raw;
        }
        while (head + 0x80000000L < _lastHead)
            head += 0x100000000L;
        _lastHead = head;
        return Math.Min(head, _written);
    }

    public bool HasFreeBuffer => _written - Head() <= CapacityFrames - BlockFrames;

    public int ReclaimProcessed(Queue<int> framesPerBuffer)
    {
        var head = Head();
        var frames = 0;
        while (framesPerBuffer.Count > 0 && _reclaimed + framesPerBuffer.Peek() <= head)
        {
            var n = framesPerBuffer.Dequeue();
            _reclaimed += n;
            frames += n;
        }
        return frames;
    }

    public void Queue(float[] interleaved, int frames)
    {
        var samples = frames * 2;
        var done = 0;
        while (done < samples)
        {
            var n = _track.Write(interleaved, done, samples - done, WriteMode.Blocking);
            if (n <= 0)
                break;
            done += n;
        }
        _written += frames;
    }

    public void EnsurePlaying()
    {
        if (_track.PlayState != global::Android.Media.PlayState.Playing)
            _track.Play();
    }

    public void Pause()
    {
        if (_track.PlayState == global::Android.Media.PlayState.Playing)
            _track.Pause();
    }

    public void StopAndClear()
    {
        try
        {
            _track.Pause();
            _track.Flush();
            _track.Stop();
        }
        catch (Java.Lang.IllegalStateException)
        {
        }
        _headBase = (uint)_track.PlaybackHeadPosition;
        _lastHead = 0;
        _written = 0;
        _reclaimed = 0;
    }

    public int SampleOffset => (int)Math.Max(0, Head() - _reclaimed);

    public float Gain
    {
        get => _gain;
        set
        {
            _gain = value;
            _track.SetVolume(value);
        }
    }

    public bool IsConnected => true;

    public bool Reopen(string? deviceName) => false;

    public void Dispose()
    {
        try
        {
            _track.Stop();
        }
        catch
        {
        }
        _track.Release();
    }
}
