using System.Collections.Generic;
using Godot;
using ProjectXs.Core;
using ProjectXs.Save;
using ProjectXs.Vehicles;

namespace ProjectXs.Props;

/// <summary>
/// Dünyadaki bütün fizik objelerini ve aralarındaki bağlantıları (kaynak, menteşe) yönetir.
/// Objeleri sadece sunucu oluşturur ve siler; MultiplayerSpawner bunları herkese (sonradan katılanlar dahil) kopyalar.
/// Bağlantılar (joint) sadece sunucuda vardır, çünkü fiziği sadece sunucu hesaplar; istemciler sonucu görür.
/// Oyuncular isteklerini RPC ile sunucuya gönderir. Her oyuncunun bir geri alma (Z) geçmişi vardır.
/// </summary>
public partial class PropManager : Node3D
{
    public const int MaxProps = 300;
    private const long WorldOwner = 0;

    public static PropManager Instance { get; private set; }

    private Node3D _container;
    private Node3D _constraints;
    private MultiplayerSpawner _spawner;
    private int _nextId;
    private int _nextConstraintId;

    private const string VehicleSettingsPath = "res://data/vehicle_default.tres";
    private const ulong DriveInputTimeoutMs = 500; // Bu kadar süre girdi gelmezse araç fren yapar
    private VehicleSettings _vehicleSettings;
    private readonly Dictionary<string, DriveInput> _driveInputs = new(); // Dolu koltuk adı -> son girdi

