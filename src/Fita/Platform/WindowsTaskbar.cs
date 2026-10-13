using System.Runtime.InteropServices;
using Fita.Audio;

namespace Fita.Platform;

/// <summary>
/// O botão da Fita na barra de tarefas do Windows: anterior, tocar/pausa e seguinte na
/// miniatura (como no Winamp e no Windows Media Player) e o progresso da faixa no ícone.
/// ITaskbarList3 chamado pela tabela de métodos. Os cliques chegam à janela como WM_COMMAND
/// (<see cref="HandleMessage"/>). Só se cria no Windows; chama-se só da thread da interface.
/// </summary>
internal sealed unsafe class WindowsTaskbar : IDisposable
{
    private static readonly Guid CLSID_TaskbarList = new("56fdf344-fd6d-11d0-958a-006097c9a090");
    private static readonly Guid IID_ITaskbarList3 = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");

    // ITaskbarList (3..7), ITaskbarList2 (8), ITaskbarList3 (9..)
    private const int HrInit = 3, SetProgressValue = 9, SetProgressState = 10, ThumbBarAddButtons = 15, ThumbBarUpdateButtons = 16;
    private const int TBPF_NOPROGRESS = 0, TBPF_NORMAL = 2, TBPF_PAUSED = 8;
    private const uint THB_ICON = 0x2, THB_TOOLTIP = 0x4, THB_FLAGS = 0x8;
    private const uint THBF_ENABLED = 0, THBF_DISABLED = 0x1;
    private const int WM_COMMAND = 0x0111;
    private const int THBN_CLICKED = 0x1800;

    private enum ButtonId : uint { Previous = 1, PlayPause = 2, Next = 3 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ThumbButton
    {
        public uint Mask;
        public uint Id;
        public uint Bitmap;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Tip;
        public uint Flags;
    }

    private readonly IntPtr _window;
    private readonly Action<MediaCommand> _onCommand;
    private readonly Func<GlyphIcon, IntPtr> _makeIcon;
    private readonly uint _buttonCreatedMessage;
    private readonly Dictionary<GlyphIcon, IntPtr> _icons = [];
    private IntPtr _taskbar;
    private bool _buttonsAdded;
    private PlayState _state = PlayState.Stopped;
    private bool _hasTrack;
    private int _progressState = -1;
    private bool _disposed;

    public enum GlyphIcon { Previous, Play, Pause, Next }

    /// <param name="makeIcon">Desenha o ícone (HICON) de um botão; quem chama sabe desenhar.</param>
    public WindowsTaskbar(IntPtr window, Action<MediaCommand> onCommand, Func<GlyphIcon, IntPtr> makeIcon)
    {
        _window = window;
        _onCommand = onCommand;
        _makeIcon = makeIcon;
        _buttonCreatedMessage = RegisterWindowMessageW("TaskbarButtonCreated");
        Connect();
    }

    /// <summary>Progresso no ícone (pode desligar-se no menu).</summary>
    public bool ShowProgress { get; set; } = true;

    /// <summary>
    /// Mensagens da janela: os cliques nos botões e o aviso de que a barra (re)criou o botão
    /// da Fita (ao arrancar, ou quando o Explorador reinicia). Verdadeiro se tratou.
    /// </summary>
    public bool HandleMessage(uint message, IntPtr wParam)
    {
        if (_disposed)
            return false;
        if (message == _buttonCreatedMessage)
        {
            Release();
            Connect();
            return false;
        }
        if (message == WM_COMMAND && ((long)wParam >> 16 & 0xFFFF) == THBN_CLICKED)
        {
            switch ((ButtonId)((long)wParam & 0xFFFF))
            {
                case ButtonId.Previous: _onCommand(MediaCommand.Previous); return true;
                case ButtonId.PlayPause: _onCommand(MediaCommand.PlayPause); return true;
                case ButtonId.Next: _onCommand(MediaCommand.Next); return true;
            }
        }
        return false;
    }

    public void SetState(PlayState state, bool hasTrack)
    {
        if (state == _state && hasTrack == _hasTrack)
            return;
        _state = state;
        _hasTrack = hasTrack;
        UpdateButtons(add: false);
    }

