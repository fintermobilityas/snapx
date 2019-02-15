using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Snap.Installer.Controls;
using Snap.Installer.Core;
using Snap.Installer.ViewModels;
using Snap.Logging;

namespace Snap.Installer
{
    internal sealed class MainWindow : ChromeLessWindow
    {
        public static ISnapInstallerEnvironment Environment { get; set; }
        public static MainWindowViewModel ViewModel { get; set; }

        public MainWindow()
        {
            Debug.Assert(Environment != null, nameof(Environment) + " != null");
            
            var logger = Environment.BuildLogger<MainWindow>();

            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif

            DataContext = ViewModel;

            Environment.CancellationToken.Register(() =>
            {
                logger.Info("Cancellation detected, closing main window.");
                Dispatcher.UIThread.InvokeAsync(Close);
            });
        }

        protected override void OnOpened(EventArgs eventArgs)
        {
            var thisHandle = PlatformImpl.Handle.Handle;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NativeMethodsWindows.FocusThisWindow(thisHandle);
            }

            base.OnOpened(eventArgs);
        }

        void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
