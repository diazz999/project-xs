using System.Collections.Generic;
using System.Linq;

namespace ProjectXs.Combat;

public enum WeaponKind
{
    /// <summary>Anında isabet eden mermi (tabanca, makineli, pompalı).</summary>
    Hitscan,
    /// <summary>Uçan ve çarptığı yerde patlayan roket.</summary>
    Rocket,
}

/// <summary>
/// Bir silahın bütün özellikleri.
/// Damage: hitscan'de mermi (saçma) başına hasar, roketde patlamanın merkezindeki hasar.
/// FireInterval: iki atış arası saniye. Pellets: bir atıştaki saçma sayısı. SpreadDegrees: sapma konisi.
/// PropImpulse: objelere verilen itme (hitscan'de saçma başına, roketde patlama merkezinde).
/// Knockback: patlamanın oyuncuları fırlatma hızı (m/s). SelfDamageScale: kendi roketinin sana verdiği hasar oranı.
/// BlockDamageRadius: patlamanın yok ettiği blokların yarıçapı (metre), 0 = blok kırmaz.
/// </summary>
public sealed record WeaponDefinition(
    string Id,
    string DisplayName,
    WeaponKind Kind,
    float Damage,
    float FireInterval,
    bool Automatic,
    int Pellets = 1,
    float SpreadDegrees = 0.0f,
    float Range = 200.0f,
    float PropImpulse = 50.0f,
    float ProjectileSpeed = 0.0f,
    float ExplosionRadius = 0.0f,
    float Knockback = 0.0f,
    float SelfDamageScale = 0.5f,
    float BlockDamageRadius = 0.0f);

/// <summary>
/// Oyundaki bütün silahlar. Yeni bir silah eklemek için listeye bir satır eklemek yeterli;
/// araç çubuğunda gravity gun ve tool gun'dan sonra bu sırayla yer alırlar.
/// </summary>
public static class WeaponCatalog
{
    public static readonly IReadOnlyList<WeaponDefinition> All = new List<WeaponDefinition>
    {
        new("pistol",  "Tabanca",   WeaponKind.Hitscan, Damage: 25, FireInterval: 0.25f, Automatic: false,
            SpreadDegrees: 0.4f, Range: 200, PropImpulse: 60),

        new("smg",     "Makineli",  WeaponKind.Hitscan, Damage: 11, FireInterval: 0.08f, Automatic: true,
            SpreadDegrees: 2.5f, Range: 120, PropImpulse: 25),

        new("shotgun", "Pompalı",   WeaponKind.Hitscan, Damage: 11, FireInterval: 0.9f, Automatic: false,
            Pellets: 9, SpreadDegrees: 6.0f, Range: 50, PropImpulse: 30),

        new("rocket",  "Roketatar", WeaponKind.Rocket,  Damage: 100, FireInterval: 1.0f, Automatic: false,
            PropImpulse: 900, ProjectileSpeed: 28, ExplosionRadius: 5, Knockback: 14, SelfDamageScale: 0.4f,
            BlockDamageRadius: 2.2f),
    };

    private static readonly Dictionary<string, WeaponDefinition> ById = All.ToDictionary(w => w.Id);

    public static bool TryGet(string id, out WeaponDefinition definition) => ById.TryGetValue(id, out definition);
}
