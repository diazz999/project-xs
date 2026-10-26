using Godot;
using ProjectXs.Combat;
using ProjectXs.Props;
using ProjectXs.Tools;

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
    private const float UseRange = 3.5f;          // Koltuğa binmek için en fazla bu kadar uzakta olmalısın
    private const float SeatHeightOffset = -0.3f; // Otururken ayakların koltuğun merkezine göre yüksekliği
    private const float SeatConfirmTimeout = 1.0f;
    private const float MaxHealth = 100.0f;
    private const float RespawnDelay = 3.0f;
    private const float KnockbackAirTime = 0.15f; // Patlama sonrası bu süre yere yapışma yok, roket zıplaması için

    /// <summary>Menüde yazılan isim; yerel oyuncu doğunca kendi ismi olarak kullanır.</summary>
    public static string LocalName { get; set; } = "";

    public long PeerId { get; private set; }
    public MovementSettings Settings { get; set; }

    /// <summary>
    /// Oturulan koltuğun adı; boşsa oyuncu yürüyor. Ağ üzerinden herkese gider, böylece her bilgisayarda
    /// oturan oyuncunun çarpışması kapanır ve aracı itip bozmaz.
    /// </summary>
    [Export]
    public string SeatedIn
    {
        get => _seatedIn;
        set
        {
            _seatedIn = value ?? "";
            _collision?.SetDeferred(CollisionShape3D.PropertyName.Disabled, IsSeated);
        }
    }

    public bool IsSeated => !string.IsNullOrEmpty(_seatedIn);

    // Can, ölüm ve isim oyuncunun kendi bilgisayarında belirlenir ve senkronizasyonla herkese gider.
    [Export] public float Health { get; set; } = MaxHealth;

    [Export]
    public bool Dead
    {
        get => _dead;
        set
        {
            _dead = value;
            ApplyDeadVisuals();
        }
    }

    [Export]
    public string PlayerName
    {
        get => _playerName;
        set
        {
            _playerName = value ?? "";
            if (_nameTag != null)
                _nameTag.Text = _playerName;
        }
    }

    private Node3D _head;
    private Camera3D _camera;
    private CollisionShape3D _collision;

    private string _seatedIn = "";
    private Prop _seat;
    private Prop _pendingSeat;
    private float _pendingTimer;
    private float _seatYawOffset; // Otururken bakış yönün koltuğa göre

    private bool _dead;
    private string _playerName = "";
    private float _respawnTimer;
    private float _knockbackTimer;
    private Label3D _nameTag;
    private Label _crosshair;
    private Label _healthLabel;
    private Label _deathLabel;
    private ColorRect _damageFlash;
    private MeshInstance3D _bodyMesh;
    private MeshInstance3D _visor;
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

        // Sadece KENDİ gövdeni özel katmana taşıyıp kendi kamerandan gizliyoruz.
        // Diğer oyuncuların gövdeleri varsayılan katmanda kalır, böylece onları görürsün.
        _bodyMesh.Layers = OwnBodyLayer;
        _visor.Layers = OwnBodyLayer;
        _nameTag.Layers = OwnBodyLayer;

        PlayerName = string.IsNullOrWhiteSpace(LocalName) ? $"Oyuncu {PeerId % 1000}" : LocalName;

        _camera.Current = true;
        _camera.CullMask &= ~OwnBodyLayer;
        var hud = BuildHud();
        BuildTools(hud);
        // Fare otomatik kilitlenmez; oyun penceresine tıklayınca kilitlenir.
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority()) return;

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;

        if (@event is InputEventMouseMotion motion && captured)
        {
            float sensitivity = Settings.MouseSensitivity;
            if (IsSeated)
                _seatYawOffset -= motion.Relative.X * sensitivity; // Araç dönünce bakışın da onunla döner
            else
                RotateY(-motion.Relative.X * sensitivity);
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sensitivity, -1.55f, 1.55f);
            _head.Rotation = new Vector3(_pitch, 0, 0);
        }
        else if (@event.IsActionPressed("use") && captured && !Dead)
        {
            if (IsSeated)
                ExitSeat();
            else
                TryEnterSeat();
        }
        // Esc artık menüyü açıyor (PauseMenu); oyun ekranına tıklamak fareyi tekrar kilitler
        else if (@event is InputEventMouseButton { Pressed: true } && !captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsMultiplayerAuthority()) return;
        if (UpdateDeath((float)delta)) return; // Ölüyken kontrol yok, sadece düşüyoruz
        if (UpdateSeat((float)delta)) return;  // Otururken yürüme yok, koltuk bizi taşıyor

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

        // Patlamayla havaya fırlatıldıysak kısa bir süre "yerdeyiz" saymıyoruz, yoksa sürtünme hızı sıfırlar
        if (IsOnFloor() && _knockbackTimer <= 0)
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
        _healthLabel.Text = $"Can: {Mathf.CeilToInt(Health)}";
        _deathLabel.Visible = Dead;
        if (Dead)
            _deathLabel.Text = $"Öldün\n{Mathf.CeilToInt(Mathf.Max(_respawnTimer, 0))} saniye sonra yeniden doğacaksın";
    }

    // --- Can ve ölüm ---

    /// <summary>
    /// Sunucu, bu oyuncuya hasar geldiğinde bunu sadece oyuncunun kendi bilgisayarında çağırır.
    /// knockback, patlamalardan gelen fırlatma hızıdır.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveDamage(float damage, long attacker, Vector3 knockback)
    {
        // Sadece sunucu hasar verebilir (sunucu kendi oyuncusuna yerel çağrı yaptığında gönderen 0 görünebilir)
        int sender = Multiplayer.GetRemoteSenderId();
        bool fromServer = sender == 1 || (sender == 0 && Multiplayer.IsServer());
        if (!IsMultiplayerAuthority() || !fromServer) return;
        if (Dead) return;

        if (!IsSeated && knockback != Vector3.Zero)
        {
            Velocity += knockback;
            _knockbackTimer = KnockbackAirTime;
        }

        if (damage <= 0) return;

        Health = Mathf.Max(Health - damage, 0);
        FlashDamage();

        if (Health <= 0)
            Die(attacker);
    }

    /// <summary>Silah birine isabet ettiğinde nişangahı kısa bir süre kırmızı yakar.</summary>
    public void ShowHitMarker()
    {
        if (_crosshair == null) return;

        _crosshair.Modulate = new Color(1, 0.2f, 0.2f);
        var tween = _crosshair.CreateTween();
        tween.TweenProperty(_crosshair, "modulate", Colors.White, 0.2f);
    }

    private void Die(long attacker)
    {
        if (IsSeated)
            ExitSeat();

        Health = 0;
        Dead = true;
        _respawnTimer = RespawnDelay;
        CombatManager.Instance?.ReportDeath(attacker);
    }

    /// <summary>Ölüysek cesedi yerçekimiyle düşürür ve süre dolunca yeniden doğurur; ölüysek true döner.</summary>
    private bool UpdateDeath(float dt)
    {
        _knockbackTimer -= dt;
        if (!Dead) return false;

        Vector3 velocity = Velocity;
        velocity.Y -= Settings.Gravity * dt;
        if (IsOnFloor())
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0, 10 * dt);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0, 10 * dt);
        }
        Velocity = velocity;
        MoveAndSlide();

        _respawnTimer -= dt;
        if (_respawnTimer <= 0 || GlobalPosition.Y < FallLimit)
            Respawn();

        return true;
    }

    private void FlashDamage()
    {
        if (_damageFlash == null) return;

        _damageFlash.Color = new Color(1, 0, 0, 0.35f);
        var tween = _damageFlash.CreateTween();
        tween.TweenProperty(_damageFlash, "color:a", 0.0f, 0.4f);
    }

    // Ölü oyuncunun kapsülü yere yatar; vizör ve isim etiketi gizlenir
    private void ApplyDeadVisuals()
    {
        if (_bodyMesh == null) return;

        _bodyMesh.RotationDegrees = _dead ? new Vector3(90, 0, 0) : Vector3.Zero;
        _bodyMesh.Position = new Vector3(0, _dead ? BodyRadius : BodyHeight / 2, 0);
        if (_visor != null)
            _visor.Visible = !_dead;
        if (_nameTag != null)
            _nameTag.Visible = !_dead;
    }

    // --- Koltuk ve sürüş ---

    private void TryEnterSeat()
    {
        if (_pendingSeat != null || PropManager.Instance == null) return;
        if (!ToolRaycast.Cast(this, _camera, UseRange, out var hit)) return;
        if (hit.Collider is not Prop { TypeId: "seat" } seat || seat.DriverId != 0) return;

        // Sunucu onaylayınca (koltuğun DriverId'si bizim id'miz olunca) oturacağız
        _pendingSeat = seat;
        _pendingTimer = 0;
        PropManager.Instance.RequestEnterSeat(seat.Name);
    }

    /// <summary>Oturuyorsak koltuğu takip eder ve sürüş girdisini gönderir; oturuyorsak true döner.</summary>
    private bool UpdateSeat(float dt)
    {
        long myId = Multiplayer.GetUniqueId();

        if (_pendingSeat != null)
        {
            _pendingTimer += dt;
            if (!IsInstanceValid(_pendingSeat) || _pendingTimer > SeatConfirmTimeout)
                _pendingSeat = null; // Sunucu reddetti ya da koltuk silindi
            else if (_pendingSeat.DriverId == myId)
            {
                EnterSeat(_pendingSeat);
                _pendingSeat = null;
            }
        }

        if (!IsSeated) return false;

        if (_seat == null || !IsInstanceValid(_seat) || _seat.IsQueuedForDeletion() || _seat.DriverId != myId)
        {
            ExitSeat(); // Koltuk silindi ya da sunucu bizi indirdi
            return false;
        }

        var seatTransform = _seat.GlobalTransform;
        GlobalPosition = seatTransform.Origin + seatTransform.Basis.Y.Normalized() * SeatHeightOffset;
        Rotation = new Vector3(0, YawOf(seatTransform.Basis) + _seatYawOffset, 0);
        Velocity = Vector3.Zero;

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        float throttle = captured ? Input.GetAxis("move_back", "move_forward") : 0;
        float steer = captured ? Input.GetAxis("move_left", "move_right") : 0;
        bool boost = captured && Input.IsActionPressed("sprint");
        PropManager.Instance?.SendDriveInput(_seat.Name, throttle, steer, boost);

        return true;
    }

    private void EnterSeat(Prop seat)
    {
        _seat = seat;
        _seatYawOffset = Rotation.Y - YawOf(seat.GlobalBasis); // Bindiğin anda baktığın yöne bakmaya devam et
        Velocity = Vector3.Zero;
        SeatedIn = seat.Name;
    }

    private void ExitSeat()
    {
        var seat = _seat;
        string seatName = _seatedIn;

        _seat = null;
        SeatedIn = "";
        Velocity = Vector3.Zero;
        Rotation = new Vector3(0, Rotation.Y, 0);

        if (seat != null && IsInstanceValid(seat))
        {
            PropManager.Instance?.RequestExitSeat(seatName);
            // Koltuğun sağ yanına in
            GlobalPosition = seat.GlobalPosition + seat.GlobalBasis.X.Normalized() * 1.5f + Vector3.Up * 0.5f;
        }
    }

    // Bir dönüşün yatay düzlemdeki yönü (Godot'da ileri = -Z)
    private static float YawOf(Basis basis)
    {
        Vector3 forward = -basis.Z;
        return Mathf.Atan2(-forward.X, -forward.Z);
    }

    public void Respawn()
    {
        Position = SpawnPointFor(PeerId);
        Velocity = Vector3.Zero;
        Health = MaxHealth;
        Dead = false;
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
        _collision = new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = BodyRadius, Height = BodyHeight },
            Position = new Vector3(0, BodyHeight / 2, 0),
        };
        AddChild(_collision);

        float hue = (PeerId % 1000) * 0.618034f % 1.0f;
        var bodyMaterial = new StandardMaterial3D { AlbedoColor = Color.FromHsv(hue, 0.6f, 0.9f) };

        _bodyMesh = new MeshInstance3D
        {
            Name = "BodyMesh",
            Mesh = new CapsuleMesh { Radius = BodyRadius, Height = BodyHeight },
            Position = new Vector3(0, BodyHeight / 2, 0),
            MaterialOverride = bodyMaterial,
        };
        AddChild(_bodyMesh);

        _head = new Node3D { Name = "Head", Position = new Vector3(0, Settings.EyeHeight, 0) };
        AddChild(_head);

        _camera = new Camera3D { Name = "Camera", Fov = Settings.FieldOfView, Near = 0.05f };
        _head.AddChild(_camera);

        // Başkalarının nereye baktığını görmek için "vizör"
        _visor = new MeshInstance3D
        {
            Name = "Visor",
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.15f, 0.2f) },
            Position = new Vector3(0, 0, -0.35f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.12f) },
        };
        _head.AddChild(_visor);

        // Herkesin başının üstünde ismi; her zaman kameraya döner
        _nameTag = new Label3D
        {
            Name = "NameTag",
            Text = _playerName,
            Position = new Vector3(0, BodyHeight + 0.35f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40,
            OutlineSize = 10,
            PixelSize = 0.004f,
        };
        AddChild(_nameTag);
    }

    private void BuildSynchronizer()
    {
        var config = new SceneReplicationConfig();
        foreach (var path in new[] { ".:position", ".:rotation", "Head:rotation", ".:SeatedIn", ".:Health", ".:Dead", ".:PlayerName" })
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

    // Araçlar sadece yerel oyuncuda oluşur. Oyuncunun alt node'ları olarak girdileri oyuncudan önce alırlar,
    // böylece örneğin gravity gun ile döndürürken fare kamerayı çevirmez.
    private void BuildTools(CanvasLayer hud)
    {
        var toolBelt = new ToolBelt { Name = "Tools" };
        toolBelt.Init(this, _camera, hud);
        AddChild(toolBelt);
    }

    private CanvasLayer BuildHud()
    {
        var hud = new CanvasLayer { Name = "Hud" };
        AddChild(hud);

        _speedLabel = new Label { Position = new Vector2(16, 16) };
        hud.AddChild(_speedLabel);

        _crosshair = new Label
        {
            Text = "+",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hud.AddChild(_crosshair);

        // Sol altta can
        _healthLabel = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _healthLabel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _healthLabel.OffsetLeft = 16;
        _healthLabel.OffsetTop = -52;
        _healthLabel.OffsetBottom = -16;
        _healthLabel.GrowVertical = Control.GrowDirection.Begin;
        _healthLabel.AddThemeFontSizeOverride("font_size", 28);
        _healthLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _healthLabel.AddThemeConstantOverride("outline_size", 6);
        hud.AddChild(_healthLabel);

        // Hasar alınca ekran kısaca kırmızıya döner
        _damageFlash = new ColorRect { Color = new Color(1, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _damageFlash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hud.AddChild(_damageFlash);

        _deathLabel = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _deathLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _deathLabel.AddThemeFontSizeOverride("font_size", 32);
        _deathLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _deathLabel.AddThemeConstantOverride("outline_size", 8);
        hud.AddChild(_deathLabel);

        return hud;
    }
}
