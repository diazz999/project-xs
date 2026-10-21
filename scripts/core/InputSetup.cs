using Godot;

namespace ProjectXs.Core;

/// <summary>
/// Tuş atamalarını editör yerine kodda tanımlar. Yeni bir eylem eklemek için Register içine bir satır yazmak yeterli.
/// PhysicalKeycode kullanıldığı için Türkçe Q/F klavyede de tuşlar fiziksel konumuna göre çalışır.
/// </summary>
public static class InputSetup
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        AddKey("move_forward", Key.W);
        AddKey("move_back", Key.S);
        AddKey("move_left", Key.A);
        AddKey("move_right", Key.D);
        AddKey("jump", Key.Space);
        AddKey("sprint", Key.Shift);
        AddKey("toggle_mouse", Key.Escape);
    }

    private static void AddKey(string action, Key key)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);

        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
