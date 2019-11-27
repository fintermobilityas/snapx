using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Snap.Installer
{
    internal sealed class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            base.OnFrameworkInitializationCompleted();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)
            {
                classicDesktopStyleApplicationLifetime.MainWindow = new MainWindow();
                return;
            }

            throw new NotSupportedException($"Unsupported {nameof(ApplicationLifetime)}: {ApplicationLifetime?.GetType().FullName}");
        }
    }
}
