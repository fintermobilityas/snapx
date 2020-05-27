using System.Threading.Tasks;

namespace Snap.Installer.ViewModels
{
    internal sealed class ConsoleMainViewModel : IMainWindowViewModel
    {
        public bool Headless => true;
        
        public Task SetStatusTextAsync(string text)
        {
            return Task.CompletedTask;
        }
    }
}
