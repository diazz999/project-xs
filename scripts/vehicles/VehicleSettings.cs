using Godot;

namespace ProjectXs.Vehicles;

/// <summary>
/// Araç sürüş ayarları. data/vehicle_default.tres dosyasından Inspector ile düzenlenebilir.
/// Sürüşü sunucu hesapladığı için host eden kişinin dosyası geçerlidir.
/// </summary>
[GlobalClass]
public partial class VehicleSettings : Resource
{
    [ExportGroup("Hız")]
    /// <summary>Tam gazda tekerleklerin ulaşmaya çalıştığı hız (m/s).</summary>
    [Export] public float MaxSpeed { get; set; } = 10.0f;
    /// <summary>Shift basılıyken hız ve tork çarpanı.</summary>
    [Export] public float BoostMultiplier { get; set; } = 1.6f;

    [ExportGroup("Dönüş")]
    /// <summary>A/D ile sağ ve sol tekerlekler arasında yaratılan hız farkı (m/s). Yüksek = daha keskin dönüş.</summary>
    [Export] public float TurnSpeed { get; set; } = 6.0f;
    /// <summary>Dönerken tekerlek sürtünmesinin çarpanı. 1 = değişmez, düşük = yanlara daha kolay kayar, daha çevik döner.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")] public float SteeringGrip { get; set; } = 0.55f;
    /// <summary>Dönüş desteğinin hedeflediği dönme hızı (radyan/sn). 0 = destek kapalı, sadece tekerleklerle döner.</summary>
    [Export] public float YawAssistRate { get; set; } = 2.0f;
    /// <summary>Dönüş desteğinin gücü (en fazla açısal ivme). Havadayken de çalışır; yüksek değer havada takla attırır.</summary>
    [Export] public float YawAssistStrength { get; set; } = 6.0f;

    [ExportGroup("Motor")]
    /// <summary>Her motorlu tekerleğin uygulayabileceği en büyük tork (Nm).</summary>
    [Export] public float MotorTorque { get; set; } = 60.0f;
    /// <summary>Gaz bırakıldığında tekerlekleri durdurmak için uygulanan en büyük tork (Nm).</summary>
    [Export] public float BrakeTorque { get; set; } = 40.0f;
    /// <summary>Tekerleğin hedef hıza ne kadar sert yöneldiği. Yüksek = ani, düşük = yumuşak hızlanma.</summary>
    [Export] public float MotorGain { get; set; } = 30.0f;
}
