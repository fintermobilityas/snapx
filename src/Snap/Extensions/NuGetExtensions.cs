using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Configuration;
using Snap.NuGet;

#if NET45
using System.IO;
#endif

namespace Snap.Extensions
{
    internal static class NuGetExtensions
    {
        public static bool IsProtocolV2([NotNull] this PackageSource packageSource)
        {
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            return packageSource.ProtocolVersion == (int) NuGetProtocolVersion.NugetV2;
        }

        public static bool IsProtocolV3([NotNull] this PackageSource packageSource)
        {
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            return packageSource.ProtocolVersion == (int) NuGetProtocolVersion.NugetV3;
        }

#if NET45
        public static IList<string> GetConfigFilePaths(this IEnumerable<ISettings> settings)
        {
            return settings.SelectMany(x => x.GetConfigFilePaths()).ToList();
        }

        public static IList<string> GetConfigFilePaths([NotNull] this ISettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var configFilePaths = settings.Priority.Select(config => Path.GetFullPath(Path.Combine(config.Root, config.FileName)));
            return configFilePaths.ToList();
        }

        public static IList<string> GetConfigRoots(this IEnumerable<ISettings> settings)
        {
            return settings.SelectMany(x => x.GetConfigRoots()).ToList();
        }

        public static IList<string> GetConfigRoots(this ISettings settings)
        {
            var configRoots = settings.Priority.Select(config => Path.GetDirectoryName(Path.Combine(config.Root, config.FileName)));
            return configRoots.ToList();
        }
#endif
    }
}
