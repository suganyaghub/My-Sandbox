using System.Windows;
using System.Windows.Threading;

namespace CaptionTranslator
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            base.OnStartup(e);
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Unhandled UI exception.", e.Exception);
            MessageBox.Show(e.Exception.Message, "Caption Translator", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
