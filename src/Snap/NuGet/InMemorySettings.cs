using JetBrains.Annotations;
using NuGet.Configuration;
using Snap.Logging;

namespace Snap.NuGet
{
    /// <inheritdoc />
    /// <summary>
    /// This is a workaround for allowing username/password basic http authentication.
    /// If anyone has a better understanding of Nuget internals then please submit a PR.
    /// </summary>
    internal sealed class InMemorySettings : Settings
    {
        readonly ILog _logger = LogProvider.For<InMemorySettings>();

        public InMemorySettings() : base("snap.nuget.config", "snap.nuget.config", false)
        {
            
        }

        [UsedImplicitly]
        public new void SaveToDisk()
        {
            _logger.Error("Attempted to save in memory nuget settings to disk");
        }        
    }
}
