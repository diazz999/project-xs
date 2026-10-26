using System.Collections.Generic;
using Godot;
using ProjectXs.Player;
using ProjectXs.Props;
using ProjectXs.Voxels;

namespace ProjectXs.Combat;

/// <summary>
/// Savaşın ağ tarafı: hasar, objelere itme, roketler, patlamalar ve öldürme bildirimleri.
///
/// Ağ modeli (arkadaş oyunu için basit tutuldu):
/// - Mermilerin isabetini ateş eden oyuncunun kendi bilgisayarı hesaplar ve sunucuya bildirir. Böylece gecikme olsa
///   bile nişan aldığın yere vurursun.
/// - Sunucu hasarı, vurulan oyuncunun kendi bilgisayarına iletir; can değeri oradan herkese yayılır.
/// - Roketleri ve patlamaları sunucu hesaplar, herkes sadece görüntüsünü çizer.
/// </summary>
public partial class CombatManager : Node3D
{
    private const float MaxDamagePerReport = 300.0f;
    private const float MaxPropImpulse = 3000.0f;
    private const float MaxExplosionPropSpeed = 25.0f; // Hafif objelerin patlamada uzaya uçmasını engeller
    private const float RocketLifetime = 6.0f;
    private const float KillFeedDuration = 5.0f;

    public static CombatManager Instance { get; private set; }

    /// <summary>Oyuncu node'larının bulunduğu yer (NetworkManager'dan gelir).</summary>
    public Node3D PlayersRoot { get; set; }

    private sealed class Rocket
    {
        public int Id;
        public long Shooter;
        public WeaponDefinition Definition;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Life;
    }

    private readonly List<Rocket> _rockets = new();
    private readonly Dictionary<int, RocketVisual> _rocketVisuals = new();
    private int _nextRocketId;
    private VBoxContainer _killFeed;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Ready()
    {
        var layer = new CanvasLayer { Name = "KillFeed", Layer = 5 };
        AddChild(layer);

        _killFeed = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        _killFeed.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _killFeed.OffsetLeft = -420;
        _killFeed.OffsetRight = -16;
        _killFeed.OffsetTop = 16;
        _killFeed.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(_killFeed);
    }

    // --- İstemcinin çağırdığı kısa yollar ---

    public void ReportPlayerHit(long target, float damage)
        => RpcId(1, MethodName.ServerDamagePlayer, target, damage);

    public void ReportPropHit(string propName, Vector3 point, Vector3 impulse)
        => RpcId(1, MethodName.ServerImpulseProp, propName, point, impulse);

    /// <summary>Atış efektlerini önce yerelde hemen çizer, sonra diğer oyunculara iletilmesi için sunucuya yollar.</summary>
    public void BroadcastShot(Vector3 from, Vector3[] ends)
    {
        CombatEffects.Shot(this, from, ends);
        RpcId(1, MethodName.ServerShotEffects, from, ends);
    }

    public void FireRocket(string weaponId, Vector3 origin, Vector3 direction)
        => RpcId(1, MethodName.ServerFireRocket, weaponId, origin, direction);

    public void ReportDeath(long attacker)
        => RpcId(1, MethodName.ServerReportDeath, attacker);

    /// <summary>Oturum bitince yerel görüntüleri temizler.</summary>
    public void ClearAll()
    {
        _rockets.Clear();
        foreach (var visual in _rocketVisuals.Values)
            if (IsInstanceValid(visual))
                visual.QueueFree();
        _rocketVisuals.Clear();

        foreach (Node child in _killFeed.GetChildren())
            child.QueueFree();
    }

