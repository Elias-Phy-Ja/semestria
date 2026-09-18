using SchulnetzSync.Core.Update;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Builds the update source once the main window has a handle.
///
/// StoreContext insists on a window handle, which does not exist yet while the window is
/// being constructed. The factory also lets startup swap in the fake source without the
/// window having to know that two variants exist.
/// </summary>
public interface IUpdateSourceFactory
{
    IUpdateSource Create(IntPtr windowHandle);

    /// <summary>
    /// True when the postpone window should be ignored.
    ///
    /// The fake source needs this: it always offers the same version, so a single
    /// "Später erinnern" would make the whole switch useless for a day.
    /// </summary>
    bool BypassSnooze => false;
}

/// <summary>The real thing: asks the Microsoft Store.</summary>
public sealed class StoreUpdateSourceFactory : IUpdateSourceFactory
{
    public IUpdateSource Create(IntPtr windowHandle) => new StoreUpdateSource(windowHandle);
}

/// <summary>Invents an update; picked by --fake-update.</summary>
public sealed class FakeUpdateSourceFactory(string version, bool mandatory) : IUpdateSourceFactory
{
    public IUpdateSource Create(IntPtr windowHandle) => new FakeUpdateSource(version, mandatory);

    public bool BypassSnooze => true;
}
