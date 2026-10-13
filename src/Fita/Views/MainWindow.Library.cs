using Fita.Library;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace Fita.Views;

/// <summary>Uma linha da biblioteca: um disco (que se abre com um clique) ou uma faixa dele.</summary>
public sealed class LibraryRow
{
    public required LibraryAlbum Album { get; init; }
    /// <summary>null numa linha de disco.</summary>
    public PlaylistStore.Entry? Entry { get; init; }
    public bool IsAlbum => Entry is null;
    public bool IsTrack => Entry is not null;
    public required string Text { get; init; }
    public string Detail { get; init; } = "";
}

/// <summary>
/// A biblioteca vive no lugar da lista (o mesmo painel, o mesmo tamanho): o botão BIB (ou
/// Alt+L) troca uma pela outra. Discos agrupados por pasta e álbum; um clique abre o disco,
/// duplo clique toca-o (junta-o ao fim da lista). A procura procura na biblioteca.
/// </summary>
public partial class MainWindow
{
    private MusicLibrary? _library;
    private readonly HashSet<string> _expandedAlbums = [];
    private bool _libraryMode;

    private static string AlbumKey(LibraryAlbum a) => a.Folder + "\n" + a.Title;

    private void SetupLibrary()
    {
        LibToggle.IsCheckedChanged += (_, _) => SetLibraryMode(LibToggle.IsChecked == true);
        FoldersButton.Click += (_, _) => FoldersMenu().ShowAt(FoldersButton);

        LibraryBox.Tapped += (_, e) =>
        {
            if (RowOf(e.Source) is { IsAlbum: true } row)
                ToggleAlbum(row.Album);
        };
        LibraryBox.DoubleTapped += (_, e) =>
        {
            if (RowOf(e.Source) is { } row)
                PlayFromLibrary(row, replace: false);
        };
        LibraryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && LibraryBox.SelectedItem is LibraryRow row)
            {
                PlayFromLibrary(row, replace: false);
                e.Handled = true;
            }
            else if (e.Key is Key.Right or Key.Left && LibraryBox.SelectedItem is LibraryRow { IsAlbum: true } album
                     && _expandedAlbums.Contains(AlbumKey(album.Album)) == (e.Key == Key.Left))
            {
                ToggleAlbum(album.Album);
                e.Handled = true;
            }
        };
        LibraryBox.ContextRequested += (_, e) =>
        {
            if (RowOf(e.Source) is not { } row)
                return;
            LibraryBox.SelectedItem = row;
            Menu(
                (Loc.T("Play"), () => PlayFromLibrary(row, replace: false)),
                (Loc.T("LibEnqueue"), () => AddFromLibrary(row, play: false, replace: false)),
                (Loc.T("LibReplace"), () => PlayFromLibrary(row, replace: true)),
                ("-", null),
                (Loc.T("ShowInFolder"), () => ShowInFolder(row.Entry?.Path ?? row.Album.Tracks[0].Path))).ShowAt(LibraryBox, showAtPointer: true);
            e.Handled = true;
        };

        if (_demo)
            return;
        _library = new MusicLibrary(() => _ffprobe);
        _library.Changed += () =>
        {
            if (_libraryMode)
            {
                RefreshLibraryRows();
                UpdateFooterSoon();
            }
        };
        _library.Start(_settings.LibraryFolders);
        if (_settings.LibraryMode)
            SetLibraryMode(true);
    }

    private static LibraryRow? RowOf(object? source) =>
        (source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as LibraryRow;

    private void SetLibraryMode(bool on)
    {
        _libraryMode = on;
        _settings.LibraryMode = on;
        if (LibToggle.IsChecked != on)
            LibToggle.IsChecked = on;
        if (on && !PlPanel.IsVisible)
            PlToggle.IsChecked = true;
        PlaylistBox.IsVisible = !on;
        LibraryBox.IsVisible = on;
        foreach (var b in new Control[] { AddButton, RemoveButton, SortButton, ListButton })
            b.IsVisible = !on;
        FoldersButton.IsVisible = on;
        SearchBox.Watermark = Loc.T(on ? "SearchLibrary" : "Search");
        SearchBox.Text = "";
        if (on)
            RefreshLibraryRows();
        else
            ApplyFilter();
        UpdateFooter();
    }

    private void ToggleAlbum(LibraryAlbum album)
    {
        var key = AlbumKey(album);
        if (!_expandedAlbums.Remove(key))
            _expandedAlbums.Add(key);
        var selected = LibraryBox.SelectedItem as LibraryRow;
        RefreshLibraryRows();
        // A seleção fica no disco que se abriu ou fechou.
        if (selected is not null && LibraryBox.ItemsSource is List<LibraryRow> rows
            && rows.FirstOrDefault(r => r.IsAlbum && AlbumKey(r.Album) == key) is { } same)
            LibraryBox.SelectedItem = same;
    }

    /// <summary>
    /// Monta as linhas: os discos que batem com a procura e, por baixo dos abertos, as faixas.
    /// Um disco que só bate por causa de uma faixa abre-se sozinho, só com essas faixas.
    /// </summary>
    private void RefreshLibraryRows()
    {
        if (_library is null)
        {
            LibraryBox.ItemsSource = null;
            return;
        }
        var words = MusicLibrary.Fold(SearchBox.Text?.Trim() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rows = new List<LibraryRow>();
        foreach (var album in _library.Albums)
        {
            if (words.Length > 0 && !words.All(album.SearchText.Contains))
                continue;
            var header = MusicLibrary.Fold(album.Artist + " " + album.Title);
            var byTracks = words.Length > 0 && !words.All(header.Contains);
            rows.Add(new LibraryRow
            {
                Album = album,
                Text = string.IsNullOrEmpty(album.Artist) ? album.Title : $"{album.Artist} – {album.Title}",
                Detail = (album.Year > 0 ? album.Year + " · " : "") + album.Tracks.Count,
            });
            if (!byTracks && !_expandedAlbums.Contains(AlbumKey(album)))
                continue;
            foreach (var t in album.Tracks)
            {
                var name = t.Title ?? Path.GetFileNameWithoutExtension(t.Path);
                if (byTracks && !words.All(MusicLibrary.Fold(name + " " + t.Artist).Contains))
                    continue;
                var showArtist = !string.IsNullOrWhiteSpace(t.Artist) && !string.Equals(t.Artist, album.Artist, StringComparison.CurrentCultureIgnoreCase);
                rows.Add(new LibraryRow
                {
                    Album = album,
                    Entry = t,
                    Text = (t.TrackNumber > 0 ? t.TrackNumber + ". " : "") + (showArtist ? $"{t.Artist} – {name}" : name),
                    Detail = t.Duration > 0 ? Format.Time(t.Duration) : "",
                });
            }
        }
        LibraryBox.ItemsSource = rows;
        var empty = rows.Count == 0;
        EmptyHint.IsVisible = empty;
        if (empty)
        {
            var nothing = _library.Folders.Count == 0;
            EmptyTitle.Text = nothing ? Loc.T("LibraryEmpty") : words.Length > 0 ? Loc.F("NoMatches", SearchBox.Text!.Trim()) : Loc.T("LibraryScanning");
            EmptyBody.Text = nothing ? Loc.T("LibraryEmptyBody") : "";
            EmptyBody.IsVisible = nothing;
        }
    }

    private void PlayFromLibrary(LibraryRow row, bool replace) => AddFromLibrary(row, play: true, replace: replace);

    /// <summary>
    /// Junta à lista o disco (ou a faixa) e, com <paramref name="play"/>, toca-o. Com
    /// <paramref name="replace"/>, a lista passa a ser só isso.
    /// </summary>
    private void AddFromLibrary(LibraryRow row, bool play, bool replace)
    {
        var entries = row.Entry is { } one ? [one] : row.Album.Tracks;
        var tracks = entries.Select(PlaylistStore.FromEntry).ToList();
        if (tracks.Count == 0)
            return;
        if (replace)
            ClearList();
        _playlist.Add(tracks, null);
        ShowStatus(Loc.F("AddedN", tracks.Count));
        if (play)
            PlayTrack(tracks[0]);
        UpdateFooter();
    }

    private MenuFlyout FoldersMenu()
    {
        var menu = new MenuFlyout();
        var add = new MenuItem { Header = Loc.T("AddMusicFolder") };
        add.Click += async (_, _) => await AddLibraryFolderAsync();
        menu.Items.Add(add);
        if (_library is { Folders.Count: > 0 } library)
        {
            var remove = new MenuItem { Header = Loc.T("RemoveMusicFolder") };
            foreach (var folder in library.Folders)
            {
                var item = new MenuItem { Header = folder };
                item.Click += (_, _) => SetLibraryFolders(library.Folders.Where(f => f != folder));
                remove.Items.Add(item);
            }
            menu.Items.Add(remove);
            var rescan = new MenuItem { Header = Loc.T("Rescan") };
            rescan.Click += (_, _) => library.Rescan();
            menu.Items.Add(new Separator());
            menu.Items.Add(rescan);
        }
        return menu;
    }

    private async Task AddLibraryFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = true,
            Title = Loc.T("AddMusicFolder"),
            SuggestedStartLocation = await StartFolder(),
        });
        var paths = folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0 && _library is not null)
            SetLibraryFolders(_library.Folders.Concat(paths));
    }

    private void SetLibraryFolders(IEnumerable<string> folders)
    {
        _settings.LibraryFolders = folders.Distinct(Playlist.PathComparer).ToList();
        _library?.SetFolders(_settings.LibraryFolders);
        RefreshLibraryRows();
        UpdateFooter();
    }

    /// <summary>O rodapé no modo biblioteca: discos, faixas e o que falta ler.</summary>
    private string LibraryFooter()
    {
        if (_library is null)
            return "";
        var text = Loc.F("AlbumsN", _library.Albums.Count) + " · " + Loc.Count(_library.TrackCount);
        return _library.Scanning > 0 ? Loc.F("Reading", _library.Scanning) + "  ·  " + text : text;
    }
}
