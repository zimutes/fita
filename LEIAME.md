<p align="center">
  <img src="docs/icon.png" width="96" alt="Ícone da Fita: uma cassete">
</p>

<h1 align="center">Fita</h1>

<p align="center">
  <b>Toca tudo.</b><br>
  Um leitor de música para Windows e Linux, no espírito do Winamp clássico.<br>
  <sub>by zimutek</sub>
</p>

<p align="center">
  <a href="README.md">Read me in English</a> · Windows 10 / 11 · Linux · .NET 10 · ffmpeg · MIT
</p>

<p align="center">
  <img src="docs/themes.png" width="900" alt="A janela da Fita nos seis temas: Purple Haze, 1995 Calling, Midnight OLED, Boutique Gold, Toxic Terminal e Oceanic Flow">
</p>

---

> ### Leia isto primeiro
>
> **Testada** no Windows 11 e em Debian 13 (X11), com ficheiros MP3, FLAC, Ogg Vorbis, Opus,
> AAC, WAV, WMA e AIFF. Partilhada tal como está.
>
> **Sem ligação ao Winamp.** «Winamp» é uma marca dos seus donos; a Fita só lhe pede o
> espírito emprestado. Sem garantia de qualquer tipo, ver a [LICENSE](LICENSE).

---

## Porquê

Uma janela pequena, que abre depressa, toca tudo o que lhe atirarem, mostra o espectro
enquanto toca e tem um equalizador que dá mesmo para afinar. Era isso o Winamp. A Fita
(o nome vem das cassetes) é essa ideia, refeita para hoje, em Windows e em Linux.

## O que faz

- **Toca quase tudo**, através do ffmpeg: MP3, FLAC, Ogg Vorbis, Opus, AAC/M4A, ALAC, WAV,
  AIFF, WMA, APE, WavPack, Musepack, TTA, DSD (DSF/DFF), AC-3, DTS, áudio Matroska e, quando
  o seu ffmpeg os traz, módulos de tracker (MOD, XM, IT, S3M) e música de consolas (NSF, SPC, VGM).
- **Sem silêncio entre faixas** (gapless). A faixa seguinte é lida antes de a atual acabar e
  encosta-se a ela, por isso os discos ao vivo e as misturas correm como foram gravados.
- **Equalizador com detalhe.** Dez bandas (31 Hz a 16 kHz) com controlos de deslizar e um
  gráfico da curva real por cima do espectro ao vivo. Arraste um ponto para mudar o ganho e a
  frequência, use a roda para a largura (Q), e o botão direito para os valores exatos e o tipo
  de banda: sino, prateleira grave/aguda, corte de graves/agudos. Pré-amplificador, um
  limitador para os reforços não saturarem, pré *Auto* opcional (desligado por omissão: baixa
  tudo tanto quanto o maior reforço), 12 predefinições e as suas.
- **Cada nível tem uma caixa onde se escreve o valor:** volume, balanço (`E30`, `C`, `D30`),
  pré-amplificador e cada banda. Enter aplica; vazio + Enter ou duplo clique repõe (volume 80,
  balanço ao centro, 0 dB). Setas e roda do rato andam um passo.
- **Crossfade, se quiser** (desligado por omissão; de 2 a 8 s, no menu): de potência igual,
  para o volume não afundar a meio. Nunca dentro de um disco nem entre as faixas de um `.cue`:
  essas continuam sem silêncio.
- **Folhas CUE:** um disco inteiro num só FLAC/APE/WAV com um `.cue` ao lado toca faixa a
  faixa (abra o `.cue`, ou junte a pasta), e cada faixa passa à seguinte sem silêncio nem
  estalido, porque o mesmo leitor continua por cima da fronteira.
- **Lista de reprodução:** arraste ficheiros, pastas ou listas M3U/PLS; mude a ordem a
  arrastar; procure enquanto escreve (`J`); ordene por título, artista, álbum, ficheiro, pasta
  ou duração; tire repetidas ou ficheiros que já não existem; guarde em M3U8 ou PLS. Lembra-se
  da lista, da faixa e de onde parou.
