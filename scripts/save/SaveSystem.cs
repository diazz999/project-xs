using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using ProjectXs.Props;
using ProjectXs.Voxels;

namespace ProjectXs.Save;

/// <summary>
/// Dünyayı, objeleri ve bağlantıları kaydeder ve yükler. Sadece host kullanabilir; yüklemede herkes güncel hali alır.
///
/// Kayıtlar Godot'nun kullanıcı klasöründe durur. Linux'ta bu genellikle:
/// ~/.local/share/godot/app_userdata/project xs/saves/
/// </summary>
public static class SaveSystem
{
    private const string SaveDirectory = "user://saves";
    private const string Extension = ".json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Kayıtları en yeniden en eskiye sıralı döndürür (uzantısız isimler).</summary>
    public static List<string> ListSaves()
    {
        DirAccess.MakeDirRecursiveAbsolute(SaveDirectory);

        return DirAccess.GetFilesAt(SaveDirectory)
            .Where(file => file.EndsWith(Extension))
            .OrderByDescending(file => FileAccess.GetModifiedTime($"{SaveDirectory}/{file}"))
            .Select(file => file[..^Extension.Length])
            .ToList();
    }

    public static string Save(string name)
    {
        var world = VoxelWorld.Instance;
        var props = PropManager.Instance;
        if (world == null || props == null) return "Dünya hazır değil.";

        name = Sanitize(name);
        var (propSaves, constraintSaves) = props.ExportState();

        var file = new SaveFile
        {
            Name = name,
            SavedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            WorldInts = world.CurrentParams.Ints,
            WorldFloats = world.CurrentParams.Floats,
            BlockEdits = world.ExportEdits(),
            Props = propSaves,
            Constraints = constraintSaves,
        };

        DirAccess.MakeDirRecursiveAbsolute(SaveDirectory);
        using var output = FileAccess.Open(PathOf(name), FileAccess.ModeFlags.Write);
        if (output == null)
            return $"Kaydedilemedi: {FileAccess.GetOpenError()}";

        output.StoreString(JsonSerializer.Serialize(file, JsonOptions));
        return $"\"{name}\" kaydedildi: {propSaves.Count} obje, {constraintSaves.Count} bağlantı, {file.BlockEdits.Length / 4} değişen blok.";
    }

    public static string Load(string name)
    {
        var world = VoxelWorld.Instance;
        var props = PropManager.Instance;
        if (world == null || props == null) return "Dünya hazır değil.";

        string path = PathOf(Sanitize(name));
        if (!FileAccess.FileExists(path)) return "Kayıt bulunamadı.";

        SaveFile file;
        try
        {
            using var input = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            file = JsonSerializer.Deserialize<SaveFile>(input.GetAsText(), JsonOptions);
        }
        catch (Exception exception)
        {
            GD.PushError($"Kayıt okunamadı: {exception.Message}");
            return "Kayıt dosyası bozuk.";
        }

        if (file == null || file.WorldInts.Length < 5 || file.WorldFloats.Length < 4)
            return "Kayıt dosyası bozuk.";
        if (file.Version > SaveFile.CurrentVersion)
            return "Bu kayıt oyunun daha yeni bir sürümüyle yapılmış.";

        // Önce dünya (objeler yeni zeminin üstüne konsun), sonra objeler ve bağlantılar
        world.ServerLoad(WorldParams.From(file.WorldInts, file.WorldFloats), file.BlockEdits);
        props.ServerRestore(file.Props, file.Constraints);

        return $"\"{file.Name}\" yüklendi.";
    }

    private static string PathOf(string name) => $"{SaveDirectory}/{name}{Extension}";

    // Dosya adında sorun çıkarabilecek karakterleri temizler
    private static string Sanitize(string name)
    {
        var builder = new StringBuilder();
        foreach (char c in name.Trim())
            builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');

        string result = builder.ToString();
        return string.IsNullOrEmpty(result) ? "kayit" : result[..Math.Min(result.Length, 40)];
    }
}
