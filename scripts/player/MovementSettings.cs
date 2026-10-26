using Godot;

namespace ProjectXs.Player;

/// <summary>
/// Oyuncu hareketinin bütün ayarları. Koddan bağımsız bir veri dosyası (data/movement_default.tres)
/// olarak durur; Godot'da o dosyayı seçip Inspector'dan değerleri değiştirebilirsin.
/// Birimler metre ve saniye.
/// </summary>
[GlobalClass]
public partial class MovementSettings : Resource
{
    [ExportGroup("Yerde")]
    [Export] public float WalkSpeed { get; set; } = 5.0f;
    [Export] public float SprintSpeed { get; set; } = 8.0f;
    [Export] public float GroundAcceleration { get; set; } = 10.0f;
    [Export] public float Friction { get; set; } = 6.0f;
    /// <summary>Bu hızın altında sürtünme sabit hızda durdurur; düşük hızlarda kaymayı engeller.</summary>
    [Export] public float StopSpeed { get; set; } = 2.0f;

    [ExportGroup("Havada")]
    /// <summary>Havadayken yön değiştirme gücü. Yüksek değer = daha kolay air strafe.</summary>
    [Export] public float AirAcceleration { get; set; } = 12.0f;
    /// <summary>Havada istenen yöne eklenebilecek hız sınırı. Source'taki meşhur 30 birimlik sınırın karşılığı.</summary>
    [Export] public float AirSpeedCap { get; set; } = 0.6f;

    [ExportGroup("Zıplama ve yerçekimi")]
    /// <summary>6 m/s yaklaşık 1.2 metre zıplatır; bir blok yüksekliğine rahatça çıkılır.</summary>
    [Export] public float JumpVelocity { get; set; } = 6.0f;
    [Export] public float Gravity { get; set; } = 15.0f;
    /// <summary>Açıksa zıplama tuşunu basılı tutmak yere değer değmez tekrar zıplatır (bunny hop).</summary>
    [Export] public bool AutoBunnyHop { get; set; } = true;

    [ExportGroup("Kamera")]
    [Export] public float MouseSensitivity { get; set; } = 0.0025f;
    [Export] public float EyeHeight { get; set; } = 1.6f;
    [Export] public float FieldOfView { get; set; } = 90.0f;
}
