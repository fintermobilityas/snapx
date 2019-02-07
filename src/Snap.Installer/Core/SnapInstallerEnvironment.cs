using System.Threading;
using LightInject;

namespace Snap.Installer.Core
{
    internal interface IEnvironment
    {
        IServiceContainer Container { get; }
        CancellationToken CancellationToken { get; }
    }

    internal class SnapInstallerEnvironment : IEnvironment
    {
        public IServiceContainer Container { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }
}
