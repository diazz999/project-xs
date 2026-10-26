using Godot;

namespace ProjectXs.Core;

/// <summary>
/// Tuş atamalarını editör yerine kodda tanımlar. Yeni bir eylem eklemek için Register içine bir satır yazmak yeterli.
/// PhysicalKeycode kullanıldığı için Türkçe Q/F klavyede de tuşlar fiziksel konumuna göre çalışır.
/// </summary>
public static class InputSetup
{
    public const int SlotCount = 9;

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        // Hareket
        AddKey("move_forward", Key.W);
        AddKey("move_back", Key.S);
        AddKey("move_left", Key.A);
        AddKey("move_right", Key.D);
        AddKey("jump", Key.Space);
        AddKey("sprint", Key.Shift);
        AddKey("toggle_mouse", Key.Escape);
        AddKey("use", Key.F);

        // Gravity gun
        AddMouse("physgun_grab", MouseButton.Left);
        AddMouse("physgun_freeze", MouseButton.Right);
        AddKey("physgun_rotate", Key.E);
        AddKey("physgun_unfreeze", Key.R);

        // Tool gun ve araç seçimi
        AddMouse("tool_primary", MouseButton.Left);
        AddMouse("tool_secondary", MouseButton.Right);
        AddKey("tool_reset", Key.R);
        AddKey("select_physgun", Key.G);
        AddKey("select_toolgun", Key.T);

        // Araç çubuğu: 1-9 tuşları araç ve silah seçer
        for (int i = 1; i <= SlotCount; i++)
            AddKey($"slot_{i}", (Key)((long)Key.Key1 + i - 1));

        // Obje menüsü (basılı tut) ve geri alma
        AddKey("spawn_menu", Key.Q);
        AddKey("undo", Key.Z);
    }

    private static void AddKey(string action, Key key)
    {
        EnsureAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    private static void AddMouse(string action, MouseButton button)
    {
        EnsureAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }

    private static void EnsureAction(string action)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);
    }
}