- **Uma biblioteca no mesmo espaço:** o botão *BIB* (ou `Alt+L`) troca a lista pelas suas
  pastas de música, disco a disco (agrupados por pasta, para as compilações não se partirem).
  Um clique abre o disco, duplo clique toca-o (junta-se ao fim da lista), o botão direito
  pode tocá-lo em vez da lista, e o `J` procura na biblioteca inteira, com ou sem acentos. A
  Fita fica atenta às pastas e só volta a ler os ficheiros que mudaram.
- **Mostrador clássico:** tempo (clique para o que falta), título a correr, linha do formato,
  espectro ou osciloscópio (clique para trocar) e as luzinhas de estado: EQ, aleatório,
  repetir, ReplayGain, sem som, parar depois desta, limitador. A capa (a do ficheiro, ou um
  `cover.jpg` / `folder.jpg` ao lado) fica esbatida por trás do texto, para a janela não
  crescer, e a barra de posição desenha a forma de onda da faixa, na mesma altura de uma barra
  simples. Com o rato por cima, as barras sobem e ondulam, cada tema à sua maneira; e o clique
  conta mesmo que não acerte em cheio.
- **Tirar o som de uma vez:** `M`, ou clique no *VOL*. Mexer no volume devolve o som.
- **Visualização grande, à MilkDrop:** duplo clique no espectro (ou `Ctrl+Shift+K`, como no
  Winamp) abre uma janela à parte com uma visualização feita de raiz: cada imagem é a anterior
  com zoom, rodada, em remoinho e a desvanecer, com a onda da música por cima e um empurrão em
  cada batida. Oito presets que se fundem uns nos outros; `N` passa ao seguinte, duplo clique
  ou `F` põe em ecrã inteiro, `Esc` fecha. Sem bibliotecas extra nem placa gráfica.
- **Seis temas**, tirados dos seis temas do painel do Jazmin (o CMS do autor): Purple Haze,
  1995 Calling, Midnight OLED, Boutique Gold, Toxic Terminal e Oceanic Flow. Cada um muda mais
  do que as cores: fontes, forma dos botões, relevo ou brilho, o desenho dos controlos e do
  espectro, e até se há animação.
- **Modo compacto** (o antigo *windowshade*): o leitor inteiro na barra de título.
- **Janelas separadas, para quem gosta do clássico** (no menu, desligado por omissão): o
  equalizador e a lista ganham janelas pequenas suas, que se colam ao leitor, umas às outras e
  às bordas do ecrã, e arrastar o leitor leva as que estão coladas, como no Winamp 2.
- **ReplayGain** (por faixa ou por álbum). Os ficheiros sem etiquetas de ReplayGain são
  medidos em segundo plano (EBU R128, com o ffmpeg) para tocarem também ao mesmo volume: só a
  faixa atual e as próximas, com prioridade baixa, e cada ficheiro uma vez.
- **Extras pequenos, que não ocupam espaço:** velocidade de 0,5× a 2×, mantendo o tom ou como
  uma fita (mais depressa é mais agudo); ciclo A–B (carregue em `A` no A, outra vez no B e uma
  terceira para desligar; as marcas aparecem na barra de posição); temporizador para dormir,
  que baixa o som devagar antes de parar; letra sincronizada (um `.lrc` ao lado do ficheiro,
  ou a letra com tempos nas etiquetas) na linha do álbum. O que estiver ligado aparece no
  começo da linha do formato. Estão no menu *Reprodução*.
- Escolha da saída de som, sempre à frente, parar depois desta.
- **Parte do sistema:** a Fita aparece no painel multimédia do Windows e no ecrã de bloqueio
  (no Linux, por MPRIS: GNOME, KDE, `playerctl`) com título, artista, capa e posição, e recebe
  por aí as teclas multimédia e os botões dos auscultadores, mesmo com a janela em segundo
  plano. As teclas vão para a última app que tocou, como com qualquer outro leitor.
  No Windows, a miniatura na barra de tarefas tem anterior, tocar/pausa e seguinte, e o botão
  da barra enche à medida que a faixa toca (desliga-se no menu).
