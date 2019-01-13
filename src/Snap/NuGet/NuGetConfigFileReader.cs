using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Configuration;
using Splat;

namespace Snap.NugetApi
{
    internal interface INugetConfigFileReader
    {
        NuGetPackageSources ReadNugetSources(string workingDirectory);
    }

    internal class NuGetConfigFileReader : INugetConfigFileReader, IEnableLogger
    {        
        public NuGetPackageSources ReadNugetSources([NotNull] string workingDirectory)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var settings = Settings.LoadDefaultSettings(workingDirectory);

            foreach (var file in settings.GetConfigFilePaths())
            {
                this.Log().Info($"Reading file {file} for package sources.");
            }

            var enabledSources = SettingsUtility.GetEnabledSources(settings).ToList();

            return ReadFromFile(enabledSources);
        }

        NuGetPackageSources ReadFromFile([NotNull] IReadOnlyCollection<PackageSource> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            foreach (var source in sources)
            {
                this.Log().Info(
                    $"Read [{source.Name}] : {source.SourceUri} from file: {source.Source}.");
            }

            return new NuGetPackageSources(sources);
        }
    }
}
