using System.Diagnostics;
using Fita.Audio;
using Fita.Library;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Fita.Views;

public partial class MainWindow
{
    /// <summary>Formato interno: faixas a mudar de lugar dentro da própria lista.</summary>
    private static readonly DataFormat<string> InternalDragFormat = DataFormat.CreateStringApplicationFormat("fita-tracks");
    private Point? _dragFrom;
    private Track? _dropMarked;
    private bool _footerQueued;
    private bool _saveQueued;
    private int _adding;

    private void SetupPlaylist()
    {
        PlaylistBox.ItemsSource = _playlist.Items;
        _playlist.Changed += () =>
        {
            UpdateFooterSoon();
            QueueSave();
            EmptyHint.IsVisible = _playlist.Items.Count == 0 && string.IsNullOrEmpty(SearchBox.Text);
        };

        PlaylistBox.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is Track t)
                PlayTrack(t);
        };
        PlaylistBox.ContextRequested += (_, e) =>
        {
            if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is Track t)
            {
                if (!PlaylistBox.SelectedItems!.Contains(t))
                    PlaylistBox.SelectedItem = t;
                TrackMenu(t).ShowAt(PlaylistBox, showAtPointer: true);
                e.Handled = true;
            }
        };

        SearchBox.TextChanged += (_, _) => ApplyFilter();
        SearchBox.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (PlaylistBox.ItemCount > 0 && PlaylistBox.Items[0] is Track first)
                        PlayTrack(PlaylistBox.SelectedItem as Track ?? first);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    SearchBox.Text = "";
                    PlaylistBox.Focus();
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (PlaylistBox.ItemCount > 0)
                    {
                        PlaylistBox.SelectedIndex = Math.Max(0, PlaylistBox.SelectedIndex);
                        PlaylistBox.ContainerFromIndex(PlaylistBox.SelectedIndex)?.Focus();
                    }
                    e.Handled = true;
                    break;
            }
        };

        AddButton.Click += (_, _) => Menu(
            (Loc.T("AddFiles"), async () => await AddFilesAsync(play: false)),
            (Loc.T("AddFolder"), async () => await AddFolderAsync()),
            (Loc.T("OpenPlaylist"), async () => await AddPlaylistAsync())).ShowAt(AddButton);
        RemoveButton.Click += (_, _) => Menu(
            (Loc.T("RemoveSelected"), RemoveSelected),
            (Loc.T("RemoveMissing"), () => _playlist.RemoveMissing()),
            (Loc.T("RemoveDuplicates"), () => _playlist.RemoveDuplicates()),
            ("-", null),
            (Loc.T("ClearList"), ClearList)).ShowAt(RemoveButton);
        SortButton.Click += (_, _) => Menu(
            (Loc.T("SortTitle"), () => _playlist.Sort(SortKey.Title)),
            (Loc.T("SortArtist"), () => _playlist.Sort(SortKey.Artist)),
            (Loc.T("SortAlbum"), () => _playlist.Sort(SortKey.Album)),
            (Loc.T("SortFileName"), () => _playlist.Sort(SortKey.FileName)),
            (Loc.T("SortPath"), () => _playlist.Sort(SortKey.Path)),
            (Loc.T("SortDuration"), () => _playlist.Sort(SortKey.Duration)),
            ("-", null),
            (Loc.T("SortRandom"), () => _playlist.Sort(SortKey.Random)),
            (Loc.T("SortReverse"), () => _playlist.Sort(SortKey.Reverse))).ShowAt(SortButton);
        ListButton.Click += (_, _) => Menu(
            (Loc.T("SavePlaylist"), async () => await SavePlaylistAsync()),
            (Loc.T("NewPlaylist"), ClearList)).ShowAt(ListButton);

        // Arrastar ficheiros do Explorador / gestor de ficheiros, e reordenar dentro da lista.
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => MarkDrop(null));
        PlaylistBox.AddHandler(PointerPressedEvent, (_, e) =>
        {
            var point = e.GetCurrentPoint(PlaylistBox);
            _dragFrom = point.Properties.IsLeftButtonPressed && e.KeyModifiers == KeyModifiers.None ? point.Position : null;
        }, RoutingStrategies.Tunnel);
        PlaylistBox.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (_dragFrom is not { } from || !e.GetCurrentPoint(PlaylistBox).Properties.IsLeftButtonPressed)
                return;
            var p = e.GetPosition(PlaylistBox);
            if (Math.Abs(p.Y - from.Y) < 6)
                return;
            _dragFrom = null;
            if (PlaylistBox.SelectedItems is not { Count: > 0 } || !string.IsNullOrEmpty(SearchBox.Text))
                return;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(InternalDragFormat, "1"));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
            MarkDrop(null);
        }, RoutingStrategies.Tunnel);
        PlaylistBox.AddHandler(PointerReleasedEvent, (_, _) => _dragFrom = null, RoutingStrategies.Tunnel);

        EmptyHint.IsVisible = true;
    }

    private static MenuFlyout Menu(params (string Text, Action? Action)[] items)
    {
        var menu = new MenuFlyout();
        foreach (var (text, action) in items)
        {
            if (text == "-")
            {
                menu.Items.Add(new Separator());
                continue;
            }
            var item = new MenuItem { Header = text };
            if (action is not null)
                item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        return menu;
    }

    private MenuFlyout TrackMenu(Track track) => Menu(
        (Loc.T("Play"), () => PlayTrack(track)),
        (Loc.T("PlayNext"), () => PlayNext(SelectedTracks())),
        ("-", null),
        (Loc.T("FileInfo"), () => ShowFileInfo(track)),
        (Loc.T("ShowInFolder"), () => ShowInFolder(track.Path)),
        ("-", null),
        (Loc.T("Remove"), RemoveSelected));

    private List<Track> SelectedTracks() =>
        PlaylistBox.SelectedItems?.OfType<Track>().OrderBy(t => t.Number).ToList() ?? [];

    private void PlayNext(List<Track> tracks)
    {
        if (tracks.Count == 0)
            return;
        var current = PlayingTrack ?? _playlist.Current;
        var at = current is null ? 0 : _playlist.IndexOf(current) + 1;
        _playlist.Move(tracks, at);
    }

    private void RemoveSelected()
    {
        var selected = SelectedTracks();
        if (selected.Count == 0)
            return;
        var index = PlaylistBox.SelectedIndex;
        _playlist.Remove(selected);
        if (_playlist.Items.Count > 0 && string.IsNullOrEmpty(SearchBox.Text))
            PlaylistBox.SelectedIndex = Math.Clamp(index, 0, _playlist.Items.Count - 1);
    }

    private void ClearList()
    {
        _playlist.Clear();
        _resumeAt = 0;
        SearchBox.Text = "";
        Refresh(force: true);
    }

    private void MoveSelected(int delta)
    {
        var selected = SelectedTracks();
        if (selected.Count == 0 || !string.IsNullOrEmpty(SearchBox.Text))
            return;
        var first = _playlist.IndexOf(selected[0]);
        var target = delta < 0 ? first - 1 : first + selected.Count + 1;
        if (target < 0 || target > _playlist.Items.Count)
            return;
        _playlist.Move(selected, target);
        foreach (var t in selected)
            PlaylistBox.SelectedItems!.Add(t);
        ScrollTo(selected[0]);
    }

    private void ScrollTo(Track track)
    {
        if (PlaylistBox.IsVisible)
            PlaylistBox.ScrollIntoView(track);
    }

    private void ApplyFilter()
    {
        var text = SearchBox.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            PlaylistBox.ItemsSource = _playlist.Items;
            EmptyHint.IsVisible = _playlist.Items.Count == 0;
            EmptyTitle.Text = Loc.T("DropTitle");
            EmptyBody.IsVisible = true;
            if (_playlist.Current is { } current)
                ScrollTo(current);
            return;
        }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _playlist.Items.Where(t => words.All(w =>
            t.Display.Contains(w, StringComparison.CurrentCultureIgnoreCase)
            || (t.Album?.Contains(w, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || t.Path.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
        PlaylistBox.ItemsSource = matches;
        if (matches.Count > 0)
            PlaylistBox.SelectedIndex = 0;
        EmptyHint.IsVisible = matches.Count == 0;
        EmptyTitle.Text = Loc.F("NoMatches", text);
        EmptyBody.IsVisible = false;
    }

    private void UpdateFooterSoon()
    {
        if (_footerQueued)
            return;
        _footerQueued = true;
        DispatcherTimer.RunOnce(() =>
        {
            _footerQueued = false;
            UpdateFooter();
        }, TimeSpan.FromMilliseconds(300));
    }

    private void UpdateFooter()
    {
        var count = _playlist.Items.Count;
        var text = Loc.Count(count);
        var total = _playlist.TotalDuration;
        if (total > 0)
            text += " · " + Format.Time(total);
        if (_adding > 0)
            text = Loc.T("Adding") + "  " + text;
        else if (_tags.Pending > 0)
            text = Loc.F("Reading", _tags.Pending) + "  ·  " + text;
        FooterText.Text = text;
        if (_shownTrack is not null)
            ShowTrackText();
    }

    private void QueueSave()
    {
        if (_saveQueued || _demo)
            return;
        _saveQueued = true;
        DispatcherTimer.RunOnce(() =>
        {
            _saveQueued = false;
            var snap = _engine.Snapshot();
            PlaylistStore.Save(_playlist.Items, _playlist.Current, snap.State == PlayState.Stopped ? _resumeAt : snap.Position);
        }, TimeSpan.FromSeconds(2));
    }

    // ── Juntar ficheiros ──────────────────────────────────────────────────────

    /// <summary>Junta ficheiros, pastas e listas (expandidos fora da thread da interface).</summary>
    public async Task AddPathsAsync(IReadOnlyList<string> paths, bool play, int? insertAt = null)
    {
        if (paths.Count == 0)
            return;
        _adding++;
        UpdateFooter();
        List<Track> tracks;
        try
        {
            tracks = await Task.Run(() => PlaylistFiles.Expand(paths));
        }
        finally
        {
            _adding--;
        }
        if (tracks.Count > 0)
        {
            var folder = Path.GetDirectoryName(paths[0]);
            if (Directory.Exists(paths[0]))
                folder = paths[0];
            if (!string.IsNullOrEmpty(folder))
                _settings.LastFolder = folder;
            _playlist.Add(tracks, insertAt);
            _tags.Enqueue(tracks);
            if (play)
            {
                PlayTrack(tracks[0]);
                ScrollTo(tracks[0]);
            }
        }
        UpdateFooter();
    }

    private async Task<IStorageFolder?> StartFolder()
    {
        try
        {
            if (!string.IsNullOrEmpty(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
                return await StorageProvider.TryGetFolderFromPathAsync(_settings.LastFolder);
        }
        catch { }
        return null;
    }

    private async Task AddFilesAsync(bool play)
    {
        var audio = new FilePickerFileType("Audio")
        {
            Patterns = PlaylistFiles.AudioExtensions.Concat(PlaylistFiles.PlaylistExtensions).Select(e => "*" + e).ToList(),
        };
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = Loc.T("AddFiles").TrimEnd('…'),
            FileTypeFilter = [audio, FilePickerFileTypes.All],
            SuggestedStartLocation = await StartFolder(),
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        await AddPathsAsync(paths, play);
    }

    private async Task AddFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = true,
            Title = Loc.T("AddFolder").TrimEnd('…'),
            SuggestedStartLocation = await StartFolder(),
        });
        await AddPathsAsync(folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList(), play: false);
    }

    private async Task AddPlaylistAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = Loc.T("OpenPlaylist").TrimEnd('…'),
            FileTypeFilter = [new FilePickerFileType("M3U / PLS") { Patterns = ["*.m3u", "*.m3u8", "*.pls"] }],
            SuggestedStartLocation = await StartFolder(),
        });
        await AddPathsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList(), play: false);
    }

    private async Task SavePlaylistAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("SavePlaylist").TrimEnd('…'),
            SuggestedFileName = "playlist.m3u8",
            DefaultExtension = "m3u8",
            FileTypeChoices =
            [
                new FilePickerFileType("M3U8") { Patterns = ["*.m3u8"] },
                new FilePickerFileType("PLS") { Patterns = ["*.pls"] },
            ],
            SuggestedStartLocation = await StartFolder(),
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
            return;
        try
        {
            PlaylistFiles.Write(path, _playlist.Items);
        }
        catch (Exception ex)
        {
            Log.Write("Falha a gravar a lista: " + ex.Message);
            ShowStatus(ex.Message);
        }
    }

    private void ShowInFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Pelo caminho completo (nunca um explorer.exe da pasta atual). Os nomes de ficheiro
                // no Windows não têm aspas, por isso a linha de comandos é segura.
                var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(new ProcessStartInfo(explorer, $"/select,\"{path}\"") { UseShellExecute = false });
            }
            else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
                _ = Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(dir));
        }
        catch (Exception ex)
        {
            Log.Write("Falha a abrir a pasta: " + ex.Message);
        }
    }

    // ── Arrastar e largar ─────────────────────────────────────────────────────

    private (Track? Before, int Index) DropTarget(DragEventArgs e)
    {
        if (!PlaylistBox.IsVisible || !string.IsNullOrEmpty(SearchBox.Text))
            return (null, -1);
        var pos = e.GetPosition(PlaylistBox);
        if (pos.X < 0 || pos.Y < 0 || pos.X > PlaylistBox.Bounds.Width || pos.Y > PlaylistBox.Bounds.Height)
            return (null, -1);
        var item = (PlaylistBox.InputHitTest(pos) as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item?.DataContext is not Track over)
            return (null, _playlist.Items.Count);
        var index = _playlist.IndexOf(over);
        if (e.GetPosition(item).Y > item.Bounds.Height / 2)
            index++;
        return (index < _playlist.Items.Count ? _playlist.Items[index] : null, index);
    }

    private void MarkDrop(Track? track)
    {
        if (_dropMarked == track)
            return;
        if (_dropMarked is not null)
            _dropMarked.DropTarget = false;
        _dropMarked = track;
        if (track is not null)
            track.DropTarget = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(InternalDragFormat))
        {
            var (before, index) = DropTarget(e);
            e.DragEffects = index >= 0 ? DragDropEffects.Move : DragDropEffects.None;
            MarkDrop(before);
        }
        else if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            MarkDrop(DropTarget(e).Before);
        }
        else
            e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var (_, index) = DropTarget(e);
        MarkDrop(null);
        e.Handled = true;
        if (e.DataTransfer.Contains(InternalDragFormat))
        {
            var selected = SelectedTracks();
            if (index >= 0 && selected.Count > 0)
            {
                _playlist.Move(selected, index);
                foreach (var t in selected)
                    PlaylistBox.SelectedItems!.Add(t);
            }
            return;
        }
        var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        // Largar em cima do leitor (fora da lista) toca logo, como no Winamp.
        var overList = index >= 0;
        await AddPathsAsync(files, play: !overList, insertAt: overList ? index : null);
    }
}
