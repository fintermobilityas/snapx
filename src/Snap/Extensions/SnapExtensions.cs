using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Configuration;
using Snap.Attributes;
using Snap.Core;
using Snap.Core.Specs;
using Snap.NuGet;
using Snap.Reflection;

namespace Snap.Extensions
{
    public static class SnapExtensions
    {
        public const string SnapAppDllFilename = SnapAppWriter.SnapAppDllFilename;
        static readonly OSPlatform AnyOs = OSPlatform.Create("AnyOs");

        internal static string BuildNugetUpstreamPackageId([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var fullOrDelta = "full"; // Todo: Update me when delta updates support lands.
            return $"{snapApp.Id}-{fullOrDelta}-{snapApp.Channel.Name}-{snapApp.Target.OsPlatform}-{snapApp.Target.Framework.Name}-{snapApp.Target.Framework.RuntimeIdentifier}".ToLowerInvariant();
        }

        internal static PackageSource BuildPackageSource([NotNull] this SnapFeed snapFeed, [NotNull] InMemorySettings inMemorySettings)
        {
            if (snapFeed == null) throw new ArgumentNullException(nameof(snapFeed));
            if (inMemorySettings == null) throw new ArgumentNullException(nameof(inMemorySettings));

            var packageSource = new PackageSource(snapFeed.SourceUri.ToString(), snapFeed.Name, true, true, false);
            
            if (snapFeed.Username != null && snapFeed.Password != null)
            {
                packageSource.Credentials = PackageSourceCredential.FromUserInput(packageSource.Name, 
                    snapFeed.Username, snapFeed.Password, false);

                inMemorySettings.AddOrUpdate(ConfigurationConstants.CredentialsSectionName, packageSource.Credentials.AsCredentialsItem());
            }

            if (snapFeed.ApiKey != null)
            {
                SettingsUtility.SetEncryptedValueForAddItem(inMemorySettings, ConfigurationConstants.ApiKeys, packageSource.Source, snapFeed.ApiKey);
            }

            packageSource.ProtocolVersion = (int)snapFeed.ProtocolVersion;

            return packageSource;
        }

        internal static INuGetPackageSources BuildNugetSourcesFromSnapApp([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var inMemorySettings = new InMemorySettings();

            return new NuGetPackageSources(inMemorySettings, snapApp.Feeds.Select(x => x.BuildPackageSource(inMemorySettings)).ToList());
        }

        internal static SnapApp GetSnapApp(this AssemblyDefinition assemblyDefinition, ISnapAppReader snapAppReader)
        {
            var snapReleaseDetailsAttribute = new CecilAssemblyReflector(assemblyDefinition).GetAttribute<SnapAppReleaseDetailsAttribute>();
            if (snapReleaseDetailsAttribute == null)
            {
                throw new Exception($"Unable to find {nameof(SnapAppReleaseDetailsAttribute)} in assembly {assemblyDefinition.FullName}.");
            }

            var snapSpecResource = assemblyDefinition.MainModule.Resources.SingleOrDefault(x => x.Name == SnapAppWriter.SnapAppLibraryName);
            if (!(snapSpecResource is EmbeddedResource snapSpecEmbeddedResource))
            {
                throw new Exception($"Unable to find resource {SnapAppWriter.SnapAppLibraryName} in assembly {assemblyDefinition.FullName}.");
            }

            using (var resourceStream = snapSpecEmbeddedResource.GetResourceStream())
            using (var snapAppMemoryStream = new MemoryStream())
            {
                resourceStream.CopyTo(snapAppMemoryStream);
                snapAppMemoryStream.Seek(0, SeekOrigin.Begin);

                return snapAppReader.BuildSnapAppFromStream(snapAppMemoryStream);
            }
        }

        internal static SnapApp GetSnapAppFromDirectory([NotNull] this string workingDirectory)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var snapAppDll = Path.Combine(workingDirectory, SnapAppDllFilename);
            if (!File.Exists(snapAppDll))
            {
                throw new FileNotFoundException(snapAppDll);
            }

            using (var snapAppDllFileStream = new FileStream(snapAppDll, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var snapAppDllAssemblyDefinition = AssemblyDefinition.ReadAssembly(snapAppDllFileStream))
            {
                return snapAppDllAssemblyDefinition.GetSnapApp(new SnapAppReader());
            }
        }

        [UsedImplicitly]
        internal static string GetSnapStubExecutableFullPath([NotNull] this string stubExecutableWorkingDirectory, out string stubExecutableExeName)
        {
            if (stubExecutableWorkingDirectory == null) throw new ArgumentNullException(nameof(stubExecutableWorkingDirectory));

            var snapApp = stubExecutableWorkingDirectory.GetSnapAppFromDirectory();

            stubExecutableExeName = $"{snapApp.Id}.exe";

            return Path.Combine(stubExecutableWorkingDirectory, $"..\\{stubExecutableExeName}");
        }

        public static SnapApp GetSnapApp([NotNull] this Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            var snapSpecDllDirectory = Path.GetDirectoryName(assembly.Location);
            if (snapSpecDllDirectory == null)
            {
                throw new Exception($"Unable to find snap app dll: {SnapAppDllFilename}. Assembly location: {assembly.Location}. Assembly name: {assembly.FullName}.");
            }

            return snapSpecDllDirectory.GetSnapAppFromDirectory();
        }

        [UsedImplicitly]
        public static string GetSnapStubExecutableFullPath([NotNull] this Assembly assembly, out string stubExecutableExeName)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            return Path.GetDirectoryName(assembly.Location).GetSnapStubExecutableFullPath(out stubExecutableExeName);
        }

        public static bool IsAnyOs(this OSPlatform oSPlatform)
        {
            return oSPlatform.ToString().Equals(AnyOs.ToString(), StringComparison.InvariantCultureIgnoreCase);
        }

        public static bool IsSupportedOsVersion(this OSPlatform oSPlatform)
        {
            return oSPlatform == OSPlatform.Linux || oSPlatform == OSPlatform.Windows || oSPlatform.IsAnyOs();
        }
    }
}
