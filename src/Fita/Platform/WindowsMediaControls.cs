using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Fita.Audio;

namespace Fita.Platform;

/// <summary>
/// O painel multimédia do Windows (SystemMediaTransportControls): título, artista, capa e
/// posição no painel do volume e no ecrã de bloqueio; teclas multimédia, botões dos auscultadores
/// e do painel. É WinRT, chamado à mão pela tabela de métodos (como o OpenAL em
/// <see cref="OpenAl"/>), para não arrastar a projeção inteira do SDK do Windows para o .exe.
/// Os números das posições na tabela e os GUIDs vêm de C:\Windows\System32\WinMetadata\Windows.Media.winmd.
/// Chama-se só da thread da interface.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsMediaControls : IMediaSession
{
    private static readonly Guid IID_Interop = new("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");
    private static readonly Guid IID_Smtc = new("99fa3ff4-1742-42a6-902e-087d41f965ec");
    private static readonly Guid IID_Smtc2 = new("ea98d2f6-7f3c-4af2-a586-72889808efb1");
    private static readonly Guid IID_Music2 = new("00368462-97d3-44b9-b00f-008afcefaf18");
    private static readonly Guid IID_Timeline = new("5125316a-c3a2-475b-8507-93534dc88f15");
    private static readonly Guid IID_StreamRefStatics = new("857309dc-3fbf-4e7d-986f-ef3b1a07a964");
    private static readonly Guid IID_RandomAccessStream = new("905a0fe1-bc53-11df-8c49-001e4fc686da");
    // TypedEventHandler<SystemMediaTransportControls, …EventArgs>: IIDs de tipos genéricos,
    // calculados pela regra do WinRT (SHA-1 da assinatura), como fazem os cabeçalhos do SDK.
    private static readonly Guid IID_ButtonHandler = new("0557e996-7b23-5bae-aa81-ea0d671143a4");
    private static readonly Guid IID_PositionHandler = new("44e34f15-bdc0-50a7-ace4-39e91fb753f1");

    // ISystemMediaTransportControls
    private const int PutPlaybackStatus = 7, GetDisplayUpdater = 8, PutIsEnabled = 11, PutIsPlayEnabled = 13,
        PutIsStopEnabled = 15, PutIsPauseEnabled = 17, PutIsPreviousEnabled = 25, PutIsNextEnabled = 27,
        AddButtonPressed = 32, RemoveButtonPressed = 33;
    // ISystemMediaTransportControls2
    private const int UpdateTimelineProperties = 12, AddPositionChangeRequested = 13, RemovePositionChangeRequested = 14;
    // ISystemMediaTransportControlsDisplayUpdater
    private const int PutType = 7, PutThumbnail = 11, GetMusicProperties = 12, ClearAll = 16, Update = 17;
    // IMusicDisplayProperties (e …2)
    private const int PutTitle = 7, PutAlbumArtist = 9, PutArtist = 11, PutAlbumTitle = 7, PutTrackNumber = 9;
    // ISystemMediaTransportControlsTimelineProperties
    private const int PutStartTime = 7, PutEndTime = 9, PutMinSeekTime = 11, PutMaxSeekTime = 13, PutPosition = 15;

    private const int MediaPlaybackTypeMusic = 1;
    private enum Status { Closed = 0, Changing = 1, Stopped = 2, Playing = 3, Paused = 4 }

    private readonly IntPtr _smtc;
    private readonly IntPtr _smtc2;
    private readonly IntPtr _updater;
    private readonly IntPtr _music;
    private readonly IntPtr _music2;
    private readonly IntPtr _timeline;
    private readonly IntPtr _buttonHandler;
    private readonly IntPtr _positionHandler;
    private readonly long _buttonToken;
    private readonly long _positionToken;
    private bool _disposed;

    private WindowsMediaControls(IntPtr window, Action<MediaCommand> onCommand, Action<double> onSeek)
    {
        var factory = ActivationFactory("Windows.Media.SystemMediaTransportControls", IID_Interop);
        try
        {
            IntPtr smtc;
            var iid = IID_Smtc;
            // ISystemMediaTransportControlsInterop::GetForWindow
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)Slot(factory, 6))(factory, window, &iid, &smtc));
            _smtc = smtc;
        }
        finally
        {
            Release(factory);
        }

        foreach (var slot in new[] { PutIsEnabled, PutIsPlayEnabled, PutIsPauseEnabled, PutIsStopEnabled, PutIsPreviousEnabled, PutIsNextEnabled })
            Check(CallInt(_smtc, slot, 1));

        _buttonHandler = WinRtHandler.Create(IID_ButtonHandler, args =>
        {
            int button;
            if (((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(args, 6))(args, &button) < 0)
                return;
            MediaCommand? command = button switch
            {
                0 => MediaCommand.Play,
                1 => MediaCommand.Pause,
                2 => MediaCommand.Stop,
                6 => MediaCommand.Next,
                7 => MediaCommand.Previous,
                _ => null,
            };
            if (command is { } c)
                onCommand(c);
        });
        long token;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)Slot(_smtc, AddButtonPressed))(_smtc, _buttonHandler, &token));
        _buttonToken = token;

        IntPtr updater;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(_smtc, GetDisplayUpdater))(_smtc, &updater));
        _updater = updater;
        Check(CallInt(_updater, PutType, MediaPlaybackTypeMusic));
        IntPtr music;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(_updater, GetMusicProperties))(_updater, &music));
        _music = music;
        _music2 = QueryInterface(_music, IID_Music2);

        // A barra de posição e o salto a partir do painel (Windows 10 1607 em diante).
        _smtc2 = QueryInterface(_smtc, IID_Smtc2);
        if (_smtc2 != IntPtr.Zero)
        {
            var timeline = ActivateInstance("Windows.Media.SystemMediaTransportControlsTimelineProperties");
            if (timeline != IntPtr.Zero)
            {
                _timeline = QueryInterface(timeline, IID_Timeline);
                Release(timeline);
            }
            _positionHandler = WinRtHandler.Create(IID_PositionHandler, args =>
            {
                long ticks;
                if (((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)Slot(args, 6))(args, &ticks) >= 0)
                    onSeek(ticks / 1e7);
            });
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)Slot(_smtc2, AddPositionChangeRequested))(_smtc2, _positionHandler, &token) >= 0)
                _positionToken = token;
        }

        SetState(PlayState.Stopped);
    }

    public static WindowsMediaControls Create(IntPtr window, Action<MediaCommand> onCommand, Action<double> onSeek) =>
        new(window, onCommand, onSeek);

    public void SetState(PlayState state)
    {
        if (_disposed)
            return;
        var status = state switch { PlayState.Playing => Status.Playing, PlayState.Paused => Status.Paused, _ => Status.Stopped };
        CallInt(_smtc, PutPlaybackStatus, (int)status);
    }

    public void SetTrack(NowPlaying? track)
    {
        if (_disposed)
            return;
        if (track is null)
        {
            CallVoid(_updater, ClearAll);
            CallInt(_updater, PutType, MediaPlaybackTypeMusic);
            CallVoid(_updater, Update);
            return;
        }
        PutString(_music, PutTitle, track.Title);
        PutString(_music, PutArtist, track.Artist ?? "");
        PutString(_music, PutAlbumArtist, track.Artist ?? "");
        if (_music2 != IntPtr.Zero)
        {
            PutString(_music2, PutAlbumTitle, track.Album ?? "");
            CallInt(_music2, PutTrackNumber, (int)track.TrackNumber);
        }
        var thumbnail = track.Cover is { Length: > 0 } cover ? StreamReference(cover) : IntPtr.Zero;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(_updater, PutThumbnail))(_updater, thumbnail);
        Release(thumbnail);
        CallVoid(_updater, Update);
    }

    public void SetPosition(double position, double duration)
    {
        if (_disposed || _smtc2 == IntPtr.Zero || _timeline == IntPtr.Zero)
            return;
        var end = (long)(Math.Max(0, duration) * 1e7);
        var at = Math.Clamp((long)(position * 1e7), 0, end);
        CallLong(_timeline, PutStartTime, 0);
        CallLong(_timeline, PutMinSeekTime, 0);
        CallLong(_timeline, PutEndTime, end);
        CallLong(_timeline, PutMaxSeekTime, end);
        CallLong(_timeline, PutPosition, at);
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(_smtc2, UpdateTimelineProperties))(_smtc2, _timeline);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            CallInt(_smtc, PutPlaybackStatus, (int)Status.Closed);
            CallVoid(_updater, ClearAll);
            CallVoid(_updater, Update);
            ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(_smtc, RemoveButtonPressed))(_smtc, _buttonToken);
            if (_smtc2 != IntPtr.Zero && _positionToken != 0)
                ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(_smtc2, RemovePositionChangeRequested))(_smtc2, _positionToken);
        }
        catch (Exception ex)
        {
            Log.Write("Painel multimédia: " + ex.Message);
        }
        foreach (var p in new[] { _timeline, _music2, _music, _updater, _smtc2, _smtc, _buttonHandler, _positionHandler })
            Release(p);
    }

    // ── Capa ──────────────────────────────────────────────────────────────────

    /// <summary>RandomAccessStreamReference.CreateFromStream sobre uma cópia da imagem em memória.</summary>
    private static IntPtr StreamReference(byte[] image)
    {
        IntPtr stream = IntPtr.Zero, randomAccess = IntPtr.Zero, statics = IntPtr.Zero, reference = IntPtr.Zero;
        try
        {
            fixed (byte* p = image)
                stream = SHCreateMemStream(p, (uint)image.Length);
            if (stream == IntPtr.Zero)
                return IntPtr.Zero;
            var iid = IID_RandomAccessStream;
            if (CreateRandomAccessStreamOverStream(stream, 0, &iid, &randomAccess) < 0)
                return IntPtr.Zero;
            statics = ActivationFactory("Windows.Storage.Streams.RandomAccessStreamReference", IID_StreamRefStatics);
            // IRandomAccessStreamReferenceStatics::CreateFromStream
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(statics, 8))(statics, randomAccess, &reference) < 0)
                reference = IntPtr.Zero;
            return reference;
        }
        catch (Exception ex)
        {
            Log.Write("Capa no painel multimédia: " + ex.Message);
            return IntPtr.Zero;
        }
        finally
        {
            Release(statics);
            Release(randomAccess);
            Release(stream);
        }
    }

    // ── WinRT à mão ───────────────────────────────────────────────────────────

    private static void* Slot(IntPtr p, int index) => (*(void***)p)[index];

    private static int CallInt(IntPtr p, int slot, int value) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(p, slot))(p, value);

    private static int CallLong(IntPtr p, int slot, long value) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(p, slot))(p, value);

    private static int CallVoid(IntPtr p, int slot) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(p, slot))(p);

    private static void PutString(IntPtr p, int slot, string value)
    {
        IntPtr h;
        fixed (char* s = value)
            if (WindowsCreateString(s, (uint)value.Length, &h) < 0)
                return;
        try
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(p, slot))(p, h);
        }
        finally
        {
            WindowsDeleteString(h);
        }
    }

    private static IntPtr QueryInterface(IntPtr p, Guid iid)
    {
        IntPtr result;
        return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(p, 0))(p, &iid, &result) >= 0 ? result : IntPtr.Zero;
    }

    private static void Release(IntPtr p)
    {
        if (p != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(p, 2))(p);
    }

    private static IntPtr ActivationFactory(string className, Guid iid)
    {
        IntPtr h, factory;
        fixed (char* s = className)
            Check(WindowsCreateString(s, (uint)className.Length, &h));
        try
        {
            Check(RoGetActivationFactory(h, &iid, &factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(h);
        }
    }

    private static IntPtr ActivateInstance(string className)
    {
        IntPtr h, instance;
        fixed (char* s = className)
            if (WindowsCreateString(s, (uint)className.Length, &h) < 0)
                return IntPtr.Zero;
        try
        {
            return RoActivateInstance(h, &instance) >= 0 ? instance : IntPtr.Zero;
        }
        finally
        {
            WindowsDeleteString(h);
        }
    }

    private static int Check(int hr) => hr >= 0 ? hr : throw Marshal.GetExceptionForHR(hr)!;

    [DllImport("combase.dll")]
    private static extern int WindowsCreateString(char* source, uint length, IntPtr* hstring);
    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, Guid* iid, IntPtr* factory);
    [DllImport("combase.dll")]
    private static extern int RoActivateInstance(IntPtr classId, IntPtr* instance);
    [DllImport("shlwapi.dll", EntryPoint = "SHCreateMemStream")]
    private static extern IntPtr SHCreateMemStream(byte* data, uint size);
    [DllImport("shcore.dll")]
    private static extern int CreateRandomAccessStreamOverStream(IntPtr stream, int options, Guid* iid, IntPtr* result);

    /// <summary>
    /// Um TypedEventHandler&lt;,&gt; do WinRT feito à mão: um objeto COM em memória nativa com a
    /// tabela QueryInterface/AddRef/Release/Invoke. É ágil (o sistema chama-o de outras threads).
    /// </summary>
    private static class WinRtHandler
    {
        private static readonly Guid IID_IUnknown = new("00000000-0000-0000-c000-000000000046");
        private static readonly Guid IID_IAgileObject = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
        private static readonly void** Vtable = CreateVtable();

        [StructLayout(LayoutKind.Sequential)]
        private struct Handler
        {
            public void** Vtable;
            public int References;
            public IntPtr Callback;
            public Guid Iid;
        }

        public static IntPtr Create(Guid iid, Action<IntPtr> onInvoke)
        {
            var o = (Handler*)NativeMemory.AllocZeroed((nuint)sizeof(Handler));
            o->Vtable = Vtable;
            o->References = 1;
            o->Callback = GCHandle.ToIntPtr(GCHandle.Alloc(onInvoke));
            o->Iid = iid;
            return (IntPtr)o;
        }

        private static void** CreateVtable()
        {
            var v = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
            v[0] = (delegate* unmanaged[Stdcall]<Handler*, Guid*, IntPtr*, int>)&QueryInterface;
            v[1] = (delegate* unmanaged[Stdcall]<Handler*, uint>)&AddRef;
            v[2] = (delegate* unmanaged[Stdcall]<Handler*, uint>)&Release;
            v[3] = (delegate* unmanaged[Stdcall]<Handler*, IntPtr, IntPtr, int>)&Invoke;
            return v;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int QueryInterface(Handler* self, Guid* iid, IntPtr* result)
        {
            if (*iid == IID_IUnknown || *iid == IID_IAgileObject || *iid == self->Iid)
            {
                Interlocked.Increment(ref self->References);
                *result = (IntPtr)self;
                return 0;
            }
            *result = IntPtr.Zero;
            return unchecked((int)0x80004002); // E_NOINTERFACE
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddRef(Handler* self) => (uint)Interlocked.Increment(ref self->References);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint Release(Handler* self)
        {
            var left = Interlocked.Decrement(ref self->References);
            if (left == 0)
            {
                GCHandle.FromIntPtr(self->Callback).Free();
                NativeMemory.Free(self);
            }
            return (uint)left;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(Handler* self, IntPtr sender, IntPtr args)
        {
            try
            {
                ((Action<IntPtr>)GCHandle.FromIntPtr(self->Callback).Target!)(args);
            }
            catch (Exception ex)
            {
                Log.Write("Painel multimédia: " + ex.Message);
            }
            return 0;
        }
    }
}
