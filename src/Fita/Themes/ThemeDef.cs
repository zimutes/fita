using Avalonia;
using Avalonia.Media;

namespace Fita.Themes;

/// <summary>Como se desenham os controlos de deslizar.</summary>
public enum SliderLook
{
    /// <summary>Cápsula com enchimento e bola luminosa.</summary>
    Pill,
    /// <summary>Calha afundada e cursor em relevo, à Windows 95.</summary>
    Bevel,
    /// <summary>Blocos, como uma barra de progresso de terminal.</summary>
    Block,
    /// <summary>Fio de luz e ponto pequeno (OLED).</summary>
    Hairline,
    /// <summary>Fio de ouro e cursor em losango.</summary>
    Jewel,
}

/// <summary>Como se desenha o espectro.</summary>
public enum VisLook
{
    /// <summary>Barras cheias em degradê.</summary>
    Bars,
    /// <summary>Barras de LED em segmentos (verde, amarelo, vermelho).</summary>
    Segments,
    /// <summary>Barras finas.</summary>
    Thin,
    /// <summary>Curva cheia, como uma onda.</summary>
    Wave,
}

/// <summary>Uma camada de luz do fundo: um gradiente radial em coordenadas relativas à janela.</summary>
public sealed record GlowLayer(double X, double Y, double Radius, Color Color);

/// <summary>
/// Um tema. Inspirados nos seis temas do painel do Jazmin (Purple Haze, 1995 Calling, Midnight
/// OLED, Boutique Gold, Toxic Terminal, Oceanic Flow): cada um muda cores, fontes, relevo,
/// forma dos botões e o desenho do visualizador e dos controlos.
/// </summary>
public sealed class ThemeDef
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public bool IsLight { get; init; }

    // Fontes
    public required FontFamily FontSans { get; init; }
    public required FontFamily FontDisplay { get; init; }
    public required FontFamily FontMono { get; init; }
    public required FontFamily FontTime { get; init; }
    /// <summary>Fonte dos botões (por omissão, a dos títulos).</summary>
    public FontFamily? BtnFont { get; init; }
    public FontWeight TimeWeight { get; init; } = FontWeight.SemiBold;
    public double TimeSize { get; init; } = 30;
    public FontWeight TitleWeight { get; init; } = FontWeight.Bold;
    public double TitleSpacing { get; init; } = 2;

    // Fundo
    public required Color Base { get; init; }
    public IReadOnlyList<GlowLayer> Glows { get; init; } = [];
    /// <summary>Linhas de varrimento de CRT (0 = nenhuma).</summary>
    public Color Scanlines { get; init; }
    /// <summary>Escurece o fundo de cima para baixo (abismo do Oceanic).</summary>
    public Color BottomShade { get; init; }

    // Texto
    public required Color Text { get; init; }
    public required Color TextDim { get; init; }
    public required Color TextMuted { get; init; }
    public required Color Accent { get; init; }
    public required Color Brand { get; init; }
    public required Color OnAccent { get; init; }

    // Painéis
    public required Color PanelBg { get; init; }
    public required Color PanelBorder { get; init; }
    public double PanelRadius { get; init; } = 14;
    public string PanelShadow { get; init; } = "";
    public required Color InsetBg { get; init; }
    public required Color InsetBorder { get; init; }
    public double InsetRadius { get; init; } = 10;
    public string InsetShadow { get; init; } = "";
    public Color TitleBg { get; init; } = Colors.Transparent;
    public Color? TitleFg { get; init; }
    public Color ListBg { get; init; } = Colors.Transparent;
    public Color ListBorder { get; init; } = Colors.Transparent;
    public string ListShadow { get; init; } = "";
    public required Color Divider { get; init; }

    // Mostrador
    public required Color Lcd { get; init; }
    public required Color LcdDim { get; init; }
    /// <summary>Brilho do texto do mostrador (transparente = sem brilho).</summary>
    public Color LcdGlow { get; init; } = Colors.Transparent;

    // Botões
    public required Color BtnBg { get; init; }
    public required Color BtnBgHover { get; init; }
    public Color? BtnBgPressed { get; init; }
    public required Color BtnBorder { get; init; }
    public required Color BtnBorderHover { get; init; }
    public required Color BtnFg { get; init; }
    public Color? BtnFgHover { get; init; }
    public double BtnRadius { get; init; } = 10;
    public double BtnBorderWidth { get; init; } = 1;
    public string BtnShadow { get; init; } = "";
    public string BtnShadowHover { get; init; } = "";
    public string BtnShadowPressed { get; init; } = "";
    public FontWeight BtnWeight { get; init; } = FontWeight.SemiBold;
    public double BtnSpacing { get; init; }
    public double BtnFontSize { get; init; } = 12;
    public bool Uppercase { get; init; }
    public required Color ToggleOnBg { get; init; }
    public required Color ToggleOnFg { get; init; }
    public required Color ToggleOnBorder { get; init; }
    public string ToggleOnShadow { get; init; } = "";
    public required Color PlayBg { get; init; }
    public required Color PlayFg { get; init; }
    public string PlayShadow { get; init; } = "";
    /// <summary>Premir afunda 1 px na diagonal (Windows 95).</summary>
    public bool PressSinks { get; init; }
    /// <summary>Passar por cima levanta 1 px (Oceanic).</summary>
    public bool HoverLifts { get; init; }
    public bool Motion { get; init; } = true;

    // Campos e listas
    public required Color InputBg { get; init; }
    public required Color InputBorder { get; init; }
    public required Color InputFocus { get; init; }
    public double InputRadius { get; init; } = 8;
    public string InputShadow { get; init; } = "";
    public required Color SelectionBg { get; init; }
    public required Color SelectionFg { get; init; }
    public required Color RowHover { get; init; }
    public required Color CurrentFg { get; init; }

    // Menus
    public required Color MenuBg { get; init; }
    public required Color MenuBorder { get; init; }
    public required Color MenuHover { get; init; }
    public required Color MenuHoverFg { get; init; }
    public double MenuRadius { get; init; } = 10;

    // Controlos desenhados
    public SliderLook Slider { get; init; } = SliderLook.Pill;
    public required Color SliderTrack { get; init; }
    public required Color SliderFill { get; init; }
    public required Color SliderThumb { get; init; }
    public Color SliderThumbGlow { get; init; } = Colors.Transparent;
    public VisLook Vis { get; init; } = VisLook.Bars;
    public required Color VisLow { get; init; }
    public required Color VisHigh { get; init; }
    public required Color VisPeak { get; init; }
    public Color VisMid { get; init; } = Colors.Transparent;
    public required Color GraphCurve { get; init; }
    public required Color GraphFill { get; init; }
    public required Color GraphGrid { get; init; }
    public required Color GraphSpectrum { get; init; }

    /// <summary>Cantos da janela no Windows 11 (falso: cantos vivos).</summary>
    public bool RoundWindow { get; init; } = true;

    public Color TitleText => TitleFg ?? Text;
}