    // Her oyuncunun geri alınabilir işlemleri: bu node'a göre yol, ör. "Container/Prop_5" veya "Constraints/Weld_2"
    private readonly Dictionary<long, List<string>> _history = new();

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Ready()
    {
        _container = new Node3D { Name = "Container" };
        AddChild(_container);

        _constraints = new Node3D { Name = "Constraints" };
        AddChild(_constraints);

        _vehicleSettings = SettingsLoader.Load<VehicleSettings>(VehicleSettingsPath);

        _spawner = new MultiplayerSpawner { Name = "PropSpawner" };
        AddChild(_spawner);
        _spawner.SpawnPath = _spawner.GetPathTo(_container);
        _spawner.SpawnFunction = Callable.From<Variant, Node>(CreateProp);

        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    // --- İstemcinin çağırdığı kısa yollar ---

    public void RequestSpawn(string typeId, Vector3 position, Quaternion rotation)
        => RpcId(1, MethodName.ServerSpawn, typeId, position, rotation);

    public void RequestUndo()
        => RpcId(1, MethodName.ServerUndo);

    public void RequestRemove(string propName)
        => RpcId(1, MethodName.ServerRemove, propName);

    /// <summary>propB boşsa obje dünyaya kaynak yapılır.</summary>
    public void RequestWeld(string propA, string propB)
        => RpcId(1, MethodName.ServerWeld, propA, propB);

    public Prop GetProp(string name) => _container.GetNodeOrNull<Prop>(name);

    public IEnumerable<Prop> AllProps()
    {
        foreach (Node child in _container.GetChildren())
            if (child is Prop prop && !prop.IsQueuedForDeletion())
                yield return prop;
    }

    /// <summary>Baktığın yüzeye motorlu bir tekerlek takar. point ve normal dünya koordinatlarında.</summary>
    public void RequestWheel(string propName, Vector3 point, Vector3 normal)
        => RpcId(1, MethodName.ServerAddWheel, propName, point, normal);

    public void RequestEnterSeat(string seatName)
        => RpcId(1, MethodName.ServerEnterSeat, seatName);

    public void RequestExitSeat(string seatName)
        => RpcId(1, MethodName.ServerExitSeat, seatName);

    public void SendDriveInput(string seatName, float throttle, float steer, bool boost)
        => RpcId(1, MethodName.ServerDriveInput, seatName, throttle, steer, boost);

    /// <summary>propB boşsa obje dünyaya menteşelenir. point ve axis dünya koordinatlarında.</summary>
    public void RequestAxis(string propA, string propB, Vector3 point, Vector3 axis)
        => RpcId(1, MethodName.ServerAxis, propA, propB, point, axis);

    // --- Sunucu tarafı ---

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerSpawn(string typeId, Vector3 position, Quaternion rotation)
    {
        if (!Multiplayer.IsServer()) return;
        if (!PropCatalog.TryGet(typeId, out _)) return;
        if (_container.GetChildCount() >= MaxProps) return;

        long sender = SenderId();
        var prop = SpawnProp(typeId, position, rotation, sender);
        if (prop != null)
            AddHistory(sender, $"Container/{prop.Name}");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerUndo()
    {
        if (!Multiplayer.IsServer()) return;
        if (!_history.TryGetValue(SenderId(), out var list)) return;

        // Listenin sonundan başlayıp hâlâ var olan ilk şeyi geri al (silinmiş ya da düşüp yok olmuş olabilir)
        while (list.Count > 0)
        {
            string path = list[^1];
            list.RemoveAt(list.Count - 1);

            var node = GetNodeOrNull(path);
            if (node == null || node.IsQueuedForDeletion()) continue;

            if (node is Prop prop)
                RemoveProp(prop);
            else
                RemoveConstraint(node);
            return;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerRemove(string propName)
    {
        if (!Multiplayer.IsServer()) return;

        var prop = _container.GetNodeOrNull<Prop>(propName);
        if (prop != null)
            RemoveProp(prop);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerWeld(string propA, string propB)
    {
        if (!Multiplayer.IsServer()) return;
        if (!TryResolvePair(propA, propB, out var a, out var b)) return;

        // Generic6DOF eklemi varsayılan olarak bütün eksenlerde kilitlidir; yani iki obje birbirine sabitlenir.
        // Ekleme anındaki göreceli konumları korunur, bu yüzden eklemin nerede durduğu çok önemli değil.
        Vector3 anchor = b != null ? (a.GlobalPosition + b.GlobalPosition) * 0.5f : a.GlobalPosition;
        AddConstraint(new Generic6DofJoint3D(), "Weld", new Transform3D(Basis.Identity, anchor), a, b);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerAxis(string propA, string propB, Vector3 point, Vector3 axis)
    {
        if (!Multiplayer.IsServer()) return;
        if (axis.LengthSquared() < 0.0001f) return;
        if (!TryResolvePair(propA, propB, out var a, out var b)) return;

        // HingeJoint3D kendi Z ekseni etrafında döner; eklemin Z eksenini istenen eksene çeviriyoruz.
        axis = axis.Normalized();
        Vector3 up = Mathf.Abs(axis.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        var basis = Basis.LookingAt(axis, up);
        AddConstraint(new HingeJoint3D(), "Axis", new Transform3D(basis, point), a, b);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerAddWheel(string propName, Vector3 point, Vector3 normal)
    {
        if (!Multiplayer.IsServer()) return;
        if (normal.LengthSquared() < 0.0001f) return;

        var body = GetProp(propName);
        if (body == null || body.IsQueuedForDeletion()) return;
        if (!PropCatalog.TryGet("wheel", out var wheelDef)) return;

        normal = normal.Normalized();

        // Tekerleği yüzeyin hemen dışına, dönme ekseni (kendi Y ekseni) yüzeye dik olacak şekilde koyuyoruz
        Vector3 center = point + normal * (wheelDef.Size.Y * 0.5f + 0.05f);
        Quaternion rotation = normal.Dot(Vector3.Up) < -0.999f
            ? new Quaternion(Vector3.Right, Mathf.Pi)
            : new Quaternion(Vector3.Up, normal);

        long sender = SenderId();
        var wheel = SpawnProp("wheel", center, rotation, sender);
        if (wheel == null) return;

        Vector3 up = Mathf.Abs(normal.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        var basis = Basis.LookingAt(normal, up);
        AddConstraint(new HingeJoint3D(), "Wheel", new Transform3D(basis, center), wheel, body,
            recordHistory: false, motor: true);

        // Geri alınca tekerlek ve menteşesi birlikte gider
        AddHistory(sender, $"Container/{wheel.Name}");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerEnterSeat(string seatName)
    {
        if (!Multiplayer.IsServer()) return;

        var seat = GetProp(seatName);
        if (seat == null || seat.TypeId != "seat" || seat.DriverId != 0) return;

        long sender = SenderId();
        foreach (Node child in _container.GetChildren())
        {
            if (child is Prop other && other.DriverId == sender)
            {
                other.DriverId = 0; // Aynı anda iki koltukta oturulamaz
                StopVehicle(other);
            }
        }

        seat.DriverId = sender;
        _driveInputs[seatName] = new DriveInput { ReceivedAtMs = Time.GetTicksMsec() };
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerExitSeat(string seatName)
    {
        if (!Multiplayer.IsServer()) return;

        var seat = GetProp(seatName);
        if (seat == null || seat.DriverId != SenderId()) return;

        seat.DriverId = 0;
        StopVehicle(seat);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ServerDriveInput(string seatName, float throttle, float steer, bool boost)
    {
        if (!Multiplayer.IsServer()) return;

        var seat = GetProp(seatName);
        if (seat == null || seat.DriverId != SenderId()) return;

        _driveInputs[seatName] = new DriveInput
        {
            Throttle = Mathf.Clamp(throttle, -1.0f, 1.0f),
            Steer = Mathf.Clamp(steer, -1.0f, 1.0f),
            Boost = boost,
            ReceivedAtMs = Time.GetTicksMsec(),
        };
    }

    // Sunucuda, dolu her koltuk için ona bağlı motorlu tekerlekleri sürer
    public override void _PhysicsProcess(double delta)
    {
        if (_driveInputs.Count == 0 || !Multiplayer.IsServer()) return;

        ulong now = Time.GetTicksMsec();
        List<string> stale = null;

        foreach (var (seatName, input) in _driveInputs)
        {
            var seat = GetProp(seatName);
            if (seat == null || seat.IsQueuedForDeletion() || seat.DriverId == 0)
            {
                (stale ??= new List<string>()).Add(seatName);
                continue;
            }

            var effective = now - input.ReceivedAtMs > DriveInputTimeoutMs ? default : input;
            var bodies = FindVehicle(seat, out var wheels);
            VehicleDriver.Drive(seat, wheels, bodies, effective, _vehicleSettings);
        }

        if (stale != null)
            foreach (var name in stale)
                _driveInputs.Remove(name);
    }

    /// <summary>Sürücü inince aracı bir kez "gaz yok" girdisiyle sürüp fren yaptırır ve tekerlek tutuşunu normale döndürür.</summary>
    private void StopVehicle(Prop seat)
    {
        _driveInputs.Remove(seat.Name);
        if (!IsInstanceValid(seat) || seat.IsQueuedForDeletion()) return;

        var bodies = FindVehicle(seat, out var wheels);
        VehicleDriver.Drive(seat, wheels, bodies, default, _vehicleSettings);
    }

    /// <summary>
    /// Koltuktan başlayıp kaynak ve menteşeler üzerinden ulaşılabilen bütün objeleri (aracın parçalarını) döndürür;
    /// aralarındaki motorlu tekerlekleri de ayrıca verir. Yani koltuğa bağlı her şey aynı araçtır.
    /// </summary>
    private List<Prop> FindVehicle(Prop seat, out List<Prop> wheels)
    {
        var neighbors = new Dictionary<string, List<string>>();
        var motorWheels = new HashSet<string>();

        foreach (Node constraint in _constraints.GetChildren())
        {
            if (constraint.IsQueuedForDeletion()) continue;

            string a = constraint.GetMeta("a", "").AsString();
            string b = constraint.GetMeta("b", "").AsString();
            if (constraint.GetMeta("motor", false).AsBool())
                motorWheels.Add(a);
            if (string.IsNullOrEmpty(b)) continue; // Dünyaya bağlantı araca dahil değil

            Link(neighbors, a, b);
            Link(neighbors, b, a);
        }

        var bodies = new List<Prop>();
        wheels = new List<Prop>();
        var visited = new HashSet<string> { seat.Name };
        var queue = new Queue<string>();
        queue.Enqueue(seat.Name);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (GetProp(current) is { } prop && !prop.IsQueuedForDeletion())
            {
                bodies.Add(prop);
                if (motorWheels.Contains(current))
                    wheels.Add(prop);
            }

            if (!neighbors.TryGetValue(current, out var list)) continue;
            foreach (string next in list)
                if (visited.Add(next))
                    queue.Enqueue(next);
        }

        return bodies;
    }

    private static void Link(Dictionary<string, List<string>> neighbors, string from, string to)
    {
        if (!neighbors.TryGetValue(from, out var list))
            neighbors[from] = list = new List<string>();
        list.Add(to);
    }

    /// <summary>Sunucuda bir objeyi ve ona bağlı bütün bağlantıları siler.</summary>
    public void RemoveProp(Prop prop)
    {
        if (!IsInstanceValid(prop) || prop.IsQueuedForDeletion()) return;

        string name = prop.Name;
        foreach (Node constraint in _constraints.GetChildren())
        {
            if (constraint.GetMeta("a", "").AsString() == name || constraint.GetMeta("b", "").AsString() == name)
                RemoveConstraint(constraint);
        }

        prop.QueueFree();
    }

    /// <summary>Sunucu açıldığında test için birkaç obje dizer.</summary>
    public void SpawnStarterProps()
    {
        if (!Multiplayer.IsServer() || _container.GetChildCount() > 0) return;

        // Sandık piramidi
        for (int row = 0; row < 3; row++)
        for (int i = 0; i < 3 - row; i++)
            SpawnProp("crate", new Vector3(-6 + i * 1.05f + row * 0.52f, 0.5f + row * 1.0f, -2), Quaternion.Identity, WorldOwner);

        for (int i = 0; i < 3; i++)
            SpawnProp("barrel", new Vector3(-1 + i * 0.9f, 0.55f, -3), Quaternion.Identity, WorldOwner);

        SpawnProp("plank", new Vector3(3, 0.06f, -2), Quaternion.Identity, WorldOwner);
        SpawnProp("plank", new Vector3(3.6f, 0.06f, -2), Quaternion.Identity, WorldOwner);
        SpawnProp("pipe", new Vector3(5, 0.15f, -3), new Quaternion(Vector3.Right, Mathf.Pi / 2), WorldOwner);

        for (int i = 0; i < 3; i++)
            SpawnProp("ball", new Vector3(-2 + i * 1.2f, 0.3f, 1), Quaternion.Identity, WorldOwner);

        SpawnProp("big_crate", new Vector3(8, 1, -6), Quaternion.Identity, WorldOwner);
        SpawnProp("slab", new Vector3(-10, 0.15f, 2), Quaternion.Identity, WorldOwner);
    }

    /// <summary>Oturum bitince bütün objeleri yerelde temizler.</summary>
    // --- Kaydetme ve yükleme ---

    /// <summary>Bütün objelerin ve bağlantıların şu anki halini kayıt için çıkarır.</summary>
    public (List<PropSave> Props, List<ConstraintSave> Constraints) ExportState()
    {
        var props = new List<PropSave>();
        foreach (var prop in AllProps())
        {
            props.Add(new PropSave
            {
                Id = prop.Name,
                Type = prop.TypeId,
                Position = SaveConvert.From(prop.GlobalPosition),
                Rotation = SaveConvert.From(prop.GlobalBasis.Orthonormalized().GetRotationQuaternion()),
                Frozen = prop.NetFrozen,
            });
        }

        var constraints = new List<ConstraintSave>();
        foreach (Node node in _constraints.GetChildren())
        {
            if (node is not Joint3D joint || joint.IsQueuedForDeletion()) continue;

            var a = GetProp(joint.GetMeta("a", "").AsString());
            if (a == null) continue;

            string b = joint.GetMeta("b", "").AsString();
            if (!string.IsNullOrEmpty(b) && GetProp(b) == null) continue;

            // Eski sürümde oluşturulmuş eklemlerde göreceli konum yok; o zaman eklemin kendi konumunu kullanıyoruz
            Transform3D world = joint.HasMeta("localA")
                ? a.GlobalTransform * joint.GetMeta("localA").AsTransform3D()
                : joint.GlobalTransform;

            string fallbackKind = joint.Name.ToString().Split('_')[0];
            constraints.Add(new ConstraintSave
            {
                Kind = joint.GetMeta("kind", fallbackKind).AsString(),
                A = a.Name,
                B = b,
                Transform = SaveConvert.From(world),
                Motor = joint.GetMeta("motor", false).AsBool(),
            });
        }

        return (props, constraints);
    }

    /// <summary>Sunucuda bütün objeleri silip kayıttakileri yeniden kurar; spawner herkese kopyalar.</summary>
    public void ServerRestore(List<PropSave> props, List<ConstraintSave> constraints)
    {
        if (!Multiplayer.IsServer()) return;

        // Koltuktaki oyuncular koltukları silinince kendiliğinden iner
        foreach (Node child in _constraints.GetChildren())
            child.QueueFree();
        foreach (Node child in _container.GetChildren())
            child.QueueFree();
        _history.Clear();
        _driveInputs.Clear();

        // Kayıttaki adlar ile yeni oluşan objeler arasındaki eşleme (yeni objeler yeni adlar alır)
        var restored = new Dictionary<string, Prop>();
        foreach (var save in props ?? new List<PropSave>())
        {
            var prop = SpawnProp(save.Type, SaveConvert.ToVector3(save.Position), SaveConvert.ToQuaternion(save.Rotation), 0);
            if (prop == null) continue;

            if (save.Frozen)
            {
                prop.NetFrozen = true;
                prop.Freeze = true;
            }
            restored[save.Id] = prop;
        }

        foreach (var save in constraints ?? new List<ConstraintSave>())
        {
            if (!restored.TryGetValue(save.A, out var a)) continue;

            Prop b = null;
            if (!string.IsNullOrEmpty(save.B) && !restored.TryGetValue(save.B, out b)) continue;

            Joint3D joint = save.Kind == "Weld" ? new Generic6DofJoint3D() : new HingeJoint3D();
            AddConstraint(joint, save.Kind, SaveConvert.ToTransform(save.Transform), a, b,
                recordHistory: false, motor: save.Motor);
        }
    }

    public void ClearAll()
    {
        foreach (Node child in _constraints.GetChildren())
            child.QueueFree();
        foreach (Node child in _container.GetChildren())
            child.QueueFree();
        _history.Clear();
        _driveInputs.Clear();
    }

    private Prop SpawnProp(string typeId, Vector3 position, Quaternion rotation, long ownerPeerId)
    {
        if (!PropCatalog.TryGet(typeId, out _)) return null;

        var data = new Godot.Collections.Array { typeId, position, rotation, ownerPeerId, _nextId++ };
        return _spawner.Spawn(data) as Prop;
    }

    // Hem sunucuda hem istemcilerde aynı veriyle çalışır.
    private Node CreateProp(Variant data)
    {
        var array = data.AsGodotArray();
        string typeId = array[0].AsString();
        var position = array[1].AsVector3();
        var rotation = array[2].AsQuaternion();
        long owner = array[3].AsInt64();
        int id = array[4].AsInt32();

        if (!PropCatalog.TryGet(typeId, out var definition))
        {
            GD.PushError($"Bilinmeyen obje türü: {typeId}");
            return new Node3D { Name = $"Prop_{id}" };
        }

        var prop = new Prop { Name = $"Prop_{id}" };
        prop.Setup(definition, position, rotation, owner);
        return prop;
    }

    private bool TryResolvePair(string nameA, string nameB, out Prop a, out Prop b)
    {
        a = _container.GetNodeOrNull<Prop>(nameA);
        b = string.IsNullOrEmpty(nameB) ? null : _container.GetNodeOrNull<Prop>(nameB);

        if (a == null || a.IsQueuedForDeletion()) return false;
        if (!string.IsNullOrEmpty(nameB) && (b == null || b.IsQueuedForDeletion())) return false; // İkinci obje bu arada silinmiş
        return a != b;
    }

    private void AddConstraint(Joint3D joint, string kind, Transform3D transform, Prop a, Prop b,
        bool recordHistory = true, bool motor = false)
    {
        joint.Name = $"{kind}_{++_nextConstraintId}";
        joint.SetMeta("a", a.Name.ToString());
        joint.SetMeta("b", b != null ? b.Name.ToString() : "");
        joint.SetMeta("motor", motor);
        joint.SetMeta("kind", kind);
        // Eklem node'u gövdelerle birlikte hareket etmez; kaydederken doğru yeri bulabilmek için
        // eklemin A gövdesine göre konumunu saklıyoruz
        joint.SetMeta("localA", a.GlobalTransform.AffineInverse() * transform);

        // Önce sahneye ekleyip konumlandırıyoruz, sonra gövdeleri bağlıyoruz;
        // eklem, gövdeler atandığı andaki konumuna göre kurulur.
        _constraints.AddChild(joint);
        joint.GlobalTransform = transform;
        joint.NodeA = joint.GetPathTo(a);
        if (b != null)
            joint.NodeB = joint.GetPathTo(b); // NodeB boş kalırsa obje dünyaya bağlanır

        a.Sleeping = false;
        if (b != null)
            b.Sleeping = false;

        if (recordHistory)
            AddHistory(SenderId(), $"Constraints/{joint.Name}");
    }

    private static void RemoveConstraint(Node constraint)
    {
        if (constraint.IsQueuedForDeletion()) return;

        // Bağlantı kalkınca uyuyan objeler kendiliğinden uyanmaz; serbest kalsınlar diye uyandırıyoruz.
        if (constraint is Joint3D joint)
        {
            foreach (var path in new[] { joint.NodeA, joint.NodeB })
            {
                if (!path.IsEmpty && joint.GetNodeOrNull(path) is RigidBody3D body)
                    body.Sleeping = false;
            }
        }

        constraint.QueueFree();
    }

    private void AddHistory(long peer, string path)
    {
        if (!_history.TryGetValue(peer, out var list))
            _history[peer] = list = new List<string>();
        list.Add(path);
    }

    private void OnPeerDisconnected(long id)
    {
        if (!Multiplayer.IsServer()) return;

        foreach (Node child in _container.GetChildren())
        {
            if (child is not Prop prop) continue;

            if (prop.HolderId == id)
                prop.ForceRelease();
            if (prop.DriverId == id)
            {
                prop.DriverId = 0;
                StopVehicle(prop);
            }
        }
    }

    private long SenderId()
    {
        int sender = Multiplayer.GetRemoteSenderId();
        return sender == 0 ? Multiplayer.GetUniqueId() : sender;
    }
}
