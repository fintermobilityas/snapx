using Avalonia;
using Avalonia.Markup.Xaml;

namespace Snap.Installer
{
    internal sealed class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
