using Godot;
using ProjectXs.Player;
using ProjectXs.Voxels;

namespace ProjectXs.Tools;

/// <summary>
/// Minecraft tarzı inşa aracı. Sol tık blok kırar, sağ tık seçili bloğu koyar (basılı tutunca devam eder),
/// fare tekerleği blok türünü değiştirir, orta tık baktığın bloğun türünü seçer.
/// Bloklar voxel dünyanın yanında objelerin ve test yapılarının yüzeyine de konabilir.
/// </summary>
public partial class BuildTool : Node, ITool
{
    private const float Range = 8.0f;
    private const float ActionInterval = 0.18f;

    private PlayerController _player;
    private Camera3D _camera;
    private int _selected;
    private float _cooldown;
    private MeshInstance3D _highlight;

    public bool Active { get; set; }
    public string Title => "İnşa";

    public string HelpText =>
        $"Seçili blok: {BlockCatalog.Placeable[_selected].DisplayName}\n" +
        "Sol tık: kır   Sağ tık: koy   Tekerlek: blok değiştir   Orta tık: baktığın bloğu seç";

    public void Init(PlayerController player, Camera3D camera)
    {
        _player = player;
        _camera = camera;
    }

    public override void _Ready()
    {
        // Hedeflenen bloğun etrafında yarı saydam bir kutu; sadece bu oyuncu görür
        _highlight = new MeshInstance3D
        {
            Name = "BlockHighlight",
            Visible = false,
            Mesh = new BoxMesh { Size = Vector3.One * 1.02f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1, 1, 1, 0.18f),
            },
        };
        AddChild(_highlight);
    }

    public void OnDeactivated()
    {
        if (_highlight != null)
            _highlight.Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Active || Input.MouseMode != Input.MouseModeEnum.Captured) return;
        if (@event is not InputEventMouseButton { Pressed: true } button) return;

        int count = BlockCatalog.Placeable.Count;
        if (button.ButtonIndex == MouseButton.WheelUp)
        {
            _selected = (_selected - 1 + count) % count;
            GetViewport().SetInputAsHandled();
        }
        else if (button.ButtonIndex == MouseButton.WheelDown)
        {
            _selected = (_selected + 1) % count;
            GetViewport().SetInputAsHandled();
        }
        else if (button.ButtonIndex == MouseButton.Middle)
        {
            PickTargetBlock();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _cooldown -= (float)delta;

        var world = VoxelWorld.Instance;
        if (!Active || world == null || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            _highlight.Visible = false;
            return;
        }

        bool hasTarget = FindTarget(world, out var breakBlock, out var placeBlock, out bool isVoxel);

        _highlight.Visible = hasTarget && isVoxel;
        if (_highlight.Visible)
            _highlight.GlobalPosition = world.BlockCenter(breakBlock);

        if (!hasTarget || _cooldown > 0) return;

        if (Input.IsActionPressed("tool_primary") && isVoxel)
        {
            world.RequestSetBlock(breakBlock, BlockType.Air);
            _cooldown = ActionInterval;
        }
        else if (Input.IsActionPressed("tool_secondary") && world.InBounds(placeBlock) && !OverlapsSelf(world, placeBlock))
        {
            world.RequestSetBlock(placeBlock, BlockCatalog.Placeable[_selected].Type);
            _cooldown = ActionInterval;
        }
    }

    /// <summary>
    /// Baktığın yüzeyden kırılacak bloğu (yüzeyin hemen arkası) ve konacak bloğun yerini (yüzeyin hemen önü) bulur.
    /// </summary>
    private bool FindTarget(VoxelWorld world, out Vector3I breakBlock, out Vector3I placeBlock, out bool isVoxel)
    {
        breakBlock = default;
        placeBlock = default;
        isVoxel = false;

        if (!ToolRaycast.Cast(_player, _camera, Range, out var hit)) return false;

        isVoxel = VoxelWorld.IsVoxelBody(hit.Collider);
        world.TryWorldToBlock(hit.Position - hit.Normal * 0.01f, out breakBlock);
        world.TryWorldToBlock(hit.Position + hit.Normal * 0.01f, out placeBlock);
        return true;
    }

    private void PickTargetBlock()
    {
        var world = VoxelWorld.Instance;
        if (world == null || !FindTarget(world, out var breakBlock, out _, out bool isVoxel) || !isVoxel) return;

        var type = world.GetBlock(breakBlock);
        for (int i = 0; i < BlockCatalog.Placeable.Count; i++)
        {
            if (BlockCatalog.Placeable[i].Type == type)
            {
                _selected = i;
                return;
            }
        }
    }

    // Kendi içine blok koyup sıkışmamak için (diğer oyuncuları sunucu kontrol ediyor)
    private bool OverlapsSelf(VoxelWorld world, Vector3I block)
    {
        var blockBox = new Aabb(world.BlockCenter(block) - Vector3.One * 0.5f, Vector3.One);
        var playerBox = new Aabb(_player.GlobalPosition + new Vector3(-0.4f, 0, -0.4f), new Vector3(0.8f, 1.8f, 0.8f));
        return blockBox.Intersects(playerBox);
    }
}
