using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Snap.Installer.Controls;
using Snap.Installer.Core;
using Snap.Installer.ViewModels;
using Snap.Installer.Windows;
using Snap.Logging;

namespace Snap.Installer
{
    internal sealed class MainWindow : ChromelessWindow
    {
        public static ISnapInstallerEnvironment Environment { get; set; }
        public static AvaloniaMainWindowViewModel ViewModel { get; set; }

        public MainWindow()
        {
            Debug.Assert(Environment != null, nameof(Environment) + " != null");
            
            var logger = Environment.BuildLogger<MainWindow>();

            InitializeComponent();

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

            ViewModel.GifAnimation = this.FindControl<GifAnimationControl>("GifAnimation");
            ViewModel.OnInitialized();

            base.OnOpened(eventArgs);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!Environment.CancellationToken.IsCancellationRequested)
            {
                Environment.Shutdown();
            }
            base.OnClosing(e);
        }

        void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
