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
> **Escrito por IA.** Esta aplicação (código, ícone e este texto) foi escrita por IA
> (Claude, da Anthropic, através do Claude Code) a pedido do autor, e é partilhada tal como está.
> Foi testada no PC do autor, com Windows 11, e em Debian 13 (X11), com ficheiros MP3, FLAC,
> Ogg Vorbis, Opus, AAC, WAV, WMA e AIFF.
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
  de banda: sino, prateleira grave/aguda, corte de graves/agudos. Pré-amplificador, pré *Auto*
  para os reforços nunca saturarem, um limitador de segurança, 12 predefinições e as suas.
- **Cada nível tem uma caixa onde se escreve o valor:** volume, balanço (`E30`, `C`, `D30`),
  pré-amplificador e cada banda. Enter aplica; vazio + Enter ou duplo clique repõe (volume 80,
  balanço ao centro, 0 dB). Setas e roda do rato andam um passo.
- **Lista de reprodução:** arraste ficheiros, pastas ou listas M3U/PLS; mude a ordem a
  arrastar; procure enquanto escreve (`J`); ordene por título, artista, álbum, ficheiro, pasta
  ou duração; tire repetidas ou ficheiros que já não existem; guarde em M3U8 ou PLS. Lembra-se
  da lista, da faixa e de onde parou.
- **Mostrador clássico:** tempo (clique para o que falta), título a correr, linha do formato,
  espectro ou osciloscópio (clique para trocar) e as luzinhas de estado: EQ, aleatório,
  repetir, ReplayGain, parar depois desta, limitador.
- **Seis temas**, tirados dos seis temas do painel do Jazmin (o CMS do autor): Purple Haze,
  1995 Calling, Midnight OLED, Boutique Gold, Toxic Terminal e Oceanic Flow. Cada um muda mais
  do que as cores: fontes, forma dos botões, relevo ou brilho, o desenho dos controlos e do
  espectro, e até se há animação.
- **Modo compacto** (o antigo *windowshade*): o leitor inteiro na barra de título.
- **ReplayGain** (por faixa ou por álbum), escolha da saída de som, sempre à frente, parar
  depois desta.
- **Teclado primeiro**, com as teclas do Winamp (abaixo). As teclas multimédia funcionam com a
  janela em segundo plano, mas só enquanto a Fita está a tocar ou em pausa: parado, voltam
  para o navegador.
- **Uma só janela:** abrir ficheiros com a Fita já aberta junta-os a ela. No Windows há
  *Abrir com* e, no botão direito das pastas e dos ficheiros de áudio, *Tocar na Fita* e
  *Juntar à lista da Fita*.
- **Inglês ou português.**

### Teclas

| Teclas | |
|---|---|
| `Z` `X` `C` `V` `B` | Anterior · tocar · pausa · parar · seguinte |
| `Espaço` | Tocar / pausa |
| `←` `→` | Recuar / avançar 5 s (`Ctrl`: 30 s) |
| `↑` `↓` | Volume |
| `J` | Procurar na lista |
| `L` · `Shift+L` | Juntar ficheiros · juntar uma pasta |
| `S` · `R` | Aleatório · repetir (não, tudo, uma) |
| `Alt+G` · `Alt+E` | Mostrar o equalizador · a lista |
| `Ctrl+W` | Modo compacto |
| `Ctrl+T` | Tempo decorrido / que falta |
| `T` | Tema seguinte |
| `Ctrl+V` | Parar depois desta faixa |
| `Enter` · `Del` · `Alt+↑↓` | Tocar · tirar · mudar de lugar as faixas selecionadas |

## O que é preciso

- **[ffmpeg](https://ffmpeg.org)**, gratuito: `winget install Gyan.FFmpeg` no Windows,
  `sudo apt install ffmpeg` em Debian/Ubuntu (ou o pacote da sua distribuição). A Fita
  encontra-o sozinho (winget, Chocolatey, Scoop, `PATH`, ou uma cópia portátil numa pasta
  `ffmpeg/bin` ao lado da app); se não o encontrar, avisa e deixa indicar onde está. O ffmpeg
  não vem incluído.
- **Windows 10 ou 11 (x64)**, ou **Linux** (x64 ou arm64) com X11 ou XWayland.
- Para compilar: o [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## Instalar

Ainda não há transferências, por isso compila-se a partir do código:

**Windows** (PowerShell):

```powershell
git clone https://github.com/zimutes/fita
cd fita
.\tools\publicar.ps1              # compila e instala para o utilizador atual
.\tools\publicar.ps1 -Autonomo    # autónomo, sem precisar do .NET instalado
.\tools\publicar.ps1 -SemMenus    # sem as entradas no botão direito do Explorador
```

Fica em `%LOCALAPPDATA%\Programs\Fita`, com o **Fita** no menu Iniciar e no *Abrir com*
dos ficheiros de áudio e (a não ser com `-SemMenus`) *Tocar na Fita* / *Juntar à lista da
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

Quando houver versões publicadas, trazem um `Fita.exe` pequeno (precisa do runtime .NET 10),
um zip autónomo para Windows e pacotes autónomos para Linux (o `./fita/instalar.sh` instala
o que vem com ele).

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
- `src/Fita/Library/`: faixas, ordem da lista (aleatório, repetir), M3U/PLS, etiquetas.
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
