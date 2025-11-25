using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Shared.Tests;

internal class SnapReleaseBuilder : IDisposable, IEnumerable<string>
{
    readonly Dictionary<string, IDisposable> _nuspec;

    public SnapAppsReleases SnapAppsReleases { get; }
    public ILibPal LibPal { get; }
    public ISnapFilesystem SnapFilesystem { get; }
    public SnapApp SnapApp { get; }
    public ISnapCryptoProvider SnapCryptoProvider { get; }
    public ISnapPack SnapPack { get; }
    public DisposableDirectory BaseDirectory { get; }
    public string SnapAppBaseDirectory { get; }
    public string SnapAppInstallDirectory { get; }
    public string SnapAppPackagesDirectory { get; }
    public string NugetPackagingDirectory { get; }
    public string StubExeFileName => SnapApp.GetStubExeFilename();
 
    public SnapReleaseBuilder([NotNull] DisposableDirectory disposableDirectory, SnapAppsReleases snapAppsReleases, [NotNull] SnapApp snapApp, [NotNull] SnapReleaseBuilderContext builderContext)
    {
        if (builderContext == null) throw new ArgumentNullException(nameof(builderContext));

        _nuspec = new Dictionary<string, IDisposable>();

        SnapFilesystem = builderContext.SnapFilesystem ?? throw new ArgumentNullException(nameof(builderContext.SnapFilesystem));
        SnapAppsReleases = snapAppsReleases ?? throw new ArgumentNullException(nameof(snapAppsReleases));
        SnapApp = snapApp ?? throw new ArgumentNullException(nameof(snapApp));
        LibPal = builderContext.LibPal ?? throw new ArgumentNullException(nameof(builderContext.LibPal));
        SnapCryptoProvider = builderContext.SnapCryptoProvider ?? throw new ArgumentNullException(nameof(builderContext.SnapCryptoProvider));
        SnapPack = builderContext.SnapPack ?? throw new ArgumentNullException(nameof(builderContext.SnapPack));

        BaseDirectory = disposableDirectory ?? throw new ArgumentNullException(nameof(disposableDirectory));
        NugetPackagingDirectory = SnapFilesystem.PathCombine(BaseDirectory, "nuget", $"app-{snapApp.Version}");
        SnapAppBaseDirectory = SnapFilesystem.PathCombine(BaseDirectory, snapApp.Id);
        SnapAppInstallDirectory = SnapFilesystem.PathCombine(SnapAppBaseDirectory, $"app-{snapApp.Version}");
        SnapAppPackagesDirectory = SnapFilesystem.PathCombine(SnapAppBaseDirectory, "packages");
            
        SnapFilesystem.DirectoryCreateIfNotExists(NugetPackagingDirectory);
        SnapFilesystem.DirectoryCreateIfNotExists(SnapAppPackagesDirectory);
        SnapFilesystem.DirectoryCreateIfNotExists(SnapAppInstallDirectory);            
    }

