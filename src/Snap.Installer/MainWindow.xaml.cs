using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls.Primitives;
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
        readonly ILog _logger;

        public static ISnapInstallerEnvironment Environment { get; set; }
        public static MainWindowViewModel ViewModel { get; set; }
        public static ManualResetEventSlim OnStartEvent { get; set; }

        public MainWindow()
        {
            Debug.Assert(Environment != null, nameof(Environment) + " != null");
            
            _logger = Environment.BuildLogger<MainWindow>();

            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif

            DataContext = ViewModel;

            Environment.CancellationToken.Register(() =>
            {
                _logger.Info("Cancellation detected, closing main window.");
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

            OnStartEvent.Set();

            base.OnOpened(eventArgs);
        }

        void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
