using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Configuration;
using Snap.Logging;

namespace Snap.NuGet
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface INugetConfigFileReader
    {
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
                Logger.Info($"Adding package sources from {file}");
            }

            var packageSources = PackageSourceProvider.LoadPackageSources(settings).ToList();
            return ReadFromFile(settings, packageSources);
        }

        static NuGetPackageSources ReadFromFile([NotNull] ISettings settings, [NotNull] IReadOnlyCollection<PackageSource> sources)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            return new NuGetPackageSources(settings, sources);
        }
    }
}
