using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using SharpCompress.Readers;
using Snap.AnyOS;
using Snap.Core.Json;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Core;

public sealed record SnapReleaseDetails
{
    [JsonInclude, JsonConverter(typeof(SemanticVersionSystemTextJsonConverter))]
    public SemanticVersion Version { get; init; }
    [JsonInclude]
    public string Channel { get; init; }
    [JsonInclude]
    public DateTime CreatedDateUtc { get; init; }

    public SnapReleaseDetails()
    {
        
    }

    public SnapReleaseDetails([NotNull] string channel, [NotNull] SnapRelease release)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(release);
        Version = release.Version;
        Channel = channel;
        CreatedDateUtc = release.CreatedDateUtc;
    }
}

public interface ISnapNugetService
{
    Task<List<SnapReleaseDetails>> GetLatestReleasesAsync([NotNull] string applicationName, [NotNull] string rid,
        [NotNull] SnapNugetFeed nugetFeed, CancellationToken cancellationToken);
}

public sealed class SnapNugetService : ISnapNugetService
{
    readonly ISnapFilesystem _fileSystem;
    readonly ISnapAppReader _appReader;
    readonly INugetService _nugetService;
    readonly ISnapOsSpecialFolders _specialFolders;
    readonly Func<MemoryStream> _memoryStreamAllocator;
    readonly ILog _logger;

    public SnapNugetService([NotNull] ISnapNugetLogger snapNugetManagerLogger, [CanBeNull] Func<MemoryStream> memoryStreamAllocator = null) : this(
        Snapx.SnapOs.Filesystem, new SnapAppReader(), new NugetService(Snapx.SnapOs.Filesystem, snapNugetManagerLogger), Snapx.SnapOs.SpecialFolders, memoryStreamAllocator) => 
        ArgumentNullException.ThrowIfNull(snapNugetManagerLogger);

    internal SnapNugetService(ISnapFilesystem fileSystem, ISnapAppReader appReader, INugetService nugetService, ISnapOsSpecialFolders specialFolders, [CanBeNull] Func<MemoryStream> memoryStreamAllocator = null)
    {
        _logger = LogProvider.For<SnapNugetService>();
        _fileSystem = fileSystem;
        _appReader = appReader;
        _nugetService = nugetService;
        _specialFolders = specialFolders;
        _memoryStreamAllocator = memoryStreamAllocator ?? (() => new MemoryStream());
    }
    
    public async Task<List<SnapReleaseDetails>> GetLatestReleasesAsync(string applicationName,
        string rid,
        SnapNugetFeed nugetFeed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applicationName);
        ArgumentNullException.ThrowIfNull(rid);
        ArgumentNullException.ThrowIfNull(nugetFeed);
        
        var packageSource = nugetFeed.BuildNugetSources(_specialFolders.NugetCacheDirectory).Single();
        var packageId = $"{applicationName}-{rid}_snapx";
        
        var snapReleasesDownloadResult =
            await _nugetService.DownloadLatestAsync(packageId, packageSource, false, true, cancellationToken);

        var sourceLocation = packageSource.IsLocalOrUncPath()
            ? $"path: {_fileSystem.PathGetFullPath(packageSource.SourceUri.AbsolutePath)}. Does the location exist?"
            : packageSource.Name;

        if (!snapReleasesDownloadResult.SuccessSafe())
        {
            _logger.Error($"Unknown error while downloading releases nupkg {packageId} from {sourceLocation}. Status: {snapReleasesDownloadResult.Status}.");
            return [];
        }

        using var packageArchiveReader = new PackageArchiveReader(snapReleasesDownloadResult.PackageStream, true);
        var snapReleases = await GetReleasesAsync(packageArchiveReader, cancellationToken);
        if (snapReleases == null)
        {
            _logger.Error($"Unknown error while reading releases nupkg {packageId} from {sourceLocation}. Status: {snapReleasesDownloadResult.Status}");
            return [];
        }

        snapReleasesDownloadResult.PackageStream.Seek(0, SeekOrigin.Begin);

        var channels = snapReleases.Releases.SelectMany(x => x.Channels).ToHashSet();

        var releases = new List<SnapReleaseDetails>();

        foreach (var channel in channels)
        {
            var mostRecentRelease = snapReleases.Where(x => x.Channels.Contains(channel)).MaxBy(x => x.Version);
            if (mostRecentRelease == null)
            {
                continue;
            }

            releases.Add(new SnapReleaseDetails(channel, mostRecentRelease));
        }

        return releases;
    }
    
    async Task<SnapAppsReleases> GetReleasesAsync([NotNull] IAsyncPackageCoreReader packageArchiveReader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageArchiveReader);

        var snapReleasesFilename = _fileSystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.ReleasesFilename);
        await using var snapReleasesCompressedStream =
            await packageArchiveReader
                .GetStreamAsync(snapReleasesFilename, cancellationToken)
                .ReadToEndAsync(cancellationToken: cancellationToken);
        await using var snapReleasesUncompressedStream = _memoryStreamAllocator();
        using var reader = ReaderFactory.Open(snapReleasesCompressedStream);
        reader.MoveToNextEntry();
        reader.WriteEntryTo(snapReleasesUncompressedStream);
        snapReleasesUncompressedStream.Seek(0, SeekOrigin.Begin);
        return await _appReader.BuildSnapAppsReleasesFromStreamAsync(snapReleasesUncompressedStream);
    }
}
