using Godot;
using ProjectXs.Core;
using ProjectXs.Tools;

namespace ProjectXs.Props;

/// <summary>
/// Dünyadaki bir fizik objesi (sandık, varil, top...).
///
/// Ağ modeli: fiziği sadece sunucu (host) hesaplar. Sunucu her karede objenin konumunu NetPosition/NetRotation'a yazar,
/// MultiplayerSynchronizer bunu değiştiğinde istemcilere gönderir. İstemcilerde obje "kinematik" olarak dondurulur
/// (kendi fiziği çalışmaz) ve gelen konuma yumuşakça kayar. Böylece herkes aynı kutunun aynı şekilde devrildiğini görür.
///
/// Gravity gun istekleri RPC olarak sunucuya gelir; sunucu kimin tuttuğunu (HolderId) takip eder ve kuralları uygular.
/// </summary>
public partial class Prop : RigidBody3D
{
    private const string GunSettingsPath = "res://data/gravity_gun_default.tres";
    private const float FallLimit = -50.0f;
    private const float ClientSmoothing = 18.0f;   // İstemcide hedef konuma kayma hızı
    private const float ClientSnapDistance = 3.0f; // Bundan uzaksa kaymadan ışınla

    private static GravityGunSettings _gunSettings;

    // --- Ağ üzerinden senkronize edilen durum ---
    [Export] public Vector3 NetPosition { get; set; }
    [Export] public Quaternion NetRotation { get; set; } = Quaternion.Identity;
    /// <summary>Objeyi gravity gun ile tutan oyuncunun peer id'si, kimse tutmuyorsa 0.</summary>
    [Export] public long HolderId { get; set; }
    [Export] public bool NetFrozen { get; set; }
    /// <summary>Koltuk objelerinde, oturan oyuncunun peer id'si; boşsa 0.</summary>
    [Export] public long DriverId { get; set; }

    /// <summary>Tool gun ile seçildiğinde sadece seçen oyuncunun ekranında sarı parlar; ağ üzerinden gönderilmez.</summary>
    public bool LocalSelected
    {
        get => _localSelected;
        set
        {
            _localSelected = value;
            _visualState = -1; // Görüntüyü bir sonraki karede yenile
        }
    }

    public string TypeId { get; private set; }
    public long OwnerPeerId { get; private set; }

    private bool _isServer;
    private bool _hasTarget;
    private Vector3 _targetPosition;
    private Quaternion _targetRotation = Quaternion.Identity;
    private bool _snapped;
    private StandardMaterial3D _material;
    private int _visualState = -1;
    private bool _localSelected;

    /// <summary>Obje sahneye eklenmeden önce çağrılır; çarpışma, görüntü ve senkronizasyonu kurar.</summary>
    public void Setup(PropDefinition definition, Vector3 position, Quaternion rotation, long ownerPeerId)
    {
        TypeId = definition.Id;
        OwnerPeerId = ownerPeerId;

        Position = position;
        Quaternion = rotation;
        NetPosition = position;
        NetRotation = rotation;

        Mass = definition.Mass;
        ContinuousCd = true;
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = definition.Friction,
            Bounce = definition.Bounce,
        };

