# Third-party notices

Fita itself is MIT-licensed (see [LICENSE](LICENSE)). It uses these projects, each under its
own license:

| Component | Use | License |
|---|---|---|
| [Avalonia](https://avaloniaui.net) | User interface | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) / [HarfBuzzSharp](https://github.com/mono/SkiaSharp) | Drawing and text (through Avalonia) | MIT |
| [OpenAL Soft](https://github.com/kcat/openal-soft) (`soft_oal.dll`, `libopenal.so`, from [Silk.NET.OpenAL.Soft.Native](https://www.nuget.org/packages/Silk.NET.OpenAL.Soft.Native)) | Sound output | LGPL-2.0-or-later |
| [TagLib#](https://github.com/mono/taglib-sharp) | Reading tags | LGPL-2.1 |
| Barlow, Barlow Condensed, JetBrains Mono, IBM Plex Sans, IBM Plex Mono, Outfit, DM Sans, Playfair Display, Share Tech Mono (from [Google Fonts](https://github.com/google/fonts)) | Theme fonts | SIL Open Font License 1.1 |

- **OpenAL Soft** is used as a separate shared library, loaded at run time. In the single-file
  builds it travels inside the executable and is written out to a temporary folder before it
  is loaded; the source is at the link above. A build without `PublishSingleFile` keeps it as
  a plain file next to the app, which you can replace with your own build.
- **TagLib#** is used as an unmodified library (`TagLibSharp.dll`).
- **Fonts:** the static weights were cut from the variable fonts with fontTools and their
  names normalised; the license of each family is next to it in `src/Fita/Assets/Fonts/*/OFL.txt`.
- **FFmpeg** is not included in the desktop versions: Fita runs the `ffmpeg` you install, as a
  separate program. The **Android** app carries `ffmpeg` and `ffprobe` 8.0 (LGPL-2.1-or-later,
  no GPL or non-free parts), unmodified, as separate programs (packed as `libffmpeg.so` and
  `libffprobe.so` so Android extracts them). They are built from the official source
  (<https://ffmpeg.org/releases/ffmpeg-8.0.tar.xz>) by `tools/android/ffmpeg-android.sh`,
  which has the exact configuration; the license text is
  [COPYING.LGPLv2.1](https://github.com/FFmpeg/FFmpeg/blob/master/COPYING.LGPLv2.1).
