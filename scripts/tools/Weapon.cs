using System.Collections.Generic;
using Godot;
using ProjectXs.Combat;
using ProjectXs.Player;
using ProjectXs.Props;

namespace ProjectXs.Tools;

/// <summary>
/// Bütün silahlar için tek bir araç sınıfı; davranışı WeaponCatalog'daki tanım belirler.
/// Hitscan silahlarda isabeti burada (ateş eden oyuncunun bilgisayarında) hesaplayıp sunucuya bildiriyoruz.
/// </summary>
public partial class Weapon : Node, ITool
{
    // Mermi izinin çıktığı nokta, kameraya göre (sağ alt, biraz ileride) — namlu gibi görünsün diye
    private static readonly Vector3 MuzzleOffset = new(0.18f, -0.15f, -0.5f);

    private PlayerController _player;
    private Camera3D _camera;
    private WeaponDefinition _definition;
    private float _cooldown;
    private readonly RandomNumberGenerator _random = new();

    public bool Active { get; set; }
    public string Title => _definition.DisplayName;

    public string HelpText => _definition.Kind == WeaponKind.Rocket
        ? "Sol tık: roket at   (Ayağının dibine atıp roket zıplaması yapabilirsin)"
        : _definition.Automatic ? "Sol tık basılı: ateş et" : "Sol tık: ateş et";

    public void Init(PlayerController player, Camera3D camera, WeaponDefinition definition)
    {
        _player = player;
        _camera = camera;
        _definition = definition;
    }

    public void OnDeactivated()
    {
    }

    public override void _PhysicsProcess(double delta)
    {
        _cooldown -= (float)delta;
        if (!Active || Input.MouseMode != Input.MouseModeEnum.Captured) return;

        bool trigger = _definition.Automatic
            ? Input.IsActionPressed("tool_primary")
            : Input.IsActionJustPressed("tool_primary");
        if (!trigger || _cooldown > 0) return;

        _cooldown = _definition.FireInterval;
        Fire();
    }

    private void Fire()
    {
        var combat = CombatManager.Instance;
        if (combat == null) return;

        var view = _camera.GlobalTransform;
        Vector3 muzzle = view * MuzzleOffset;

        if (_definition.Kind == WeaponKind.Rocket)
        {
            combat.FireRocket(_definition.Id, muzzle, -view.Basis.Z);
            return;
        }

        var ends = new Vector3[_definition.Pellets];
        var playerDamage = new Dictionary<long, float>();
        var propHits = new Dictionary<string, (Vector3 Point, Vector3 Impulse)>();

        // Pompalıda her saçma ayrı bir ışın; aynı hedefe gelen hasarları toplayıp tek mesajla gönderiyoruz
        for (int i = 0; i < ends.Length; i++)
        {
            Vector3 direction = Spread(view.Basis, _definition.SpreadDegrees);
            if (!ToolRaycast.CastRay(_player, view.Origin, direction, _definition.Range, out var hit))
            {
                ends[i] = view.Origin + direction * _definition.Range;
                continue;
            }

            ends[i] = hit.Position;

            if (hit.Collider is PlayerController target)
            {
                playerDamage[target.PeerId] = playerDamage.GetValueOrDefault(target.PeerId) + _definition.Damage;
            }
            else if (hit.Collider is Prop prop)
            {
                string name = prop.Name;
                var previous = propHits.GetValueOrDefault(name);
                propHits[name] = (hit.Position, previous.Impulse + direction * _definition.PropImpulse);
            }
        }

        foreach (var (target, damage) in playerDamage)
            combat.ReportPlayerHit(target, damage);
        foreach (var (name, data) in propHits)
            combat.ReportPropHit(name, data.Point, data.Impulse);

        if (playerDamage.Count > 0)
            _player.ShowHitMarker();

        combat.BroadcastShot(muzzle, ends);
    }

    // Nişangahın etrafındaki bir koni içinde rastgele bir yön seçer
    private Vector3 Spread(Basis view, float degrees)
    {
        Vector3 forward = -view.Z;
        if (degrees <= 0) return forward;

        float radius = Mathf.Tan(Mathf.DegToRad(degrees)) * Mathf.Sqrt(_random.Randf());
        float angle = _random.Randf() * Mathf.Tau;
        return (forward + view.X * radius * Mathf.Cos(angle) + view.Y * radius * Mathf.Sin(angle)).Normalized();
    }
}
