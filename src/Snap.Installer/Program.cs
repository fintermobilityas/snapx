using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using LightInject;
using Snap.Installer.Core;

namespace Snap.Installer
{
    internal static class Program
    {
        static void Main()
        {
            var globalCts = new CancellationTokenSource();

            var container = new ServiceContainer();
            container.Register<IInstallerEmbeddedResources>(x => new InstallerEmbeddedResources());

            var environment = new SnapInstallerEnvironment
            {
                Container = container,
                CancellationToken = globalCts.Token
            };

            BuildAvaloniaApp().BeforeStarting(builder =>
            {
                MainWindow.Environment = environment;
            }).AfterSetup(builder =>
            {

            }).Start<MainWindow>();

            globalCts.Cancel();

            Application.Current.Exit();
        }

        static AppBuilder BuildAvaloniaApp()
        {
            var result = AppBuilder.Configure<App>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                result
                    .UseWin32()
                    .UseDirect2D1();
                return result;
            }

            result.UsePlatformDetect();
            return result;
        }
    }
}
