namespace Fita.Audio;

/// <summary>
/// A saída de som que a thread de saída do <see cref="AudioEngine"/> alimenta: uma fila de blocos
/// curtos de float estéreo intercalado. No desktop é o OpenAL Soft (<see cref="AlOutput"/>); no
/// Android, o AudioTrack.
/// </summary>
internal interface IAudioOutput : IDisposable
{
    int SampleRate { get; }

    /// <summary>Cabe mais um bloco na fila.</summary>
    bool HasFreeBuffer { get; }

    /// <summary>Tira da conta os blocos que já tocaram e devolve quantas amostras (por canal) tinham.</summary>
    int ReclaimProcessed(Queue<int> framesPerBuffer);

    /// <summary>Põe um bloco na fila.</summary>
    void Queue(float[] interleaved, int frames);

    void EnsurePlaying();

    void Pause();

    /// <summary>Pára e esvazia a fila.</summary>
    void StopAndClear();

    /// <summary>Amostras já tocadas do bloco que está a tocar.</summary>
    int SampleOffset { get; }

    /// <summary>Ganho final (os testes correm com zero).</summary>
    float Gain { get; set; }

    /// <summary>Falso quando o dispositivo desapareceu.</summary>
    bool IsConnected { get; }

    /// <summary>Passa para outro dispositivo sem perder a fila. Falso se não der.</summary>
    bool Reopen(string? deviceName);
}
