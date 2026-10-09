using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Fita.Platform;

/// <summary>
/// Teclas multimédia do teclado (tocar/pausa, seguinte, anterior, parar) com a janela em
/// segundo plano, por RegisterHotKey. Uma thread própria com fila de mensagens; registar e
/// largar as teclas faz-se nessa thread, a pedido. Só se arranca no Windows.
/// </summary>
public sealed class WindowsMediaKeys : IDisposable
{
    public enum MediaKey { PlayPause = 1, Next = 2, Previous = 3, Stop = 4 }

    private const int WM_HOTKEY = 0x0312;
    private const int WM_APP = 0x8000;
    private const int MsgRegister = WM_APP + 1;
    private const int MsgUnregister = WM_APP + 2;
    private const int WM_QUIT = 0x0012;
    private const uint MOD_NOREPEAT = 0x4000;

    private static readonly (MediaKey Key, uint Vk)[] Keys =
    [
        (MediaKey.PlayPause, 0xB3), (MediaKey.Next, 0xB0), (MediaKey.Previous, 0xB1), (MediaKey.Stop, 0xB2),
    ];

    private readonly Action<MediaKey> _onKey;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private bool _active;

    private WindowsMediaKeys(Action<MediaKey> onKey) => _onKey = onKey;

    public static WindowsMediaKeys Start(Action<MediaKey> onKey)
    {
        var keys = new WindowsMediaKeys(onKey);
        new Thread(keys.Loop) { IsBackground = true, Name = "fita-media-keys" }.Start();
        keys._started.Wait(2000);
        return keys;
    }

    public void SetActive(bool active)
    {
        if (active == _active || _threadId == 0)
            return;
        _active = active;
        PostThreadMessage(_threadId, active ? MsgRegister : MsgUnregister, IntPtr.Zero, IntPtr.Zero);
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        // Garante que a fila de mensagens existe antes de alguém lhe mandar mensagens.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _started.Set();
        var registered = false;
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            switch (msg.message)
            {
                case WM_HOTKEY:
                    var id = (int)msg.wParam;
                    if (Enum.IsDefined(typeof(MediaKey), id))
                        _onKey((MediaKey)id);
                    break;
                case MsgRegister when !registered:
                    foreach (var (key, vk) in Keys)
                        if (!RegisterHotKey(IntPtr.Zero, (int)key, MOD_NOREPEAT, vk))
                            Log.Write($"Tecla multimédia {key} já está com outra aplicação.");
                    registered = true;
                    break;
                case MsgUnregister when registered:
                    foreach (var (key, _) in Keys)
                        UnregisterHotKey(IntPtr.Zero, (int)key);
                    registered = false;
                    break;
            }
        }
        if (registered)
            foreach (var (key, _) in Keys)
                UnregisterHotKey(IntPtr.Zero, (int)key);
    }

    public void Dispose()
    {
        if (_threadId != 0)
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Msg msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

/// <summary>Cantos e borda da janela no Windows 11 (no Windows 10 as chamadas falham sem mal).</summary>
[SupportedOSPlatform("windows")]
public static class WindowsDwm
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    public static void Apply(IntPtr hwnd, bool round)
    {
        try
        {
            var corner = round ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            var border = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
