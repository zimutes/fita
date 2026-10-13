using System.Text.Json;
using Fita.Library;

namespace Fita.Producer;

/// <summary>Uma nota num ponto de uma faixa ("02:13 tarola alta demais").</summary>
public sealed class Note
{
    public double Time { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>As notas das faixas, em notes.json, pela chave da faixa.</summary>
public sealed class NotesStore
{
    private readonly Dictionary<string, List<Note>> _items;
    private readonly string _file;

    private NotesStore(string file, Dictionary<string, List<Note>> items)
    {
        _file = file;
        _items = items;
    }

    public static string FilePath => Path.Combine(SettingsStore.Folder, "notes.json");

    public static NotesStore Load(string? file = null)
    {
        file ??= FilePath;
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<Dictionary<string, List<Note>>>(File.ReadAllText(file)) is { } items)
                return new NotesStore(file, items);
        }
        catch (Exception ex)
        {
            Log.Write("Falha a ler as notas: " + ex.Message);
        }
        return new NotesStore(file, []);
    }

    public IReadOnlyList<Note> For(Track track) =>
        _items.TryGetValue(track.Key, out var list) ? list : [];

    public void Add(Track track, double time, string text)
    {
        if (!_items.TryGetValue(track.Key, out var list))
            _items[track.Key] = list = [];
        list.Add(new Note { Time = Math.Round(Math.Max(0, time), 1), Text = text.Trim() });
        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        Save();
    }

    public void Remove(Track track, Note note)
    {
        if (_items.TryGetValue(track.Key, out var list) && list.Remove(note))
        {
            if (list.Count == 0)
                _items.Remove(track.Key);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Write("Falha a gravar as notas: " + ex.Message);
        }
    }
}
