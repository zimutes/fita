using System.Globalization;
using Avalonia;

namespace Fita;

public enum UiLanguage { En, Pt }

/// <summary>
/// Textos da interface, em inglês (por omissão) e em português de Portugal.
/// O XAML usa-os como {DynamicResource L.Chave}; os botões usam {DynamicResource LU.Chave}, que
/// fica em maiúsculas nos temas que as pedem. Trocar de língua atualiza a janela na hora.
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, (string En, string Pt)> Texts = new()
    {
        ["Tagline"] = ("Plays it all.", "Toca tudo."),
        ["By"] = ("by zimutek", "by zimutek"),
        ["About"] = ("A music player for Windows and Linux, in the spirit of the classic Winamp.",
                     "Um leitor de música para Windows e Linux, no espírito do Winamp clássico."),

        // Barra de título
        ["Menu"] = ("Menu", "Menu"),
        ["CompactTip"] = ("Compact mode (Ctrl+W)", "Modo compacto (Ctrl+W)"),
        ["Minimize"] = ("Minimize", "Minimizar"),
        ["Close"] = ("Close", "Fechar"),

        // Transporte
        ["PrevTip"] = ("Previous (Z)", "Anterior (Z)"),
        ["PlayTip"] = ("Play (X)", "Tocar (X)"),
        ["PauseTip"] = ("Pause (C)", "Pausa (C)"),
        ["StopTip"] = ("Stop (V)", "Parar (V)"),
        ["NextTip"] = ("Next (B)", "Seguinte (B)"),
        ["OpenTip"] = ("Open files (L)", "Abrir ficheiros (L)"),
        ["Shuffle"] = ("Shuffle", "Aleatório"),
        ["Repeat"] = ("Repeat", "Repetir"),
        ["RepeatOne"] = ("Repeat 1", "Repetir 1"),
        ["ShuffleTip"] = ("Shuffle (S)", "Ordem aleatória (S)"),
        ["RepeatTip"] = ("Repeat: off, all, one track (R)", "Repetir: não, tudo, uma faixa (R)"),
        ["EqToggle"] = ("EQ", "EQ"),
        ["PlToggle"] = ("PL", "PL"),
        ["EqToggleTip"] = ("Equalizer (Alt+G)", "Equalizador (Alt+G)"),
        ["PlToggleTip"] = ("Playlist (Alt+E)", "Lista (Alt+E)"),
        ["Vol"] = ("Vol", "Vol"),
        ["Bal"] = ("Bal", "Bal"),
        ["VolTip"] = ("Volume (↑ ↓, wheel)", "Volume (↑ ↓, roda)"),
        ["BalTip"] = ("Balance (double-click: centre)", "Balanço (duplo clique: centro)"),
        ["ValueTip"] = ("Type a value and press Enter · empty or double-click: back to default",
                        "Escreva um valor e carregue em Enter · vazio ou duplo clique: repõe"),
        ["BalValueTip"] = ("L 0–100, C, R 0–100 (or −100 to 100) · empty or double-click: centre",
                           "E 0–100, C, D 0–100 (ou −100 a 100) · vazio ou duplo clique: centro"),
        ["SeekTip"] = ("Position (← →: 5 s)", "Posição (← →: 5 s)"),

        // Mostrador
        ["Stopped"] = ("Stopped", "Parado"),
        ["Paused"] = ("Paused", "Em pausa"),
        ["Opening"] = ("Opening…", "A abrir…"),
        ["Idle"] = ("Drop music here, or press L", "Arraste música para aqui, ou carregue em L"),
        ["TimeTip"] = ("Click: elapsed or remaining (Ctrl+T)", "Clique: tempo decorrido ou que falta (Ctrl+T)"),
        ["VisTip"] = ("Click: spectrum, scope, off", "Clique: espectro, osciloscópio, desligado"),
        ["NoFfmpeg"] = ("ffmpeg not found: click here", "Falta o ffmpeg: clique aqui"),
        ["NoOutput"] = ("No sound output", "Sem saída de som"),
        ["NoOpenAl"] = ("OpenAL library missing", "Falta a biblioteca OpenAL"),
        ["CantRead"] = ("Can't read: {0}", "Não consigo ler: {0}"),
        ["StopAfterOn"] = ("Will stop after this track", "Pára depois desta faixa"),
        ["Mono"] = ("mono", "mono"),
        ["Stereo"] = ("stereo", "estéreo"),
        ["Limit"] = ("LIMIT", "LIMITE"),
        ["LimitTip"] = ("The limiter is working: without it, the sound would clip.",
                        "O limitador está a trabalhar: sem ele, o som saturava."),

        // Equalizador
        ["EqOn"] = ("On", "Ligado"),
        ["EqOnTip"] = ("Equalizer on/off", "Ligar/desligar o equalizador"),
        ["Auto"] = ("Auto", "Auto"),
        ["AutoTip"] = ("Lowers the preamp so boosts never clip", "Baixa o pré-amplificador para os reforços nunca saturarem"),
        ["Presets"] = ("Presets", "Predefinições"),
        ["SavePreset"] = ("Save", "Guardar"),
        ["ResetEq"] = ("Reset", "Repor"),
        ["Preamp"] = ("Preamp", "Pré"),
        ["PreampTip"] = ("Preamp", "Pré-amplificador"),
        ["EqHint"] = ("Drag the points · wheel: width · right-click: details · double-click: 0 dB",
                      "Arraste os pontos · roda: largura · botão direito: detalhes · duplo clique: 0 dB"),
        ["Frequency"] = ("Frequency", "Frequência"),
        ["Gain"] = ("Gain", "Ganho"),
        ["Width"] = ("Width (Q)", "Largura (Q)"),
        ["Type"] = ("Type", "Tipo"),
        ["Peak"] = ("Bell", "Sino"),
        ["LowShelf"] = ("Low shelf", "Prateleira grave"),
        ["HighShelf"] = ("High shelf", "Prateleira aguda"),
        ["LowCut"] = ("Low cut", "Corte de graves"),
        ["HighCut"] = ("High cut", "Corte de agudos"),
        ["ResetBand"] = ("Reset band", "Repor banda"),
        ["Band"] = ("Band {0}", "Banda {0}"),
        ["PresetName"] = ("Preset name", "Nome da predefinição"),
        ["Save"] = ("Save", "Guardar"),
        ["Cancel"] = ("Cancel", "Cancelar"),
        ["DeletePreset"] = ("Delete “{0}”", "Apagar «{0}»"),
        ["MyPresets"] = ("My presets", "As minhas"),
        ["Custom"] = ("Custom", "Personalizado"),
        ["P.Flat"] = ("Flat", "Plano"),
        ["P.Rock"] = ("Rock", "Rock"),
        ["P.Pop"] = ("Pop", "Pop"),
        ["P.Jazz"] = ("Jazz", "Jazz"),
        ["P.Classical"] = ("Classical", "Clássica"),
        ["P.Electronic"] = ("Electronic", "Eletrónica"),
        ["P.Hip-hop"] = ("Hip-hop", "Hip-hop"),
        ["P.Vocal"] = ("Vocal", "Voz"),
        ["P.BassBoost"] = ("Bass boost", "Mais graves"),
        ["P.TrebleBoost"] = ("Treble boost", "Mais agudos"),
        ["P.Loudness"] = ("Loudness", "Loudness"),
        ["P.LateNight"] = ("Late night", "Noite"),

        // Lista
        ["Search"] = ("Search (J)", "Procurar (J)"),
        ["Add"] = ("Add", "Juntar"),
        ["Remove"] = ("Remove", "Tirar"),
        ["Sort"] = ("Sort", "Ordenar"),
        ["List"] = ("List", "Lista"),
        ["AddFiles"] = ("Files…", "Ficheiros…"),
        ["AddFolder"] = ("Folder…", "Pasta…"),
        ["OpenPlaylist"] = ("Playlist (M3U, PLS)…", "Lista (M3U, PLS)…"),
        ["RemoveSelected"] = ("Selected (Del)", "Selecionadas (Del)"),
        ["RemoveMissing"] = ("Missing files", "Ficheiros que já não existem"),
        ["RemoveDuplicates"] = ("Duplicates", "Repetidas"),
        ["ClearList"] = ("Clear list", "Limpar a lista"),
        ["SortTitle"] = ("By title", "Por título"),
        ["SortArtist"] = ("By artist", "Por artista"),
        ["SortAlbum"] = ("By album", "Por álbum"),
        ["SortFileName"] = ("By file name", "Por nome do ficheiro"),
        ["SortPath"] = ("By folder and file", "Por pasta e ficheiro"),
        ["SortDuration"] = ("By length", "Por duração"),
        ["SortRandom"] = ("Shuffle the list", "Baralhar a lista"),
        ["SortReverse"] = ("Reverse", "Inverter"),
        ["SavePlaylist"] = ("Save list as…", "Guardar a lista como…"),
        ["NewPlaylist"] = ("New (clear)", "Nova (limpar)"),
        ["Play"] = ("Play", "Tocar"),
        ["PlayNext"] = ("Play next", "Tocar a seguir"),
        ["ShowInFolder"] = ("Show in folder", "Mostrar na pasta"),
        ["FileInfo"] = ("File info", "Informação do ficheiro"),
        ["Tracks1"] = ("1 track", "1 faixa"),
        ["TracksN"] = ("{0} tracks", "{0} faixas"),
        ["Reading"] = ("reading tags… {0}", "a ler etiquetas… {0}"),
        ["DropTitle"] = ("Drop music here", "Arraste música para aqui"),
        ["DropFormats"] = ("Files, folders or playlists · MP3, FLAC, Ogg, Opus, M4A, WAV, WMA, APE and more",
                           "Ficheiros, pastas ou listas · MP3, FLAC, Ogg, Opus, M4A, WAV, WMA, APE e outros"),
        ["NoMatches"] = ("Nothing matches “{0}”", "Nada corresponde a «{0}»"),
        ["Adding"] = ("Adding…", "A juntar…"),

        // Menu principal
        ["Theme"] = ("Theme", "Tema"),
        ["Language"] = ("Language", "Língua"),
        ["Output"] = ("Sound output", "Saída de som"),
        ["DefaultDevice"] = ("System default", "A do sistema"),
        ["ReplayGain"] = ("ReplayGain", "ReplayGain"),
        ["RgOff"] = ("Off", "Desligado"),
        ["RgTrack"] = ("Track", "Por faixa"),
        ["RgAlbum"] = ("Album", "Por álbum"),
        ["Visualizer"] = ("Visualizer", "Visualizador"),
        ["VisSpectrum"] = ("Spectrum", "Espectro"),
        ["VisScope"] = ("Oscilloscope", "Osciloscópio"),
        ["VisOff"] = ("Off", "Desligado"),
        ["ShowCover"] = ("Album art in the display", "Capa no mostrador"),
        ["AlwaysOnTop"] = ("Always on top", "Sempre à frente"),
        ["StopAfter"] = ("Stop after current (Ctrl+V)", "Parar depois desta (Ctrl+V)"),
        ["ResumeOption"] = ("Resume where I left off", "Continuar onde fiquei"),
        ["LocateFfmpeg"] = ("Locate ffmpeg…", "Indicar o ffmpeg…"),
        ["Shortcuts"] = ("Keyboard shortcuts", "Atalhos de teclado"),
        ["AboutMenu"] = ("About Fita", "Sobre a Fita"),
        ["Quit"] = ("Quit", "Sair"),

        // Faltas
        ["FfmpegMissingTitle"] = ("ffmpeg is missing", "Falta o ffmpeg"),
        ["FfmpegMissingBefore"] = ("Fita reads music with ffmpeg, which is free. Install it with",
                                   "A Fita lê a música com o ffmpeg, que é gratuito. Instale-o com"),
        ["FfmpegMissingAfter"] = ("and reopen Fita, or show where it is.", "e volte a abrir a Fita, ou indique onde está."),
        ["FindFfmpeg"] = ("Find ffmpeg", "Procurar o ffmpeg"),

        // Informação do ficheiro
        ["Title"] = ("Title", "Título"),
        ["Artist"] = ("Artist", "Artista"),
        ["Album"] = ("Album", "Álbum"),
        ["Year"] = ("Year", "Ano"),
        ["Genre"] = ("Genre", "Género"),
        ["TrackNo"] = ("Track", "Faixa"),
        ["Length"] = ("Length", "Duração"),
        ["FormatLabel"] = ("Format", "Formato"),
        ["File"] = ("File", "Ficheiro"),
        ["Size"] = ("Size", "Tamanho"),

        // Atalhos
        ["K.Transport"] = ("Previous · play · pause · stop · next", "Anterior · tocar · pausa · parar · seguinte"),
        ["K.Seek"] = ("Back / forward 5 seconds", "Recuar / avançar 5 segundos"),
        ["K.Volume"] = ("Volume", "Volume"),
        ["K.Jump"] = ("Search the list", "Procurar na lista"),
        ["K.Open"] = ("Add files / folder", "Juntar ficheiros / pasta"),
        ["K.Modes"] = ("Shuffle / repeat", "Aleatório / repetir"),
        ["K.Panels"] = ("Equalizer / playlist", "Equalizador / lista"),
        ["K.Compact"] = ("Compact mode", "Modo compacto"),
        ["K.Time"] = ("Elapsed / remaining time", "Tempo decorrido / que falta"),
        ["K.Theme"] = ("Next theme", "Tema seguinte"),
        ["K.StopAfter"] = ("Stop after current", "Parar depois desta"),
        ["K.Enter"] = ("Play the selected track", "Tocar a faixa selecionada"),
        ["K.Delete"] = ("Remove the selected tracks", "Tirar as faixas selecionadas"),
        ["K.Move"] = ("Move the selected tracks", "Mudar as faixas selecionadas de lugar"),
        ["Credits"] = ("Sound: FFmpeg, OpenAL Soft · Tags: TagLib# · Interface: Avalonia · Fonts: SIL Open Font License",
                       "Som: FFmpeg, OpenAL Soft · Etiquetas: TagLib# · Interface: Avalonia · Fontes: SIL Open Font License"),
        ["AiNote"] = ("Written by AI (Claude, by Anthropic) at the author's request.",
                      "Escrito por IA (Claude, da Anthropic) a pedido do autor."),
    };

    public static UiLanguage Language { get; private set; } = UiLanguage.En;
    public static bool Upper { get; private set; }

    public static CultureInfo Culture => Language == UiLanguage.Pt ? Pt.Value : En.Value;

    // Sem ICU (Linux mínimo, ou DOTNET_SYSTEM_GLOBALIZATION_INVARIANT) só há a cultura invariante.
    private static readonly Lazy<CultureInfo> Pt = new(() => Find("pt-PT"));
    private static readonly Lazy<CultureInfo> En = new(() => Find("en-GB"));

    private static CultureInfo Find(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public static event Action? Changed;

    public static string T(string key) =>
        Texts.TryGetValue(key, out var v) ? (Language == UiLanguage.Pt ? v.Pt : v.En) : key;

    public static string F(string key, params object[] args) => string.Format(Culture, T(key), args);

    public static string Count(int n) => n == 1 ? T("Tracks1") : F("TracksN", n);

    /// <summary>Nome visível de uma predefinição do EQ (as de origem são traduzidas).</summary>
    public static string PresetName(string name) => Texts.ContainsKey("P." + name) ? T("P." + name) : name;

    public static UiLanguage Parse(string? code) => code == "pt" ? UiLanguage.Pt : UiLanguage.En;

    public static string Code(UiLanguage language) => language == UiLanguage.Pt ? "pt" : "en";

    public static void Apply(UiLanguage language, bool upper)
    {
        Language = language;
        Upper = upper;
        var resources = Application.Current?.Resources;
        if (resources is not null)
            foreach (var key in Texts.Keys)
            {
                var text = T(key);
                resources["L." + key] = text;
                resources["LU." + key] = upper ? text.ToUpper(Culture) : text;
            }
        Changed?.Invoke();
    }
}
