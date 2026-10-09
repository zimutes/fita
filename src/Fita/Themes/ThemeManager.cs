using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Fita.Themes;

/// <summary>
/// Aplica um tema: põe as cores, fontes, raios e sombras nos recursos da aplicação (o XAML usa
/// {DynamicResource …}, por isso tudo muda na hora) e avisa os controlos desenhados à mão.
/// </summary>
public static class ThemeManager
{
    public static ThemeDef Current { get; private set; } = Catalog.All[0];

    public static event Action? Changed;

    public static void Apply(ThemeDef t)
    {
        Current = t;
        var app = Application.Current;
        if (app is null)
            return;
        app.RequestedThemeVariant = t.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
        var r = app.Resources;

        void B(string key, Color color) => r[key] = new SolidColorBrush(color);
        void S(string key, string shadows) =>
            r[key] = string.IsNullOrWhiteSpace(shadows) ? new BoxShadows() : BoxShadows.Parse(shadows);

        r["FontSans"] = t.FontSans;
        r["FontDisplay"] = t.FontDisplay;
        r["FontMono"] = t.FontMono;
        r["FontTime"] = t.FontTime;
        r["BtnFont"] = t.BtnFont ?? t.FontDisplay;
        r["TimeWeight"] = t.TimeWeight;
        r["TimeSize"] = t.TimeSize;
        r["TitleWeight"] = t.TitleWeight;
        r["TitleSpacing"] = t.TitleSpacing;

        B("WindowBase", t.Base);
        B("Text", t.Text);
        B("TextDim", t.TextDim);
        B("TextMuted", t.TextMuted);
        B("Accent", t.Accent);
        B("Brand", t.Brand);
        B("OnAccent", t.OnAccent);

        B("PanelBg", t.PanelBg);
        B("PanelBorder", t.PanelBorder);
        r["PanelRadius"] = new CornerRadius(t.PanelRadius);
        S("PanelShadow", t.PanelShadow);
        B("InsetBg", t.InsetBg);
        B("InsetBorder", t.InsetBorder);
        r["InsetRadius"] = new CornerRadius(t.InsetRadius);
        S("InsetShadow", t.InsetShadow);
        B("TitleBg", t.TitleBg);
        B("TitleFg", t.TitleText);
        B("ListBg", t.ListBg);
        B("ListBorder", t.ListBorder);
        S("ListShadow", t.ListShadow);
        r["ListRadius"] = new CornerRadius(Math.Min(t.InsetRadius, 12));
        B("Divider", t.Divider);

        B("Lcd", t.Lcd);
        B("LcdDim", t.LcdDim);
        r["LcdEffect"] = t.LcdGlow.A > 0
            ? new DropShadowEffect { BlurRadius = 10, OffsetX = 0, OffsetY = 0, Color = t.LcdGlow, Opacity = 1 }
            : null;

        B("BtnBg", t.BtnBg);
        B("BtnBgHover", t.BtnBgHover);
        B("BtnBgPressed", t.BtnBgPressed ?? t.BtnBgHover);
        B("BtnBorder", t.BtnBorder);
        B("BtnBorderHover", t.BtnBorderHover);
        B("BtnFg", t.BtnFg);
        B("BtnFgHover", t.BtnFgHover ?? t.BtnFg);
        r["BtnRadius"] = new CornerRadius(t.BtnRadius);
        r["BtnBorderThickness"] = new Thickness(t.BtnBorderWidth);
        S("BtnShadow", t.BtnShadow);
        S("BtnShadowHover", string.IsNullOrEmpty(t.BtnShadowHover) ? t.BtnShadow : t.BtnShadowHover);
        S("BtnShadowPressed", string.IsNullOrEmpty(t.BtnShadowPressed) ? t.BtnShadow : t.BtnShadowPressed);
        r["BtnWeight"] = t.BtnWeight;
        r["BtnSpacing"] = t.BtnSpacing;
        r["BtnFontSize"] = t.BtnFontSize;
        B("ToggleOnBg", t.ToggleOnBg);
        B("ToggleOnFg", t.ToggleOnFg);
        B("ToggleOnBorder", t.ToggleOnBorder);
        S("ToggleOnShadow", t.ToggleOnShadow);
        B("PlayBg", t.PlayBg);
        B("PlayFg", t.PlayFg);
        S("PlayShadow", t.PlayShadow);

        B("InputBg", t.InputBg);
        B("InputBorder", t.InputBorder);
        B("InputFocus", t.InputFocus);
        r["InputRadius"] = new CornerRadius(t.InputRadius);
        r["ValueRadius"] = new CornerRadius(Math.Min(t.InputRadius, 6));
        S("InputShadow", t.InputShadow);
        B("SelectionBg", t.SelectionBg);
        B("SelectionFg", t.SelectionFg);
        B("RowHover", t.RowHover);
        B("CurrentFg", t.CurrentFg);

        B("MenuBg", t.MenuBg);
        B("MenuBorder", t.MenuBorder);
        B("MenuHover", t.MenuHover);
        B("MenuHoverFg", t.MenuHoverFg);
        r["MenuRadius"] = new CornerRadius(t.MenuRadius);

        // Recursos do tema Fluent (menus, dicas, campos de texto, barras de deslocação).
        B("MenuFlyoutPresenterBackground", t.MenuBg);
        B("MenuFlyoutPresenterBorderBrush", t.MenuBorder);
        B("MenuFlyoutItemBackground", Colors.Transparent);
        B("MenuFlyoutItemBackgroundPointerOver", t.MenuHover);
        B("MenuFlyoutItemBackgroundPressed", t.MenuHover);
        B("MenuFlyoutItemForeground", t.Text);
        B("MenuFlyoutItemForegroundPointerOver", t.MenuHoverFg);
        B("MenuFlyoutItemForegroundPressed", t.MenuHoverFg);
        B("MenuFlyoutItemKeyboardAcceleratorTextForeground", t.TextMuted);
        B("MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver", t.MenuHoverFg);
        B("MenuFlyoutSubItemChevron", t.TextMuted);
        B("MenuFlyoutSubItemChevronPointerOver", t.MenuHoverFg);
        B("MenuFlyoutSubItemChevronSubMenuOpened", t.MenuHoverFg);
        r["OverlayCornerRadius"] = new CornerRadius(t.MenuRadius);
        r["ControlCornerRadius"] = new CornerRadius(Math.Min(t.InputRadius, 8));
        B("ToolTipBackground", t.MenuBg);
        B("ToolTipForeground", t.Text);
        B("ToolTipBorderBrush", t.MenuBorder);
        B("TextControlForeground", t.Text);
        B("TextControlForegroundPointerOver", t.Text);
        B("TextControlForegroundFocused", t.Text);
        B("TextControlPlaceholderForeground", t.TextMuted);
        B("TextControlPlaceholderForegroundPointerOver", t.TextMuted);
        B("TextControlPlaceholderForegroundFocused", t.TextMuted);
        B("TextControlSelectionHighlightColor", t.Accent);
        B("TextControlBackground", t.InputBg);
        B("TextControlBackgroundPointerOver", t.InputBg);
        B("TextControlBackgroundFocused", t.InputBg);
        B("TextControlBorderBrush", t.InputBorder);
        B("TextControlBorderBrushPointerOver", t.InputBorder);
        B("TextControlBorderBrushFocused", t.InputFocus);
        r["ScrollBarThumbBackgroundColor"] = t.Slider == SliderLook.Bevel ? Color.Parse("#808080") : t.SliderTrack;
        r["ScrollBarPanningThumbBackgroundColor"] = t.SliderTrack;
        B("ScrollBarPanningThumbBackground", t.SliderTrack);
        B("ScrollBarThumbFillPointerOver", t.SliderFill);
        B("ScrollBarThumbFillPressed", t.SliderFill);

        Loc.Apply(Loc.Language, t.Uppercase);
        Changed?.Invoke();
    }
}