    public SnapReleaseBuilder AddNuspecItem([NotNull] string relativePath, [NotNull] AssemblyDefinition assemblyDefinition)
    {
        if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));
        if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
        var targetPath = SnapFilesystem.PathCombine(relativePath, assemblyDefinition.BuildRelativeFilename());

        _nuspec.Add(targetPath, assemblyDefinition);
        return this;
    }

    public SnapReleaseBuilder AddNuspecItem([NotNull] string targetPath, [NotNull] MemoryStream memoryStream)
    {
        if (targetPath == null) throw new ArgumentNullException(nameof(targetPath));
        if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
            
        _nuspec.Add(targetPath, memoryStream);
        return this;
    }
        
    public SnapReleaseBuilder AddNuspecItem([NotNull] SnapReleaseBuilder releaseBuilder, int index)
    {
        if (releaseBuilder == null) throw new ArgumentNullException(nameof(releaseBuilder));
        var kv = releaseBuilder._nuspec.ElementAt(index);
        _nuspec.Add(kv.Key, kv.Value);
        return this;
    }

    public SnapReleaseBuilder AddNuspecItem([NotNull] AssemblyDefinition assemblyDefinition)
    {
        if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
        _nuspec.Add(assemblyDefinition.BuildRelativeFilename(), assemblyDefinition);
        return this;
    }
        
    public SnapReleaseBuilder AddSnapDll()
    {
        var assemblyDefinition = AssemblyDefinition.ReadAssembly(typeof(SnapPack).Assembly.Location);
        _nuspec.Add(assemblyDefinition.BuildRelativeFilename(), assemblyDefinition);
        return this;
    }

    public string GetLibPalRelativePath() => $"runtimes/{SnapApp.Target.Rid}/native/{SnapApp.GetLibPalFilename()}";
        
    public string GetLibBsdiffRelativePath() => $"runtimes/{SnapApp.Target.Rid}/native/{SnapApp.GetLibBsdiffFilename()}";
        
    public Dictionary<string, IDisposable> GetNuspecItems()
    {
        return _nuspec;
    }

    public async Task<string> WritePackageAsync([NotNull] MemoryStream memoryStream, [NotNull] SnapRelease snapRelease, CancellationToken cancellationToken = default)
    {
        if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            
        var task = !snapRelease.IsDelta ? 
            WriteFullPackageAsync(memoryStream, snapRelease, cancellationToken) :
            WriteDeltaPackageAsync(memoryStream, snapRelease, cancellationToken);
                 
        return await task;
    }

    async Task<string> WriteDeltaPackageAsync([NotNull] MemoryStream memoryStream, [NotNull] SnapRelease snapRelease, CancellationToken cancellationToken = default)
    {
        if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        memoryStream.Seek(0, SeekOrigin.Begin);
        var path = SnapFilesystem.PathCombine(SnapAppPackagesDirectory, snapRelease.BuildNugetDeltaFilename());
        await SnapFilesystem.FileWriteAsync(memoryStream, path, cancellationToken);
        return path;
    }
        
    async Task<string> WriteFullPackageAsync([NotNull] MemoryStream memoryStream, [NotNull] SnapRelease snapRelease, CancellationToken cancellationToken = default)
    {
        if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        memoryStream.Seek(0, SeekOrigin.Begin);
        var path = SnapFilesystem.PathCombine(SnapAppPackagesDirectory, snapRelease.BuildNugetFullFilename());
        await SnapFilesystem.FileWriteAsync(memoryStream, path, cancellationToken);
        return path;
    }

    internal void AssertSnapAppIsGenesis([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        Assert.True(snapApp.IsGenesis);
        Assert.True(snapApp.IsFull);
        Assert.False(snapApp.IsDelta);
    }

    internal void AssertSnapAppIsFull([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        Assert.False(snapApp.IsGenesis);
        Assert.True(snapApp.IsFull);
        Assert.False(snapApp.IsDelta);
    }

    internal void AssertSnapAppIsDelta([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        Assert.False(snapApp.IsGenesis);
        Assert.False(snapApp.IsFull);
        Assert.True(snapApp.IsDelta);
    }

    internal void AssertSnapReleaseIsGenesis([NotNull] SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        Assert.True(snapRelease.IsGenesis);
        Assert.True(snapRelease.IsFull);
        Assert.False(snapRelease.IsDelta);
        Assert.Equal(snapRelease.BuildNugetFullFilename(), snapRelease.Filename);
        Assert.Equal(snapRelease.BuildNugetFullUpstreamId(), snapRelease.UpstreamId);
        Assert.NotEmpty(snapRelease.Files);
        Assert.Empty(snapRelease.New);
        Assert.Empty(snapRelease.Modified);
        Assert.Empty(snapRelease.Unmodified);
        Assert.Empty(snapRelease.Deleted);
    }

    internal void AssertSnapReleaseIsFull([NotNull] SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        Assert.False(snapRelease.IsGenesis);
        Assert.True(snapRelease.IsFull);
        Assert.False(snapRelease.IsDelta);
        Assert.Equal(snapRelease.BuildNugetFullFilename(), snapRelease.Filename);
        Assert.Equal(snapRelease.BuildNugetFullUpstreamId(), snapRelease.UpstreamId);
        Assert.NotEmpty(snapRelease.Files);
        Assert.NotNull(snapRelease.New);
        Assert.NotNull(snapRelease.Modified);
        Assert.NotNull(snapRelease.Unmodified);
        Assert.NotNull(snapRelease.Deleted);
    }
        
    internal void AssertSnapReleaseIsDelta([NotNull] SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        Assert.False(snapRelease.IsGenesis);
        Assert.False(snapRelease.IsFull);
        Assert.True(snapRelease.IsDelta);
        Assert.Equal(snapRelease.BuildNugetDeltaFilename(), snapRelease.Filename);
        Assert.Equal(snapRelease.BuildNugetDeltaUpstreamId(), snapRelease.UpstreamId);
        Assert.NotEmpty(snapRelease.Files);
        Assert.NotNull(snapRelease.New);
        Assert.NotNull(snapRelease.Modified);
        Assert.NotNull(snapRelease.Unmodified);
        Assert.NotNull(snapRelease.Deleted);
    }

    internal void AssertSnapReleaseFiles([NotNull] SnapRelease snapRelease, [NotNull] params string[] targetPaths)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
        if (targetPaths.Length == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(targetPaths));
        Assert.Equal(targetPaths.Length, snapRelease.Files.Count);

        foreach (var targetPath in targetPaths)
        {
            var checksum = snapRelease.Files.SingleOrDefault(x => x.NuspecTargetPath == targetPath);
            Assert.NotNull(checksum);
        }            
    }

    internal void AssertDeltaChangeset(SnapRelease snapRelease, string[] newNuspecTargetPaths = null, string[] deletedNuspecTargetPaths = null,
        string[] modifiedNuspecTargetPaths = null, string[] unmodifiedNuspecTargetPaths = null)
    {
        static void AssertDeltaChangesetSnapReleaseImpl(string[] expectedTargetPaths, List<SnapReleaseChecksum> actualChecksums)
        {
            if (actualChecksums == null) throw new ArgumentNullException(nameof(actualChecksums));
            if (expectedTargetPaths == null) throw new ArgumentNullException(nameof(expectedTargetPaths));
            Assert.Equal(expectedTargetPaths.Length, actualChecksums.Count);

            for (var index = 0; index < actualChecksums.Count; index++)
            {
                var expectedTargetPath = expectedTargetPaths[index];
                var actualChecksum = actualChecksums[index];
                Assert.Equal(expectedTargetPath, actualChecksum.NuspecTargetPath);
            }
        }

        static void AssertDeltaChangesetNuspecTargetPathImpl(string[] expectedTargetPaths, List<string> actualTargetPaths)
        {
            if (actualTargetPaths == null) throw new ArgumentNullException(nameof(actualTargetPaths));
            if (expectedTargetPaths == null) throw new ArgumentNullException(nameof(expectedTargetPaths));
            Assert.Equal(expectedTargetPaths.Length, actualTargetPaths.Count);

            for (var index = 0; index < actualTargetPaths.Count; index++)
            {
                var expectedTargetPath = expectedTargetPaths[index];
                var actualChecksumNuspecTargetPath = actualTargetPaths[index];
                Assert.Equal(expectedTargetPath, actualChecksumNuspecTargetPath);
            }
        }

        AssertDeltaChangesetSnapReleaseImpl(newNuspecTargetPaths ?? [], snapRelease.New);
        AssertDeltaChangesetNuspecTargetPathImpl(deletedNuspecTargetPaths ?? [], snapRelease.Deleted);
        AssertDeltaChangesetSnapReleaseImpl(modifiedNuspecTargetPaths ?? [], snapRelease.Modified);
        AssertDeltaChangesetNuspecTargetPathImpl(unmodifiedNuspecTargetPaths ?? [], snapRelease.Unmodified);            
    }

    internal void AssertChannels([NotNull] SnapApp snapApp, [NotNull] params string[] channels)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        if (channels == null) throw new ArgumentNullException(nameof(channels));
        Assert.Equal(snapApp.Channels.Count, channels.Length);

        for (var index = 0; index < snapApp.Channels.Count; index++)
        {
            var expectedChannelName = snapApp.Channels[index].Name;
            var actualChannelName = channels[index];
            Assert.Equal(expectedChannelName, actualChannelName);
        }
    } 
        
    internal void AssertChannels([NotNull] SnapRelease snapRelease, [NotNull] params string[] channels)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        if (channels == null) throw new ArgumentNullException(nameof(channels));
        Assert.Equal(channels.Length, snapRelease.Channels.Count);

        for (var index = 0; index < snapRelease.Channels.Count; index++)
        {
            var expectedChannelName = snapRelease.Channels[index];
            var actualChannelName = channels[index];
            Assert.Equal(expectedChannelName, actualChannelName);
        }
    } 

    internal void AssertChecksums([NotNull] SnapApp snapApp, [NotNull] SnapRelease snapRelease, [NotNull] List<string> extractedFiles)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        if (extractedFiles == null) throw new ArgumentNullException(nameof(extractedFiles));

        var nupkgAbsoluteFilename = SnapFilesystem.PathCombine(SnapAppPackagesDirectory, snapRelease.Filename);
        Assert.True(SnapFilesystem.FileExists(nupkgAbsoluteFilename));

        using var packageArchiveReader = new PackageArchiveReader(nupkgAbsoluteFilename);
        Assert.Equal(snapApp.ReleaseNotes, snapRelease.ReleaseNotes);

        foreach (var releaseChecksum in snapRelease.Files)
        {
            var (_, _, checksumFileAbsolutePath) = NormalizePath(snapRelease, releaseChecksum.NuspecTargetPath);
            Assert.NotNull(checksumFileAbsolutePath);

            if (snapRelease.IsDelta)
            {
                var deletedChecksum = snapRelease.Deleted.SingleOrDefault(x => x == releaseChecksum.NuspecTargetPath);
                if (deletedChecksum != null)
                {
                    Assert.False(SnapFilesystem.FileExists(checksumFileAbsolutePath));
                    continue;
                }

                var unmodifiedChecksum = snapRelease.Unmodified.SingleOrDefault(x => x == releaseChecksum.NuspecTargetPath);
                if (unmodifiedChecksum != null)
                {
                    Assert.False(SnapFilesystem.FileExists(checksumFileAbsolutePath));
                    continue;
                }               
            }

            Assert.True(SnapFilesystem.FileExists(checksumFileAbsolutePath));

            using var fileStream = SnapFilesystem.FileRead(checksumFileAbsolutePath);
            var diskSha256Checksum = SnapCryptoProvider.Sha256(fileStream);
            var diskFilesize = SnapFilesystem.FileStat(checksumFileAbsolutePath).Length;

            SnapReleaseChecksum targetChecksum;
            bool useFullChecksum;
            if (snapRelease.IsFull)
            {
                targetChecksum = releaseChecksum;
                useFullChecksum = true;
                goto checksum;
            }

            if (SnapPack.NeverGenerateBsDiffsTheseAssemblies.Any(x => x == releaseChecksum.NuspecTargetPath))
            {
                targetChecksum = releaseChecksum;
                useFullChecksum = true;
                goto checksum;
            }

            targetChecksum = snapRelease.New.SingleOrDefault(x => x.NuspecTargetPath == releaseChecksum.NuspecTargetPath);
            if (targetChecksum != null)
            {
                useFullChecksum = true;
            }
            else
            {
                targetChecksum = snapRelease.Modified.SingleOrDefault(x => x.NuspecTargetPath == releaseChecksum.NuspecTargetPath);
                useFullChecksum = false;
            }

            checksum:
            Assert.NotNull(targetChecksum);
   
            var expectedChecksum = useFullChecksum ? targetChecksum.FullSha256Checksum : targetChecksum.DeltaSha256Checksum;
            var expectedFilesize = useFullChecksum ? targetChecksum.FullFilesize : targetChecksum.DeltaFilesize;

            Assert.NotNull(expectedChecksum);
            if (expectedChecksum == SnapConstants.Sha256EmptyFileChecksum)
            {
                Assert.Equal(0, expectedFilesize);
            }
            else
            {
                Assert.True(expectedFilesize > 0);
            }

            Assert.Equal(expectedChecksum, diskSha256Checksum);
            Assert.Equal(expectedFilesize, diskFilesize);
        }

        var snapReleaseChecksum = SnapCryptoProvider.Sha256(snapRelease, packageArchiveReader, SnapPack);
        var snapReleaseFilesize = SnapFilesystem.FileStat(nupkgAbsoluteFilename)?.Length;
                
        var expectedReleaseChecksum = snapRelease.IsFull ? snapRelease.FullSha256Checksum : snapRelease.DeltaSha256Checksum;
        var expectedReleaseFilesize = snapRelease.IsFull ? snapRelease.FullFilesize : snapRelease.DeltaFilesize;

        Assert.Equal(expectedReleaseChecksum, snapReleaseChecksum);
        Assert.Equal(expectedReleaseFilesize, snapReleaseFilesize);
    }

    public void Dispose()
    {
        foreach (var kv in _nuspec)
        {
            kv.Value?.Dispose();
        }

        _nuspec.Clear();
    }

    public IEnumerator<string> GetEnumerator()
    {
        foreach (var kv in _nuspec)
        {
            yield return kv.Key;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    (string targetPath, string nuspecPath, string diskAbsoluteFilename) NormalizePath([NotNull] SnapRelease snapRelease, [NotNull] string relativePath)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));

        var snapAppInstallDirectory = SnapFilesystem.PathCombine(SnapAppBaseDirectory, $"app-{snapRelease.Version}");

        string diskAbsoluteFilename;
        string targetPath;
        if (relativePath.StartsWith(SnapConstants.NuspecAssetsTargetPath))
        {
            relativePath = relativePath[(SnapConstants.NuspecAssetsTargetPath.Length + 1)..];
            targetPath = SnapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, relativePath);
            var isStubExe = relativePath.EndsWith(StubExeFileName);
            diskAbsoluteFilename = SnapFilesystem.PathCombine(isStubExe ? SnapAppBaseDirectory : snapAppInstallDirectory, relativePath);
        }
        else if (relativePath.StartsWith(SnapConstants.NuspecRootTargetPath))
        {
            relativePath = relativePath[(SnapConstants.NuspecRootTargetPath.Length + 1)..];
            targetPath = SnapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, relativePath);
            diskAbsoluteFilename = SnapFilesystem.PathCombine(snapAppInstallDirectory, relativePath);
        }
        else
        {
            throw new Exception($"Unexpected file: {relativePath}");
        }

        return (targetPath, relativePath, diskAbsoluteFilename);
    }

}