- **Teclado primeiro**, com as teclas do Winamp (abaixo).
- **Uma só janela:** abrir ficheiros com a Fita já aberta junta-os a ela. No Windows há
  *Abrir com* e, no botão direito das pastas e dos ficheiros de áudio, *Tocar na Fita* e
  *Juntar à lista da Fita*, com quantos se quiser escolhidos (entram de uma vez, pela ordem
  natural).
- **Inglês ou português.**

### Modo producer

Para conferir misturas e masters com referências, sem a janela crescer (`Ctrl+P`, ou
*Producer* no menu):

<p align="center">
  <img src="docs/producer.png" width="400" alt="A Fita em modo producer: volume e pico real no mostrador, saturações marcadas na barra de posição, e os números e as barrinhas do balanço tonal de cada faixa na lista">
</p>

- **Cada faixa analisada** em segundo plano (uma passagem do ffmpeg, guardada em cache):
  volume integrado, gama de volume e pico real pelo medidor EBU R128 do ffmpeg; pico de
  amostra, PLR, máximos de curta duração e momentâneo, desvio DC, saturações (com os
  tempos), silêncio no início e no fim, correlação estéreo (no total, por banda e ao longo do
  tempo), perda em mono e largura por banda, e o balanço tonal em seis bandas e em terços de
  oitava.
- **Na lista:** volume integrado e pico real, e seis barrinhas com cada banda em relação à
  comparação: as faixas de referência, ou a média da lista se não houver nenhuma. No
  mostrador: os números da faixa atual (com o rato por cima, os avisos) e um medidor ao vivo
  (curta duração, momentâneo, correlação).
- **A/B com referências:** botão direito numa faixa, *Usar como referência*, e o `Tab` salta
  entre o que está a ouvir e a referência, no mesmo ponto (`Shift+Tab`: a referência
  seguinte). Com *Tudo ao mesmo volume* (ligado por omissão) todas as faixas tocam a
  −18 LUFS, para a mais alta não ganhar só por ser mais alta.
- **Maneiras de ouvir:** mono, só o lado, só a esquerda ou a direita (o `O` vai de estéreo a
  mono e a lado), e uma banda sozinha (`1` a `6`, `0` para todas).
- **Notas num ponto da faixa** (`N`): "02:13 tarola alta demais". Aparecem na barra de
  posição, ao lado das marcas das saturações.
