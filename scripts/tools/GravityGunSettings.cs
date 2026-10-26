using Godot;

namespace ProjectXs.Tools;

/// <summary>
/// Gravity gun ayarları. data/gravity_gun_default.tres dosyasından Inspector ile düzenlenebilir.
/// "Tutma gücü" değerlerini sunucu (host) kullanır; yani bunları host eden kişinin dosyası belirler.
/// </summary>
[GlobalClass]
public partial class GravityGunSettings : Resource
{
    [ExportGroup("Menzil")]
    /// <summary>Objeyi yakalayabileceğin en uzak mesafe (metre).</summary>
    [Export] public float Range { get; set; } = 25.0f;
    [Export] public float MinDistance { get; set; } = 1.5f;
    [Export] public float MaxDistance { get; set; } = 40.0f;
    /// <summary>Fare tekerleğinin her çentiğinde objenin ne kadar yaklaşıp uzaklaşacağı.</summary>
    [Export] public float ScrollStep { get; set; } = 0.75f;

    [ExportGroup("Tutma gücü")]
    /// <summary>Objenin hedefe ne kadar sıkı yapıştığı. Yüksek = daha tepkili, düşük = daha "yaylı".</summary>
    [Export] public float LinearStiffness { get; set; } = 14.0f;
    [Export] public float MaxLinearSpeed { get; set; } = 30.0f;
    [Export] public float AngularStiffness { get; set; } = 12.0f;
    [Export] public float MaxAngularSpeed { get; set; } = 20.0f;
    /// <summary>Kütlenin tutmaya etkisi. 0 = ağırlık fark etmez; büyüdükçe ağır objeler daha hantal gelir.</summary>
    [Export] public float MassInfluence { get; set; } = 0.002f;

    [ExportGroup("Kontrol")]
    /// <summary>E basılıyken fareyle döndürme hassasiyeti.</summary>
    [Export] public float RotateSensitivity { get; set; } = 0.006f;
}
