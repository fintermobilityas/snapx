using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;

namespace Snap.Shared.Tests
{
    public class BaseFixturePackaging : BaseFixture
    {        
        internal async Task<(MemoryStream nupkgStream, ISnapPackageDetails packageDetails)> BuildPackageAsync(
            [NotNull] DisposableTempDirectory disposableTempDirectory, [NotNull] SnapAppsReleases snapAppsReleases, [NotNull] SnapApp snapApp, [NotNull] ICoreRunLib coreRunLib, [NotNull] ISnapFilesystem filesystem, 
            [NotNull] ISnapPack snapPack, [NotNull] ISnapEmbeddedResources snapEmbeddedResources, [NotNull] Dictionary<string, object> nuspecFilesLayout, 
            string releaseNotes = "My Release Notes?", ISnapProgressSource progressSource = null, CancellationToken cancellationToken = default)
        {
            if (disposableTempDirectory == null) throw new ArgumentNullException(nameof(disposableTempDirectory));
            if (snapAppsReleases == null) throw new ArgumentNullException(nameof(snapAppsReleases));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapEmbeddedResources == null) throw new ArgumentNullException(nameof(snapEmbeddedResources));
            if (nuspecFilesLayout == null) throw new ArgumentNullException(nameof(nuspecFilesLayout));

            var (coreRunMemoryStream, _, _) = snapEmbeddedResources.GetCoreRunForSnapApp(snapApp, filesystem, coreRunLib);
            coreRunMemoryStream.Dispose();
            
            string nuspecContent = $@"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
    <metadata>
        <title>Random Title</title>
        <authors>Peter Rekdal Sunde</authors>
        <releaseNotes>{releaseNotes}</releaseNotes>
    </metadata>
</package>";

            var packagesDirectory = filesystem.PathCombine(disposableTempDirectory.WorkingDirectory, "packages");
            filesystem.DirectoryCreate(packagesDirectory);
            
            var nuspecRootDirectory = filesystem.PathCombine(disposableTempDirectory.WorkingDirectory, "content", snapApp.Version.ToNormalizedString());
            filesystem.DirectoryCreate(nuspecRootDirectory);

            var nuspecFilename = filesystem.PathCombine(nuspecRootDirectory, "test.nuspec");

            var nuspecBaseDirectory = filesystem.PathCombine(nuspecRootDirectory, "artifacts");
            filesystem.DirectoryCreate(nuspecBaseDirectory);
            
            var snapPackDetails = new SnapPackageDetails
            {
                NuspecFilename = nuspecFilename,
                NuspecBaseDirectory = nuspecBaseDirectory,
                PackagesDirectory = packagesDirectory,
                SnapProgressSource = progressSource,
                SnapApp = snapApp,
                SnapAppsReleases = snapAppsReleases,
                NuspecProperties = new Dictionary<string, string>()
            };

            foreach (var (key, value) in nuspecFilesLayout)
            {
                var dstFilename = filesystem.PathCombine(snapPackDetails.NuspecBaseDirectory, key);
                var directory = filesystem.PathGetDirectoryName(dstFilename);
                filesystem.DirectoryCreateIfNotExists(directory);
                switch (value)
                {
                    case AssemblyDefinition assemblyDefinition:
                        assemblyDefinition.Write(dstFilename);
                        break;
                    case MemoryStream memoryStream:
                        await filesystem.FileWriteAsync(memoryStream, dstFilename, cancellationToken);
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        break;
                    default:
                        throw new NotSupportedException($"{key}: {value?.GetType().FullName}");
                }
            }

            await filesystem.FileWriteUtf8StringAsync(nuspecContent, snapPackDetails.NuspecFilename, cancellationToken);

            var nupkgStream = await snapPack.BuildPackageAsync(snapPackDetails, coreRunLib, cancellationToken: cancellationToken);
            return (nupkgStream, snapPackDetails);
        }
    }
}
