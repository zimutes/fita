#!/bin/sh
#
#   Instala a Fita no Linux, só para o utilizador atual (sem root):
#     ~/.local/opt/fita/Fita          o executável (autónomo, não precisa do .NET)
#     ~/.local/bin/fita                 atalho para a linha de comandos
#     ~/.local/share/applications/…       entrada no menu de aplicações e "Abrir com"
#
#   Se houver uma Fita já compilada ao lado deste script (o pacote da release), instala essa.
#   Senão, compila a partir do código (precisa do .NET 10 SDK).
#
#   Uso:
#       ./instalar.sh                instala ou atualiza
#       ./instalar.sh --remover      remove (as definições em ~/.config/fita ficam)
#

set -eu

aqui=$(cd "$(dirname "$0")" && pwd)
destino="$HOME/.local/opt/fita"
atalho="$HOME/.local/bin/fita"
menu="$HOME/.local/share/applications/fita.desktop"

if [ "${1:-}" = "--remover" ] || [ "${1:-}" = "--uninstall" ]; then
    rm -f "$atalho" "$menu"
    rm -rf "$destino"
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$HOME/.local/share/applications" || true
    echo "Fita removida. As definições ficaram em ~/.config/fita."
    exit 0
fi

case "$(uname -m)" in
    x86_64|amd64) rid=linux-x64 ;;
    aarch64|arm64) rid=linux-arm64 ;;
    *) echo "Arquitetura não suportada: $(uname -m)" >&2; exit 1 ;;
esac

if [ -f "$aqui/Fita" ]; then
    origem="$aqui"
    icone="$aqui/fita.png"
else
    raiz=$(cd "$aqui/.." && pwd)
    command -v dotnet >/dev/null 2>&1 || { echo "Falta o .NET 10 SDK para compilar (ou use o pacote da release)." >&2; exit 1; }
    echo "A compilar ($rid)…"
    dotnet publish "$raiz/src/Fita" -c Release -r "$rid" --self-contained \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
        -o "$raiz/publicar/$rid"
    origem="$raiz/publicar/$rid"
    icone="$raiz/src/Fita/Assets/fita.png"
fi

mkdir -p "$destino" "$(dirname "$atalho")" "$(dirname "$menu")"
cp "$origem/Fita" "$destino/Fita"
chmod +x "$destino/Fita"
[ -f "$icone" ] && cp "$icone" "$destino/fita.png"
ln -sf "$destino/Fita" "$atalho"

cat > "$menu" <<EOF
[Desktop Entry]
Type=Application
Name=Fita
GenericName=Music Player
GenericName[pt]=Leitor de música
Comment=Music player in the spirit of the classic Winamp · by zimutek
Comment[pt]=Leitor de música no espírito do Winamp clássico · by zimutek
Exec="$destino/Fita" %F
Icon=$destino/fita.png
Terminal=false
Categories=AudioVideo;Audio;Player;
MimeType=audio/mpeg;audio/flac;audio/x-flac;audio/ogg;audio/x-vorbis+ogg;audio/opus;audio/x-opus+ogg;audio/wav;audio/x-wav;audio/aiff;audio/x-aiff;audio/mp4;audio/x-m4a;audio/aac;audio/x-ms-wma;audio/x-ape;audio/x-wavpack;audio/x-musepack;audio/x-matroska;audio/x-mpegurl;audio/x-scpls;audio/x-mod;audio/x-xm;audio/x-it;audio/x-s3m;
Actions=Enqueue;

[Desktop Action Enqueue]
Name=Add to playlist
Name[pt]=Juntar à lista
Exec="$destino/Fita" --enqueue %F
EOF
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$HOME/.local/share/applications" || true

echo "Fita instalada em $destino."
command -v ffmpeg >/dev/null 2>&1 || echo "Atenção: falta o ffmpeg, que a Fita usa para ler a música (Debian/Ubuntu: sudo apt install ffmpeg)."
case ":$PATH:" in
    *":$HOME/.local/bin:"*) ;;
    *) echo "Nota: ~/.local/bin não está no PATH; a Fita aparece no menu de aplicações na mesma." ;;
esac
