using System.Collections.Generic;
using Godot;
using ProjectXs.Combat;
using ProjectXs.Player;

namespace ProjectXs.Tools;

/// <summary>
/// Oyuncunun araç çubuğu. Rakam tuşları araç seçer: 1 gravity gun, 2 tool gun, 3 inşa, 4 ve sonrası silahlar
/// (WeaponCatalog sırasıyla). G ve T de gravity gun ve tool gun için kısa yol olarak çalışır.
/// Obje menüsü (Q) ve geri alma (Z) hangi araç seçili olursa olsun çalışır.
/// Yeni bir araç eklemek için ITool uygulayan bir sınıf yazıp Init içinde listeye eklemek yeterli.
/// </summary>
public partial class ToolBelt : Node
{
    private const int MaxSlots = 9;

    private readonly List<ITool> _tools = new();
    private readonly List<string> _slotNames = new();
    private int _activeIndex = -1;
    private Label _helpLabel;
    private PlayerController _player;
    private PropSpawnTool _spawnTool;

    public ITool ActiveTool => _activeIndex >= 0 ? _tools[_activeIndex] : null;

    public void Init(PlayerController player, Camera3D camera, CanvasLayer hud)
    {
        _player = player;

        var gravityGun = new GravityGun { Name = "GravityGun" };
        gravityGun.Init(player, camera);
        AddTool(gravityGun, "Gravity gun");

        var toolGun = new ToolGun { Name = "ToolGun" };
        toolGun.Init(player, camera);
        AddTool(toolGun, "Tool gun");

        var buildTool = new BuildTool { Name = "BuildTool" };
        buildTool.Init(player, camera);
        AddTool(buildTool, "İnşa");

        foreach (var definition in WeaponCatalog.All)
        {
            var weapon = new Weapon { Name = $"Weapon_{definition.Id}" };
            weapon.Init(player, camera, definition);
            AddTool(weapon, definition.DisplayName);
        }

        _helpLabel = new Label
        {
            Position = new Vector2(16, 44),
            Modulate = new Color(1, 1, 1, 0.8f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _helpLabel.AddThemeFontSizeOverride("font_size", 13);
        _helpLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _helpLabel.AddThemeConstantOverride("outline_size", 3);
        hud.AddChild(_helpLabel);

        // Menü HUD'a en son eklensin ki diğer yazıların üstünde dursun
        _spawnTool = new PropSpawnTool { Name = "SpawnTool" };
        _spawnTool.Init(player, camera, hud);
        AddChild(_spawnTool);

        Select(0);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured || !CanUseTools) return;

        int slots = Mathf.Min(_tools.Count, MaxSlots);
        for (int i = 0; i < slots; i++)
        {
            if (@event.IsActionPressed($"slot_{i + 1}"))
            {
                Select(i);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event.IsActionPressed("select_physgun"))
        {
            Select(0);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("select_toolgun"))
        {
            Select(1);
            GetViewport().SetInputAsHandled();
        }
    }

    // Araç sürerken ya da ölüyken elindeki araç ve obje menüsü kapanır, sonra geri gelir
    public override void _PhysicsProcess(double delta)
    {
        if (ActiveTool == null) return;

        bool usable = CanUseTools;
        if (ActiveTool.Active != usable)
        {
            ActiveTool.Active = usable;
            if (!usable)
                ActiveTool.OnDeactivated();
        }
        _spawnTool.Enabled = usable;
    }

    public override void _Process(double delta)
    {
        if (_helpLabel == null || ActiveTool == null) return;

        if (_player.Dead)
        {
            _helpLabel.Text = "";
            return;
        }

        if (_player.IsSeated)
        {
            _helpLabel.Text = "[Sürüyorsun]\nW/S: ileri / geri   A/D: dön   Shift: turbo   F: in";
            return;
        }

        var slots = new List<string>();
        for (int i = 0; i < _slotNames.Count && i < MaxSlots; i++)
            slots.Add(i == _activeIndex ? $"[{i + 1} {_slotNames[i]}]" : $"{i + 1} {_slotNames[i]}");

        _helpLabel.Text =
            $"{string.Join("   ", slots)}\n" +
            $"{ActiveTool.Title}: {ActiveTool.HelpText}\n" +
            "Q (basılı): obje menüsü   Z: geri al   F: koltuğa bin   Esc: menü";
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _tools.Count || index == _activeIndex) return;

        if (ActiveTool != null)
        {
            ActiveTool.Active = false;
            ActiveTool.OnDeactivated();
        }

        _activeIndex = index;
        ActiveTool.Active = CanUseTools;
    }

    private bool CanUseTools => !_player.IsSeated && !_player.Dead;

    private void AddTool<T>(T tool, string slotName) where T : Node, ITool
    {
        tool.Active = false;
        AddChild(tool);
        _tools.Add(tool);
        _slotNames.Add(slotName);
    }
}
