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
        public static string SetupNupkgFilename = "Setup.nupkg";
        public const string Sha256EmptyFileChecksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        
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
