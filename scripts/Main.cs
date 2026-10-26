using Godot;
using ProjectXs.Combat;
using ProjectXs.Core;
using ProjectXs.Net;
using ProjectXs.Props;
using ProjectXs.UI;
using ProjectXs.Voxels;
using ProjectXs.World;

namespace ProjectXs;

/// <summary>
/// Oyunun giriş noktası. Dünyayı, objeleri, ağ yöneticisini ve menüyü kurar, aralarındaki bağlantıları yapar.
/// Sistemler birbirini doğrudan tanımaz; buradaki sinyal bağlantıları sayesinde konuşurlar.
/// </summary>
public partial class Main : Node3D
{
    private NetworkManager _network;
    private VoxelWorld _voxels;
    private PropManager _props;
    private CombatManager _combat;
    private ConnectMenu _menu;

    public override void _Ready()
    {
        InputSetup.Register();

        _voxels = new VoxelWorld { Name = "Voxels" };
        AddChild(_voxels);

        AddChild(TestWorld.Build());

        _props = new PropManager { Name = "Props" };
        AddChild(_props);

        _network = new NetworkManager { Name = "Network" };
        AddChild(_network);

        _combat = new CombatManager { Name = "Combat", PlayersRoot = _network.PlayersRoot };
        AddChild(_combat);

        _voxels.PlayersRoot = _network.PlayersRoot;

        _menu = new ConnectMenu { Name = "Menu" };
        AddChild(_menu);

        _menu.HostRequested += port => _network.Host(port);
        _menu.JoinRequested += (address, port) => _network.Join(address, port);

        _network.StatusChanged += message => _menu.SetStatus(message);
        _network.SessionStarted += OnSessionStarted;
        _network.SessionEnded += OnSessionEnded;
    }

    private void OnSessionStarted()
    {
        _menu.Visible = false;

        // Objeleri sadece sunucu oluşturur; istemciler onları spawner üzerinden otomatik alır.
        if (Multiplayer.IsServer())
            _props.SpawnStarterProps();
    }

    private void OnSessionEnded()
    {
        _props.ClearAll();
        _combat.ClearAll();
        _voxels.ResetWorld();
        _menu.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
