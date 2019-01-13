using Avalonia;
using Avalonia.Logging.Serilog;

namespace Snap.CrossPlatform.DemoApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            BuildAvaloniaApp().Start<MainWindow>();
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToDebug();
    }
}
