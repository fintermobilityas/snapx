using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Snap.CrossPlatform.DemoApp
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
