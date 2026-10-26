namespace ProjectXs.Tools;

/// <summary>
/// Oyuncunun elindeki seçilebilir araçlar (gravity gun, tool gun...) bunu uygular.
/// Aynı anda sadece bir araç aktiftir; pasif araçlar girdiyi yok sayar.
/// </summary>
public interface ITool
{
    bool Active { get; set; }

    /// <summary>HUD'da gösterilen araç adı.</summary>
    string Title { get; }

    /// <summary>HUD'da gösterilen kontrol açıklaması.</summary>
    string HelpText { get; }

    /// <summary>Araç bırakılırken çağrılır (ör. tutulan objeyi bırakmak için).</summary>
    void OnDeactivated();
}
