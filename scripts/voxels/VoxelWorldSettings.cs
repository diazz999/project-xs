using Godot;

namespace ProjectXs.Voxels;

/// <summary>
/// Voxel dünyanın üretim ayarları. data/voxel_world_default.tres dosyasından Inspector ile düzenlenebilir.
/// Host'un ayarları geçerlidir; katılan oyuncular dünyayı host'un ayarlarıyla kendi bilgisayarlarında yeniden üretir.
/// </summary>
[GlobalClass]
public partial class VoxelWorldSettings : Resource
{
    [ExportGroup("Boyut")]
    /// <summary>Aynı tohum her zaman aynı dünyayı üretir.</summary>
    [Export] public int Seed { get; set; } = 1337;
    /// <summary>Yatayda kaç chunk (her biri 16 blok). 16 = 256 x 256 metre.</summary>
    [Export(PropertyHint.Range, "2,32")] public int ChunksX { get; set; } = 16;
    [Export(PropertyHint.Range, "2,32")] public int ChunksZ { get; set; } = 16;
    /// <summary>Dikeyde kaç chunk. 4 = 64 metre yükseklik.</summary>
    [Export(PropertyHint.Range, "2,8")] public int ChunksY { get; set; } = 4;
    /// <summary>Dünyanın en alt bloğunun yüksekliği. Doğma noktasındaki düz alanın yüzeyi her zaman 0'dadır.</summary>
    [Export] public int MinY { get; set; } = -32;

    [ExportGroup("Arazi")]
    /// <summary>Haritanın ortasında tamamen düz kalan alanın yarıçapı (metre). Test rampaları burada durur.</summary>
    [Export] public float FlatRadius { get; set; } = 48.0f;
    /// <summary>Düz alandan tepelere geçişin genişliği (metre).</summary>
    [Export] public float BlendWidth { get; set; } = 40.0f;
    /// <summary>Tepelerin en fazla yüksekliği / vadilerin derinliği (metre).</summary>
    [Export] public float HillHeight { get; set; } = 18.0f;
    /// <summary>Tepelerin sıklığı. Küçük = geniş ve yumuşak tepeler, büyük = sık ve dik tepeler.</summary>
    [Export] public float NoiseFrequency { get; set; } = 0.012f;

    public WorldParams ToParams()
        => new(Seed, ChunksX, ChunksY, ChunksZ, MinY, FlatRadius, BlendWidth, HillHeight, NoiseFrequency);
}
