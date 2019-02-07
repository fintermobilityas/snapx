using Avalonia;
using Avalonia.Markup.Xaml;
using LightInject;
using Snap.Installer.Controls;
using Snap.Installer.Core;
using Snap.Installer.ViewModels;

namespace Snap.Installer
{
    internal sealed class MainWindow : ChromeLessWindow
    {
        readonly IInstallerEmbeddedResources _installerEmbeddedResources;
        MainWindowViewModel _viewModel;

        public static IEnvironment Environment { get; set; }

        public MainWindow()
        {
            _installerEmbeddedResources = Environment.Container.GetInstance<IInstallerEmbeddedResources>();

            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif

            DataContext = _viewModel;
        }

        void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _viewModel = new MainWindowViewModel(_installerEmbeddedResources, Environment.CancellationToken);
        }
    }
}
