using Godot;
using ProjectXs.Player;
using ProjectXs.Props;
using ProjectXs.UI;

namespace ProjectXs.Tools;

/// <summary>
/// GMod'daki gibi obje oluşturma menüsü: Q basılı tutunca açılır, bir objeye tıklayınca baktığın yere koyar,
/// Q bırakınca kapanır. Z son yaptığın işlemi geri alır. Menü PropCatalog'daki bütün objeleri otomatik listeler.
/// </summary>
public partial class PropSpawnTool : Node
{
    private const float MaxSpawnDistance = 12.0f;
    private const float FallbackDistance = 4.0f;

    private PlayerController _player;
    private Camera3D _camera;
    private Control _menu;

    /// <summary>Araç sürerken ya da ölüyken kapatmak için.</summary>
    public bool Enabled { get; set; } = true;

    public bool MenuOpen => _menu != null && _menu.Visible;

    public void Init(PlayerController player, Camera3D camera, CanvasLayer hud)
    {
        _player = player;
        _camera = camera;
        BuildMenu(hud);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled)
        {
            if (MenuOpen)
                CloseMenu();
            return;
        }

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;

        if (@event.IsActionPressed("spawn_menu") && captured)
        {
            OpenMenu();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionReleased("spawn_menu") && MenuOpen)
        {
            CloseMenu();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("undo") && (captured || MenuOpen))
        {
            PropManager.Instance?.RequestUndo();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OpenMenu()
    {
        _menu.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void CloseMenu()
    {
        _menu.Visible = false;
        if (!PauseMenu.IsOpen)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void BuildMenu(CanvasLayer hud)
    {
        // Ekranın tamamını kaplayan şeffaf bir katman: menü açıkken boşluğa tıklamak oyuna gitmesin
        _menu = new Control { Name = "SpawnMenu", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _menu.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hud.AddChild(_menu);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.25f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menu.AddChild(dim);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menu.AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        margin.AddChild(box);

        box.AddChild(new Label
        {
            Text = "Obje oluştur  (tıkla: baktığın yere koy, Q bırak: kapat, Z: geri al)",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        box.AddChild(grid);

        foreach (var definition in PropCatalog.All)
        {
            var button = new Button
            {
                Text = definition.DisplayName,
                CustomMinimumSize = new Vector2(160, 48),
                FocusMode = Control.FocusModeEnum.None, // Klavye odağı almasın, yoksa Q/Space butona gider
            };
            var captured = definition;
            button.Pressed += () => Spawn(captured);
            grid.AddChild(button);
        }
    }

    private void Spawn(PropDefinition definition)
    {
        Vector3 position;
        if (ToolRaycast.Cast(_player, _camera, MaxSpawnDistance, out var hit))
        {
            // Zemine konuyorsa yarı yüksekliği kadar, duvara konuyorsa en uzun kenarının yarısı kadar dışarı it
            float offset = Mathf.Abs(hit.Normal.Y) > 0.7f ? definition.Size.Y * 0.5f : definition.MaxExtent * 0.5f;
            position = hit.Position + hit.Normal * (offset + 0.05f);
        }
        else
        {
            position = _camera.GlobalPosition - _camera.GlobalBasis.Z * FallbackDistance;
        }

        var rotation = new Quaternion(Vector3.Up, _player.GlobalRotation.Y);
        PropManager.Instance?.RequestSpawn(definition.Id, position, rotation);
    }
}