internal class SnapReleaseBuilderContext(
    [NotNull] ILibPal libPal,
    [NotNull] ISnapFilesystem snapFilesystem,
    [NotNull] ISnapCryptoProvider snapCryptoProvider,
    [NotNull] ISnapPack snapPack)
{
    public ILibPal LibPal { get; } = libPal ?? throw new ArgumentNullException(nameof(libPal));
    public ISnapFilesystem SnapFilesystem { get; } = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
    public ISnapCryptoProvider SnapCryptoProvider { get; } = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
    public ISnapPack SnapPack { get; } = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
}

internal class BuildPackageContext : IDisposable
{
    public MemoryStream FullPackageMemoryStream { get; init; }
    public SnapApp FullPackageSnapApp { get; init; }
    public SnapRelease FullPackageSnapRelease { get; init; }
    public MemoryStream DeltaPackageMemoryStream { get; init; }
    public SnapApp DeltaPackageSnapApp { get; init; }
    public SnapRelease DeltaPackageSnapRelease { get; init; }
    public string FullPackageAbsolutePath { get; init; }
    public string DeltaPackageAbsolutePath { get; init; }

    public void Dispose()
    {
        FullPackageMemoryStream?.Dispose();
        DeltaPackageMemoryStream?.Dispose();
    }

