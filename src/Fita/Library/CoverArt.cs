namespace Fita.Library;

/// <summary>
/// A capa de uma faixa: a imagem dentro do ficheiro (de preferência a da frente) ou, se não
/// houver, uma imagem com nome de capa na pasta (cover, folder, front…), como o Winamp fazia.
/// </summary>
public static class CoverArt
{
    // Por ordem de preferência.
    private static readonly string[] FolderNames = ["cover", "folder", "front", "album", "albumart", "albumartlarge", "albumartsmall"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];
    private const long MaxBytes = 16 * 1024 * 1024;

    /// <summary>Os bytes da imagem, ou null. Lê disco: chamar fora da thread da interface.</summary>
    public static byte[]? Find(string path)
    {
        try
        {
            if (Embedded(path) is { } embedded)
                return embedded;
        }
        catch { }
        try
        {
            return InFolder(path);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? Embedded(string path)
    {
        using var file = TagLib.File.Create(path);
        var pictures = file.Tag.Pictures;
        if (pictures is null || pictures.Length == 0)
            return null;
        var picture = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? pictures[0];
        var data = picture.Data?.Data;
        return data is { Length: > 0 and < (int)MaxBytes } ? data : null;
    }

    private static byte[]? InFolder(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder))
            return null;
        var images = Directory.EnumerateFiles(folder)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        foreach (var name in FolderNames)
        {
            var match = images.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase))
                // O Windows Media Player guardava "AlbumArt_{…}_Large.jpg".
                ?? (name == "albumartlarge" ? images.FirstOrDefault(f => Path.GetFileName(f).StartsWith("AlbumArt_", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(f).EndsWith("_Large", StringComparison.OrdinalIgnoreCase)) : null);
            if (match is not null && new FileInfo(match).Length < MaxBytes)
                return File.ReadAllBytes(match);
        }
        return null;
    }
}
