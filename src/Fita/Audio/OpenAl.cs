using System.Reflection;
using System.Runtime.InteropServices;

namespace Fita.Audio;

/// <summary>
/// Ligação mínima ao OpenAL Soft: só as funções de que a saída de som precisa.
/// A biblioteca nativa vem do pacote Silk.NET.OpenAL.Soft.Native (soft_oal.dll no Windows,
/// libopenal.so no Linux); se faltar, tenta a do sistema.
/// </summary>
internal static unsafe class OpenAl
{
    private const string Lib = "fita-openal";

    public const int ALC_FREQUENCY = 0x1007;
    public const int ALC_ALL_DEVICES_SPECIFIER = 0x1013;
    public const int ALC_DEVICE_SPECIFIER = 0x1005;
    public const int ALC_CONNECTED = 0x313;
    public const int ALC_HRTF_SOFT = 0x1992;

    public const int AL_GAIN = 0x100A;
    public const int AL_SOURCE_RELATIVE = 0x202;
    public const int AL_ROLLOFF_FACTOR = 0x1021;
    public const int AL_BUFFER = 0x1009;
    public const int AL_SOURCE_STATE = 0x1010;
    public const int AL_PLAYING = 0x1012;
    public const int AL_PAUSED = 0x1013;
    public const int AL_BUFFERS_QUEUED = 0x1015;
    public const int AL_BUFFERS_PROCESSED = 0x1016;
    public const int AL_SAMPLE_OFFSET = 0x1025;
    public const int AL_FORMAT_STEREO16 = 0x1103;
    public const int AL_FORMAT_STEREO_FLOAT32 = 0x10011;
    public const int AL_DIRECT_CHANNELS_SOFT = 0x1033;

    static OpenAl()
    {
        NativeLibrary.SetDllImportResolver(typeof(OpenAl).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib)
            return IntPtr.Zero;
        string[] candidates = OperatingSystem.IsWindows() ? ["soft_oal.dll", "OpenAL32.dll"]
            : OperatingSystem.IsMacOS() ? ["libopenal.dylib", "libopenal.1.dylib"]
            : ["libopenal.so", "libopenal.so.1"];
        foreach (var candidate in candidates)
            if (NativeLibrary.TryLoad(candidate, assembly, path, out var handle))
                return handle;
        return IntPtr.Zero;
    }

    /// <summary>Obriga a carregar a biblioteca; falha com DllNotFoundException se não existir.</summary>
    public static void EnsureLoaded() => alcGetError(IntPtr.Zero);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr alcOpenDevice(byte* deviceName);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte alcCloseDevice(IntPtr device);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr alcCreateContext(IntPtr device, int* attributes);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte alcMakeContextCurrent(IntPtr context);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alcDestroyContext(IntPtr context);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int alcGetError(IntPtr device);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alcGetIntegerv(IntPtr device, int param, int size, int* values);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* alcGetString(IntPtr device, int param);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte alcIsExtensionPresent(IntPtr device, byte* name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr alcGetProcAddress(IntPtr device, byte* name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int alGetError();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte alIsExtensionPresent(byte* name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alGenSources(int n, uint* sources);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alDeleteSources(int n, uint* sources);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alGenBuffers(int n, uint* buffers);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alDeleteBuffers(int n, uint* buffers);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alBufferData(uint buffer, int format, void* data, int size, int frequency);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alSourceQueueBuffers(uint source, int n, uint* buffers);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alSourceUnqueueBuffers(uint source, int n, uint* buffers);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alSourcePlay(uint source);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alSourcePause(uint source);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alSourceStop(uint source);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alSourcei(uint source, int param, int value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alSourcef(uint source, int param, float value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void alGetSourcei(uint source, int param, out int value);

    public static bool AlExtension(string name)
    {
        var bytes = Utf8z(name);
        fixed (byte* p = bytes)
            return alIsExtensionPresent(p) != 0;
    }

    public static bool AlcExtension(IntPtr device, string name)
    {
        var bytes = Utf8z(name);
        fixed (byte* p = bytes)
            return alcIsExtensionPresent(device, p) != 0;
    }

    public static IntPtr ProcAddress(IntPtr device, string name)
    {
        var bytes = Utf8z(name);
        fixed (byte* p = bytes)
            return alcGetProcAddress(device, p);
    }

    public static byte[]? Utf8z(string? text) =>
        text is null ? null : System.Text.Encoding.UTF8.GetBytes(text + "\0");

    /// <summary>Lê uma lista de cadeias separadas por \0 e terminada por \0\0.</summary>
    public static List<string> ReadList(byte* list)
    {
        var result = new List<string>();
        if (list == null)
            return result;
        while (*list != 0)
        {
            var text = Marshal.PtrToStringUTF8((IntPtr)list) ?? "";
            result.Add(text);
            list += System.Text.Encoding.UTF8.GetByteCount(text) + 1;
        }
        return result;
    }
}
