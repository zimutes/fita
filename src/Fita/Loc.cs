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
        ["MuteTip"] = ("Mute (M)", "Sem som (M)"),
        ["Mute"] = ("Mute (M)", "Sem som (M)"),
        ["MuteInd"] = ("MUTE", "MUDO"),
        ["MutedOn"] = ("Muted", "Sem som"),
        ["MutedOff"] = ("Sound back", "Som de volta"),
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
        ["VisTip"] = ("Click: spectrum, scope, off · double-click: big visuals", "Clique: espectro, osciloscópio, desligado · duplo clique: visualização grande"),
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
        ["AutoTip"] = ("Lowers the preamp by the biggest boost: nothing clips, but boosts no longer sound louder",
                       "Baixa o pré-amplificador tanto quanto o maior reforço: nada satura, mas os reforços deixam de soar mais alto"),
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
        ["SearchLibrary"] = ("Search the library (J)", "Procurar na biblioteca (J)"),
        ["Lib"] = ("Lib", "Bib"),
        ["LibTip"] = ("Library: your music folders, by album (Alt+L)", "Biblioteca: as suas pastas de música, por disco (Alt+L)"),
        ["Folders"] = ("Folders", "Pastas"),
        ["AddMusicFolder"] = ("Add a music folder…", "Juntar uma pasta de música…"),
        ["RemoveMusicFolder"] = ("Remove a folder", "Tirar uma pasta"),
        ["Rescan"] = ("Look through the folders again", "Ver as pastas outra vez"),
        ["LibraryEmpty"] = ("Your library is empty", "A biblioteca está vazia"),
        ["LibraryEmptyBody"] = ("Add the folders where your music lives with FOLDERS, below. Fita keeps an eye on them.",
                                "Junte as pastas onde tem a música em PASTAS, aqui em baixo. A Fita fica atenta a elas."),
        ["LibraryScanning"] = ("Reading your music…", "A ler a sua música…"),
        ["LibEnqueue"] = ("Add to the playlist", "Juntar à lista"),
        ["LibReplace"] = ("Play instead of the playlist", "Tocar em vez da lista"),
        ["AddedN"] = ("{0} added to the playlist", "{0} juntas à lista"),
        ["AlbumsN"] = ("{0} albums", "{0} discos"),
        ["VariousArtists"] = ("Various artists", "Vários artistas"),
        ["K.Library"] = ("Library / playlist", "Biblioteca / lista"),
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
        ["PauseVerb"] = ("Pause", "Pausa"),
        ["Previous"] = ("Previous", "Anterior"),
        ["Next"] = ("Next", "Seguinte"),
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
        ["LoudnessScan"] = ("Measure files without tags", "Medir os ficheiros sem etiquetas"),
        ["Playback"] = ("Playback", "Reprodução"),
        ["Speed"] = ("Speed", "Velocidade"),
        ["SpeedNormal"] = ("Normal speed", "Velocidade normal"),
        ["KeepPitch"] = ("Keep the pitch", "Manter o tom"),
        ["Tape"] = ("tape", "fita"),
        ["LoopSetA"] = ("A–B loop: set A (A)", "Ciclo A–B: marcar o A (A)"),
        ["LoopSetB"] = ("A–B loop: set B (A)", "Ciclo A–B: marcar o B (A)"),
        ["LoopClear"] = ("A–B loop: off (A)", "Ciclo A–B: desligar (A)"),
        ["LoopOff"] = ("A–B loop off", "Ciclo A–B desligado"),
        ["Sleep"] = ("Sleep timer", "Temporizador para dormir"),
        ["SleepOff"] = ("Sleep timer off", "Temporizador desligado"),
        ["SleepIn"] = ("Stops in {0} min", "Para daqui a {0} min"),
        ["SleepDone"] = ("Good night", "Boa noite"),
        ["Off"] = ("Off", "Desligado"),
        ["ShowLyrics"] = ("Synced lyrics in the display", "Letra sincronizada no mostrador"),
        ["Crossfade"] = ("Crossfade", "Crossfade"),
        ["CrossfadeOff"] = ("Off (gapless)", "Desligado (sem silêncio)"),
        ["CrossfadeNote"] = ("Never within an album", "Nunca dentro de um disco"),
        ["ShowCover"] = ("Album art in the display", "Capa no mostrador"),
        ["BigVision"] = ("Big visuals (Ctrl+Shift+K)", "Visualização grande (Ctrl+Shift+K)"),
        ["VisionHelp"] = ("Double-click: full screen · N: next · Esc: close", "Duplo clique: ecrã inteiro · N: seguinte · Esc: fechar"),
        ["K.Vision"] = ("Big visuals", "Visualização grande"),
        ["ShowWaveform"] = ("Waveform in the seek bar", "Forma de onda na barra de posição"),
        ["AlwaysOnTop"] = ("Always on top", "Sempre à frente"),
        ["SeparateWindows"] = ("Separate windows that snap together", "Janelas separadas, que se colam"),
        ["EqTitle"] = ("Equalizer", "Equalizador"),
        ["PlTitle"] = ("Playlist", "Lista"),
        ["TaskbarProgress"] = ("Progress on the taskbar", "Progresso na barra de tarefas"),
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
        ["K.Mute"] = ("Mute / sound back", "Tirar / repor o som"),
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
        ["K.Loop"] = ("A–B loop: set A, set B, off", "Ciclo A–B: marcar o A, o B, desligar"),
        // Android
        ["M.TabList"] = ("Playlist", "Lista"),
        ["M.TabLibrary"] = ("Library", "Biblioteca"),
        ["M.TabEq"] = ("EQ", "EQ"),
        ["M.Search"] = ("Search", "Procurar"),
        ["M.ListEmpty"] = ("The list is empty: pick an album in the Library", "A lista está vazia: escolha um álbum na Biblioteca"),
        ["M.Idle"] = ("Pick music in the Library", "Escolha música na Biblioteca"),
        ["M.ClearList"] = ("Clear the list", "Limpar a lista"),
        ["M.AddToList"] = ("Add to the list", "Juntar à lista"),
        ["M.Scanning"] = ("Reading the music on the phone…", "A ler a música do telemóvel…"),
        ["M.NeedPermission"] = ("Fita needs permission to read the music on the phone.", "A Fita precisa de autorização para ler a música do telemóvel."),
        ["M.Grant"] = ("Allow", "Autorizar"),
        ["M.NoMusic"] = ("No music found on the phone.", "Não encontrei música no telemóvel."),
        ["M.Rescan"] = ("Read the music again", "Ler a música outra vez"),
        ["M.RepeatOff"] = ("Repeat off", "Sem repetir"),
        ["M.RepeatAll"] = ("Repeat the list", "Repetir a lista"),
        ["M.RepeatOne"] = ("Repeat this track", "Repetir esta faixa"),
        ["M.Channel"] = ("Playback", "Reprodução"),

        // Modo producer
        ["Producer"] = ("Producer", "Producer"),
        ["ProducerMode"] = ("Producer mode", "Modo producer"),
        ["ProducerOn"] = ("Producer mode on", "Modo producer ligado"),
        ["ProducerOff"] = ("Producer mode off", "Modo producer desligado"),
        ["Analyzing"] = ("Analyzing… ({0} left)", "A analisar… (faltam {0})"),
        ["NoAnalysis"] = ("Couldn't analyze this track", "Não consegui analisar esta faixa"),
        ["NoFindings"] = ("Nothing to point out", "Nada a apontar"),
        ["OneWarning"] = ("1 warning", "1 aviso"),
        ["Warnings"] = ("{0} warnings", "{0} avisos"),
        ["Corr"] = ("corr", "corr"),
        ["Listen"] = ("Listen", "Ouvir"),
        ["Listen.Stereo"] = ("Stereo", "Estéreo"),
        ["Listen.Mono"] = ("Mono (L+R)", "Mono (E+D)"),
        ["Listen.Side"] = ("Side only (L−R)", "Só o lado (E−D)"),
        ["Listen.Left"] = ("Left only", "Só a esquerda"),
        ["Listen.Right"] = ("Right only", "Só a direita"),
        ["ListenShort.Mono"] = ("MONO", "MONO"),
        ["ListenShort.Side"] = ("SIDE", "LADO"),
        ["ListenShort.Left"] = ("L", "E"),
        ["ListenShort.Right"] = ("R", "D"),
        ["SoloBand"] = ("Solo a band", "Ouvir uma banda"),
        ["AllBands"] = ("All bands", "Todas as bandas"),
        ["SoloOff"] = ("All bands", "Todas as bandas"),
        ["SoloOn"] = ("Only the {0} ({1})", "Só os {0} ({1})"),
        ["MatchLoudness"] = ("Same loudness for all ({0} LUFS)", "Tudo ao mesmo volume ({0} LUFS)"),
        ["ABSwitch"] = ("Switch with the reference", "Trocar com a referência"),
        ["IsReference"] = ("This track is a reference", "Esta faixa é referência"),
        ["SetReference"] = ("Use as reference", "Usar como referência"),
        ["UnsetReference"] = ("Stop using as reference", "Deixar de ser referência"),
        ["ReferenceOn"] = ("Set as reference", "Ficou como referência"),
        ["ReferenceOff"] = ("No longer a reference", "Deixou de ser referência"),
        ["NoReference"] = ("No reference yet: right-click a track, Use as reference",
                           "Ainda não há referência: botão direito numa faixa, Usar como referência"),
        ["AddNote"] = ("Note at this point", "Nota neste ponto"),
        ["NoteHint"] = ("What to fix here…", "O que há para mexer aqui…"),
        ["NoteAt"] = ("Note at {0}", "Nota aos {0}"),
        ["NoteSaved"] = ("Note saved at {0}", "Nota guardada aos {0}"),
        ["ShowNotes"] = ("Notes…", "Notas…"),
        ["Notes"] = ("Notes", "Notas"),
        ["NoNotes"] = ("No notes", "Sem notas"),
        ["Findings"] = ("Analysis…", "Análise…"),
        ["Report"] = ("Generate report…", "Gerar relatório…"),
        ["ReportTitle"] = ("Production report", "Relatório de produção"),
        ["ReportFile"] = ("report", "relatório"),
        ["ReportEmpty"] = ("The list is empty", "A lista está vazia"),
        ["ReportWaiting"] = ("Analyzing for the report… ({0} left)", "A analisar para o relatório… (faltam {0})"),
        ["ReportDone"] = ("Report saved", "Relatório guardado"),
        ["K.Producer"] = ("Producer mode", "Modo producer"),
        ["K.AB"] = ("Switch with the reference · next reference", "Trocar com a referência · referência seguinte"),
        ["K.Listen"] = ("Mono / side · one band alone · all bands", "Mono / lado · uma banda sozinha · todas"),
        ["K.NoteReport"] = ("Note at this point · report", "Nota neste ponto · relatório"),

        ["Band.Sub"] = ("Sub", "Sub"),
        ["Band.Bass"] = ("Bass", "Graves"),
        ["Band.LowMid"] = ("Low mids", "Méd.-graves"),
        ["Band.Mid"] = ("Mids", "Médios"),
        ["Band.HighMid"] = ("High mids", "Méd.-agudos"),
        ["Band.Air"] = ("Air", "Brilho"),
        ["BandLong.Sub"] = ("sub-bass", "subgraves"),
        ["BandLong.Bass"] = ("bass", "graves"),
        ["BandLong.LowMid"] = ("low mids", "médios-graves"),
        ["BandLong.Mid"] = ("mids", "médios"),
        ["BandLong.HighMid"] = ("high mids", "médios-agudos"),
        ["BandLong.Air"] = ("highs", "agudos"),

        // Avisos da análise
        ["F.Silent"] = ("Silence: nothing to measure", "Silêncio: não há nada para medir"),
        ["F.TruePeakOver"] = ("True peak {0} dBTP: above 0, it will distort when converted",
                              "Pico real {0} dBTP: acima de 0, vai distorcer na conversão"),
        ["F.TruePeakHigh"] = ("True peak {0} dBTP: above −1, it can clip when encoded to MP3/AAC",
                              "Pico real {0} dBTP: acima de −1, pode saturar ao passar a MP3/AAC"),
        ["F.Clipping"] = ("Clipping: {0} (first at {1})", "Saturações: {0} (a primeira aos {1})"),
        ["F.PhaseRun"] = ("Negative correlation from {0} to {1}: the phase is inverted",
                          "Correlação negativa entre {0} e {1}: a fase está invertida"),
        ["F.BassNotMono"] = ("Bass not in mono (correlation {0} below 250 Hz)", "Graves fora de mono (correlação {0} abaixo dos 250 Hz)"),
        ["F.MonoLoss"] = ("Loses {0} dB in mono in the {1}", "Perde {0} dB em mono nos {1}"),
        ["F.VeryWide"] = ("Very wide (correlation {0}): check it in mono", "Muito larga (correlação {0}): ouça em mono"),
        ["F.Crushed"] = ("PLR {0} dB: heavily compressed", "PLR {0} dB: muito comprimida"),
        ["F.Dc"] = ("DC offset of {0} dBFS", "Desvio DC de {0} dBFS"),
        ["F.Outlier"] = ("{0} LU away from the rest of the list", "{0} LU em relação ao resto da lista"),
        ["F.TonalRef"] = ("{0} dB in the {1} compared with the references", "{0} dB nos {1} em relação às referências"),
        ["F.TonalList"] = ("{0} dB in the {1} compared with the list average", "{0} dB nos {1} em relação à média da lista"),
        ["F.Loud"] = ("Very loud: Spotify turns it down {0} dB", "Muito alta: o Spotify baixa-a {0} dB"),
        ["F.LeadIn"] = ("{0} s of silence at the start", "{0} s de silêncio no início"),
        ["F.Tail"] = ("{0} s of silence at the end", "{0} s de silêncio no fim"),

        // Relatório
        ["R.OneTrack"] = ("1 track", "1 faixa"),
        ["R.Tracks"] = ("{0} tracks", "{0} faixas"),
        ["R.AgainstRefs"] = ("tonal balance compared with the reference tracks", "balanço tonal comparado com as faixas de referência"),
        ["R.AgainstList"] = ("tonal balance compared with the list average (no reference tracks)",
                             "balanço tonal comparado com a média da lista (sem faixas de referência)"),
        ["R.Findings"] = ("Warnings", "Avisos"),
        ["R.NoFindings"] = ("No problems or warnings.", "Sem problemas nem avisos."),
        ["R.Summary"] = ("Summary", "Resumo"),
        ["R.Track"] = ("Track", "Faixa"),
        ["R.File"] = ("File", "Ficheiro"),
        ["R.SummaryNote"] = ("LUFS: integrated loudness. TP: true peak (dBTP). PLR: true peak minus loudness (low means heavily compressed). LRA: loudness range. Bands: difference in dB to the comparison. Platforms: how much each one turns the track up or down.",
                             "LUFS: volume integrado. TP: pico real (dBTP). PLR: pico real menos o volume (baixo quer dizer muito comprimida). LRA: gama de volume. Bandas: diferença em dB para a comparação. Plataformas: quanto cada uma sobe ou baixa a faixa."),
        ["R.Footer"] = ("Loudness, range and true peak by the EBU R128 meter in ffmpeg; the rest measured by Fita. Platform figures are approximate public values, and they change.",
                        "Volume, gama e pico real pelo medidor EBU R128 do ffmpeg; o resto medido pela Fita. Os valores das plataformas são públicos e aproximados, e mudam."),
        ["R.Integrated"] = ("Integrated", "Integrado"),
        ["R.Range"] = ("Range (LRA)", "Gama (LRA)"),
        ["R.TruePeak"] = ("True peak", "Pico real"),
        ["R.SamplePeak"] = ("Sample peak", "Pico de amostra"),
        ["R.ShortMax"] = ("Short-term max", "Máx. de curta duração"),
        ["R.MomentaryMax"] = ("Momentary max", "Máx. momentâneo"),
        ["R.Clips"] = ("Clipping", "Saturações"),
        ["R.Clip"] = ("Clipping", "Saturação"),
        ["R.Silence"] = ("Silence at start · end", "Silêncio no início · fim"),
        ["R.SilenceStart"] = ("Silence at start", "Silêncio no início"),
        ["R.SilenceEnd"] = ("Silence at end", "Silêncio no fim"),
        ["R.Duration"] = ("Duration", "Duração"),
        ["R.Format"] = ("Format", "Formato"),
        ["R.Timeline"] = ("Over time", "Ao longo do tempo"),
        ["R.Tonal"] = ("Tonal balance", "Balanço tonal"),
        ["R.Level"] = ("Level (dB)", "Nível (dB)"),
        ["R.VsRefs"] = ("vs. references", "vs. referências"),
        ["R.VsList"] = ("vs. list average", "vs. média da lista"),
        ["R.ThisTrack"] = ("this track", "esta faixa"),
        ["R.MonoLoss"] = ("Mono loss (dB)", "Perda em mono (dB)"),
        ["R.Width"] = ("Side vs mid (dB)", "Lado vs meio (dB)"),
        ["R.Streaming"] = ("Streaming", "Streaming"),
        ["R.PlaysAt"] = ("plays at {0} LUFS", "toca a {0} LUFS"),
        ["R.Median"] = ("median", "mediana"),
        ["Credits"] = ("Sound: FFmpeg, OpenAL Soft · Tags: TagLib# · Interface: Avalonia · Fonts: SIL Open Font License",
                       "Som: FFmpeg, OpenAL Soft · Etiquetas: TagLib# · Interface: Avalonia · Fontes: SIL Open Font License"),
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
