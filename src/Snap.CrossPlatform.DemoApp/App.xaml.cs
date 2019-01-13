using Avalonia;
using Avalonia.Markup.Xaml;

namespace Snap.CrossPlatform.DemoApp
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
