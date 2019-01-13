using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Common;
using NuGet.Configuration;

namespace Snap.NugetApi
{
    internal interface INuGetPackageSources
    {
        IReadOnlyCollection<PackageSource> Items { get; }
    }

    internal class NugetMachineWideSettings : IMachineWideSettings
    {
        readonly Lazy<ISettings> _settings;

        public ISettings Settings => _settings.Value;

        public NugetMachineWideSettings()
        {
            var baseDirectory = NuGetEnvironment.GetFolderPath(NuGetFolderPath.MachineWideConfigDirectory);
            _settings = new Lazy<ISettings>(() => NuGet.Configuration.Settings.LoadMachineWideSettings(baseDirectory));
        }
    }

    internal class NuGetPackageSources : INuGetPackageSources
    {
        public IReadOnlyCollection<PackageSource> Items { get; }

        [UsedImplicitly]
        public NuGetPackageSources() : this(Settings.LoadDefaultSettings(string.Empty, null, new NugetMachineWideSettings()))
        {
            
        }

        public NuGetPackageSources([NotNull] ISettings settings) : this(settings.GetConfigFilePaths().ToArray())
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
        }

        public NuGetPackageSources(params string[] sources)
        {
            if (!sources.Any())
            {
                throw new ArgumentException("At least one source must be specified", nameof(sources));
            }

            Items = sources
                .Select(s => new PackageSource(s))
                .ToList();
        }

        public NuGetPackageSources(IEnumerable<PackageSource> sources)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            var items = sources.ToList();

            if (!items.Any())
            {
                throw new ArgumentException(nameof(items));
            }

            Items = items;
        }

        public override string ToString()
        {
            return string.Join(",", Items.Select(s => s.SourceUri.ToString()));
        }
    }
}
