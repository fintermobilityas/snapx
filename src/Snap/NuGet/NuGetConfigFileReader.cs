using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Configuration;
using Snap.Logging;

namespace Snap.NuGet
{
    internal interface INugetConfigFileReader
    {
        NuGetPackageSources ReadNugetSources(string workingDirectory);
    }

    internal class NuGetConfigFileReader : INugetConfigFileReader
    {        
        static readonly ILog Logger = LogProvider.For<NuGetConfigFileReader>();

        public NuGetPackageSources ReadNugetSources([NotNull] string workingDirectory)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var settings = Settings.LoadDefaultSettings(workingDirectory);

            foreach (var file in settings.GetConfigFilePaths())
            {
                Logger.Info($"Reading file {file} for package sources.");
            }

            var enabledSources = SettingsUtility.GetEnabledSources(settings).ToList();

            return ReadFromFile(settings, enabledSources);
        }

        NuGetPackageSources ReadFromFile([NotNull] ISettings settings, [NotNull] IReadOnlyCollection<PackageSource> sources)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            return new NuGetPackageSources(settings, sources);
        }
    }
}
