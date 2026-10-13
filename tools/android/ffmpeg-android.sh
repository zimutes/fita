#!/bin/sh
# Compila o ffmpeg e o ffprobe para Android (arm64-v8a para os telemóveis, x86_64 para o
# emulador), estáticos e sem bibliotecas de fora, só com o que a Fita usa: ler áudio, os
# filtros do motor e da análise, e escrever PCM. Corre num contentor Debian descartável:
#
#   docker run --rm -v "$PWD/saida:/saida" -v "$PWD/ffmpeg-android.sh:/x.sh:ro" debian:trixie sh /x.sh
#
# O resultado fica em saida/<abi>/libffmpeg.so e libffprobe.so: o nome "lib*.so" é o que deixa
# o Android extrair os executáveis com a app e corrê-los da pasta das bibliotecas nativas.
set -eu
NDK_VERSION=r27c
FFMPEG_VERSION=${FFMPEG_VERSION:-8.0}
API=24
OUT=/saida
JOBS=$(nproc)

apt-get update -qq
apt-get install -y -qq --no-install-recommends ca-certificates curl unzip make pkg-config xz-utils perl gcc libc6-dev >/dev/null

# As descargas ficam na pasta de saída, para não se repetirem se for preciso voltar a correr.
CACHE=$OUT/.cache
mkdir -p "$CACHE"
cd /tmp
[ -f "$CACHE/ndk.zip" ] || curl -fsSL -o "$CACHE/ndk.zip" "https://dl.google.com/android/repository/android-ndk-${NDK_VERSION}-linux.zip"
unzip -q "$CACHE/ndk.zip"
TC=/tmp/android-ndk-${NDK_VERSION}/toolchains/llvm/prebuilt/linux-x86_64
[ -f "$CACHE/ffmpeg-$FFMPEG_VERSION.tar.xz" ] || curl -fsSL -o "$CACHE/ffmpeg-$FFMPEG_VERSION.tar.xz" "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz"
tar xf "$CACHE/ffmpeg-$FFMPEG_VERSION.tar.xz"
SRC=/tmp/ffmpeg-${FFMPEG_VERSION}

build() {
    abi=$1 arch=$2 cpu=$3 triple=$4 extra=$5
    rm -rf "/tmp/build-$abi" && mkdir -p "/tmp/build-$abi" && cd "/tmp/build-$abi"
    # shellcheck disable=SC2086
    "$SRC/configure" \
        --target-os=android --arch="$arch" --cpu="$cpu" --enable-cross-compile \
        --cc="$TC/bin/${triple}${API}-clang" --cxx="$TC/bin/${triple}${API}-clang++" \
        --nm="$TC/bin/llvm-nm" --ar="$TC/bin/llvm-ar" --ranlib="$TC/bin/llvm-ranlib" --strip="$TC/bin/llvm-strip" \
        --sysroot="$TC/sysroot" \
        --enable-static --disable-shared --enable-pic \
        --extra-ldflags="-Wl,-z,max-page-size=16384" \
        --disable-autodetect --disable-doc --disable-debug --disable-network --disable-avdevice \
        --disable-ffplay --enable-ffmpeg --enable-ffprobe \
        --disable-encoders --enable-encoder=pcm_f32le,pcm_s16le,pcm_s24le \
        --disable-muxers --enable-muxer=pcm_f32le,null,wav \
        --disable-hwaccels --disable-indevs --disable-outdevs \
        --disable-filters \
        --enable-filter=aresample,aformat,anull,anullsink,asplit,ebur128,atempo,asetrate,volume,pan,amerge,amix,aeval,aevalsrc,anoisesrc,sine,lowpass,highpass,null,nullsink \
        $extra > configure.log 2>&1 || { tail -40 configure.log; tail -40 ffbuild/config.log; exit 1; }
    make -j"$JOBS" > make.log 2>&1 || { tail -40 make.log; exit 1; }
    mkdir -p "$OUT/$abi"
    "$TC/bin/llvm-strip" -o "$OUT/$abi/libffmpeg.so" ffmpeg
    "$TC/bin/llvm-strip" -o "$OUT/$abi/libffprobe.so" ffprobe
    echo "$abi: $(du -h "$OUT/$abi/libffmpeg.so" | cut -f1) ffmpeg, $(du -h "$OUT/$abi/libffprobe.so" | cut -f1) ffprobe"
}

build arm64-v8a aarch64 armv8-a aarch64-linux-android ""
build x86_64 x86_64 x86-64 x86_64-linux-android "--disable-x86asm"
cp "$SRC/COPYING.LGPLv2.1" "$OUT/"
echo "ffmpeg $FFMPEG_VERSION, NDK $NDK_VERSION, API $API" > "$OUT/VERSION"
echo feito
