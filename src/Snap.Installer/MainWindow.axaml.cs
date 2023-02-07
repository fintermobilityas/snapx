using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ReactiveUI;
using Snap.Installer.Controls;
using Snap.Installer.Core;
using Snap.Installer.ViewModels;
using Snap.Installer.Windows;
using Snap.Logging;

namespace Snap.Installer;

internal sealed class MainWindow : CustomChromeWindow
{
    public static ISnapInstallerEnvironment Environment { get; set; }
    public static AvaloniaMainWindowViewModel ViewModel { get; set; }

    public MainWindow()
    {
        Debug.Assert(Environment != null, nameof(Environment) + " != null");
            
        var logger = Environment.BuildLogger<MainWindow>();

        InitializeComponent();

        ViewModel.CancelCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await ViewModel.SetStatusTextAsync("Cancelling installation because user pressed CTRL + C");
            await Task.Delay(TimeSpan.FromSeconds(3));
            Close();
        });

        DataContext = ViewModel;

        Environment.CancellationToken.Register(() =>
        {
            logger.Info("Cancellation detected, closing main window.");
            Dispatcher.UIThread.InvokeAsync(Close);
        });
    }

    protected override void OnOpened(EventArgs eventArgs)
    {
        ViewModel.GifAnimation = this.FindControl<GifAnimationControl>("GifAnimation");
        ViewModel.OnInitialized();

        base.OnOpened(eventArgs);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (!Environment.CancellationToken.IsCancellationRequested)
        {
            Environment.Shutdown();
        }
    }

    void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