    public async Task<(string fullNupkgFilename, string deltaNupkgFilename)> WriteToAsync([NotNull] string directory,
        [NotNull] ISnapFilesystem snapFilesystem, CancellationToken cancellationToken, bool writeFullNupkg = true, bool writeDeltaNupkg = true)
    {
        if (directory == null) throw new ArgumentNullException(nameof(directory));
        if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));

        snapFilesystem.DirectoryExistsThrowIfNotExists(directory);

        var fullNupkgFilename =
            snapFilesystem.PathCombine(directory, snapFilesystem.PathGetFileName(FullPackageAbsolutePath));

        if (writeFullNupkg)
        {
            await snapFilesystem.FileWriteAsync(FullPackageMemoryStream, fullNupkgFilename, cancellationToken);
            FullPackageMemoryStream.Seek(0, SeekOrigin.Begin);
        }

        if (writeDeltaNupkg && !FullPackageSnapRelease.IsGenesis)
        {
            if (DeltaPackageMemoryStream == null)
            {
                throw new ArgumentNullException(nameof(DeltaPackageMemoryStream), $"Unable to write delta package to {directory}");
            }

            var deltaNupkgFilename =
                snapFilesystem.PathCombine(directory, snapFilesystem.PathGetFileName(DeltaPackageAbsolutePath));

            await snapFilesystem.FileWriteAsync(DeltaPackageMemoryStream, deltaNupkgFilename, cancellationToken);
            DeltaPackageMemoryStream.Seek(0, SeekOrigin.Begin);

            return (fullNupkgFilename, deltaNupkgFilename);
        }

        return (fullNupkgFilename, null);
    }
}

