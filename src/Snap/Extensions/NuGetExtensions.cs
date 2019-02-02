using System;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NuGet.Configuration;
using Snap.Core.Models;
using Snap.NuGet;

namespace Snap.Extensions
{
    internal static class NuGetExtensions
    {
        internal static bool IsPasswordEncryptionSupported()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        internal static bool IsPasswordEncryptionSupported([NotNull] this SnapNugetFeed nugetFeed)
        {
            if (nugetFeed == null) throw new ArgumentNullException(nameof(nugetFeed));
            return IsPasswordEncryptionSupported();
        }
        
        internal static bool IsPasswordEncryptionSupported([NotNull] this PackageSourceCredential credential)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            return IsPasswordEncryptionSupported();
        }
        
        internal static bool IsPasswordEncryptionSupported([NotNull] this ISettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return IsPasswordEncryptionSupported();
        }
        
        internal static bool IsPasswordEncryptionSupported([NotNull] this INuGetPackageSources packageSources)
        {
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));
            return IsPasswordEncryptionSupported();
        }
    }
}
