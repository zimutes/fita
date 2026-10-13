using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Fita.Views;

/// <summary>
/// As janelas da Fita no modo de janelas separadas, como no Winamp clássico: ao arrastar, uma
/// janela cola-se às outras (e às bordas do ecrã) quando passa perto; arrastar o leitor leva
/// atrás as que estão coladas a ele (e as coladas a essas). O arrastar é feito aqui, à mão, e
/// não pelo sistema: assim funciona igual no Windows e no Linux (X11). Tudo em píxeis do ecrã.
/// </summary>
public sealed class WindowDock
{
    private const int Snap = 10;
    private const int Touch = 2;

    private readonly Window _main;
    private readonly List<Window> _windows = [];
    private Window? _moving;
    private PixelPoint _grabScreen;
    private List<(Window Window, PixelPoint Start)> _group = [];

    public WindowDock(Window main)
    {
        _main = main;
        _windows.Add(main);
        main.SizeChanged += (_, e) => FollowResize(main, e);
    }

    public void Add(Window window)
    {
        _windows.Add(window);
        window.SizeChanged += (_, e) => FollowResize(window, e);
    }

    /// <summary>Uma zona da janela (a barra de título) passa a servir de pega para arrastar.</summary>
    public void Grip(Window window, Control area, Func<PointerPressedEventArgs, bool>? accept = null)
    {
        area.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed || e.ClickCount > 1)
                return;
            if (accept is not null && !accept(e))
                return;
            Begin(window, window.PointToScreen(e.GetPosition(window)));
            e.Pointer.Capture(area);
            e.Handled = true;
        };
        area.PointerMoved += (_, e) =>
        {
            if (_moving == window)
                Move(window.PointToScreen(e.GetPosition(window)));
        };
        area.PointerReleased += (_, e) =>
        {
            if (_moving != window)
                return;
            _moving = null;
            e.Pointer.Capture(null);
        };
        area.PointerCaptureLost += (_, _) =>
        {
            if (_moving == window)
                _moving = null;
        };
    }

    private void Begin(Window window, PixelPoint screen)
    {
        _moving = window;
        _grabScreen = screen;
        // O leitor leva as coladas; as outras andam sozinhas (e descolam-se).
        var members = window == _main ? Connected(_main) : [window];
        _group = members.Select(w => (w, w.Position)).ToList();
    }

    private void Move(PixelPoint screen)
    {
        if (_moving is null)
            return;
        var dx = screen.X - _grabScreen.X;
        var dy = screen.Y - _grabScreen.Y;
        var movingStart = _group.First(g => g.Window == _moving).Start;
        var size = SizeOf(_moving);
        var proposed = new PixelRect(movingStart.X + dx, movingStart.Y + dy, size.Width, size.Height);
        var (sx, sy) = SnapOffset(proposed, _group.Select(g => g.Window).ToHashSet());
        foreach (var (w, start) in _group)
            w.Position = new PixelPoint(start.X + dx + sx, start.Y + dy + sy);
    }

    /// <summary>O ajuste para encostar às outras janelas ou às bordas do ecrã, se estiver perto.</summary>
    private (int X, int Y) SnapOffset(PixelRect r, HashSet<Window> moving)
    {
        int? bestX = null, bestY = null;
        void TryX(int from, int to)
        {
            var d = to - from;
            if (Math.Abs(d) <= Snap && (bestX is null || Math.Abs(d) < Math.Abs(bestX.Value)))
                bestX = d;
        }
        void TryY(int from, int to)
        {
            var d = to - from;
            if (Math.Abs(d) <= Snap && (bestY is null || Math.Abs(d) < Math.Abs(bestY.Value)))
                bestY = d;
        }
        foreach (var other in _windows.Where(w => w.IsVisible && !moving.Contains(w)))
        {
            var o = RectOf(other);
            var overlapsV = r.Y < o.Bottom + Snap && r.Bottom > o.Y - Snap;
            var overlapsH = r.X < o.Right + Snap && r.Right > o.X - Snap;
            if (overlapsV)
            {
                TryX(r.X, o.Right);       // à direita da outra
                TryX(r.Right, o.X);       // à esquerda da outra
                TryX(r.X, o.X);           // alinhadas à esquerda
                TryX(r.Right, o.Right);   // alinhadas à direita
            }
            if (overlapsH)
            {
                TryY(r.Y, o.Bottom);      // por baixo da outra
                TryY(r.Bottom, o.Y);      // por cima da outra
                TryY(r.Y, o.Y);
                TryY(r.Bottom, o.Bottom);
            }
        }
        if (_main.Screens.ScreenFromPoint(new PixelPoint(r.X + r.Width / 2, r.Y + r.Height / 2)) is { } screen)
        {
            var a = screen.WorkingArea;
            TryX(r.X, a.X);
            TryX(r.Right, a.Right);
            TryY(r.Y, a.Y);
            TryY(r.Bottom, a.Bottom);
        }
        return (bestX ?? 0, bestY ?? 0);
    }

    /// <summary>As janelas coladas a esta, e as coladas a essas.</summary>
    private List<Window> Connected(Window root)
    {
        var result = new List<Window> { root };
        for (var i = 0; i < result.Count; i++)
        {
            var a = RectOf(result[i]);
            foreach (var w in _windows)
                if (w.IsVisible && !result.Contains(w) && Touching(a, RectOf(w)))
                    result.Add(w);
        }
        return result;
    }

    private static bool Touching(PixelRect a, PixelRect b)
    {
        var overlapH = a.X < b.Right && a.Right > b.X;
        var overlapV = a.Y < b.Bottom && a.Bottom > b.Y;
        return (overlapH && (Math.Abs(a.Bottom - b.Y) <= Touch || Math.Abs(b.Bottom - a.Y) <= Touch))
            || (overlapV && (Math.Abs(a.Right - b.X) <= Touch || Math.Abs(b.Right - a.X) <= Touch));
    }

    /// <summary>
    /// Uma janela mudou de altura (o modo compacto, o equalizador): as que estavam coladas por
    /// baixo dela acompanham, para não ficarem penduradas nem tapadas.
    /// </summary>
    private void FollowResize(Window window, SizeChangedEventArgs e)
    {
        if (_moving is not null || !window.IsVisible || e.PreviousSize.Height <= 0)
            return;
        var scale = window.RenderScaling;
        var dy = (int)Math.Round((e.NewSize.Height - e.PreviousSize.Height) * scale);
        if (dy == 0)
            return;
        var oldBottom = window.Position.Y + (int)Math.Round(e.PreviousSize.Height * scale);
        var r = RectOf(window);
        var below = _windows.Where(w => w != window && w.IsVisible && Math.Abs(w.Position.Y - oldBottom) <= Touch
            && w.Position.X < r.Right && w.Position.X + SizeOf(w).Width > r.X).ToList();
        foreach (var w in below)
        {
            // Leva também as que estão coladas por baixo dessa.
            var chain = Connected(w).Where(c => c != window && c.Position.Y >= w.Position.Y).ToList();
            foreach (var c in chain)
                c.Position = new PixelPoint(c.Position.X, c.Position.Y + dy);
        }
    }

    public static PixelSize SizeOf(Window w)
    {
        var size = w.FrameSize ?? w.ClientSize;
        return new PixelSize((int)Math.Round(size.Width * w.RenderScaling), (int)Math.Round(size.Height * w.RenderScaling));
    }

    public static PixelRect RectOf(Window w) => new(w.Position, SizeOf(w));
}