public class BaseFixturePackaging : BaseFixture
{
    internal SnapReleaseBuilder WithSnapReleaseBuilder(DisposableDirectory disposableDirectory, [NotNull] SnapAppsReleases snapAppsReleases, [NotNull] SnapApp snapApp, [NotNull] SnapReleaseBuilderContext builderContext)
    {
        if (snapAppsReleases == null) throw new ArgumentNullException(nameof(snapAppsReleases));
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        if (builderContext == null) throw new ArgumentNullException(nameof(builderContext));
        return new SnapReleaseBuilder(disposableDirectory, snapAppsReleases, snapApp, builderContext);
    }
    
    internal async Task<BuildPackageContext> BuildPackageAsync([NotNull] SnapReleaseBuilder releaseBuilder, CancellationToken cancellationToken = default)
    {
        if (releaseBuilder == null) throw new ArgumentNullException(nameof(releaseBuilder));
            
        var nuspecBaseDirectory = releaseBuilder.SnapFilesystem.PathCombine(releaseBuilder.NugetPackagingDirectory, "content");
        releaseBuilder.SnapFilesystem.DirectoryCreate(nuspecBaseDirectory);

        var snapPackDetails = new SnapPackageDetails
        {
            NuspecBaseDirectory = nuspecBaseDirectory,
            PackagesDirectory = releaseBuilder.SnapAppPackagesDirectory,
            SnapApp = releaseBuilder.SnapApp,
            SnapAppsReleases = releaseBuilder.SnapAppsReleases
        };

        foreach (var kv in releaseBuilder.GetNuspecItems())
        {
            var targetPath = kv.Key;
            var value = kv.Value;
            var destinationFilename = releaseBuilder.SnapFilesystem.PathCombine(snapPackDetails.NuspecBaseDirectory, targetPath);
            var destinationDirectory = releaseBuilder.SnapFilesystem.PathGetDirectoryName(destinationFilename);
            releaseBuilder.SnapFilesystem.DirectoryCreateIfNotExists(destinationDirectory);
                
            switch (value)
            {
                case AssemblyDefinition assemblyDefinition:
                    assemblyDefinition.Write(destinationFilename);
                    break;
                case Stream stream:
                {
                    await releaseBuilder.SnapFilesystem.FileWriteAsync(stream, destinationFilename, cancellationToken);
                    if (stream.CanSeek)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    }
                    break;
                }
                default:
                    throw new NotSupportedException($"{targetPath}: {value?.GetType().FullName}");
            }                
        }

