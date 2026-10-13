using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

using Avalonia;
using Avalonia.Android;
using Fita.Library;
using Fita.Views;

namespace Fita;

/// <summary>
/// A atividade. O Avalonia arranca uma vez por processo (na primeira atividade) e a vista e o
/// leitor ficam vivos com ele: se o Android fechar a atividade, a música continua no serviço, e
/// a atividade seguinte volta a mostrar a mesma vista.
/// </summary>
[Activity(Label = "Fita", Icon = "@mipmap/fita", Theme = "@style/Fita.Theme", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
    private const int PermissionRequest = 7;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (App.MainViewFactory is null)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Erro: " + e.ExceptionObject);
            var context = ApplicationContext ?? this;
            // O ffmpeg e o ffprobe vêm na APK como "bibliotecas" e o Android extrai-os para aqui.
            var native = ApplicationInfo?.NativeLibraryDir ?? "";
            var ffmpeg = System.IO.Path.Combine(native, "libffmpeg.so");
            var ffprobe = System.IO.Path.Combine(native, "libffprobe.so");
            App.MainViewFactory = () =>
            {
                Player.Start(File.Exists(ffmpeg) ? ffmpeg : null, File.Exists(ffprobe) ? ffprobe : null);
                PlaybackService.Watch(context);
                return new MobileView();
            };
            MobileView.ScanMusic = () => MusicStore.Scan(context);
        }
        base.OnCreate(savedInstanceState);
        MobileView.RequestMusicPermission = AskPermissions;
        MobileView.BackToSystem = () => MoveTaskToBack(true);
        AskPermissions();
    }

    private void AskPermissions()
    {
        var wanted = new List<string>();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            wanted.Add(Manifest.Permission.ReadMediaAudio);
            wanted.Add(Manifest.Permission.PostNotifications);
        }
        else
            wanted.Add(Manifest.Permission.ReadExternalStorage);
        var missing = wanted.Where(p => CheckSelfPermission(p) != Permission.Granted).ToArray();
        if (missing.Length > 0)
            RequestPermissions(missing, PermissionRequest);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == PermissionRequest)
            MobileView.RaisePermissionChanged();
    }

    protected override void OnPause()
    {
        base.OnPause();
        if (Player.Started)
            Player.Current.Save();
    }
}

/// <summary>A música do telemóvel, pelo MediaStore (o índice do sistema): caminhos e etiquetas.</summary>
internal static class MusicStore
{
    /// <summary>As faixas de música, ou null se ainda não há permissão para as ler.</summary>
    public static List<Track>? Scan(Context context)
    {
        var permission = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? Manifest.Permission.ReadMediaAudio
            : Manifest.Permission.ReadExternalStorage;
        if (context.CheckSelfPermission(permission) != Permission.Granted)
            return null;
        var resolver = context.ContentResolver;
        var uri = Android.Provider.MediaStore.Audio.Media.ExternalContentUri;
        if (resolver is null || uri is null)
            return [];
        string[] columns = ["_data", "title", "artist", "album", "duration", "track", "year"];
        var list = new List<Track>();
        using var cursor = resolver.Query(uri, columns, "is_music != 0", null, null);
        if (cursor is null)
            return list;
        while (cursor.MoveToNext())
        {
            var path = cursor.GetString(0);
            if (string.IsNullOrEmpty(path) || !PlaylistFiles.IsAudio(path))
                continue;
            static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) || s == "<unknown>" ? null : s;
            var track = cursor.GetInt(5);
            list.Add(new Track(path)
            {
                Title = Clean(cursor.GetString(1)),
                Artist = Clean(cursor.GetString(2)),
                Album = Clean(cursor.GetString(3)),
                Duration = cursor.GetLong(4) / 1000.0,
                // O MediaStore guarda o disco nos milhares (1003 = disco 1, faixa 3).
                TrackNumber = (uint)Math.Max(0, track % 1000),
                Year = (uint)Math.Max(0, cursor.GetInt(6)),
            });
        }
        return list;
    }
}