- **Relatório** (`Ctrl+R`): das faixas escolhidas, ou da lista toda. Um ficheiro HTML que abre
  em qualquer navegador e imprime em PDF, com as cores do tema: primeiro os avisos ("pico
  real +0,3 dBTP", "satura aos 02:13", "fase invertida entre 1:40 e 1:55", "+3 dB de
  subgraves em relação às referências"), depois uma tabela de resumo, o volume de cada faixa
  em relação às outras e, por faixa, os números, o volume e a correlação ao longo do tempo, a
  curva tonal por cima da de comparação, o estéreo por banda, quanto o Spotify, o YouTube, o
  Apple Music e o Tidal a sobem ou baixam, e as notas. Ao lado fica um CSV com os mesmos
  números. Os limiares são orientação e não regras: há géneros que os quebram de propósito.

### Teclas

| Teclas | |
|---|---|
| `Z` `X` `C` `V` `B` | Anterior · tocar · pausa · parar · seguinte |
| `Espaço` | Tocar / pausa |
| `←` `→` | Recuar / avançar 5 s (`Ctrl`: 30 s) |
| `↑` `↓` | Volume |
| `M` | Tirar / repor o som |
| `J` | Procurar na lista |
| `L` · `Shift+L` | Juntar ficheiros · juntar uma pasta |
| `S` · `R` | Aleatório · repetir (não, tudo, uma) |
| `Alt+G` · `Alt+E` | Mostrar o equalizador · a lista |
| `Ctrl+W` | Modo compacto |
| `Ctrl+T` | Tempo decorrido / que falta |
| `T` | Tema seguinte |
| `Ctrl+V` | Parar depois desta faixa |
| `Enter` · `Del` · `Alt+↑↓` | Tocar · tirar · mudar de lugar as faixas selecionadas |
| `A` | Ciclo A–B: marcar o A, o B, desligar |
| `Alt+L` | Biblioteca / lista |
| `Ctrl+Shift+K` | Visualização grande |
| `Ctrl+P` | Modo producer |
| `Tab` · `Shift+Tab` | Producer: trocar com a referência · referência seguinte |
| `O` · `1`–`6` · `0` | Producer: estéreo / mono / lado · uma banda sozinha · todas |
| `N` · `Ctrl+R` | Producer: nota neste ponto · relatório |

## O que é preciso

- **[ffmpeg](https://ffmpeg.org)**, gratuito: `winget install Gyan.FFmpeg` no Windows,
  `sudo apt install ffmpeg` em Debian/Ubuntu, `sudo dnf install ffmpeg-free` em Fedora (ou o
  pacote da sua distribuição; o `ffmpeg-free` do Fedora já lê AAC, WMA e ALAC). A Fita
  encontra-o sozinho (winget, Chocolatey, Scoop, `PATH`, ou uma cópia portátil numa pasta
  `ffmpeg/bin` ao lado da app); se não o encontrar, avisa e deixa indicar onde está. O ffmpeg
  não vem incluído.
- **Windows 10 ou 11 (x64)**, ou **Linux** (x64 ou arm64) com X11 ou XWayland (testada em
  Debian e Fedora 44; em GNOME/KDE com Wayland corre pelo XWayland, que já vem com eles).
- Para compilar: o [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
  (Fedora: `sudo dnf install dotnet-sdk-10.0`).

## Instalar

### Descarregar

Da [versão mais recente](https://github.com/zimutes/fita/releases/latest):

| Ficheiro | Para |
|---|---|
| `Fita-autonomo-win-x64.zip` | Windows 10/11 (x64). Autónomo: descomprimir onde quiser e abrir o `Fita.exe`. |
| `Fita.exe` | Windows 10/11 (x64), mais pequeno. Precisa do [runtime .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0). |
| `Fita-linux-x64.tar.gz` · `Fita-linux-arm64.tar.gz` | Linux. Autónomo: `tar -xzf Fita-linux-x64.tar.gz && ./fita/instalar.sh` |

A Fita precisa também do **ffmpeg** (ver acima). Os ficheiros não estão assinados: o SmartScreen
do Windows pode perguntar primeiro (*Mais informações → Executar mesmo assim*). As somas de
verificação estão em `SHA256SUMS`. Para ter a Fita no menu Iniciar, no *Abrir com* e no botão
direito do Explorador, use antes o instalador de baixo.

### Compilar a partir do código

**Windows** (PowerShell):

```powershell
git clone https://github.com/zimutes/fita
cd fita
.\tools\publicar.ps1              # compila e instala para o utilizador atual
.\tools\publicar.ps1 -Autonomo    # autónomo, sem precisar do .NET instalado
.\tools\publicar.ps1 -SemMenus    # sem as entradas no botão direito do Explorador
```

Fica em `%LOCALAPPDATA%\Programs\Fita`, com o **Fita** no menu Iniciar e no *Abrir com*
dos ficheiros de áudio (e nas *Aplicações predefinidas* do Windows, sem passar a ser a
predefinida) e (a não ser com `-SemMenus`) *Tocar na Fita* / *Juntar à lista da
Fita* no botão direito das pastas e dos ficheiros de áudio. Não precisa de permissões de
administrador. Para remover: `.\tools\desinstalar.ps1` (`-Tudo` apaga também as definições,
`-WhatIf` mostra o que faria).

**Linux**:

```sh
git clone https://github.com/zimutes/fita
cd fita
./tools/instalar.sh               # compila e instala para o utilizador atual
./tools/instalar.sh --remover     # remove
```

Fica em `~/.local/opt/fita`, com o comando `fita` em `~/.local/bin` e uma entrada no menu
de aplicações (com *Abrir com* para ficheiros de áudio e *Juntar à lista*).

## Como funciona

- **Leitura:** um processo do ffmpeg por faixa (e por salto), a escrever estéreo em float de
  32 bits, à frequência da saída, para um pipe.
- **Motor:** uma thread de leitura mantém cerca de dois segundos numa fila; a thread de saída
  tira blocos de 1024 amostras, passa-os pelo equalizador (filtros biquad, receitas RBJ, em
  precisão dupla), pelo volume e balanço (em rampa, sem estalidos) e por um limitador de picos,
  e entrega-os ao **OpenAL Soft** (WASAPI no Windows, PipeWire/PulseAudio/ALSA no Linux), com o
  HRTF desligado para os auscultadores ouvirem estéreo simples. As fronteiras entre faixas
  viajam dentro da fila: é isso que dá a passagem sem silêncio e mantém o mostrador a par do
  que se ouve.
- **Espectro:** uma FFT de 2048 pontos das amostras que se estão a ouvir, 60 vezes por segundo.
- **Etiquetas:** TagLib#, com o ffprobe de recurso para formatos que ele não conhece.
- **Interface:** Avalonia, com os controlos importantes desenhados à mão, para os temas lhes
  poderem mudar a forma e não só a cor.

## Segurança

A Fita abre ficheiros que não foi o utilizador a fazer, através do ffmpeg, por isso:

- **Sem shell.** O ffmpeg arranca diretamente, com os argumentos passados em lista: um nome de
  ficheiro nunca se transforma numa opção ou num comando.
- **Só ficheiros locais e formatos de áudio.** As entradas abrem com
  `-protocol_whitelist file` e um `-format_whitelist` de leitores de áudio. O ffmpeg deteta o
  formato pelo conteúdo e não pela extensão; sem isto, um ficheiro disfarçado de `.mp3` (uma
  lista HLS ou concat, por exemplo) podia pô-lo a ir buscar endereços ou a ler outros ficheiros.
  As listas M3U/PLS só juntam ficheiros locais; endereços ficam de fora.
- **Prefixo `file:`** em todos os caminhos, para um nome nunca ser lido como protocolo.
- **Nada de executáveis plantados.** O ffmpeg só é procurado onde os instaladores o põem,
  nunca na pasta atual nem solto ao lado da app; o Explorador é chamado pelo caminho completo.
- **Uma só instância, só do próprio utilizador:** o pipe que passa ficheiros à janela aberta
  só aceita ligações do mesmo utilizador.
- Sem acesso à rede, sem telemetria, sem permissões de administrador. As definições e a lista
  ficam em `%APPDATA%\Fita` (Windows) ou `~/.config/fita` (Linux), e os erros em `log.txt`.

## Compilar

```sh
dotnet build Fita.slnx -c Release
dotnet test tests/Fita.Tests
```

- `src/Fita/Audio/`: o motor (`AudioEngine`), o ffmpeg (`FfmpegDecoder`, `FfmpegLocator`),
  o OpenAL (`OpenAl`, `AlOutput`), o equalizador (`Equalizer`) e o espectro (`SpectrumAnalyzer`).
- `src/Fita/Library/`: faixas, ordem da lista (aleatório, repetir), M3U/PLS, folhas CUE,
  etiquetas, capas.
- `src/Fita/Themes/`: os seis temas (`Catalog`) e como se aplicam (`ThemeManager`).
- `src/Fita/Controls/`: controlos desenhados à mão (deslizadores, espectro, gráfico do EQ,
  título a correr).
- `src/Fita/Views/`: a janela, dividida por zonas (reprodução, lista, EQ, menus, moldura).
- `src/Fita/Loc.cs`: todos os textos da interface, em inglês e em português.
- `Fita --selftest ficheiro…` passa ficheiros pelo motor inteiro, em silêncio e sem janela.
- `tools/Capturas` desenha a janela em todos os temas sem ecrã (as imagens acima).
- `tools/gerar-icone.ps1` desenha o ícone.
- Uma etiqueta `v*` põe o GitHub Actions a publicar uma versão (Windows e Linux) com `SHA256SUMS`.

## Licença

[MIT](LICENSE) © zimutek. As partes de terceiros (OpenAL Soft, TagLib#, Avalonia, as fontes)
mantêm as suas licenças: ver [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). O ffmpeg é um
projeto à parte, com licença própria, e não vem incluído.
