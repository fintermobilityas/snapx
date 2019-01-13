using System;
using JetBrains.Annotations;
using Splat;

namespace Snap.NugetApi
{
    internal interface INuGetSourcesReader
    {
        INuGetPackageSources Read(string workingDirectory, INuGetPackageSources overrideValues);
    }

    internal sealed class NuGetSourcesReader : INuGetSourcesReader, IEnableLogger
    {
        readonly INugetConfigFileReader _reader;

        public NuGetSourcesReader([NotNull] INugetConfigFileReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public INuGetPackageSources Read([NotNull] string workingDirectory, INuGetPackageSources overrideValues)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (overrideValues != null)
            {
                return overrideValues;
            }

            var fromConfigFile = _reader.ReadNugetSources(workingDirectory);

            if (fromConfigFile != null)
            {
                return fromConfigFile;
            }

            this.Log().Info("Using default global NuGet feed.");

            return new NuGetPackageSources();
        }
    }
}
