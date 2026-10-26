using Godot;
using ProjectXs.Net;
using ProjectXs.Player;

namespace ProjectXs.UI;

/// <summary>
/// Basit bağlantı menüsü: sunucu kur veya bir IP adresine katıl.
/// Menü ağ kodunu bilmez, sadece sinyal yayar; bağlantıyı Main.cs kurar.
/// </summary>
public partial class ConnectMenu : CanvasLayer
{
    [Signal] public delegate void HostRequestedEventHandler(int port);
    [Signal] public delegate void JoinRequestedEventHandler(string address, int port);

    private LineEdit _nameInput;
    private LineEdit _addressInput;
    private Label _statusLabel;

    public override void _Ready()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
        center.AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 18);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        margin.AddChild(box);

        var title = new Label { Text = "Project XS", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);

        _nameInput = new LineEdit
        {
            PlaceholderText = "Adın",
            MaxLength = 20,
        };
        box.AddChild(_nameInput);

        _addressInput = new LineEdit
        {
            Text = "127.0.0.1",
            PlaceholderText = "IP adresi (isteğe bağlı :port)",
        };
        box.AddChild(_addressInput);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        box.AddChild(buttons);

        var hostButton = new Button { Text = "Sunucu kur", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        hostButton.Pressed += () =>
        {
            ApplyName();
            EmitSignal(SignalName.HostRequested, ParseAddress(_addressInput.Text).port);
        };
        buttons.AddChild(hostButton);

        var joinButton = new Button { Text = "Katıl", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        joinButton.Pressed += () =>
        {
            ApplyName();
            var (host, port) = ParseAddress(_addressInput.Text);
            EmitSignal(SignalName.JoinRequested, host, port);
        };
        buttons.AddChild(joinButton);

        _statusLabel = new Label
        {
            Text = "Bir sunucu kur ya da arkadaşının IP adresini yazıp katıl.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1, 1, 1, 0.75f),
        };
        box.AddChild(_statusLabel);
    }

    public void SetStatus(string message)
    {
        if (_statusLabel != null)
            _statusLabel.Text = message;
    }

    private void ApplyName() => PlayerController.LocalName = _nameInput.Text.Trim();

    private static (string host, int port) ParseAddress(string text)
    {
        text = text.Trim();
        string host = text;
        int port = NetworkManager.DefaultPort;

        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out int parsedPort))
        {
            host = text[..colon];
            port = parsedPort;
        }

        if (string.IsNullOrEmpty(host))
            host = "127.0.0.1";

        return (host, port);
    }
}
