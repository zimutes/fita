using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Fita.Controls;

/// <summary>
/// Título a correr no mostrador, como no Winamp: se não cabe, desliza em contínuo com " *** "
/// entre voltas. A janela chama <see cref="Tick"/> a cada fotograma.
/// </summary>
public sealed class Marquee : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<Marquee, string?>(nameof(Text));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Marquee>();
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner<Marquee>();
    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<Marquee>();
    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextElement.FontWeightProperty.AddOwner<Marquee>();

    private const string Separator = "   ***   ";
    private const double Speed = 34;

    private FormattedText? _text;
    private FormattedText? _separator;
    private double _offset;
    private double _wait = 2;

    static Marquee()
    {
        AffectsMeasure<Marquee>(TextProperty, FontFamilyProperty, FontSizeProperty, FontWeightProperty);
        AffectsRender<Marquee>(ForegroundProperty);
    }

    public Marquee()
    {
        ClipToBounds = true;
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    /// <summary>Verdadeiro enquanto o texto não cabe e está a deslizar.</summary>
    public bool IsScrolling => _text is not null && _text.Width > Bounds.Width + 0.5;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontFamilyProperty || change.Property == FontSizeProperty
            || change.Property == FontWeightProperty || change.Property == ForegroundProperty)
        {
            _text = null;
            if (change.Property == TextProperty)
            {
                _offset = 0;
                _wait = 2;
            }
        }
    }

    private void Build()
    {
        var typeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight);
        var brush = Foreground ?? Brushes.White;
        _text = new FormattedText(Text ?? "", Loc.Culture, FlowDirection.LeftToRight, typeface, FontSize, brush);
        _separator = new FormattedText(Separator, Loc.Culture, FlowDirection.LeftToRight, typeface, FontSize, brush);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Build();
        return new Size(0, _text!.Height);
    }

    public void Tick(double dt)
    {
        if (_text is null || !IsScrolling)
            return;
        if (_wait > 0)
        {
            _wait -= dt;
            return;
        }
        _offset += Speed * dt;
        var loop = _text.Width + _separator!.Width;
        if (_offset >= loop)
            _offset -= loop;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_text is null)
            Build();
        var y = (Bounds.Height - _text!.Height) / 2;
        if (!IsScrolling)
        {
            context.DrawText(_text, new Point(0, y));
            return;
        }
        var x = -_offset;
        context.DrawText(_text, new Point(x, y));
        context.DrawText(_separator!, new Point(x + _text.Width, y));
        context.DrawText(_text, new Point(x + _text.Width + _separator!.Width, y));
    }
}
