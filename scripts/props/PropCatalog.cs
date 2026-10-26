using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectXs.Props;

public enum PropShape
{
    Box,
    Cylinder,
    Sphere,
}

/// <summary>
/// Bir obje türünün tanımı. Size: kutu için en/boy/derinlik; silindir için X = çap, Y = yükseklik; küre için X = çap.
/// Kütle kilogram cinsinden. Bounce 0 = hiç sekmez, 1 = neredeyse kayıpsız seker.
/// </summary>
public sealed record PropDefinition(
    string Id,
    string DisplayName,
    PropShape Shape,
    Vector3 Size,
    float Mass,
    Color Color,
    float Friction = 0.8f,
    float Bounce = 0.0f)
{
    public float MaxExtent => Mathf.Max(Size.X, Mathf.Max(Size.Y, Size.Z));
}

/// <summary>
/// Oyundaki bütün obje türleri. Yeni bir obje eklemek için listeye bir satır eklemek yeterli;
/// listedeki sıra 1-9 tuşlarıyla oluşturma sırasını belirler.
/// İleride gerçek 3D modeller (Kenney, Quaternius vb.) geldiğinde bu tanımlara model yolu eklenecek.
/// </summary>
public static class PropCatalog
{
    public static readonly IReadOnlyList<PropDefinition> All = new List<PropDefinition>
    {
        new("crate",     "Sandık",        PropShape.Box,      new Vector3(1.0f, 1.0f, 1.0f),   25f,  new Color(0.72f, 0.50f, 0.30f)),
        new("barrel",    "Varil",         PropShape.Cylinder, new Vector3(0.7f, 1.1f, 0.7f),   40f,  new Color(0.70f, 0.20f, 0.15f)),
        new("plank",     "Tahta",         PropShape.Box,      new Vector3(0.4f, 0.12f, 3.0f),  10f,  new Color(0.62f, 0.46f, 0.28f)),
        new("ball",      "Top",           PropShape.Sphere,   new Vector3(0.6f, 0.6f, 0.6f),    4f,  new Color(0.95f, 0.80f, 0.20f), Friction: 0.5f, Bounce: 0.6f),
        new("big_crate", "Büyük sandık",  PropShape.Box,      new Vector3(2.0f, 2.0f, 2.0f),  120f,  new Color(0.55f, 0.40f, 0.25f)),
        new("slab",      "Beton plaka",   PropShape.Box,      new Vector3(3.0f, 0.3f, 3.0f),  400f,  new Color(0.60f, 0.60f, 0.62f)),
        new("pipe",      "Boru",          PropShape.Cylinder, new Vector3(0.3f, 3.0f, 0.3f),   15f,  new Color(0.50f, 0.55f, 0.60f), Friction: 0.4f),
        new("wheel",     "Tekerlek",      PropShape.Cylinder, new Vector3(0.9f, 0.3f, 0.9f),   15f,  new Color(0.15f, 0.15f, 0.17f), Friction: 1.5f),
        new("seat",      "Koltuk",        PropShape.Box,      new Vector3(0.8f, 0.5f, 0.8f),   15f,  new Color(0.20f, 0.45f, 0.75f)),
    };

    private static readonly Dictionary<string, PropDefinition> ById = All.ToDictionary(d => d.Id);

    public static bool TryGet(string id, out PropDefinition definition) => ById.TryGetValue(id, out definition);
}