        BuildShape(definition);
        BuildSynchronizer();
    }

    public override void _Ready()
    {
        _isServer = Multiplayer.IsServer();
        _gunSettings ??= SettingsLoader.Load<GravityGunSettings>(GunSettingsPath);

        if (!_isServer)
        {
            // İstemcide fizik kapalı; obje sadece sunucunun söylediği yere gider ama oyuncular yine üstüne basabilir.
            FreezeMode = FreezeModeEnum.Kinematic;
            Freeze = true;
        }
        else
        {
            Freeze = NetFrozen;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isServer)
        {
            NetPosition = GlobalPosition;
            NetRotation = GlobalBasis.GetRotationQuaternion();

            if (GlobalPosition.Y < FallLimit)
            {
                // Bağlantılarıyla birlikte temizlensin diye yöneticiden siliyoruz
                if (PropManager.Instance != null)
                    PropManager.Instance.RemoveProp(this);
                else
                    QueueFree();
            }
            return;
        }

        var target = new Transform3D(new Basis(NetRotation.Normalized()), NetPosition);
        if (!_snapped || GlobalPosition.DistanceTo(NetPosition) > ClientSnapDistance)
        {
            GlobalTransform = target;
            _snapped = true;
        }
        else
        {
            float weight = 1.0f - Mathf.Exp(-ClientSmoothing * (float)delta);
            GlobalTransform = GlobalTransform.InterpolateWith(target, weight);
        }
    }

    public override void _Process(double delta)
    {
        UpdateHighlight();
    }

    // Sunucuda, tutulan objeyi hedefe doğru çeker. Konumu ışınlamak yerine hız verdiğimiz için
    // obje duvarlardan geçmez, başka objelere çarpar ve bırakınca hızını korur (fırlatma buradan gelir).
    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (!_isServer || HolderId == 0 || !_hasTarget) return;

        var s = _gunSettings;
        float massFactor = 1.0f / (1.0f + Mass * s.MassInfluence);

        Vector3 toTarget = _targetPosition - state.Transform.Origin;
        state.LinearVelocity = (toTarget * s.LinearStiffness * massFactor).LimitLength(s.MaxLinearSpeed);

        Quaternion current = state.Transform.Basis.GetRotationQuaternion();
        Quaternion error = (_targetRotation * current.Inverse()).Normalized();
        if (error.W < 0)
            error = new Quaternion(-error.X, -error.Y, -error.Z, -error.W);

        float angle = error.GetAngle();
        state.AngularVelocity = angle > 0.0001f
            ? (error.GetAxis() * angle * s.AngularStiffness * massFactor).LimitLength(s.MaxAngularSpeed)
            : Vector3.Zero;
    }

    // --- Sunucuya gelen istekler (RPC) ---

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerGrab()
    {
        if (!Multiplayer.IsServer()) return;

        long sender = SenderId();
        if (HolderId != 0 && HolderId != sender) return; // Başkası tutuyor

        HolderId = sender;
        _hasTarget = false;
        NetFrozen = false;
        Freeze = false;
        CanSleep = false;
        Sleeping = false;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ServerSetTarget(Vector3 position, Quaternion rotation)
    {
        if (!Multiplayer.IsServer() || SenderId() != HolderId) return;

        _targetPosition = position;
        _targetRotation = rotation.Normalized();
        _hasTarget = true;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerRelease(bool freeze)
    {
        if (!Multiplayer.IsServer() || SenderId() != HolderId) return;

        ForceRelease();
        if (freeze)
        {
            LinearVelocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
            NetFrozen = true;
            Freeze = true;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerUnfreeze()
    {
        if (!Multiplayer.IsServer() || !NetFrozen || HolderId != 0) return;

        NetFrozen = false;
        Freeze = false;
        Sleeping = false;
    }

    /// <summary>Sunucuda objeyi tutan kişiden bağımsız olarak bırakır (ör. oyuncu oyundan çıkınca).</summary>
    public void ForceRelease()
    {
        HolderId = 0;
        _hasTarget = false;
        CanSleep = true;
    }

    private long SenderId()
    {
        int sender = Multiplayer.GetRemoteSenderId();
        return sender == 0 ? Multiplayer.GetUniqueId() : sender;
    }

    // --- Kurulum ve görüntü ---

    private void BuildShape(PropDefinition d)
    {
        Shape3D shape;
        Mesh mesh;

        switch (d.Shape)
        {
            case PropShape.Cylinder:
            {
                float radius = d.Size.X * 0.5f;
                shape = new CylinderShape3D { Radius = radius, Height = d.Size.Y };
                mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = d.Size.Y, RadialSegments = 16 };
                break;
            }
            case PropShape.Sphere:
            {
                float radius = d.Size.X * 0.5f;
                shape = new SphereShape3D { Radius = radius };
                mesh = new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 16, Rings = 8 };
                break;
            }
            default:
                shape = new BoxShape3D { Size = d.Size };
                mesh = new BoxMesh { Size = d.Size };
                break;
        }

        _material = new StandardMaterial3D { AlbedoColor = d.Color, Roughness = 0.8f };

        AddChild(new CollisionShape3D { Name = "Collision", Shape = shape });
        AddChild(new MeshInstance3D { Name = "Mesh", Mesh = mesh, MaterialOverride = _material });
    }

    private void BuildSynchronizer()
    {
        var config = new SceneReplicationConfig();
        foreach (var path in new[] { ".:NetPosition", ".:NetRotation", ".:HolderId", ".:NetFrozen", ".:DriverId" })
        {
            var nodePath = new NodePath(path);
            config.AddProperty(nodePath);
            config.PropertySetSpawn(nodePath, true); // Sonradan katılan oyuncu güncel durumu alsın
            config.PropertySetReplicationMode(nodePath, SceneReplicationConfig.ReplicationMode.OnChange);
        }

        AddChild(new MultiplayerSynchronizer
        {
            Name = "Sync",
            RootPath = new NodePath(".."),
            ReplicationConfig = config,
        });
    }

    // Tutulan obje parlak mavi, dondurulmuş obje hafif buz mavisi parlar (herkes görür).
    // Tool gun ile seçilen obje sadece seçen kişide sarı parlar.
    private void UpdateHighlight()
    {
        int state = _localSelected ? 3 : HolderId != 0 ? 2 : NetFrozen ? 1 : 0;
        if (state == _visualState) return;
        _visualState = state;

        _material.EmissionEnabled = state != 0;
        if (state == 3)
        {
            _material.Emission = new Color(1.0f, 0.8f, 0.2f);
            _material.EmissionEnergyMultiplier = 0.9f;
        }
        else if (state == 2)
        {
            _material.Emission = new Color(0.2f, 0.8f, 1.0f);
            _material.EmissionEnergyMultiplier = 0.8f;
        }
        else if (state == 1)
        {
            _material.Emission = new Color(0.5f, 0.7f, 1.0f);
            _material.EmissionEnergyMultiplier = 0.3f;
        }
    }
}
