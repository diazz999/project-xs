using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectXs.Voxels;

/// <summary>Blok türleri. Değerler kaydedilen dünyada ve ağda kullanılır; mevcutların sırasını değiştirme, yenileri sona ekle.</summary>
public enum BlockType : byte
{
    Air = 0,
    Grass = 1,
    Dirt = 2,
    Stone = 3,
    Sand = 4,
    Planks = 5,
    Brick = 6,
    Concrete = 7,
    Bedrock = 8,
}

/// <summary>
/// Bir blok türünün görünüşü ve kuralları. Color yan ve alt yüzlerin, TopColor üst yüzün rengi.
/// Breakable: kırılabilir mi. Placeable: inşa aracında seçilebilir mi.
/// </summary>
public sealed record BlockDefinition(
    BlockType Type,
    string DisplayName,
    Color Color,
    Color TopColor,
    bool Breakable = true,
    bool Placeable = true);

/// <summary>
/// Bütün blok türleri. Yeni bir blok eklemek için BlockType'a bir değer, buraya da bir satır eklemek yeterli.
/// Doku kullanmıyoruz; her blok kendi rengiyle ve küçük bir renk oynamasıyla çiziliyor (low poly görünüm).
/// </summary>
public static class BlockCatalog
{
    public static readonly IReadOnlyList<BlockDefinition> All = new List<BlockDefinition>
    {
        new(BlockType.Air,      "Hava",      Colors.Transparent,           Colors.Transparent, Breakable: false, Placeable: false),
        new(BlockType.Grass,    "Çimen",     new Color(0.47f, 0.34f, 0.22f), new Color(0.38f, 0.62f, 0.27f)),
        new(BlockType.Dirt,     "Toprak",    new Color(0.47f, 0.34f, 0.22f), new Color(0.47f, 0.34f, 0.22f)),
        new(BlockType.Stone,    "Taş",       new Color(0.52f, 0.52f, 0.54f), new Color(0.55f, 0.55f, 0.57f)),
        new(BlockType.Sand,     "Kum",       new Color(0.86f, 0.79f, 0.55f), new Color(0.88f, 0.82f, 0.58f)),
        new(BlockType.Planks,   "Tahta",     new Color(0.70f, 0.52f, 0.31f), new Color(0.72f, 0.55f, 0.33f)),
        new(BlockType.Brick,    "Tuğla",     new Color(0.66f, 0.28f, 0.22f), new Color(0.66f, 0.28f, 0.22f)),
        new(BlockType.Concrete, "Beton",     new Color(0.82f, 0.82f, 0.80f), new Color(0.84f, 0.84f, 0.82f)),
        new(BlockType.Bedrock,  "Ana kaya",  new Color(0.20f, 0.20f, 0.22f), new Color(0.20f, 0.20f, 0.22f), Breakable: false, Placeable: false),
    };

    public static readonly IReadOnlyList<BlockDefinition> Placeable = All.Where(d => d.Placeable).ToList();

    private static readonly BlockDefinition[] ByType = BuildLookup();

    public static BlockDefinition Get(BlockType type) => ByType[(int)type] ?? ByType[0];

    public static bool IsValid(int value) => value >= 0 && value < ByType.Length && ByType[value] != null;

    private static BlockDefinition[] BuildLookup()
    {
        var lookup = new BlockDefinition[256];
        foreach (var definition in All)
            lookup[(int)definition.Type] = definition;
        return lookup;
    }
}
