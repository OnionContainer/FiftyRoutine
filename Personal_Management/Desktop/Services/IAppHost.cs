using System.Windows;

namespace PersonalManagement.Desktop;

/// <summary>
/// Host surface for Pages: session, status bar, reload, and Noco connect.
/// Implemented by <see cref="MainWindow"/>. Pages must not reference each other.
/// </summary>
public interface IAppHost
{
    AppSession Session { get; }
    Window OwnerWindow { get; }
    string StatusText { get; set; }
    Task ReloadAllAsync();
    Task TryConnectNocoAsync();
}
