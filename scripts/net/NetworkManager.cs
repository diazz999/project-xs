using Godot;
using ProjectXs.Player;

namespace ProjectXs.Net;

/// <summary>
/// Sunucu kurma, sunucuya katılma ve oyuncuların doğması/silinmesinden sorumlu.
/// Oyuncuları sadece sunucu (host) oluşturur; MultiplayerSpawner bunları otomatik olarak herkese kopyalar.
/// </summary>
public partial class NetworkManager : Node
{
    public const int DefaultPort = 7777;
    private const int MaxPlayers = 8;

    [Signal] public delegate void StatusChangedEventHandler(string message);
    [Signal] public delegate void SessionStartedEventHandler();
    [Signal] public delegate void SessionEndedEventHandler();

    private Node3D _players;
    private MultiplayerSpawner _spawner;

    /// <summary>Oyuncu node'larının bulunduğu yer; her oyuncunun adı peer id'sidir.</summary>
    public Node3D PlayersRoot => _players;

    public override void _Ready()
    {
        _players = new Node3D { Name = "Players" };
        AddChild(_players);

        _spawner = new MultiplayerSpawner { Name = "PlayerSpawner" };
        AddChild(_spawner);
        _spawner.SpawnPath = _spawner.GetPathTo(_players);
        _spawner.SpawnFunction = Callable.From<Variant, Node>(SpawnPlayer);

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public void Host(int port)
    {
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, MaxPlayers);
        if (error != Error.Ok)
        {
            EmitStatus($"Sunucu kurulamadı ({error}). Port {port} başka bir program tarafından kullanılıyor olabilir.");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        _spawner.Spawn(Multiplayer.GetUniqueId());

        EmitStatus($"Sunucu {port} portunda açık.");
        EmitSignal(SignalName.SessionStarted);
    }

    public void Join(string address, int port)
    {
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(address, port);
        if (error != Error.Ok)
        {
            EmitStatus($"Bağlantı başlatılamadı ({error}).");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        EmitStatus($"{address}:{port} adresine bağlanılıyor...");
    }

    public void Leave()
    {
        Multiplayer.MultiplayerPeer?.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();

        foreach (Node child in _players.GetChildren())
            child.QueueFree();
    }

    // Hem sunucuda hem istemcilerde aynı veriyle çalışır, bu yüzden her yerde aynı oyuncu oluşur.
    private Node SpawnPlayer(Variant data)
    {
        long peerId = data.AsInt64();

        var player = new PlayerController { Name = peerId.ToString() };
        player.Setup(peerId);
        player.SetMultiplayerAuthority((int)peerId);
        return player;
    }

    private void OnPeerConnected(long id)
    {
        if (!Multiplayer.IsServer()) return;
        _spawner.Spawn(id);
    }

    private void OnPeerDisconnected(long id)
    {
        if (!Multiplayer.IsServer()) return;
        _players.GetNodeOrNull(id.ToString())?.QueueFree();
    }

    private void OnConnectedToServer()
    {
        EmitStatus("Bağlandı.");
        EmitSignal(SignalName.SessionStarted);
    }

    private void OnConnectionFailed()
    {
        Callable.From(Leave).CallDeferred();
        EmitStatus("Bağlantı kurulamadı. IP adresini ve sunucunun açık olduğunu kontrol et.");
    }

    private void OnServerDisconnected()
    {
        Callable.From(Leave).CallDeferred();
        EmitStatus("Sunucuyla bağlantı koptu.");
        EmitSignal(SignalName.SessionEnded);
    }

    private void EmitStatus(string message)
    {
        GD.Print($"[Ağ] {message}");
        EmitSignal(SignalName.StatusChanged, message);
    }
}
