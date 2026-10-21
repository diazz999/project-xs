using Godot;
using ProjectXs.Core;
using ProjectXs.Net;
using ProjectXs.UI;
using ProjectXs.World;

namespace ProjectXs;

/// <summary>
/// Oyunun giriş noktası. Dünyayı, ağ yöneticisini ve menüyü kurar, aralarındaki bağlantıları yapar.
/// Sistemler birbirini doğrudan tanımaz; buradaki sinyal bağlantıları sayesinde konuşurlar.
/// </summary>
public partial class Main : Node3D
{
    private NetworkManager _network;
    private ConnectMenu _menu;

    public override void _Ready()
    {
        InputSetup.Register();

        AddChild(TestWorld.Build());

        _network = new NetworkManager { Name = "Network" };
        AddChild(_network);

        _menu = new ConnectMenu { Name = "Menu" };
        AddChild(_menu);

        _menu.HostRequested += port => _network.Host(port);
        _menu.JoinRequested += (address, port) => _network.Join(address, port);

        _network.StatusChanged += message => _menu.SetStatus(message);
        _network.SessionStarted += () => _menu.Visible = false;
        _network.SessionEnded += () =>
        {
            _menu.Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        };
    }
}
