using Godot;

namespace ProjectXs.Core;

/// <summary>
/// data/ klasöründeki ayar dosyalarını (.tres) yükler. Dosya yoksa veya bozuksa varsayılan değerlerle devam eder,
/// böylece bir ayar dosyasını yanlışlıkla bozmak oyunu çökertmez.
/// </summary>
public static class SettingsLoader
{
    public static T Load<T>(string path) where T : Resource, new()
    {
        if (ResourceLoader.Exists(path) && GD.Load<T>(path) is { } loaded)
            return loaded;

        GD.PushWarning($"{path} yüklenemedi, varsayılan {typeof(T).Name} değerleri kullanılıyor.");
        return new T();
    }
}
