using System.Threading.Tasks;

namespace Snap.Installer.ViewModels;

internal interface IMainWindowViewModel
{
    bool Headless { get; }
    Task SetStatusTextAsync(string text);
    Task SetErrorAsync();
}