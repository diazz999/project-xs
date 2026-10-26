using Godot;
using ProjectXs.Player;
using ProjectXs.Props;

namespace ProjectXs.Tools;

public enum ToolGunMode
{
    Weld,
    Axis,
    Wheel,
    Remover,
}

/// <summary>
/// GMod tool gun benzeri çok modlu araç. Fare tekerleğiyle mod değişir.
///
/// Kaynak (weld): iki objeyi birbirine kilitler. İlk objeye, sonra ikinci objeye tıkla; ikinci tıklama zemine
/// denk gelirse obje dünyaya sabitlenir.
/// Menteşe (axis): ilk tıkladığın yüzeyin dik ekseni etrafında dönen bir bağlantı kurar (kapı, pervane...).
/// Tekerlek: tıkladığın yüzeye motorlu bir tekerlek takar; aynı yapıya bağlı bir koltuktan sürülür.
/// Silici: tıkladığın objeyi siler.
///
/// Bağlantıların fiziğini sadece sunucu kurar; bu araç sadece isteği gönderir.
/// </summary>
public partial class ToolGun : Node, ITool
{
    private const float Range = 30.0f;

    private PlayerController _player;
    private Camera3D _camera;

    private ToolGunMode _mode = ToolGunMode.Weld;
    private Prop _first;
    private Vector3 _firstLocalPoint;
    private Vector3 _firstLocalNormal;
    private string _status = "";

    public bool Active { get; set; }

    public string Title => _mode switch
    {
        ToolGunMode.Weld => "Tool gun: Kaynak",
        ToolGunMode.Axis => "Tool gun: Menteşe",
        ToolGunMode.Wheel => "Tool gun: Motorlu tekerlek",
        _ => "Tool gun: Silici",
    };

    public string HelpText
    {
        get
        {
            string help = _mode switch
            {
                ToolGunMode.Weld => "Sol tık: ilk objeyi seç, sonra ikinci objeye ya da zemine tıkla",
                ToolGunMode.Axis => "Sol tık: döneceği yüzeye tıkla, sonra bağlanacağı objeye ya da zemine tıkla",
                ToolGunMode.Wheel => "Sol tık: objenin yan yüzüne tekerlek tak (alt kenara yakın tıkla)",
                _ => "Sol tık: objeyi sil",
            };
            help += "\nTekerlek: mod değiştir   R: seçimi iptal et";
            return string.IsNullOrEmpty(_status) ? help : $"{help}\n> {_status}";
        }
    }

    public void Init(PlayerController player, Camera3D camera)
    {
        _player = player;
        _camera = camera;
    }

    public void OnDeactivated() => ClearSelection();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Active || Input.MouseMode != Input.MouseModeEnum.Captured) return;

        if (@event is InputEventMouseButton { Pressed: true } button &&
            (button.ButtonIndex == MouseButton.WheelUp || button.ButtonIndex == MouseButton.WheelDown))
        {
            int count = System.Enum.GetValues<ToolGunMode>().Length;
            int step = button.ButtonIndex == MouseButton.WheelUp ? -1 : 1;
            _mode = (ToolGunMode)(((int)_mode + step + count) % count);
            ClearSelection();
            _status = "";
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("tool_primary"))
        {
            Primary();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("tool_reset"))
        {
            ClearSelection();
            _status = "Seçim iptal edildi.";
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Seçili obje bu arada silindiyse seçimi bırak
        if (_first != null && !IsInstanceValid(_first))
            _first = null;
    }

    private void Primary()
    {
        var manager = PropManager.Instance;
        if (manager == null) return;

        if (!ToolRaycast.Cast(_player, _camera, Range, out var hit))
        {
            _status = "Menzilde hiçbir şey yok.";
            return;
        }

        var prop = hit.Collider as Prop;

        if (_mode == ToolGunMode.Wheel)
        {
            if (prop == null)
            {
                _status = "Tekerlek bir objeye takılmalı.";
                return;
            }

            manager.RequestWheel(prop.Name, hit.Position, hit.Normal);
            _status = "Tekerlek takıldı.";
            return;
        }

        if (_mode == ToolGunMode.Remover)
        {
            if (prop == null)
            {
                _status = "Sadece objeler silinebilir.";
                return;
            }

            manager.RequestRemove(prop.Name);
            _status = "Silindi.";
            return;
        }

        // Kaynak ve menteşe iki tıklamalı: önce bir obje seç, sonra hedefe tıkla
        if (_first == null)
        {
            if (prop == null)
            {
                _status = "Önce bir obje seç.";
                return;
            }

            Select(prop, hit);
            return;
        }

        if (prop == _first)
        {
            ClearSelection();
            _status = "Seçim iptal edildi.";
            return;
        }

        string other = prop != null ? prop.Name.ToString() : "";

        if (_mode == ToolGunMode.Weld)
        {
            manager.RequestWeld(_first.Name, other);
            _status = prop != null ? "Kaynak yapıldı." : "Dünyaya kaynak yapıldı.";
        }
        else
        {
            Vector3 point = _first.GlobalTransform * _firstLocalPoint;
            Vector3 axis = (_first.GlobalBasis * _firstLocalNormal).Normalized();
            manager.RequestAxis(_first.Name, other, point, axis);
            _status = prop != null ? "Menteşe kuruldu." : "Dünyaya menteşe kuruldu.";
        }

        ClearSelection();
    }

    private void Select(Prop prop, RayHit hit)
    {
        _first = prop;
        _firstLocalPoint = prop.GlobalTransform.AffineInverse() * hit.Position;
        _firstLocalNormal = (prop.GlobalBasis.Inverse() * hit.Normal).Normalized();
        prop.LocalSelected = true;
        _status = "İlk obje seçildi. Şimdi bağlanacağı yere tıkla.";
    }

    private void ClearSelection()
    {
        if (_first != null && IsInstanceValid(_first))
            _first.LocalSelected = false;
        _first = null;
    }
}