    public void SetProgress(double position, double duration)
    {
        if (_taskbar == IntPtr.Zero)
            return;
        var state = !ShowProgress || duration <= 0 || _state == PlayState.Stopped ? TBPF_NOPROGRESS
            : _state == PlayState.Paused ? TBPF_PAUSED : TBPF_NORMAL;
        if (state != _progressState)
        {
            _progressState = state;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)Slot(_taskbar, SetProgressState))(_taskbar, _window, state);
        }
        if (state != TBPF_NOPROGRESS)
        {
            // Em milésimos: o ícone não tem resolução para mais.
            var done = (ulong)Math.Clamp(position / duration * 1000, 0, 1000);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong, ulong, int>)Slot(_taskbar, SetProgressValue))(_taskbar, _window, done, 1000);
        }
    }

    private void Connect()
    {
        IntPtr taskbar;
        var clsid = CLSID_TaskbarList;
        var iid = IID_ITaskbarList3;
        if (CoCreateInstance(&clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, &iid, &taskbar) < 0)
            return;
        if (((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(taskbar, HrInit))(taskbar) < 0)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(taskbar, 2))(taskbar);
            return;
        }
        _taskbar = taskbar;
        _buttonsAdded = false;
        _progressState = -1;
        UpdateButtons(add: true);
    }

    /// <summary>Os botões só se juntam uma vez por janela; depois só se atualizam.</summary>
    private void UpdateButtons(bool add)
    {
        if (_taskbar == IntPtr.Zero || (!add && !_buttonsAdded))
            return;
        var playing = _state == PlayState.Playing;
        var flags = _hasTrack ? THBF_ENABLED : THBF_DISABLED;
        var buttons = new[]
        {
            Button(ButtonId.Previous, GlyphIcon.Previous, Loc.T("Previous"), flags),
            Button(ButtonId.PlayPause, playing ? GlyphIcon.Pause : GlyphIcon.Play, Loc.T(playing ? "PauseVerb" : "Play"), THBF_ENABLED),
            Button(ButtonId.Next, GlyphIcon.Next, Loc.T("Next"), flags),
        };
        var size = Marshal.SizeOf<ThumbButton>();
        var native = Marshal.AllocHGlobal(size * buttons.Length);
        try
        {
            for (var i = 0; i < buttons.Length; i++)
                Marshal.StructureToPtr(buttons[i], native + i * size, false);
            var hr = Send(add ? ThumbBarAddButtons : ThumbBarUpdateButtons);
            // A barra pode avisar (TaskbarButtonCreated) depois de os botões já lá estarem: juntar
            // outra vez falha, e aí basta atualizá-los.
            if (add)
                _buttonsAdded = hr >= 0 || Send(ThumbBarUpdateButtons) >= 0;

            int Send(int slot) =>
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr, int>)Slot(_taskbar, slot))(_taskbar, _window, (uint)buttons.Length, native);
        }
        finally
        {
            for (var i = 0; i < buttons.Length; i++)
                Marshal.DestroyStructure<ThumbButton>(native + i * size);
            Marshal.FreeHGlobal(native);
        }
    }

    private ThumbButton Button(ButtonId id, GlyphIcon glyph, string tip, uint flags)
    {
        if (!_icons.TryGetValue(glyph, out var icon))
            _icons[glyph] = icon = _makeIcon(glyph);
        return new ThumbButton { Mask = THB_ICON | THB_TOOLTIP | THB_FLAGS, Id = (uint)id, Icon = icon, Tip = tip, Flags = flags };
    }

    private void Release()
    {
        if (_taskbar != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(_taskbar, 2))(_taskbar);
        _taskbar = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_taskbar != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)Slot(_taskbar, SetProgressState))(_taskbar, _window, TBPF_NOPROGRESS);
        Release();
        foreach (var icon in _icons.Values)
            if (icon != IntPtr.Zero)
                DestroyIcon(icon);
        _icons.Clear();
    }

    private static void* Slot(IntPtr p, int index) => (*(void***)p)[index];

    // ── Ícones ────────────────────────────────────────────────────────────────

    /// <summary>Lado dos ícones da miniatura (o ícone pequeno do sistema, à escala do ecrã).</summary>
    public static int IconSize(IntPtr window)
    {
        try
        {
            return GetSystemMetricsForDpi(49 /* SM_CXSMICON */, GetDpiForWindow(window));
        }
        catch
        {
            return 16;
        }
    }

    /// <summary>HICON a partir de píxeis BGRA (alfa direto, linhas de cima para baixo).</summary>
    public static IntPtr CreateIcon(int size, byte[] bgra)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader), Width = size, Height = -size, Planes = 1, BitCount = 32,
        };
        void* bits;
        var color = CreateDIBSection(IntPtr.Zero, &header, 0, &bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero)
            return IntPtr.Zero;
        Marshal.Copy(bgra, 0, (IntPtr)bits, Math.Min(bgra.Length, size * size * 4));
        var mask = CreateBitmap(size, size, 1, 1, null);
        try
        {
            var info = new IconInfo { Icon = 1, Mask = mask, Color = color };
            return CreateIconIndirect(&info);
        }
        finally
        {
            DeleteObject(color);
            DeleteObject(mask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int Icon;
        public uint XHotspot;
        public uint YHotspot;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(Guid* clsid, IntPtr outer, uint context, Guid* iid, IntPtr* result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(IconInfo* info);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, BitmapInfoHeader* info, uint usage, void** bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
}
