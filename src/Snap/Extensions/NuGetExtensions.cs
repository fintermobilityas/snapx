using System;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NuGet.Configuration;
using Snap.NuGet;

namespace Snap.Extensions
{
    internal static class NuGetExtensions
    {
        internal static bool NuGetSupportsEncryption()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }
        
        internal static bool NuGetSupportsEncryption([NotNull] this PackageSourceCredential credential)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            return NuGetSupportsEncryption();
        }
        
        internal static bool NuGetSupportsEncryption([NotNull] this ISettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return NuGetSupportsEncryption();
        }
        
        internal static bool NuGetSupportsEncryption([NotNull] this INuGetPackageSources packageSources)
        {
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));
            return NuGetSupportsEncryption();
        }
    }
}