    // --- Sunucu tarafı ---

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerDamagePlayer(long target, float damage)
    {
        if (!Multiplayer.IsServer()) return;
        DealDamage(target, Mathf.Clamp(damage, 0, MaxDamagePerReport), SenderId(), Vector3.Zero);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerImpulseProp(string propName, Vector3 point, Vector3 impulse)
    {
        if (!Multiplayer.IsServer()) return;

        var prop = PropManager.Instance?.GetProp(propName);
        if (prop == null || prop.Freeze) return; // Donmuş objeler kımıldamaz

        prop.Sleeping = false;
        prop.ApplyImpulse(impulse.LimitLength(MaxPropImpulse), point - prop.GlobalPosition);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ServerShotEffects(Vector3 from, Vector3[] ends)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(MethodName.ClientShotEffects, SenderId(), from, ends);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerFireRocket(string weaponId, Vector3 origin, Vector3 direction)
    {
        if (!Multiplayer.IsServer()) return;
        if (!WeaponCatalog.TryGet(weaponId, out var definition) || definition.Kind != WeaponKind.Rocket) return;
        if (direction.LengthSquared() < 0.0001f) return;

        var rocket = new Rocket
        {
            Id = ++_nextRocketId,
            Shooter = SenderId(),
            Definition = definition,
            Position = origin,
            Velocity = direction.Normalized() * definition.ProjectileSpeed,
            Life = RocketLifetime,
        };
        _rockets.Add(rocket);

        Rpc(MethodName.ClientSpawnRocket, rocket.Id, origin, rocket.Velocity);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerReportDeath(long attacker)
    {
        if (!Multiplayer.IsServer()) return;

        long victim = SenderId();
        string victimName = NameOf(victim);
        string text = attacker == 0 || attacker == victim
            ? $"{victimName} kendini havaya uçurdu"
            : $"{NameOf(attacker)}, {victimName} oyuncusunu indirdi";

        Rpc(MethodName.ClientKillFeed, text);
    }

    // Sunucu roketleri her fizik karesinde ilerletir; önceki ve yeni konum arasına ışın atarak çarpışmayı yakalar,
    // böylece hızlı roketler ince duvarların içinden geçmez.
    public override void _PhysicsProcess(double delta)
    {
        if (_rockets.Count == 0 || !Multiplayer.IsServer()) return;

        float dt = (float)delta;
        var space = GetWorld3D().DirectSpaceState;

        for (int i = _rockets.Count - 1; i >= 0; i--)
        {
            var rocket = _rockets[i];
            Vector3 next = rocket.Position + rocket.Velocity * dt;

            var query = PhysicsRayQueryParameters3D.Create(rocket.Position, next);
            if (FindPlayer(rocket.Shooter) is { } shooter)
                query.Exclude = new Godot.Collections.Array<Rid> { shooter.GetRid() }; // Kendi kafana çarpmasın

            var hit = space.IntersectRay(query);
            rocket.Life -= dt;

            if (hit.Count > 0)
            {
                Explode(rocket, hit["position"].AsVector3());
                _rockets.RemoveAt(i);
            }
            else if (rocket.Life <= 0)
            {
                Explode(rocket, next);
                _rockets.RemoveAt(i);
            }
            else
            {
                rocket.Position = next;
            }
        }
    }

    private void Explode(Rocket rocket, Vector3 position)
    {
        var definition = rocket.Definition;
        float radius = Mathf.Max(definition.ExplosionRadius, 0.1f);

        Rpc(MethodName.ClientExplosion, rocket.Id, position, radius);

        // Bloklar: patlamanın çevresindeki kırılabilir bloklar yok olur
        VoxelWorld.Instance?.ServerExplode(position, definition.BlockDamageRadius);

        // Oyuncular: merkeze yakınlıkla azalan hasar ve geri tepme (roket zıplaması buradan gelir)
        if (PlayersRoot != null)
        {
            foreach (Node node in PlayersRoot.GetChildren())
            {
                if (node is not PlayerController player || player.Dead) continue;

                Vector3 center = player.GlobalPosition + Vector3.Up * 0.9f;
                float distance = center.DistanceTo(position);
                if (distance >= radius) continue;

                float falloff = 1.0f - distance / radius;
                Vector3 direction = distance < 0.01f ? Vector3.Up : (center - position) / distance;
                float damage = definition.Damage * falloff;
                if (player.PeerId == rocket.Shooter)
                    damage *= definition.SelfDamageScale;

                DealDamage(player.PeerId, damage, rocket.Shooter, direction * definition.Knockback * falloff);
            }
        }

        // Objeler: dışarı ve biraz yukarı doğru itilir
        var props = PropManager.Instance;
        if (props == null) return;

        foreach (var prop in props.AllProps())
        {
            if (prop.Freeze) continue;

            float distance = prop.GlobalPosition.DistanceTo(position);
            if (distance >= radius) continue;

            float falloff = 1.0f - distance / radius;
            Vector3 direction = (prop.GlobalPosition - position + Vector3.Up * 0.3f).Normalized();
            float impulse = Mathf.Min(definition.PropImpulse * falloff, prop.Mass * MaxExplosionPropSpeed);

            prop.Sleeping = false;
            prop.ApplyImpulse(direction * impulse);
        }
    }

    private void DealDamage(long target, float damage, long attacker, Vector3 knockback)
    {
        if (damage <= 0 && knockback == Vector3.Zero) return;

        var player = FindPlayer(target);
        if (player == null) return;

        // Can değeri oyuncunun kendi bilgisayarında tutulur; hasarı oraya iletiyoruz
        player.RpcId(target, PlayerController.MethodName.ReceiveDamage, damage, attacker, knockback);
    }

    private PlayerController FindPlayer(long peerId)
        => PlayersRoot?.GetNodeOrNull<PlayerController>(peerId.ToString());

    private string NameOf(long peerId)
        => FindPlayer(peerId)?.PlayerName is { Length: > 0 } name ? name : "Biri";

    private long SenderId()
    {
        int sender = Multiplayer.GetRemoteSenderId();
        return sender == 0 ? Multiplayer.GetUniqueId() : sender;
    }

    // --- Herkeste çalışan görüntü RPC'leri (sadece sunucu çağırabilir) ---

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ClientShotEffects(long shooter, Vector3 from, Vector3[] ends)
    {
        if (shooter == Multiplayer.GetUniqueId()) return; // Ateş eden zaten kendi ekranında çizdi
        CombatEffects.Shot(this, from, ends);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientSpawnRocket(int id, Vector3 origin, Vector3 velocity)
    {
        var visual = new RocketVisual { Velocity = velocity, Position = origin };
        AddChild(visual);
        _rocketVisuals[id] = visual;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientExplosion(int id, Vector3 position, float radius)
    {
        if (_rocketVisuals.Remove(id, out var visual) && IsInstanceValid(visual))
            visual.QueueFree();
        CombatEffects.Explosion(this, position, radius);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientKillFeed(string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        _killFeed.AddChild(label);

        GetTree().CreateTimer(KillFeedDuration).Timeout += () =>
        {
            if (IsInstanceValid(label))
                label.QueueFree();
        };
    }
}