        var (fullPackageMemoryStream, fullSnapApp, fullPackageSnapRelease, deltaPackageMemoryStream, deltaSnapApp, deltaSnapRelease) = await releaseBuilder.SnapPack
            .BuildPackageAsync(snapPackDetails, releaseBuilder.LibPal, cancellationToken);

        var fullPackageAbsolutePath = await releaseBuilder.WritePackageAsync(fullPackageMemoryStream, fullPackageSnapRelease, cancellationToken);
        fullPackageMemoryStream.Seek(0, SeekOrigin.Begin);

        string deltaPackageAbsolutePath = null;
        if (deltaSnapApp != null 
            && deltaPackageMemoryStream != null 
            && deltaSnapRelease != null)
        {
            deltaPackageAbsolutePath = await releaseBuilder.WritePackageAsync(deltaPackageMemoryStream, deltaSnapRelease, cancellationToken);
            deltaPackageMemoryStream.Seek(0, SeekOrigin.Begin);
        }
            
        return new BuildPackageContext
        {
            FullPackageMemoryStream = fullPackageMemoryStream,
            FullPackageSnapApp = fullSnapApp,
            FullPackageSnapRelease = fullPackageSnapRelease,
            FullPackageAbsolutePath = fullPackageAbsolutePath,
            DeltaPackageMemoryStream = deltaPackageMemoryStream,
            DeltaPackageSnapApp = deltaSnapApp,
            DeltaPackageSnapRelease = deltaSnapRelease,
            DeltaPackageAbsolutePath = deltaPackageAbsolutePath
        };
    }
}
