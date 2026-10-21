using Godot;

namespace ProjectXs.Player;

/// <summary>
/// Source/Quake tarzı birinci şahıs hareket: yer sürtünmesi, hızlanma, air strafe ve bunny hop.
/// Her oyuncu kendi karakterinin "sahibi"dir (multiplayer authority): kendi girdisini işler,
/// konumunu MultiplayerSynchronizer ile diğerlerine yayınlar. Başkalarının karakterleri sadece görüntüdür.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    private const string SettingsPath = "res://data/movement_default.tres";
    private const uint OwnBodyLayer = 1u << 1; // Kendi gövdeni kendi kameranda gizlemek için
    private const float BodyRadius = 0.4f;
    private const float BodyHeight = 1.8f;
    private const float FallLimit = -50.0f;

    public long PeerId { get; private set; }
    public MovementSettings Settings { get; set; }

    private Node3D _head;
    private Camera3D _camera;
    private Label _speedLabel;
    private float _pitch;

    /// <summary>Oyuncu sahneye eklenmeden önce çağrılır; tüm alt node'ları kurar.</summary>
    public void Setup(long peerId)
    {
        PeerId = peerId;
        Settings ??= LoadSettings();
        Position = SpawnPointFor(peerId);

        BuildBody();
        BuildSynchronizer();
    }

    public override void _Ready()
    {
        if (!IsMultiplayerAuthority()) return;

        _camera.Current = true;
        _camera.CullMask &= ~OwnBodyLayer;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        BuildHud();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority()) return;

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;

        if (@event is InputEventMouseMotion motion && captured)
        {
            float sensitivity = Settings.MouseSensitivity;
            RotateY(-motion.Relative.X * sensitivity);
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sensitivity, -1.55f, 1.55f);
            _head.Rotation = new Vector3(_pitch, 0, 0);
        }
        else if (@event.IsActionPressed("toggle_mouse"))
        {
            Input.MouseMode = captured ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        }
        else if (@event is InputEventMouseButton { Pressed: true } && !captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsMultiplayerAuthority()) return;

        float dt = (float)delta;
        var s = Settings;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;

        Vector2 input = captured
            ? Input.GetVector("move_left", "move_right", "move_forward", "move_back")
            : Vector2.Zero;

        Vector3 wishDir = GlobalTransform.Basis * new Vector3(input.X, 0, input.Y);
        wishDir.Y = 0;
        if (wishDir.LengthSquared() > 0.0001f)
            wishDir = wishDir.Normalized();

        float maxSpeed = captured && Input.IsActionPressed("sprint") ? s.SprintSpeed : s.WalkSpeed;
        float wishSpeed = maxSpeed * Mathf.Min(input.Length(), 1.0f);

        bool wantsJump = captured && (s.AutoBunnyHop
            ? Input.IsActionPressed("jump")
            : Input.IsActionJustPressed("jump"));

        Vector3 velocity = Velocity;

        if (IsOnFloor())
        {
            if (wantsJump)
            {
                // Zıplama karesinde sürtünme uygulanmaz; bunny hop hızı buradan korunur.
                velocity.Y = s.JumpVelocity;
                velocity = AirAccelerate(velocity, wishDir, wishSpeed, dt);
            }
            else
            {
                velocity.Y = 0;
                velocity = ApplyFriction(velocity, dt);
                velocity = Accelerate(velocity, wishDir, wishSpeed, s.GroundAcceleration, dt);
            }
        }
        else
        {
            velocity.Y -= s.Gravity * dt;
            velocity = AirAccelerate(velocity, wishDir, wishSpeed, dt);
        }

        Velocity = velocity;
        MoveAndSlide();

        if (GlobalPosition.Y < FallLimit)
            Respawn();
    }

    public override void _Process(double delta)
    {
        if (_speedLabel == null) return;

        float horizontalSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
        _speedLabel.Text = $"Hız: {horizontalSpeed:0.0} m/s";
    }

    public void Respawn()
    {
        Position = SpawnPointFor(PeerId);
        Velocity = Vector3.Zero;
    }

    public static Vector3 SpawnPointFor(long peerId)
    {
        int slot = (int)(peerId % 8);
        return new Vector3(slot * 2.0f - 7.0f, 1.0f, 6.0f);
    }

    // --- Hareket matematiği (Quake/Source ile aynı mantık) ---

    private Vector3 ApplyFriction(Vector3 velocity, float dt)
    {
        var horizontal = new Vector3(velocity.X, 0, velocity.Z);
        float speed = horizontal.Length();
        if (speed < 0.01f)
            return new Vector3(0, velocity.Y, 0);

        float control = Mathf.Max(speed, Settings.StopSpeed);
        float newSpeed = Mathf.Max(speed - control * Settings.Friction * dt, 0);
        horizontal *= newSpeed / speed;

        return new Vector3(horizontal.X, velocity.Y, horizontal.Z);
    }

    private static Vector3 Accelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float acceleration, float dt)
    {
        if (wishDir == Vector3.Zero) return velocity;

        float currentSpeed = velocity.Dot(wishDir);
        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0) return velocity;

        float accelSpeed = Mathf.Min(acceleration * wishSpeed * dt, addSpeed);
        return velocity + wishDir * accelSpeed;
    }

    // Air strafe'in sırrı: eklenebilecek hız AirSpeedCap ile sınırlı ama ivme tam wishSpeed üzerinden hesaplanır.
    // Fareyi strafe yönüne çevirdikçe her karede hız yönüne dik küçük eklemeler birikir ve toplam hız artar.
    private Vector3 AirAccelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float dt)
    {
        if (wishDir == Vector3.Zero) return velocity;

        float cappedSpeed = Mathf.Min(wishSpeed, Settings.AirSpeedCap);
        float currentSpeed = velocity.Dot(wishDir);
        float addSpeed = cappedSpeed - currentSpeed;
        if (addSpeed <= 0) return velocity;

        float accelSpeed = Mathf.Min(Settings.AirAcceleration * wishSpeed * dt, addSpeed);
        return velocity + wishDir * accelSpeed;
    }

    // --- Kurulum ---

    private static MovementSettings LoadSettings()
    {
        if (ResourceLoader.Exists(SettingsPath) && GD.Load<MovementSettings>(SettingsPath) is { } loaded)
            return loaded;

        GD.PushWarning($"{SettingsPath} yüklenemedi, varsayılan hareket ayarları kullanılıyor.");
        return new MovementSettings();
    }

    private void BuildBody()
    {
        AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = BodyRadius, Height = BodyHeight },
            Position = new Vector3(0, BodyHeight / 2, 0),
        });

        float hue = (PeerId % 1000) * 0.618034f % 1.0f;
        var bodyMaterial = new StandardMaterial3D { AlbedoColor = Color.FromHsv(hue, 0.6f, 0.9f) };

        AddChild(new MeshInstance3D
        {
            Name = "BodyMesh",
            Mesh = new CapsuleMesh { Radius = BodyRadius, Height = BodyHeight },
            Position = new Vector3(0, BodyHeight / 2, 0),
            MaterialOverride = bodyMaterial,
            Layers = OwnBodyLayer,
        });

        _head = new Node3D { Name = "Head", Position = new Vector3(0, Settings.EyeHeight, 0) };
        AddChild(_head);

        _camera = new Camera3D { Name = "Camera", Fov = Settings.FieldOfView, Near = 0.05f };
        _head.AddChild(_camera);

        // Başkalarının nereye baktığını görmek için "vizör"
        _head.AddChild(new MeshInstance3D
        {
            Name = "Visor",
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.15f, 0.2f) },
            Position = new Vector3(0, 0, -0.35f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.12f) },
            Layers = OwnBodyLayer,
        });
    }

    private void BuildSynchronizer()
    {
        var config = new SceneReplicationConfig();
        foreach (var path in new[] { ".:position", ".:rotation", "Head:rotation" })
        {
            var nodePath = new NodePath(path);
            config.AddProperty(nodePath);
            config.PropertySetSpawn(nodePath, false);
            config.PropertySetReplicationMode(nodePath, SceneReplicationConfig.ReplicationMode.Always);
        }

        AddChild(new MultiplayerSynchronizer
        {
            Name = "Sync",
            RootPath = new NodePath(".."),
            ReplicationConfig = config,
        });
    }

    private void BuildHud()
    {
        var hud = new CanvasLayer { Name = "Hud" };
        AddChild(hud);

        _speedLabel = new Label { Position = new Vector2(16, 16) };
        hud.AddChild(_speedLabel);

        var crosshair = new Label
        {
            Text = "+",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        crosshair.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hud.AddChild(crosshair);
    }
}
