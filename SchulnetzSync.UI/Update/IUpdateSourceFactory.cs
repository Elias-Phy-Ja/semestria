using SchulnetzSync.Core.Update;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Creates the update source once the main window has a handle.
///
/// Der StoreContext braucht ein Fensterhandle, das es beim Erzeugen des
/// Fensters noch nicht gibt. Über diese Fabrik wählt der Start ausserdem die
/// Attrappe aus, ohne dass das Fenster von beiden Varianten wissen muss.
/// </summary>
public interface IUpdateSourceFactory
{
    IUpdateSource Create(IntPtr windowHandle);

    /// <summary>
    /// True when the snooze should be ignored.
    ///
    /// Für die Attrappe zwingend: Sie bietet immer dieselbe Version an, ein
    /// einziges «Später erinnern» würde den Schalter sonst für Tage
    /// unbrauchbar machen.
    /// </summary>
    bool BypassSnooze => false;
}

/// <summary>Uses the Microsoft Store.</summary>
public sealed class StoreUpdateSourceFactory : IUpdateSourceFactory
{
    public IUpdateSource Create(IntPtr windowHandle) => new StoreUpdateSource(windowHandle);
}

/// <summary>Pretends there is an update; selected by --fake-update.</summary>
public sealed class FakeUpdateSourceFactory(string version, bool mandatory) : IUpdateSourceFactory
{
    public IUpdateSource Create(IntPtr windowHandle) => new FakeUpdateSource(version, mandatory);

    public bool BypassSnooze => true;
}
