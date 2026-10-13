using System.Globalization;
using Fita.Audio;
using Fita.Library;
using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Fita.Views;

public partial class MainWindow
{
    private MenuFlyout MainMenu()
    {
        var menu = new MenuFlyout();

        MenuItem Radio(string header, bool on, Action click)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = on };
            item.Click += (_, _) => click();
            return item;
        }
        MenuItem Check(string header, bool on, Action click)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
            item.Click += (_, _) => click();
            return item;
        }
        MenuItem Plain(string header, Action click, string? gesture = null)
        {
            var item = new MenuItem { Header = header, InputGesture = gesture is null ? null : KeyGesture.Parse(gesture) };
            item.Click += (_, _) => click();
            return item;
        }

        var theme = new MenuItem { Header = Loc.T("Theme") };
        foreach (var t in Catalog.All)
            theme.Items.Add(Radio(t.Name, ThemeManager.Current == t, () => SetTheme(t)));
        menu.Items.Add(theme);

        var language = new MenuItem { Header = Loc.T("Language") };
        language.Items.Add(Radio("English", Loc.Language == UiLanguage.En, () => SetLanguage(UiLanguage.En)));
        language.Items.Add(Radio("Português", Loc.Language == UiLanguage.Pt, () => SetLanguage(UiLanguage.Pt)));
        menu.Items.Add(language);

        var vis = new MenuItem { Header = Loc.T("Visualizer") };
        vis.Items.Add(Radio(Loc.T("VisSpectrum"), _settings.Vis == VisMode.Spectrum, () => SetVis(VisMode.Spectrum)));
        vis.Items.Add(Radio(Loc.T("VisScope"), _settings.Vis == VisMode.Scope, () => SetVis(VisMode.Scope)));
        vis.Items.Add(Radio(Loc.T("VisOff"), _settings.Vis == VisMode.Off, () => SetVis(VisMode.Off)));
        vis.Items.Add(new Separator());
        vis.Items.Add(Check(Loc.T("ShowCover"), _settings.ShowCover, () => SetShowCover(!_settings.ShowCover)));
        vis.Items.Add(Check(Loc.T("ShowWaveform"), _settings.ShowWaveform, () => SetShowWaveform(!_settings.ShowWaveform)));
        menu.Items.Add(vis);

        var rg = new MenuItem { Header = Loc.T("ReplayGain") };
        rg.Items.Add(Radio(Loc.T("RgOff"), _settings.ReplayGain == ReplayGainMode.Off, () => SetReplayGain(ReplayGainMode.Off)));
        rg.Items.Add(Radio(Loc.T("RgTrack"), _settings.ReplayGain == ReplayGainMode.Track, () => SetReplayGain(ReplayGainMode.Track)));
        rg.Items.Add(Radio(Loc.T("RgAlbum"), _settings.ReplayGain == ReplayGainMode.Album, () => SetReplayGain(ReplayGainMode.Album)));
        rg.Items.Add(new Separator());
        rg.Items.Add(Check(Loc.T("LoudnessScan"), _settings.LoudnessScan, () =>
        {
            _settings.LoudnessScan = !_settings.LoudnessScan;
            ScanUpcoming();
        }));
        menu.Items.Add(rg);

        var output = new MenuItem { Header = Loc.T("Output") };
        output.Items.Add(Radio(Loc.T("DefaultDevice"), string.IsNullOrEmpty(_settings.OutputDevice), () => SetOutput("")));
        foreach (var device in AudioEngine.OutputDevices())
            output.Items.Add(Radio(CleanDeviceName(device), _settings.OutputDevice == device, () => SetOutput(device)));
        menu.Items.Add(output);

        menu.Items.Add(new Separator());
        menu.Items.Add(Check(Loc.T("StopAfter"), _engine.StopAfterCurrent, ToggleStopAfter));
        menu.Items.Add(Check(Loc.T("ResumeOption"), _settings.Resume, () => _settings.Resume = !_settings.Resume));
        menu.Items.Add(Check(Loc.T("AlwaysOnTop"), _settings.AlwaysOnTop, () =>
        {
            _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
            Topmost = _settings.AlwaysOnTop;
        }));
        if (OperatingSystem.IsWindows())
            menu.Items.Add(Check(Loc.T("TaskbarProgress"), _settings.TaskbarProgress, () => SetTaskbarProgress(!_settings.TaskbarProgress)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Plain(Loc.T("LocateFfmpeg"), async () => await PickFfmpegAsync()));
        menu.Items.Add(Plain(Loc.T("Shortcuts"), ShowShortcuts));
        menu.Items.Add(Plain(Loc.T("AboutMenu"), ShowAbout));
        menu.Items.Add(new Separator());
        menu.Items.Add(Plain(Loc.T("Quit"), Close));
        return menu;
    }

    /// <summary>O OpenAL Soft põe "OpenAL Soft on " antes do nome do dispositivo.</summary>
    private static string CleanDeviceName(string name) =>
        name.StartsWith("OpenAL Soft on ", StringComparison.Ordinal) ? name[15..] : name;

    private void SetTheme(ThemeDef theme)
    {
        _settings.Theme = theme.Key;
        ThemeManager.Apply(theme);
    }

    private void NextTheme()
    {
        var all = Catalog.All;
        var i = all.ToList().IndexOf(ThemeManager.Current);
        var next = all[(i + 1) % all.Count];
        SetTheme(next);
        ShowStatus($"{Loc.T("Theme")}: {next.Name}");
    }

    private void SetLanguage(UiLanguage language)
    {
        _settings.Language = Loc.Code(language);
        Loc.Apply(language, ThemeManager.Current.Uppercase);
    }

    private void SetReplayGain(ReplayGainMode mode)
    {
        _settings.ReplayGain = mode;
        ScanUpcoming();
        Refresh(force: true);
    }

    private void SetOutput(string device)
    {
        _settings.OutputDevice = device;
        _engine.SetOutputDevice(string.IsNullOrEmpty(device) ? null : device);
    }

    private async Task PickFfmpegAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = Loc.T("FindFfmpeg"),
            FileTypeFilter = OperatingSystem.IsWindows()
                ? [new FilePickerFileType("ffmpeg.exe") { Patterns = ["ffmpeg.exe"] }]
                : [FilePickerFileTypes.All],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null)
            return;
        _settings.FfmpegPath = path;
        LocateFfmpeg();
        _engine.FfmpegPath = _ffmpeg;
        HideOverlay();
        Refresh(force: true);
    }

    // ── Camada por cima ───────────────────────────────────────────────────────

    private void ShowOverlay(Control content)
    {
        OverlayContent.Content = content;
        Overlay.IsVisible = true;
    }

    private void HideOverlay()
    {
        Overlay.IsVisible = false;
        OverlayContent.Content = null;
    }

    private Button OverlayButton(string key, Action click)
    {
        var button = new Button { Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Right };
        button.Bind(ContentControl.ContentProperty, this.GetResourceObservable("LU." + key));
        button.Click += (_, _) => click();
        return button;
    }

    private void ShowFfmpegMissing()
    {
        var command = OperatingSystem.IsWindows() ? "winget install Gyan.FFmpeg" : "sudo apt install ffmpeg";
        var code = new SelectableTextBlock
        {
            Text = command,
            FontSize = 13,
            Margin = new Thickness(0, 2),
        };
        code.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("FontMono"));
        code.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Accent"));
        ShowOverlay(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading(Loc.T("FfmpegMissingTitle")),
                new TextBlock { Text = Loc.T("FfmpegMissingBefore"), TextWrapping = TextWrapping.Wrap },
                code,
                new TextBlock { Text = Loc.T("FfmpegMissingAfter"), TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { OverlayButton("Close", HideOverlay), OverlayButton("FindFfmpeg", async () => await PickFfmpegAsync()) },
                },
            },
        });
    }

    private void ShowAbout()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        var name = new TextBlock { Text = "FITA", FontSize = 26 };
        name.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("FontDisplay"));
        name.Bind(TextBlock.FontWeightProperty, this.GetResourceObservable("TitleWeight"));
        name.Bind(TextBlock.LetterSpacingProperty, this.GetResourceObservable("TitleSpacing"));
        name.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Accent"));
        var tagline = new TextBlock { Text = Loc.T("Tagline"), FontSize = 15 };
        tagline.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("FontDisplay"));
        var logo = new Controls.Glyph { Kind = Controls.GlyphKind.Logo, Width = 30, Height = 30 };
        logo.Bind(Controls.Glyph.ForegroundProperty, this.GetResourceObservable("Accent"));
        ShowOverlay(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 12,
                    Children = { logo, name },
                },
                tagline,
                new TextBlock { Text = $"{version?.ToString(3)} · {Loc.T("By")}", Classes = { "muted" } },
                new TextBlock { Text = Loc.T("About"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) },
                new TextBlock { Text = Loc.T("Credits"), TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Classes = { "muted" } },
                new TextBlock { Text = Loc.T("AiNote"), TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Classes = { "muted" } },
                OverlayButton("Close", HideOverlay),
            },
        });
    }

    private void ShowShortcuts()
    {
        (string Keys, string Key)[] rows =
        [
            ("Z  X  C  V  B", "K.Transport"),
            ("Space", "PlayTip"),
            ("←  →", "K.Seek"),
            ("↑  ↓", "K.Volume"),
            ("J", "K.Jump"),
            ("L  ·  Shift+L", "K.Open"),
            ("S  ·  R", "K.Modes"),
            ("Alt+G  ·  Alt+E", "K.Panels"),
            ("Ctrl+W", "K.Compact"),
            ("Ctrl+T", "K.Time"),
            ("T", "K.Theme"),
            ("Ctrl+V", "K.StopAfter"),
            ("Enter", "K.Enter"),
            ("Del", "K.Delete"),
            ("Alt+↑  Alt+↓", "K.Move"),
        ];
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var keys = new TextBlock { Text = rows[i].Keys, Margin = new Thickness(0, 3, 18, 3), FontSize = 12 };
            keys.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("FontMono"));
            keys.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Accent"));
            var text = new TextBlock { Text = Loc.T(rows[i].Key).Replace(" (Space)", ""), Margin = new Thickness(0, 3), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(keys, i);
            Grid.SetRow(text, i);
            Grid.SetColumn(text, 1);
            grid.Children.Add(keys);
            grid.Children.Add(text);
        }
        ShowOverlay(new StackPanel { Spacing = 10, Children = { Heading(Loc.T("Shortcuts")), grid, OverlayButton("Close", HideOverlay) } });
    }

    private void ShowFileInfo(Track track)
    {
        var size = track.FileSize > 0 ? $"{track.FileSize / 1048576.0:0.0} MB" : "";
        var rg = new List<string>();
        if (!double.IsNaN(track.TrackGain))
            rg.Add($"{Loc.T("RgTrack")} {track.TrackGain.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} dB");
        if (!double.IsNaN(track.AlbumGain))
            rg.Add($"{Loc.T("RgAlbum")} {track.AlbumGain.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} dB");
        (string Label, string? Value)[] rows =
        [
            (Loc.T("Title"), track.Title),
            (Loc.T("Artist"), track.Artist),
            (Loc.T("Album"), track.Album),
            (Loc.T("TrackNo"), track.TrackNumber > 0 ? track.TrackNumber.ToString(CultureInfo.InvariantCulture) : null),
            (Loc.T("Year"), track.Year > 0 ? track.Year.ToString(CultureInfo.InvariantCulture) : null),
            (Loc.T("Genre"), track.Genre),
            (Loc.T("Length"), track.Duration > 0 ? Format.Time(track.Duration) : null),
            (Loc.T("FormatLabel"), track.Info),
            ("ReplayGain", rg.Count > 0 ? string.Join(" · ", rg) : null),
            (Loc.T("Size"), size),
            (Loc.T("File"), track.Path),
        ];
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var r = 0;
        foreach (var (label, value) in rows)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = new TextBlock { Text = label, Classes = { "muted" }, Margin = new Thickness(0, 3, 16, 3), FontSize = 12 };
            var v = new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3), FontSize = 12.5 };
            Grid.SetRow(l, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
            r++;
        }
        ShowOverlay(new StackPanel { Spacing = 10, Children = { Heading(track.Display), grid, OverlayButton("Close", HideOverlay) } });
    }
}
