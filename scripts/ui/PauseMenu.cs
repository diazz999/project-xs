using Godot;
using ProjectXs.Save;

namespace ProjectXs.UI;

/// <summary>
/// Oyun içinde Esc ile açılan menü. Oyunu durdurmaz (çok oyunculu), sadece fareyi serbest bırakır.
/// Host burada dünyayı kaydedip yükleyebilir; herkes buradan oyundan çıkabilir.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    [Signal] public delegate void LeaveRequestedEventHandler();

    /// <summary>Diğer menüler (ör. Q menüsü) kapanırken fareyi geri kilitlemesinler diye.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>Oturum açıkken true; ana menüdeyken Esc bir şey yapmaz.</summary>
    public bool SessionActive { get; set; }

    private Control _root;
    private VBoxContainer _hostSection;
    private Label _clientNote;
    private LineEdit _saveName;
    private ItemList _saveList;
    private Label _status;

    public override void _Ready()
    {
        Layer = 10;

        // Ekranın tamamını kaplayan katman: menü açıkken boşluğa tıklamak oyuna gitmesin
        _root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        center.AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 18);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        margin.AddChild(box);

        var title = new Label { Text = "Menü", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(title);

        var resume = new Button { Text = "Devam et" };
        resume.Pressed += Close;
        box.AddChild(resume);

        // --- Sadece host: kaydet / yükle ---
        _hostSection = new VBoxContainer();
        _hostSection.AddThemeConstantOverride("separation", 8);
        box.AddChild(_hostSection);

        _hostSection.AddChild(new HSeparator());

        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 8);
        _hostSection.AddChild(saveRow);

        _saveName = new LineEdit
        {
            PlaceholderText = "Kayıt adı",
            MaxLength = 40,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        saveRow.AddChild(_saveName);

        var saveButton = new Button { Text = "Kaydet" };
        saveButton.Pressed += OnSavePressed;
        saveRow.AddChild(saveButton);

        _saveList = new ItemList { CustomMinimumSize = new Vector2(0, 160) };
        _saveList.ItemSelected += index => _saveName.Text = _saveList.GetItemText((int)index);
        _saveList.ItemActivated += _ => OnLoadPressed(); // Çift tık yükler
        _hostSection.AddChild(_saveList);

        var loadButton = new Button { Text = "Seçili kaydı yükle" };
        loadButton.Pressed += OnLoadPressed;
        _hostSection.AddChild(loadButton);

        _clientNote = new Label
        {
            Text = "Kaydetme ve yükleme sadece host'ta yapılabilir.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1, 1, 1, 0.7f),
        };
        box.AddChild(_clientNote);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.8f) };
        box.AddChild(_status);

        box.AddChild(new HSeparator());

        var leave = new Button { Text = "Oyundan çık" };
        leave.Pressed += () =>
        {
            Close();
            EmitSignal(SignalName.LeaveRequested);
        };
        box.AddChild(leave);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!SessionActive || !@event.IsActionPressed("toggle_mouse")) return;

        if (IsOpen)
            Close();
        else
            Open();
        GetViewport().SetInputAsHandled();
    }

    public void Open()
    {
        bool isHost = Multiplayer.IsServer();
        _hostSection.Visible = isHost;
        _clientNote.Visible = !isHost;
        _status.Text = "";
        if (isHost)
            RefreshSaves();

        _root.Visible = true;
        IsOpen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Close()
    {
        if (!IsOpen) return;

        _root.Visible = false;
        IsOpen = false;
        if (SessionActive)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void OnSavePressed()
    {
        string name = string.IsNullOrWhiteSpace(_saveName.Text)
            ? Time.GetDatetimeStringFromSystem().Replace(":", "-").Replace("T", "_")
            : _saveName.Text;

        _status.Text = SaveSystem.Save(name);
        RefreshSaves();
    }

    private void OnLoadPressed()
    {
        var selected = _saveList.GetSelectedItems();
        if (selected.Length == 0)
        {
            _status.Text = "Önce listeden bir kayıt seç.";
            return;
        }

        _status.Text = SaveSystem.Load(_saveList.GetItemText(selected[0]));
    }

    private void RefreshSaves()
    {
        _saveList.Clear();
        foreach (string save in SaveSystem.ListSaves())
            _saveList.AddItem(save);
    }
}
