using Fita.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Fita.Views;

/// <summary>
/// A visualização grande (<see cref="Vision"/>), numa janela à parte que só aparece quando se
/// pede: duplo clique no espectro ou Ctrl+Shift+K, como o Winamp. Duplo clique (ou F) põe em
/// ecrã inteiro, N passa ao preset seguinte, Esc sai. Ao mudar de faixa, o título aparece e
/// desvanece, como no MilkDrop.
/// </summary>
public sealed class VisionWindow : Window
{
    private readonly Vision _vision = new();
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly TextBlock _caption;
    private readonly Action<float[]> _copySamples;
    private readonly float[] _samples = new float[4096];
    private WriteableBitmap? _bitmap;
    private byte[] _pixels = [];
    private TimeSpan _last;
    private double _time;
    private double _captionLeft;
    private bool _open = true;

    public VisionWindow(Action<float[]> copySamples, double baseHue)
    {
        _copySamples = copySamples;
        _vision.BaseHue = baseHue;
        Title = "Fita";
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Fita/Assets/fita.ico")));
        Width = 640;
        Height = 360;
        MinWidth = 200;
        MinHeight = 120;
        Background = Brushes.Black;
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.MediumQuality);
        _caption = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(24, 0, 24, 22),
            VerticalAlignment = VerticalAlignment.Bottom,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 8, OffsetX = 0, OffsetY = 1, Color = Colors.Black, Opacity = 0.9 },
        };
        Content = new Panel { Children = { _image, _caption } };
        Cursor = new Cursor(StandardCursorType.Arrow);

        DoubleTapped += (_, _) => ToggleFullScreen();
        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape when WindowState == WindowState.FullScreen:
                    WindowState = WindowState.Normal;
                    break;
                case Key.Escape:
                    Close();
                    break;
                case Key.F:
                case Key.Enter:
                    ToggleFullScreen();
                    break;
                case Key.N:
                    _vision.NextPreset();
                    ShowCaption(_vision.PresetName);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };
        Opened += (_, _) => RequestAnimationFrame(OnFrame);
        Closed += (_, _) => _open = false;
    }

    public void ShowCaption(string text)
    {
        _caption.Text = text;
        _captionLeft = 4;
    }

    private void ToggleFullScreen() =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    private unsafe void OnFrame(TimeSpan now)
    {
        if (!_open)
            return;
        var dt = _last == TimeSpan.Zero ? 1 / 60.0 : (now - _last).TotalSeconds;
        _last = now;
        _time += dt;

        // A grelha é um terço da janela (no máximo 480 de largura): esticada, fica macia.
        var bounds = Bounds;
        var w = (int)Math.Clamp(bounds.Width / 3, 120, 480);
        var h = (int)Math.Clamp(w * bounds.Height / Math.Max(1, bounds.Width), 60, 480);
        _vision.Resize(w, h);
        if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _pixels = new byte[w * h * 4];
            _image.Source = _bitmap;
        }
        _copySamples(_samples);
        _vision.Render(_samples, dt, _time, _pixels);
        using (var frame = _bitmap.Lock())
        {
            for (var y = 0; y < h; y++)
                new ReadOnlySpan<byte>(_pixels, y * w * 4, w * 4).CopyTo(new Span<byte>((byte*)frame.Address + y * frame.RowBytes, w * 4));
        }
        _image.InvalidateVisual();

        if (_captionLeft > 0)
        {
            _captionLeft -= dt;
            // Aparece depressa, fica, e vai-se devagar.
            _caption.Opacity = Math.Clamp(Math.Min((4 - _captionLeft) * 3, _captionLeft / 1.5), 0, 1);
        }
        RequestAnimationFrame(OnFrame);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _bitmap?.Dispose();
    }
}
