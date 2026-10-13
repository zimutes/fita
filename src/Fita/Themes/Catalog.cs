using Avalonia.Media;

namespace Fita.Themes;

/// <summary>Os seis temas, com as paletas, fontes e gestos dos temas do painel do Jazmin.</summary>
public static class Catalog
{
    private static Color C(string hex) => Color.Parse(hex);

    private static Color A(string hex, double alpha)
    {
        var c = Color.Parse(hex);
        return Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B);
    }

    private static Color W(double alpha) => A("#ffffff", alpha);
    private static Color K(double alpha) => A("#000000", alpha);

    private static FontFamily Font(string folder, string family, string fallback = "") =>
        new($"avares://Fita/Assets/Fonts/{folder}#{family}{fallback}");

    // Recurso para títulos com letras que a fonte não tem (japonês, cirílico…): o Avalonia procura
    // nas fontes do sistema.
    private static FontFamily Barlow => Font("Barlow", "Barlow");
    private static FontFamily BarlowCondensed => Font("BarlowCondensed", "Barlow Condensed");
    private static FontFamily JetBrains => Font("JetBrainsMono", "JetBrains Mono");
    private static FontFamily PlexSans => Font("IBMPlexSans", "IBM Plex Sans");
    private static FontFamily PlexMono => Font("IBMPlexMono", "IBM Plex Mono");
    private static FontFamily Outfit => Font("Outfit", "Outfit");
    private static FontFamily DmSans => Font("DMSans", "DM Sans");
    private static FontFamily Playfair => Font("PlayfairDisplay", "Playfair Display");
    private static FontFamily ShareTech => Font("ShareTechMono", "Share Tech Mono");

    public static readonly IReadOnlyList<ThemeDef> All =
    [
        // ── Purple Haze: beringela quase preta, névoa violeta, néon ciano e rosa ─────────────
        new ThemeDef
        {
            Key = "purple", Name = "Purple Haze",
            FontSans = Barlow, FontDisplay = BarlowCondensed, FontMono = JetBrains, FontTime = JetBrains,
            TimeWeight = FontWeight.Bold, TitleSpacing = 3,
            Base = C("#0a0314"),
            Glows =
            [
                new(0.12, -0.08, 0.95, A("#7c3aed", 0.42)),
                new(0.96, 0.04, 0.75, A("#ff2079", 0.13)),
                new(0.70, 1.08, 0.85, A("#0de6f6", 0.10)),
                new(0.40, 0.50, 1.20, A("#4c1d95", 0.30)),
            ],
            Text = C("#ffffff"), TextDim = W(0.72), TextMuted = W(0.58),
            Accent = C("#0de6f6"), Brand = C("#ff2079"), OnAccent = C("#020617"),
            PanelBg = W(0.055), PanelBorder = A("#c4a7ff", 0.16), PanelRadius = 16,
            PanelShadow = "inset 0 1 0 0 #17FFFFFF, 0 18 48 0 #7305010C",
            InsetBg = A("#0a0314", 0.62), InsetBorder = A("#0de6f6", 0.16), InsetRadius = 11,
            InsetShadow = "inset 0 0 24 0 #140DE6F6",
            ListBg = A("#0a0314", 0.38), ListBorder = A("#c4a7ff", 0.10),
            Divider = A("#c4a7ff", 0.12),
            Lcd = C("#0de6f6"), LcdDim = A("#0de6f6", 0.55), LcdGlow = A("#0de6f6", 0.75),
            BtnBg = W(0.06), BtnBgHover = W(0.10), BtnBorder = W(0.16), BtnBorderHover = C("#0de6f6"),
            BtnFg = W(0.86), BtnFgHover = C("#ffffff"), BtnRadius = 9,
            BtnShadowHover = "0 0 14 0 #3D0DE6F6",
            Uppercase = true, BtnSpacing = 0.8, BtnWeight = FontWeight.Bold, BtnFontSize = 12.5,
            ToggleOnBg = A("#0de6f6", 0.14), ToggleOnFg = C("#0de6f6"), ToggleOnBorder = C("#0de6f6"),
            ToggleOnShadow = "0 0 12 0 #4D0DE6F6",
            PlayBg = C("#0de6f6"), PlayFg = C("#020617"), PlayShadow = "0 0 20 0 #4D0DE6F6",
            InputBg = W(0.07), InputBorder = W(0.15), InputFocus = C("#0de6f6"), InputRadius = 9,
            SelectionBg = A("#0de6f6", 0.16), SelectionFg = C("#ffffff"), RowHover = W(0.06), CurrentFg = C("#ff2079"),
            MenuBg = C("#1a0d2e"), MenuBorder = A("#0de6f6", 0.22), MenuHover = A("#0de6f6", 0.12), MenuHoverFg = C("#ffffff"), MenuRadius = 12,
            Slider = SliderLook.Pill, SliderTrack = W(0.12), SliderFill = C("#0de6f6"), SliderThumb = C("#ffffff"),
            SliderThumbGlow = A("#0de6f6", 0.7),
            Vis = VisLook.Bars, VisLow = C("#0de6f6"), VisHigh = C("#ff2079"), VisPeak = C("#ffffff"),
            GraphCurve = C("#0de6f6"), GraphFill = A("#0de6f6", 0.12), GraphGrid = W(0.07), GraphSpectrum = A("#ff2079", 0.22),
            // A crista acende no rosa do néon.
            Ripple = new WaveRipple { Speed = 200, Width = 7, Lift = 12, Lens = 7, Crest = C("#ff2079") },
        },

        // ── 1995 Calling: cinzento de janela, relevo, barra azul-marinho, nada brilha ────────
        new ThemeDef
        {
            Key = "arctic", Name = "1995 Calling", IsLight = true,
            FontSans = PlexSans, FontDisplay = PlexSans, FontMono = PlexMono, FontTime = PlexMono,
            TimeWeight = FontWeight.SemiBold, TitleSpacing = 0.5, TitleWeight = FontWeight.Bold,
            Base = C("#c0c0c0"),
            Text = C("#000000"), TextDim = K(0.78), TextMuted = K(0.62),
            Accent = C("#000080"), Brand = C("#008080"), OnAccent = C("#ffffff"),
            PanelBg = C("#c0c0c0"), PanelBorder = Colors.Transparent, PanelRadius = 0,
            PanelShadow = "inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #808080, inset 2 2 0 0 #DFDFDF",
            InsetBg = C("#000000"), InsetBorder = Colors.Transparent, InsetRadius = 0,
            InsetShadow = "inset 1 1 0 0 #808080, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #404040, inset -2 -2 0 0 #DFDFDF",
            TitleBg = C("#000080"), TitleFg = C("#ffffff"),
            ListBg = C("#ffffff"), ListShadow = "inset 1 1 0 0 #808080, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #000000, inset -2 -2 0 0 #DFDFDF",
            Divider = C("#808080"),
            Lcd = C("#00e640"), LcdDim = C("#00802a"),
            BtnBg = C("#c0c0c0"), BtnBgHover = C("#c0c0c0"), BtnBorder = Colors.Transparent, BtnBorderHover = Colors.Transparent,
            BtnFg = C("#000000"), BtnRadius = 0, BtnBorderWidth = 0,
            BtnShadow = "inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #000000, inset 2 2 0 0 #DFDFDF, inset -2 -2 0 0 #808080",
            BtnShadowHover = "inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #000000, inset 2 2 0 0 #DFDFDF, inset -2 -2 0 0 #808080",
            BtnShadowPressed = "inset 1 1 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #808080, inset -2 -2 0 0 #DFDFDF",
            BtnWeight = FontWeight.SemiBold, BtnFontSize = 12, PressSinks = true, Motion = false,
            ToggleOnBg = C("#dfdfdf"), ToggleOnFg = C("#000080"), ToggleOnBorder = Colors.Transparent,
            ToggleOnShadow = "inset 1 1 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #808080, inset -2 -2 0 0 #DFDFDF",
            PlayBg = C("#c0c0c0"), PlayFg = C("#000080"),
            PlayShadow = "inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #000000, inset 2 2 0 0 #DFDFDF, inset -2 -2 0 0 #808080",
            InputBg = C("#ffffff"), InputBorder = Colors.Transparent, InputFocus = Colors.Transparent, InputRadius = 0,
            InputShadow = "inset 1 1 0 0 #808080, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #000000, inset -2 -2 0 0 #DFDFDF",
            SelectionBg = C("#000080"), SelectionFg = C("#ffffff"), RowHover = A("#000080", 0.08), CurrentFg = C("#000080"),
            MenuBg = C("#c0c0c0"), MenuBorder = C("#000000"), MenuHover = C("#000080"), MenuHoverFg = C("#ffffff"), MenuRadius = 0,
            Slider = SliderLook.Bevel, SliderTrack = C("#808080"), SliderFill = C("#000080"), SliderThumb = C("#c0c0c0"),
            Vis = VisLook.Segments, VisLow = C("#00e640"), VisMid = C("#f0e000"), VisHigh = C("#ff3030"), VisPeak = C("#ffffff"),
            GraphCurve = C("#00e640"), GraphFill = A("#00e640", 0.10), GraphGrid = A("#00e640", 0.14), GraphSpectrum = A("#008080", 0.55),
            // Aos solavancos e em degraus, como uma animação do Windows 95; a crista no verde-azulado do ambiente de trabalho.
            Ripple = new WaveRipple { Speed = 170, Decay = 2.6, Width = 6, Lift = 12, Lens = 7, Fps = 12, Step = 2, Crest = C("#008080") },
            RoundWindow = false,
        },

        // ── Midnight OLED: preto absoluto, fio de luz, branco como acento ────────────────────
        new ThemeDef
        {
            Key = "midnight", Name = "Midnight OLED",
            FontSans = Outfit, FontDisplay = Outfit, FontMono = JetBrains, FontTime = Outfit,
            TimeWeight = FontWeight.Light, TimeSize = 34, TitleSpacing = 4, TitleWeight = FontWeight.SemiBold,
            Base = C("#000000"),
            Text = C("#ffffff"), TextDim = W(0.72), TextMuted = W(0.58),
            Accent = C("#ffffff"), Brand = C("#38bdf8"), OnAccent = C("#000000"),
            PanelBg = C("#000000"), PanelBorder = W(0.10), PanelRadius = 24,
            PanelShadow = "inset 0 1 0 0 #0FFFFFFF",
            InsetBg = W(0.035), InsetBorder = W(0.07), InsetRadius = 18,
            ListBg = Colors.Transparent,
            Divider = W(0.10),
            Lcd = C("#ffffff"), LcdDim = W(0.5),
            BtnBg = Colors.Transparent, BtnBgHover = W(0.08), BtnBorder = W(0.22), BtnBorderHover = W(0.5),
            BtnFg = W(0.86), BtnFgHover = C("#ffffff"), BtnRadius = 999,
            BtnWeight = FontWeight.SemiBold, BtnFontSize = 12.5,
            ToggleOnBg = C("#ffffff"), ToggleOnFg = C("#000000"), ToggleOnBorder = C("#ffffff"),
            PlayBg = C("#ffffff"), PlayFg = C("#000000"),
            InputBg = W(0.04), InputBorder = W(0.12), InputFocus = W(0.5), InputRadius = 999,
            SelectionBg = W(0.10), SelectionFg = C("#ffffff"), RowHover = W(0.05), CurrentFg = C("#38bdf8"),
            MenuBg = C("#0a0a0a"), MenuBorder = W(0.16), MenuHover = W(0.10), MenuHoverFg = C("#ffffff"), MenuRadius = 14,
            Slider = SliderLook.Hairline, SliderTrack = W(0.18), SliderFill = C("#ffffff"), SliderThumb = C("#ffffff"),
            Vis = VisLook.Thin, VisLow = W(0.55), VisHigh = C("#ffffff"), VisPeak = C("#38bdf8"),
            GraphCurve = C("#ffffff"), GraphFill = W(0.06), GraphGrid = W(0.07), GraphSpectrum = A("#38bdf8", 0.25),
            // Curta e rápida; a crista apanha o azul do pico.
            Ripple = new WaveRipple { Speed = 280, Decay = 3.2, Width = 5, Lift = 10, Lens = 6, LensWidth = 8, Crest = C("#38bdf8") },
        },

        // ── Boutique Gold: castanho quente, ouro em fio, serifa nos títulos ──────────────────
        new ThemeDef
        {
            Key = "boutique", Name = "Boutique Gold",
            FontSans = DmSans, FontDisplay = Playfair, FontMono = JetBrains, FontTime = Playfair, BtnFont = DmSans,
            TimeWeight = FontWeight.Normal, TimeSize = 32, TitleSpacing = 4, TitleWeight = FontWeight.SemiBold,
            Base = C("#0a0807"),
            Glows =
            [
                new(0.50, -0.12, 0.85, A("#d4af37", 0.13)),
                new(0.50, 1.20, 1.10, K(0.55)),
            ],
            Text = C("#f5ebd7"), TextDim = A("#f5ebd7", 0.78), TextMuted = A("#f5ebd7", 0.58),
            Accent = C("#d4af37"), Brand = C("#be123c"), OnAccent = C("#1a1412"),
            PanelBg = A("#d4af37", 0.05), PanelBorder = A("#d4af37", 0.20), PanelRadius = 10,
            PanelShadow = "inset 0 1 0 0 #24D4AF37, 0 14 44 0 #80000000",
            InsetBg = K(0.36), InsetBorder = A("#d4af37", 0.24), InsetRadius = 6,
            ListBg = K(0.22), ListBorder = A("#d4af37", 0.12),
            Divider = A("#d4af37", 0.18),
            Lcd = C("#e3c35a"), LcdDim = A("#d4af37", 0.55),
            BtnBg = Colors.Transparent, BtnBgHover = A("#d4af37", 0.08), BtnBorder = A("#d4af37", 0.45), BtnBorderHover = C("#d4af37"),
            BtnFg = C("#f5ebd7"), BtnRadius = 3,
            BtnShadow = "0 8 24 0 #73000000", BtnShadowHover = "0 10 30 0 #38D4AF37",
            Uppercase = true, BtnSpacing = 1.6, BtnWeight = FontWeight.SemiBold, BtnFontSize = 10.5,
            ToggleOnBg = C("#d4af37"), ToggleOnFg = C("#1a1412"), ToggleOnBorder = C("#d4af37"),
            PlayBg = C("#d4af37"), PlayFg = C("#1a1412"), PlayShadow = "0 8 24 0 #59000000",
            InputBg = A("#d4af37", 0.06), InputBorder = A("#d4af37", 0.28), InputFocus = C("#d4af37"), InputRadius = 4,
            SelectionBg = A("#d4af37", 0.14), SelectionFg = C("#f5ebd7"), RowHover = A("#d4af37", 0.06), CurrentFg = C("#e3c35a"),
            MenuBg = C("#1a1311"), MenuBorder = A("#d4af37", 0.35), MenuHover = A("#d4af37", 0.12), MenuHoverFg = C("#f5ebd7"), MenuRadius = 6,
            Slider = SliderLook.Jewel, SliderTrack = A("#d4af37", 0.30), SliderFill = C("#d4af37"), SliderThumb = C("#e3c35a"),
            Vis = VisLook.Thin, VisLow = A("#d4af37", 0.55), VisHigh = C("#f3d77a"), VisPeak = C("#be123c"),
            GraphCurve = C("#d4af37"), GraphFill = A("#d4af37", 0.08), GraphGrid = A("#d4af37", 0.09), GraphSpectrum = A("#be123c", 0.30),
            // Lenta e larga, sem pressa; a crista brilha em ouro claro.
            Ripple = new WaveRipple { Speed = 120, Decay = 1.5, Width = 12, Lift = 9, Lens = 6, LensWidth = 16, Crest = C("#fff1c4") },
        },

        // ── Toxic Terminal: fósforo verde, varrimento de CRT, vídeo inverso ──────────────────
        new ThemeDef
        {
            Key = "toxic", Name = "Toxic Terminal",
            FontSans = ShareTech, FontDisplay = ShareTech, FontMono = ShareTech, FontTime = ShareTech,
            TimeWeight = FontWeight.Normal, TimeSize = 34, TitleSpacing = 3, TitleWeight = FontWeight.Normal,
            Base = C("#010301"),
            Glows = [new(0.5, 0.4, 0.9, A("#adff2f", 0.06))],
            Scanlines = A("#adff2f", 0.028),
            Text = C("#eaffd6"), TextDim = A("#adff2f", 0.82), TextMuted = A("#adff2f", 0.60),
            Accent = C("#adff2f"), Brand = C("#f018ff"), OnAccent = C("#010301"),
            PanelBg = A("#adff2f", 0.025), PanelBorder = A("#adff2f", 0.28), PanelRadius = 0,
            PanelShadow = "0 0 24 0 #10ADFF2F",
            InsetBg = C("#000000"), InsetBorder = A("#adff2f", 0.35), InsetRadius = 0,
            InsetShadow = "inset 0 0 12 0 #14ADFF2F",
            ListBg = C("#000000"), ListBorder = A("#adff2f", 0.22),
            Divider = A("#adff2f", 0.22),
            Lcd = C("#adff2f"), LcdDim = A("#adff2f", 0.5), LcdGlow = A("#adff2f", 0.7),
            BtnBg = Colors.Transparent, BtnBgHover = A("#adff2f", 0.14), BtnBorder = A("#adff2f", 0.5), BtnBorderHover = C("#adff2f"),
            BtnFg = C("#adff2f"), BtnRadius = 0,
            BtnShadowHover = "0 0 16 0 #59ADFF2F",
            Uppercase = true, BtnSpacing = 1, BtnWeight = FontWeight.Normal, BtnFontSize = 13,
            ToggleOnBg = C("#adff2f"), ToggleOnFg = C("#010301"), ToggleOnBorder = C("#adff2f"),
            ToggleOnShadow = "0 0 12 0 #40ADFF2F",
            PlayBg = C("#adff2f"), PlayFg = C("#010301"), PlayShadow = "0 0 12 0 #40ADFF2F",
            InputBg = C("#000000"), InputBorder = A("#adff2f", 0.35), InputFocus = C("#adff2f"), InputRadius = 0,
            SelectionBg = C("#adff2f"), SelectionFg = C("#010301"), RowHover = A("#adff2f", 0.08), CurrentFg = C("#f018ff"),
            MenuBg = C("#010301"), MenuBorder = A("#adff2f", 0.55), MenuHover = C("#adff2f"), MenuHoverFg = C("#010301"), MenuRadius = 0,
            Slider = SliderLook.Block, SliderTrack = A("#adff2f", 0.28), SliderFill = C("#adff2f"), SliderThumb = C("#adff2f"),
            Vis = VisLook.Segments, VisLow = A("#adff2f", 0.55), VisMid = C("#adff2f"), VisHigh = C("#eaffd6"), VisPeak = C("#f018ff"),
            GraphCurve = C("#adff2f"), GraphFill = A("#adff2f", 0.08), GraphGrid = A("#adff2f", 0.10), GraphSpectrum = A("#f018ff", 0.30),
            // Aos blocos e ao ritmo de um ecrã que refresca; a crista é fósforo a queimar.
            Ripple = new WaveRipple { Speed = 240, Decay = 2.4, Width = 6, Lift = 12, Lens = 7, Fps = 20, Step = 2, Crest = C("#eaffd6") },
            RoundWindow = false,
        },

        // ── Oceanic Flow: azul-petróleo, luz de superfície, cápsulas, movimento lento ────────
        new ThemeDef
        {
            Key = "ocean", Name = "Oceanic Flow",
            FontSans = Outfit, FontDisplay = Outfit, FontMono = JetBrains, FontTime = JetBrains,
            TimeWeight = FontWeight.Bold, TitleSpacing = 3, TitleWeight = FontWeight.SemiBold,
            Base = C("#020d0f"),
            Glows = [new(0.18, -0.10, 0.85, A("#00d4ff", 0.16))],
            BottomShade = A("#000406", 0.55),
            Text = C("#ffffff"), TextDim = W(0.72), TextMuted = W(0.58),
            Accent = C("#00d4ff"), Brand = C("#fcd34d"), OnAccent = C("#02090b"),
            PanelBg = A("#00d4ff", 0.06), PanelBorder = A("#00d4ff", 0.18), PanelRadius = 24,
            PanelShadow = "inset 0 1 0 0 #1A8CEBFF, 0 10 40 0 #59000000",
            InsetBg = A("#01090b", 0.55), InsetBorder = A("#00d4ff", 0.14), InsetRadius = 16,
            ListBg = A("#01090b", 0.35), ListBorder = A("#00d4ff", 0.10),
            Divider = A("#00d4ff", 0.14),
            Lcd = C("#00d4ff"), LcdDim = A("#00d4ff", 0.55), LcdGlow = A("#00d4ff", 0.45),
            BtnBg = W(0.06), BtnBgHover = W(0.10), BtnBorder = W(0.16), BtnBorderHover = C("#00d4ff"),
            BtnFg = W(0.88), BtnFgHover = C("#ffffff"), BtnRadius = 999,
            BtnShadowHover = "0 6 18 0 #3300D4FF",
            BtnWeight = FontWeight.SemiBold, BtnFontSize = 12.5, HoverLifts = true,
            ToggleOnBg = A("#00d4ff", 0.18), ToggleOnFg = C("#00d4ff"), ToggleOnBorder = C("#00d4ff"),
            ToggleOnShadow = "0 6 20 0 #4000D4FF",
            PlayBg = C("#00d4ff"), PlayFg = C("#02090b"), PlayShadow = "0 6 20 0 #4000D4FF",
            InputBg = A("#00d4ff", 0.07), InputBorder = A("#00d4ff", 0.22), InputFocus = C("#00d4ff"), InputRadius = 999,
            SelectionBg = A("#00d4ff", 0.16), SelectionFg = C("#ffffff"), RowHover = A("#00d4ff", 0.07), CurrentFg = C("#fcd34d"),
            MenuBg = C("#062025"), MenuBorder = A("#00d4ff", 0.25), MenuHover = A("#00d4ff", 0.12), MenuHoverFg = C("#ffffff"), MenuRadius = 16,
            Slider = SliderLook.Pill, SliderTrack = A("#00d4ff", 0.16), SliderFill = C("#00d4ff"), SliderThumb = C("#ffffff"),
            SliderThumbGlow = A("#00d4ff", 0.55),
            Vis = VisLook.Wave, VisLow = A("#00d4ff", 0.05), VisHigh = C("#00d4ff"), VisPeak = C("#fcd34d"),
            GraphCurve = C("#00d4ff"), GraphFill = A("#00d4ff", 0.10), GraphGrid = A("#00d4ff", 0.08), GraphSpectrum = A("#fcd34d", 0.22),
            // Ondulação: três cristas que rolam devagar, com espuma branca.
            Ripple = new WaveRipple { Speed = 150, Decay = 1.3, Width = 8, Lift = 11, Lens = 7, Crests = 3, Crest = C("#ffffff") },
        },
    ];

    public static ThemeDef Get(string? key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];
}
