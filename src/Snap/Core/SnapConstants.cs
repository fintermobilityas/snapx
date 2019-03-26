using System;
using System.Reflection;
using System.Runtime.InteropServices;
using NuGet.Frameworks;

namespace Snap.Core
{
    internal static class SnapConstants
    {
        public static readonly string SnapAppLibraryName = "Snap.App";
        public static readonly string SnapDllFilename = "Snap.dll";
        public static string SnapAppDllFilename => $"{SnapAppLibraryName}.dll";
        public const string Sha512EmptyFileChecksum = "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e";
        
        public static readonly string SnapUniqueTargetPathFolderName = BuildSnapNuspecUniqueFolderName();
        public static readonly string NuspecTargetFrameworkMoniker = NuGetFramework.AnyFramework.Framework;
        public static readonly string NuspecRootTargetPath = $"lib/{NuspecTargetFrameworkMoniker}";
        public static readonly string NuspecAssetsTargetPath = $"{NuspecRootTargetPath}/{SnapUniqueTargetPathFolderName}";
        public const string ReleasesFilename = "Snap.Releases";

        static string BuildSnapNuspecUniqueFolderName()
        {
            var guidStr = typeof(SnapConstants).Assembly.GetCustomAttribute<GuidAttribute>()?.Value;
            Guid.TryParse(guidStr, out var assemblyGuid);
            if (assemblyGuid == Guid.Empty)
            {
                throw new Exception("Fatal error! Assembly guid is empty");
            }

            return assemblyGuid.ToString("N");
        }
    }
}
