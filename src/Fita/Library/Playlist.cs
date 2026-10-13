using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Fita.Library;

public enum RepeatMode { Off, All, One }

public enum SortKey { Title, Artist, Album, FileName, Path, Duration, Random, Reverse }

/// <summary>ObservableCollection que aceita listas grandes de uma vez (um só aviso de mudança).</summary>
public sealed class BulkCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void InsertRange(int index, IReadOnlyList<T> items)
    {
        if (items.Count == 0)
            return;
        if (items.Count < 8)
        {
            for (var i = 0; i < items.Count; i++)
                Insert(index + i, items[i]);
            return;
        }
        var list = Items.ToList();
        list.InsertRange(Math.Clamp(index, 0, list.Count), items);
        ReplaceAll(list);
    }
}

/// <summary>
/// A lista de reprodução e a ordem de passagem (repetir, aleatório). Vive na thread da interface.
/// </summary>
public sealed class Playlist
{
    private readonly Random _random = new();
    private List<Track> _shuffleOrder = [];
    private bool _shuffle;

    public BulkCollection<Track> Items { get; } = new();

    /// <summary>A faixa a tocar (ou a que tocaria ao carregar em Play).</summary>
    public Track? Current { get; private set; }

    public RepeatMode Repeat { get; set; }

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            _shuffle = value;
            if (value)
                Reshuffle(Current);
        }
    }

    /// <summary>Qualquer mudança no conteúdo ou na ordem (para gravar e atualizar totais).</summary>
    public event Action? Changed;

    public Playlist()
    {
        Items.CollectionChanged += (_, _) =>
        {
            Renumber();
            Changed?.Invoke();
        };
    }

    public void SetCurrent(Track? track)
    {
        if (Current == track)
            return;
        if (Current is not null)
            Current.IsCurrent = false;
        Current = track;
        if (track is not null)
            track.IsCurrent = true;
    }

    public double TotalDuration => Items.Sum(t => t.Duration);

    public void Add(IReadOnlyList<Track> tracks, int? index = null)
    {
        Items.InsertRange(index ?? Items.Count, tracks);
        if (_shuffle)
            foreach (var t in tracks)
            {
                // Entram na ordem aleatória depois da faixa atual, em sítios ao acaso.
                var from = Current is null ? 0 : _shuffleOrder.IndexOf(Current) + 1;
                _shuffleOrder.Insert(_random.Next(Math.Max(0, from), _shuffleOrder.Count + 1), t);
            }
    }

    public void Remove(IEnumerable<Track> tracks)
    {
        var set = tracks.ToHashSet();
        if (set.Count == 0)
            return;
        Items.ReplaceAll(Items.Where(t => !set.Contains(t)).ToList());
        _shuffleOrder.RemoveAll(set.Contains);
    }

    public void Clear()
    {
        Items.ReplaceAll([]);
        _shuffleOrder.Clear();
        SetCurrent(null);
    }

    /// <summary>Remove faixas repetidas (o mesmo caminho e troço), mantendo a primeira ocorrência.</summary>
    public int RemoveDuplicates()
    {
        var seen = new HashSet<string>(PathComparer);
        var keep = Items.Where(t => seen.Add(t.Key)).ToList();
        var removed = Items.Count - keep.Count;
        if (removed > 0)
        {
            var keepSet = keep.ToHashSet();
            Items.ReplaceAll(keep);
            _shuffleOrder.RemoveAll(t => !keepSet.Contains(t));
        }
        return removed;
    }

    public int RemoveMissing()
    {
        var missing = Items.Where(t => !File.Exists(t.Path)).ToList();
        Remove(missing);
        return missing.Count;
    }

    /// <summary>Move as faixas indicadas (pela ordem da lista) para antes da posição alvo.</summary>
    public void Move(IReadOnlyCollection<Track> tracks, int target)
    {
        var moving = Items.Where(tracks.Contains).ToList();
        if (moving.Count == 0)
            return;
        var before = Items.Take(Math.Clamp(target, 0, Items.Count)).Count(t => !moving.Contains(t));
        var rest = Items.Where(t => !moving.Contains(t)).ToList();
        rest.InsertRange(Math.Clamp(before, 0, rest.Count), moving);
        Items.ReplaceAll(rest);
    }

    public void Sort(SortKey key)
    {
        List<Track> sorted = key switch
        {
            SortKey.Title => Items.OrderBy(t => t.Title ?? t.FileName, NaturalComparer.Instance).ToList(),
            SortKey.Artist => Items.OrderBy(t => t.Artist ?? "￿", NaturalComparer.Instance)
                .ThenBy(t => t.Album ?? "", NaturalComparer.Instance).ThenBy(t => t.TrackNumber)
                .ThenBy(t => t.Path, NaturalComparer.Instance).ToList(),
            SortKey.Album => Items.OrderBy(t => t.Album ?? "￿", NaturalComparer.Instance)
                .ThenBy(t => t.TrackNumber).ThenBy(t => t.Path, NaturalComparer.Instance).ToList(),
            SortKey.FileName => Items.OrderBy(t => Path.GetFileName(t.Path), NaturalComparer.Instance).ToList(),
            SortKey.Path => Items.OrderBy(t => t.Path, NaturalComparer.Instance).ToList(),
            SortKey.Duration => Items.OrderBy(t => t.Duration).ToList(),
            SortKey.Random => Items.OrderBy(_ => _random.Next()).ToList(),
            _ => Items.Reverse().ToList(),
        };
        Items.ReplaceAll(sorted);
    }

    /// <summary>
    /// A faixa que se segue a <paramref name="track"/>. <paramref name="automatic"/>: chamada no fim
    /// da faixa (respeita "repetir uma"); falso quando o utilizador carrega em Seguinte.
    /// </summary>
    public Track? After(Track? track, bool automatic)
    {
        if (Items.Count == 0)
            return null;
        if (track is null)
            return _shuffle ? FirstShuffled() : Items[0];
        if (automatic && Repeat == RepeatMode.One && Items.Contains(track))
            return track;

        if (_shuffle)
        {
            var at = _shuffleOrder.IndexOf(track);
            if (at >= 0 && at + 1 < _shuffleOrder.Count)
                return _shuffleOrder[at + 1];
            if (Repeat == RepeatMode.Off && automatic && at >= 0)
                return null;
            Reshuffle(null, avoidFirst: track);
            return _shuffleOrder.FirstOrDefault();
        }

        var index = IndexOf(track);
        // Se a faixa saiu da lista, o número antigo diz onde estava: segue-se a que ficou nesse lugar.
        var next = index >= 0 ? index + 1 : Math.Max(0, track.Number - 1);
        if (next < Items.Count)
            return Items[next];
        return Repeat != RepeatMode.Off ? Items[0] : null;
    }

    public Track? Before(Track? track)
    {
        if (Items.Count == 0)
            return null;
        if (track is null)
            return Items[0];
        if (_shuffle)
        {
            var at = _shuffleOrder.IndexOf(track);
            return at > 0 ? _shuffleOrder[at - 1] : track;
        }
        var index = IndexOf(track);
        if (index > 0)
            return Items[index - 1];
        if (index < 0)
            return Items[Math.Clamp(track.Number - 2, 0, Items.Count - 1)];
        return Repeat == RepeatMode.All ? Items[^1] : track;
    }

    public int IndexOf(Track track)
    {
        // O número é a posição (1…n) quando a lista não mudou desde a última renumeração.
        var hint = track.Number - 1;
        if (hint >= 0 && hint < Items.Count && ReferenceEquals(Items[hint], track))
            return hint;
        return Items.IndexOf(track);
    }

    private Track? FirstShuffled()
    {
        if (_shuffleOrder.Count != Items.Count)
            Reshuffle(null);
        return _shuffleOrder.FirstOrDefault();
    }

    private void Reshuffle(Track? first, Track? avoidFirst = null)
    {
        var list = Items.Where(t => t != first).OrderBy(_ => _random.Next()).ToList();
        if (avoidFirst is not null && list.Count > 1 && list[0] == avoidFirst)
            (list[0], list[^1]) = (list[^1], list[0]);
        if (first is not null)
            list.Insert(0, first);
        _shuffleOrder = list;
    }

    private void Renumber()
    {
        for (var i = 0; i < Items.Count; i++)
            Items[i].Number = i + 1;
    }

    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

/// <summary>Ordem "natural": "2 - x" vem antes de "10 - y". Sem distinguir maiúsculas.</summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                var si = i;
                var sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a.AsSpan(si, i - si).TrimStart('0');
                var nb = b.AsSpan(sj, j - sj).TrimStart('0');
                if (na.Length != nb.Length)
                    return na.Length.CompareTo(nb.Length);
                var c = na.CompareTo(nb, StringComparison.Ordinal);
                if (c != 0)
                    return c;
            }
            else
            {
                var c = string.Compare(a, i, b, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0)
                    return c;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
