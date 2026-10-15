using System.Windows;
using System.Windows.Threading;

namespace DriverUpdater;

public partial class App : Application
{
    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Driver Updater — erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
