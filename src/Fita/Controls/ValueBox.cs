using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Fita.Controls;

/// <summary>
/// Caixa de valor ao lado de um controlo de deslizar (volume, balanço, bandas do EQ): escreve-se
/// o número e Enter aplica. Repor é fácil: apagar e Enter, ou duplo clique. Setas e roda do rato
/// andam um passo; Esc desiste.
/// </summary>
public sealed class ValueBox : TextBox
{
    private double _value;
    private bool _editing;

    protected override Type StyleKeyOverride => typeof(TextBox);

    public ValueBox()
    {
        Classes.Add("value");
        AcceptsReturn = false;
        TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
    }

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public double Step { get; set; } = 1;
    public double DefaultValue { get; set; }
    /// <summary>Como o valor aparece (por omissão, um número inteiro).</summary>
    public Func<double, string> Formatter { get; set; } = v => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>Lê o que se escreveu; null se não for um valor.</summary>
    public Func<string, double?> Parser { get; set; } = ParseNumber;

    /// <summary>O utilizador fixou um valor (Enter, sair da caixa, setas, roda, repor).</summary>
    public event EventHandler<double>? Committed;

    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            if (!_editing)
                Text = Formatter(value);
        }
    }

    public static double? ParseNumber(string text)
    {
        var t = text.Trim().Replace(',', '.').Replace("dB", "", StringComparison.OrdinalIgnoreCase).Replace("%", "").Trim();
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private void Commit(double value)
    {
        value = Math.Clamp(Math.Round(value / Step) * Step, Minimum, Maximum);
        _editing = false;
        Value = value;
        Committed?.Invoke(this, value);
    }

    private void CommitText()
    {
        var text = Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
            Commit(DefaultValue);
        else if (Parser(text) is { } v && double.IsFinite(v))
            Commit(v);
        else
            Revert();
    }

    private void Revert()
    {
        _editing = false;
        Text = Formatter(_value);
    }

    private void Reset()
    {
        Commit(DefaultValue);
        Dispatcher.UIThread.Post(SelectAll);
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _editing = true;
        Dispatcher.UIThread.Post(SelectAll);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (_editing)
            CommitText();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitText();
                _editing = true;
                SelectAll();
                e.Handled = true;
                return;
            case Key.Escape:
                Revert();
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                e.Handled = true;
                return;
            case Key.Up:
            case Key.Down:
                var current = Parser(Text ?? "") ?? _value;
                Commit(current + (e.Key == Key.Up ? Step : -Step));
                _editing = true;
                SelectAll();
                e.Handled = true;
                return;
        }
        _editing = true;
        base.OnKeyDown(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        Commit(_value + Math.Sign(delta) * Step);
        if (IsFocused)
        {
            _editing = true;
            SelectAll();
        }
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Reset();
            e.Handled = true;
            return;
        }
        base.OnPointerPressed(e);
    }
}
