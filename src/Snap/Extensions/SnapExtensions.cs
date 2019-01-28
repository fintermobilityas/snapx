using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Configuration;
using Snap.Attributes;
using Snap.Core;
using Snap.NuGet;
using Snap.Reflection;

namespace Snap.Extensions
{
    public static class SnapExtensions
    {
        public const string SnapSpecDll = SnapSpecsWriter.SnapAppSpecLibraryName + ".dll";

        internal static string GetNugetUpstreamPackageId([NotNull] this SnapAppSpec snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var fullOrDelta = !snapApp.IsDelta ? "full" : "delta";
            return $"{snapApp.Id}-{fullOrDelta}-{snapApp.Channel.Name}-{snapApp.TargetFramework.OsPlatform}-{snapApp.TargetFramework.Framework}-{snapApp.TargetFramework.RuntimeIdentifier}".ToLowerInvariant();
        }

        internal static INuGetPackageSources GetNugetSourcesFromSnapFeed([NotNull] this SnapFeed snapFeed)
        {
            if (snapFeed == null) throw new ArgumentNullException(nameof(snapFeed));

            var packageSource = new PackageSource(snapFeed.SourceUri.ToString(), snapFeed.Name, true, true, false)
            {
                IsMachineWide = false,
                IsOfficial = true
            };

            if (snapFeed.Username != null && snapFeed.Password != null)
            {
                packageSource.Credentials = new PackageSourceCredential(packageSource.Source, snapFeed.Username, snapFeed.Password, true);
            }

            packageSource.ProtocolVersion = (int)snapFeed.ProtocolVersion;

            return new NuGetPackageSources(new NullSettings(), new List<PackageSource> { packageSource });
        }

        internal static INuGetPackageSources GetNugetSourcesFromSnapAppSpec([NotNull] this SnapAppSpec snapAppSpec)
        {
            if (snapAppSpec == null) throw new ArgumentNullException(nameof(snapAppSpec));

            return snapAppSpec.Feed.GetNugetSourcesFromSnapFeed();
        }

        internal static SnapAppSpec GetSnapAppSpec(this AssemblyDefinition assemblyDefinition, ISnapSpecsReader snapSpecsReader)
        {
            var snapSpecAttribute = new CecilAssemblyReflector(assemblyDefinition).GetAttribute<SnapSpecAttribute>();
            if (snapSpecAttribute == null)
            {
                throw new Exception($"Unable to find {nameof(SnapSpecAttribute)} in assembly {assemblyDefinition.FullName}.");
            }

            const string snapAppSpecResourceName = "SnapAppSpec";
            var snapSpecResource = assemblyDefinition.MainModule.Resources.SingleOrDefault(x => x.Name == snapAppSpecResourceName);
            if (!(snapSpecResource is EmbeddedResource snapSpecEmbeddedResource))
            {
                throw new Exception($"Unable to find {snapAppSpecResourceName} in assembly {assemblyDefinition.FullName}.");
            }

            using (var inputStream = snapSpecEmbeddedResource.GetResourceStream())
            using (var outputStream = new MemoryStream())
            {
                inputStream.CopyTo(outputStream);
                outputStream.Seek(0, SeekOrigin.Begin);

                var yamlString = Encoding.UTF8.GetString(outputStream.ToArray());
                var snapAppSpec = snapSpecsReader.GetSnapAppSpecFromYamlString(yamlString);

                return snapAppSpec;
            }
        }

        internal static SnapAppSpec GetSnapAppSpecFromDirectory([NotNull] this string workingDirectory)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var snapAppSpecDll = Path.Combine(workingDirectory, SnapSpecDll);
            if (!File.Exists(snapAppSpecDll))
            {
                throw new FileNotFoundException(snapAppSpecDll);
            }

            using (var fileStream = new FileStream(snapAppSpecDll, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var assemblyDefinition = AssemblyDefinition.ReadAssembly(fileStream))
            {
                return assemblyDefinition.GetSnapAppSpec(new SnapSpecsReader());
            }
        }

        [UsedImplicitly]
        internal static string GetSnapStubExecutableFullPath([NotNull] this string stubExecutableWorkingDirectory, out string stubExecutableExeName)
        {
            if (stubExecutableWorkingDirectory == null) throw new ArgumentNullException(nameof(stubExecutableWorkingDirectory));

            var snapAppSpec = stubExecutableWorkingDirectory.GetSnapAppSpecFromDirectory();

            stubExecutableExeName = $"{snapAppSpec.Id}.exe";

            return Path.Combine(stubExecutableWorkingDirectory, $"..\\{stubExecutableExeName}");
        }

        public static SnapAppSpec GetSnapAppSpec([NotNull] this Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            var snapSpecDllDirectory = Path.GetDirectoryName(assembly.Location);
            if (snapSpecDllDirectory == null)
            {
                throw new Exception($"Unable to find snap dll spec: {SnapSpecDll}. Assembly location: {assembly.Location}. Assembly name: {assembly.FullName}.");
            }

            return snapSpecDllDirectory.GetSnapAppSpecFromDirectory();
        }

        [UsedImplicitly]
        public static string GetSnapStubExecutableFullPath([NotNull] this Assembly assembly, out string stubExecutableExeName)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            return Path.GetDirectoryName(assembly.Location).GetSnapStubExecutableFullPath(out stubExecutableExeName);
        }
    }
}
