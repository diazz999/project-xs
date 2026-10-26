using Godot;
using ProjectXs.Core;
using ProjectXs.Player;
using ProjectXs.Props;

namespace ProjectXs.Tools;

/// <summary>
/// GMod physgun benzeri araç. Sadece yerel oyuncuda çalışır; ne yapmak istediğini sunucuya RPC ile söyler,
/// fiziği sunucu uygular.
///
/// Kontroller: Sol tık basılı = tut, bırak = at (hızını korur), tutarken sağ tık = havada dondur,
/// R = baktığın donmuş objeyi çöz, tekerlek = yaklaştır/uzaklaştır, E + fare = döndür.
/// </summary>
public partial class GravityGun : Node, ITool
{
    private const string SettingsPath = "res://data/gravity_gun_default.tres";
    private const float GrabConfirmTimeout = 0.5f; // Sunucu bu sürede onaylamazsa (başkası tutuyorsa) vazgeç

    private PlayerController _player;
    private Camera3D _camera;
    private GravityGunSettings _settings;

    private Prop _target;
    private Vector3 _localGrabPoint;      // Objenin tutulduğu nokta, objenin kendi koordinatlarında
    private Quaternion _relativeRotation; // Objenin oyuncunun bakış yönüne göre dönüşü
    private float _distance;
    private float _grabTimer;

    private MeshInstance3D _beam;

    public bool IsHolding => _target != null;

    public bool Active { get; set; } = true;
    public string Title => "Gravity gun";
    public string HelpText =>
        "Sol tık: tut / bırak   Tutarken sağ tık: dondur   R: baktığın donmuş objeyi çöz\n" +
        "E + fare: döndür   Tekerlek: yaklaştır / uzaklaştır";

    public void OnDeactivated()
    {
        if (_target != null)
            Release(false);
    }

    public void Init(PlayerController player, Camera3D camera)
    {
        _player = player;
        _camera = camera;
    }

    public override void _Ready()
    {
        _settings = SettingsLoader.Load<GravityGunSettings>(SettingsPath);
        BuildBeam();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Active || _target == null || Input.MouseMode != Input.MouseModeEnum.Captured) return;

        if (@event is InputEventMouseMotion motion && Input.IsActionPressed("physgun_rotate"))
        {
            // Döndürürken fare kamerayı çevirmez; olayı burada tüketiyoruz.
            float s = _settings.RotateSensitivity;
            var yaw = new Quaternion(Vector3.Up, motion.Relative.X * s);
            var pitch = new Quaternion(Vector3.Right, motion.Relative.Y * s);
            _relativeRotation = (yaw * pitch * _relativeRotation).Normalized();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
            {
                _distance = Mathf.Min(_distance + _settings.ScrollStep, _settings.MaxDistance);
                GetViewport().SetInputAsHandled();
            }
            else if (button.ButtonIndex == MouseButton.WheelDown)
            {
                _distance = Mathf.Max(_distance - _settings.ScrollStep, _settings.MinDistance);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active) return;

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;

        if (_target != null && !IsInstanceValid(_target))
            ClearLocal();

        if (_target == null)
        {
            if (!captured) return;

            if (Input.IsActionJustPressed("physgun_grab"))
                TryGrab();
            else if (Input.IsActionJustPressed("physgun_unfreeze"))
                TryUnfreeze();
            return;
        }

        _grabTimer += (float)delta;
        bool confirmed = _target.HolderId == Multiplayer.GetUniqueId();

        if (!confirmed && _grabTimer > GrabConfirmTimeout)
            ClearLocal(); // Sunucu reddetti ya da obje başkasına geçti
        else if (!captured)
            Release(false);
        else if (Input.IsActionJustPressed("physgun_freeze"))
            Release(true);
        else if (!Input.IsActionPressed("physgun_grab"))
            Release(false);
        else
            SendTarget();
    }

    public override void _Process(double delta)
    {
        UpdateBeam();
    }

    private void TryGrab()
    {
        if (!ToolRaycast.Cast(_player, _camera, _settings.Range, out var hit)) return;
        if (hit.Collider is not Prop prop || prop.HolderId != 0) return;

        _target = prop;
        _distance = Mathf.Clamp(_camera.GlobalPosition.DistanceTo(hit.Position), _settings.MinDistance, _settings.MaxDistance);
        _localGrabPoint = prop.GlobalTransform.AffineInverse() * hit.Position;
        _relativeRotation = (YawRotation().Inverse() * prop.GlobalBasis.GetRotationQuaternion()).Normalized();
        _grabTimer = 0;

        prop.RpcId(1, Prop.MethodName.ServerGrab);
    }

    private void TryUnfreeze()
    {
        if (!ToolRaycast.Cast(_player, _camera, _settings.Range, out var hit)) return;
        if (hit.Collider is Prop { NetFrozen: true } prop)
            prop.RpcId(1, Prop.MethodName.ServerUnfreeze);
    }

    // Tutma noktası kameranın önünde _distance kadar uzakta durmalı; objenin merkezini buna göre hesaplıyoruz.
    private void SendTarget()
    {
        var cam = _camera.GlobalTransform;
        Vector3 grabPoint = cam.Origin - cam.Basis.Z * _distance;
        Quaternion desiredRotation = (YawRotation() * _relativeRotation).Normalized();
        Vector3 desiredCenter = grabPoint - desiredRotation * _localGrabPoint;

        _target.RpcId(1, Prop.MethodName.ServerSetTarget, desiredCenter, desiredRotation);
    }

    private void Release(bool freeze)
    {
        if (_target != null && IsInstanceValid(_target))
            _target.RpcId(1, Prop.MethodName.ServerRelease, freeze);
        ClearLocal();
    }

    private void ClearLocal()
    {
        _target = null;
        _beam.Visible = false;
    }

    private Quaternion YawRotation() => new(Vector3.Up, _player.GlobalRotation.Y);

    // --- Işın görüntüsü ---

    private void BuildBeam()
    {
        _beam = new MeshInstance3D
        {
            Name = "Beam",
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 1.0f, RadialSegments = 6, Rings = 1 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.3f, 0.85f, 1.0f),
            },
        };
        AddChild(_beam);
    }

    private void UpdateBeam()
    {
        if (_target == null || !IsInstanceValid(_target))
        {
            _beam.Visible = false;
            return;
        }

        var cam = _camera.GlobalTransform;
        Vector3 start = cam.Origin + cam.Basis.X * 0.18f - cam.Basis.Y * 0.15f - cam.Basis.Z * 0.3f;
        Vector3 end = _target.GlobalTransform * _localGrabPoint;
        Vector3 direction = end - start;
        float length = direction.Length();

        if (length < 0.01f)
        {
            _beam.Visible = false;
            return;
        }

        // Boyu 1 metre olan silindiri iki nokta arasına yerleştirip uzatıyoruz.
        var rotation = new Quaternion(Vector3.Up, direction / length);
        var basis = new Basis(rotation) * Basis.FromScale(new Vector3(1, length, 1));
        _beam.GlobalTransform = new Transform3D(basis, start + direction * 0.5f);
        _beam.Visible = true;
    }
}
