using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NuGet.Configuration;
using NuGet.Packaging;
using Snap.AnyOS;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface ISnapPackageManagerProgressSource
    {
        Action<(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

        Action<(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)>
            DownloadProgress { get; set; }

        Action<(int progressPercentage, long filesRestored, long filesToRestore)> RestoreProgress { get; set; }
        void RaiseChecksumProgress(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum);

        void RaiseDownloadProgress(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded,
            long totalBytesToDownload);

        void RaiseRestoreProgress(int progressPercentage, long filesRestored, long filesToRestore);
    }

    internal sealed class SnapPackageManagerProgressSource : ISnapPackageManagerProgressSource
    {
        public Action<(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

        public Action<(int progressPercentage, long releasesDownloaded,
            long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)> DownloadProgress { get; set; }

        public Action<(int progressPercentage, long filesRestored, long filesToRestore)> RestoreProgress { get; set; }

        public void RaiseChecksumProgress(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum)
        {
            ChecksumProgress?.Invoke((progressPercentage, releasesOk, releasesChecksummed, releasesToChecksum));
        }

        public void RaiseDownloadProgress(int progressPercentage, long releasesDownloaded,
            long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)
        {
            DownloadProgress?.Invoke((progressPercentage, releasesDownloaded, releasesToDownload, totalBytesDownloaded, totalBytesToDownload));
        }

        public void RaiseRestoreProgress(int progressPercentage, long filesRestored, long filesToRestore)
        {
            RestoreProgress?.Invoke((progressPercentage, filesRestored, filesToRestore));
        }
    }

    public enum SnapPackageManagerRestoreType
    {
        Default,
        Pack
    }

    internal interface ISnapPackageManager
    {
        Task<PackageSource> GetPackageSourceAsync(SnapApp snapApp, ILog logger = null, string applicationId = null);

        Task<(SnapAppsReleases snapAppsReleases, PackageSource packageSource, MemoryStream releasesMemoryStream)> GetSnapsReleasesAsync(
            [JetBrains.Annotations.NotNull] SnapApp snapApp, ILog logger = null, CancellationToken cancellationToken = default, string applicationId = null);

        Task<SnapPackageManagerRestoreSummary> RestoreAsync([JetBrains.Annotations.NotNull] string packagesDirectory, 
            [JetBrains.Annotations.NotNull] ISnapAppChannelReleases snapAppChannelReleases,
            [JetBrains.Annotations.NotNull] PackageSource packageSource, SnapPackageManagerRestoreType restoreType, 
            ISnapPackageManagerProgressSource progressSource = null, 
            ILog logger = null, CancellationToken cancellationToken = default, 
            int checksumConcurrency = 1, int downloadConcurrency = 2, int restoreConcurrency = 1);
    }

    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    internal class SnapPackageManagerNugetHttpFeed
    {
        public Uri Source { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string ApiKey { get; set; }
        public NuGetProtocolVersion ProtocolVersion { get; set; }
    }

    internal class SnapPackageManagerReleaseStatus
    {
        public SnapRelease SnapRelease { get; }
        public bool Ok { get; }

        public SnapPackageManagerReleaseStatus([JetBrains.Annotations.NotNull] SnapRelease snapRelease, bool ok)
        {
            SnapRelease = snapRelease ?? throw new ArgumentNullException(nameof(snapRelease));
            Ok = ok;
        }
    }

    internal sealed class SnapPackageManagerRestoreSummary
    {
        public SnapPackageManagerRestoreType RestoreType { get; }
        public List<SnapPackageManagerReleaseStatus> ChecksumSummary { get; private set; }
        public List<SnapPackageManagerReleaseStatus> DownloadSummary { get; private set; }
        public List<SnapPackageManagerReleaseStatus> ReassembleSummary { get; private set; }
        public bool Success { get; set; }

        public SnapPackageManagerRestoreSummary(SnapPackageManagerRestoreType restoreType)
        {
            RestoreType = restoreType;
            ChecksumSummary = new List<SnapPackageManagerReleaseStatus>();
            DownloadSummary = new List<SnapPackageManagerReleaseStatus>();
            ReassembleSummary = new List<SnapPackageManagerReleaseStatus>();
        }

        public void Sort()
        {
            ChecksumSummary = ChecksumSummary.OrderBy(x => x.SnapRelease.Version).ThenBy(x => x.SnapRelease.Filename).ToList();
            DownloadSummary = DownloadSummary.OrderBy(x => x.SnapRelease.Version).ThenBy(x => x.SnapRelease.Filename).ToList();
            ReassembleSummary = ReassembleSummary.OrderBy(x => x.SnapRelease.Version).ThenBy(x => x.SnapRelease.Filename).ToList();
        }
    }

    internal sealed class SnapPackageManager : ISnapPackageManager
    {
        readonly ISnapFilesystem _filesystem;
        readonly ISnapOsSpecialFolders _specialFolders;
        readonly INugetService _nugetService;
        [JetBrains.Annotations.NotNull] readonly ISnapHttpClient _snapHttpClient;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapPack _snapPack;

        public SnapPackageManager([JetBrains.Annotations.NotNull] ISnapFilesystem filesystem, 
            [JetBrains.Annotations.NotNull] ISnapOsSpecialFolders specialFolders,
            [JetBrains.Annotations.NotNull] INugetService nugetService, 
            [JetBrains.Annotations.NotNull] ISnapHttpClient snapHttpClient,
            [JetBrains.Annotations.NotNull] ISnapCryptoProvider snapCryptoProvider, 
            [JetBrains.Annotations.NotNull] ISnapExtractor snapExtractor,
            [JetBrains.Annotations.NotNull] ISnapAppReader snapAppReader, 
            [JetBrains.Annotations.NotNull] ISnapPack snapPack)
        {
            _filesystem = filesystem ?? throw new ArgumentNullException(nameof(filesystem));
            _specialFolders = specialFolders ?? throw new ArgumentNullException(nameof(specialFolders));
            _nugetService = nugetService ?? throw new ArgumentNullException(nameof(nugetService));
            _snapHttpClient = snapHttpClient ?? throw new ArgumentNullException(nameof(snapHttpClient));
            _snapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
            _snapExtractor = snapExtractor ?? throw new ArgumentNullException(nameof(snapExtractor));
            _snapAppReader = snapAppReader ?? throw new ArgumentNullException(nameof(snapAppReader));
            _snapPack = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
        }

        public async Task<PackageSource> GetPackageSourceAsync(
            [JetBrains.Annotations.NotNull] SnapApp snapApp, 
            ILog logger = null, 
            string applicationId = null)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            try
            {
                var channel = snapApp.GetCurrentChannelOrThrow();

                switch (channel.UpdateFeed)
                {
                    case SnapHttpFeed feed:
                    {
                        var headers = new Dictionary<string, string>
                        {
                            { "X-Snapx-App-Id", snapApp.Id},
                            { "X-Snapx-Channel", channel.Name },
                            { "X-Snapx-Application-Id", applicationId },
                            { "X-Snapx-Application-Version", snapApp.Version?.ToNormalizedString() }
                        };
   
                        using var stream = await _snapHttpClient.GetStreamAsync(feed.Source, headers);
                        stream.Seek(0, SeekOrigin.Begin);

                        var jsonStream = await stream.ReadToEndAsync();

                        var utf8String = Encoding.UTF8.GetString(jsonStream.ToArray());

                        var packageManagerNugetHttp = JsonConvert.DeserializeObject<SnapPackageManagerNugetHttpFeed>(utf8String, new JsonSerializerSettings
                        {
                            Converters = new List<JsonConverter>
                            {
                                new StringEnumConverter()
                            },
                            NullValueHandling = NullValueHandling.Ignore
                        });

                        if (packageManagerNugetHttp == null)
                        {
                            throw new Exception($"Unable to deserialize nuget http feed. Url: {feed.Source}. Response length: {stream.Position}");
                        }

                        var snapNugetFeed = new SnapNugetFeed(packageManagerNugetHttp)
                        {
                            Name = $"{snapApp.Id}-{channel.Name}-http"
                        };

                        return snapNugetFeed.BuildPackageSource(new NugetInMemorySettings(_specialFolders.NugetCacheDirectory));

                    }
                    case SnapNugetFeed snapNugetFeed:
                        var nugetPackageSources = snapApp.BuildNugetSources(_specialFolders.NugetCacheDirectory);

                        var packageSource = nugetPackageSources.Items.Single(x => x.Name == snapNugetFeed.Name
                                                                                  && x.SourceUri == snapNugetFeed.Source);
                        return packageSource;
                    default:
                        throw new NotSupportedException(channel.UpdateFeed?.GetType().FullName);
                }

            }
            catch(Exception e)
            {
                logger?.ErrorException("Unknown error building package source", e);  
                return null;
            }
        }

        public async Task<(SnapAppsReleases snapAppsReleases, PackageSource packageSource, MemoryStream releasesMemoryStream)> 
            GetSnapsReleasesAsync(
            SnapApp snapApp, ILog logger = null, CancellationToken cancellationToken = default, string applicationId = null)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            var packageId = snapApp.BuildNugetReleasesUpstreamId();

            try
            {
                var packageSource = await GetPackageSourceAsync(snapApp, logger, applicationId);

                var snapReleasesDownloadResult =
                    await _nugetService.DownloadLatestAsync(packageId, packageSource, cancellationToken, false, true);

                if (!snapReleasesDownloadResult.SuccessSafe())
                {
                    var sourceLocation = packageSource.IsLocalOrUncPath()
                        ? $"path: {_filesystem.PathGetFullPath(packageSource.SourceUri.AbsolutePath)}. Does the location exist?"
                        : packageSource.Name;
                    logger?.Error($"Unknown error while downloading releases nupkg {packageId} from {sourceLocation}");
                    return (null, null, null);
                }

                using var packageArchiveReader = new PackageArchiveReader(snapReleasesDownloadResult.PackageStream, true);
                var snapReleases = await _snapExtractor.GetSnapAppsReleasesAsync(packageArchiveReader, _snapAppReader, cancellationToken);
                if (snapReleases != null)
                {
                    snapReleasesDownloadResult.PackageStream.Seek(0, SeekOrigin.Begin);
                    return (snapReleases, packageSource, (MemoryStream) snapReleasesDownloadResult.PackageStream);
                }

                logger?.Error($"Unknown error unpacking releases nupkg: {packageId}.");
                return (null, null, null);
            }
            catch (Exception e)
            {
                logger?.ErrorException($"Exception thrown while downloading releases nupkg: {packageId}.", e);
                return (null, null, null);
            }
        }

        public async Task<SnapPackageManagerRestoreSummary> RestoreAsync(string packagesDirectory, ISnapAppChannelReleases snapAppChannelReleases,
            PackageSource packageSource, SnapPackageManagerRestoreType restoreType, ISnapPackageManagerProgressSource progressSource = null, 
            ILog logger = null, CancellationToken cancellationToken = default,  int checksumConcurrency = 1, int downloadConcurrency = 2, int restoreConcurrency = 1)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (checksumConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(checksumConcurrency));
            if (downloadConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(downloadConcurrency));
            if (restoreConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(restoreConcurrency));

            var restoreSummary = new SnapPackageManagerRestoreSummary(restoreType);

            var genesisRelease = snapAppChannelReleases.GetGenesisRelease();
            if (genesisRelease == null)
            {
                logger?.Info($"Nothing to restore. Genesis release does not exist in channel: {snapAppChannelReleases.Channel.Name}.");

                restoreSummary.Success = true;
                return restoreSummary;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            // Checksum
            await ChecksumAsync();
            restoreSummary.Sort();
            restoreSummary.Success = restoreSummary.ChecksumSummary.Count > 0 
                    && restoreSummary.ChecksumSummary.All(x => x.Ok);                    

            // Download
            stopwatch.Restart();
            restoreSummary.Success = await DownloadAsync();

            // Reassamble
            stopwatch.Restart();
            restoreSummary.Success = restoreSummary.Success && await ReassembleAsync();
            restoreSummary.Sort();
            
            return restoreSummary;

            async Task ChecksumAsync()
            {
                var snapReleasesToChecksum = new List<SnapRelease>();

                switch (restoreType)
                {
                    case SnapPackageManagerRestoreType.Default:
                        snapReleasesToChecksum.AddRange(snapAppChannelReleases.Where(x => x.IsGenesis || x.IsDelta));
                        if (snapAppChannelReleases.HasDeltaReleases())
                        {
                            snapReleasesToChecksum.Add(snapAppChannelReleases.Last().AsFullRelease(false));
                        }
                        break;
                    case SnapPackageManagerRestoreType.Pack:
                        snapReleasesToChecksum.AddRange(snapAppChannelReleases.Where(x => x.IsGenesis || x.IsDelta));
                        break;
                    default:
                        throw new NotSupportedException(restoreType.ToString());
                }
                
                logger?.Info($"Verifying checksums for {snapReleasesToChecksum.Count} packages.");

                long snapReleasesChecksummed = 0;
                long snapReleasesChecksumOk = 0;
                long totalSnapReleasesToChecksum = snapReleasesToChecksum.Count;

                progressSource?.RaiseChecksumProgress(0,
                    0, snapReleasesChecksummed, totalSnapReleasesToChecksum);

                logger?.Info("Checksum progress: 0%");

                await snapReleasesToChecksum.ForEachAsync(snapRelease =>
                {
                    return Task.Run(() =>
                    {
                        long checksumOkCount = 0;
                        try
                        {
                            var nupkgAbsolutePath = _filesystem.PathCombine(packagesDirectory, snapRelease.Filename);
                            var checksumOk = TryChecksum(snapRelease, nupkgAbsolutePath, logger: logger);
                            restoreSummary.ChecksumSummary.Add(new SnapPackageManagerReleaseStatus(snapRelease, checksumOk));
                            checksumOkCount = checksumOk ? Interlocked.Increment(ref snapReleasesChecksumOk) : Interlocked.Read(ref snapReleasesChecksumOk);
                        }
                        catch (Exception e)
                        {
                            logger?.ErrorException($"Unknown error while checksumming: {snapRelease.Filename}", e);
                        }

                        var totalSnapReleasesChecksummedSoFar = Interlocked.Increment(ref snapReleasesChecksummed);
                        var totalProgressPercentage = (int) Math.Floor(totalSnapReleasesChecksummedSoFar / (double) totalSnapReleasesToChecksum * 100);
                        
                        progressSource?.RaiseChecksumProgress(totalProgressPercentage, checksumOkCount, 
                            snapReleasesChecksummed, totalSnapReleasesToChecksum);

                        if (totalProgressPercentage % 5 == 0 
                            || totalSnapReleasesToChecksum <= 5)
                        {
                            logger?.Info($"Checksum progress: {totalProgressPercentage}% - Completed {snapReleasesChecksummed} of {totalSnapReleasesToChecksum}.");
                        }

                    }, cancellationToken);
                }, checksumConcurrency);

                logger?.Info($"Checksummed {snapReleasesChecksummed} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s. ");
            }

            async Task<bool> DownloadAsync()
            {
                var releasesToDownload = restoreSummary.ChecksumSummary
                    .Where(x => !x.Ok && (x.SnapRelease.IsGenesis || x.SnapRelease.IsDelta))
                    .Select(x => x.SnapRelease)
                    .OrderBy(x => x.Version)
                    .ToList();                    
                if (!releasesToDownload.Any())
                {
                    return true;
                }
                
                var totalBytesToDownload = releasesToDownload.Sum(x => x.IsFull ? x.FullFilesize : x.DeltaFilesize);

                logger?.Info($"Downloading {releasesToDownload.Count} packages. " +
                             $"Total download size: {totalBytesToDownload.BytesAsHumanReadable()}.");

                if (_filesystem.DirectoryCreateIfNotExists(packagesDirectory))
                {
                    logger?.Debug($"Created packages directory: {packagesDirectory}");
                }

                long totalReleasesToDownload = releasesToDownload.Count;
                long totalReleasesDownloaded = default;
                long totalBytesDownloadedSoFar = default;
                long downloadProgressPercentage = default;
                var previousProgressReportDateTime = DateTime.UtcNow;

                progressSource?.RaiseDownloadProgress(0, 0,
                    totalReleasesToDownload, 0, totalBytesToDownload);

                logger?.Info($"Download progress: 0% - Transferred 0 bytes of {totalBytesToDownload.BytesAsHumanReadable()}");

                await releasesToDownload.ForEachAsync(async snapRelease =>
                {
                    var thisProgressSource = new NugetServiceProgressSource
                    {
                        Progress = tuple =>
                        {
                            var (progressPercentage, bytesRead, _, _) = tuple;

                            var totalReleasesDownloadedVolatile = Interlocked.Read(ref totalReleasesDownloaded);
                            var totalBytesDownloadedSoFarVolatile = Interlocked.Add(ref totalBytesDownloadedSoFar, bytesRead);

                            if (progressPercentage == 100)
                            {
                                totalReleasesDownloadedVolatile = Interlocked.Increment(ref totalReleasesDownloaded);
                            }

                            var totalBytesDownloadedPercentage = (int) Math.Floor(
                                (double) totalBytesDownloadedSoFarVolatile / totalBytesToDownload * 100d);

                            Interlocked.Exchange(ref downloadProgressPercentage, totalBytesDownloadedPercentage);

                            progressSource?.RaiseDownloadProgress(totalBytesDownloadedPercentage,
                                totalReleasesDownloadedVolatile, totalReleasesToDownload,
                                totalBytesDownloadedSoFarVolatile, totalBytesToDownload);

                            if (progressPercentage < 100
                                && DateTime.UtcNow - previousProgressReportDateTime <= TimeSpan.FromSeconds(0.5))
                            {
                                return;
                            }

                            previousProgressReportDateTime = DateTime.UtcNow;

                            if (totalBytesDownloadedPercentage % 5 == 0
                                || totalReleasesToDownload <= 5)
                            {
                                logger?.Info($"Download progress: {totalBytesDownloadedPercentage}% - Transferred " +
                                             $"{totalBytesDownloadedSoFarVolatile.BytesAsHumanReadable()} of " +
                                             $"{totalBytesToDownload.BytesAsHumanReadable()}.");
                            }
                        }
                    };

                    var success = await TryDownloadAsync(packagesDirectory, snapAppChannelReleases,
                        snapRelease, packageSource, thisProgressSource, logger, cancellationToken);

                    restoreSummary.DownloadSummary.Add(new SnapPackageManagerReleaseStatus(snapRelease, success));
                }, downloadConcurrency);

                var downloadedReleases = restoreSummary.DownloadSummary.Where(x => x.Ok).Select(x => x.SnapRelease).ToList();
                var allReleasesDownloaded = releasesToDownload.Count == downloadedReleases.Count;
                if (!allReleasesDownloaded)
                {
                    logger?.Error("Error downloading one or multiple packages. " +
                                  $"Downloaded {downloadedReleases.Count} of {releasesToDownload.Count}. " +
                                  $"Operation completed in {stopwatch.Elapsed.TotalSeconds:0.0}s.");
                    return false;
                }

                logger?.Info($"Downloaded {downloadedReleases.Count} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s.");
                return true;
            }

            async Task<bool> ReassembleAsync()
            {
                SnapAppChannelReleases releasesToReassemble;

                switch (restoreType)
                {
                    case SnapPackageManagerRestoreType.Default:                    
                        var snapReleases = new List<SnapRelease>();

                        var mostRecentDeltaSnapRelease = snapAppChannelReleases.GetDeltaReleases().LastOrDefault();                                
                        if (restoreSummary.ChecksumSummary.Any(x => !x.Ok) 
                            && mostRecentDeltaSnapRelease != null)
                        {
                            var mostRecentFullSnapRelease = restoreSummary.ChecksumSummary.SingleOrDefault(x =>
                                x.Ok 
                                && x.SnapRelease.IsFull 
                                && x.SnapRelease.Version == mostRecentDeltaSnapRelease.Version
                            );
                                    
                            if (mostRecentFullSnapRelease == null)
                            {
                                snapReleases.Add(mostRecentDeltaSnapRelease);
                            }
                        }
                                                    
                        releasesToReassemble = new SnapAppChannelReleases(snapAppChannelReleases, snapReleases);
                        break;
                    case SnapPackageManagerRestoreType.Pack:
                        // Noop
                        releasesToReassemble = new SnapAppChannelReleases(snapAppChannelReleases, new List<SnapRelease>());
                        break;
                    default:
                        throw new NotSupportedException(restoreType.ToString());
                }

                if (!releasesToReassemble.Any())
                {
                    return true;
                }
                
                logger?.Info($"Reassembling {releasesToReassemble.Count()} packages: {string.Join(", ", releasesToReassemble.Select(x => x.BuildNugetFullFilename()))}.");
                
                var success = true;

                var genesisSnapRelease = snapAppChannelReleases.GetGenesisRelease();
                var newestVersion = releasesToReassemble.Max(x => x.Version);

                long releasesReassembled = default;
                var totalFilesToRestore = genesisSnapRelease.Files.Count + 
                                          snapAppChannelReleases
                                            .Where(x => x.Version > genesisSnapRelease.Version && x.Version <= newestVersion)
                                            .Sum(x => x.New.Count + x.Modified.Count);
                var totalFilesRestored = 0L;
                int totalRestorePercentage;

                var compoundProgressSource = new RebuildPackageProgressSource();
                compoundProgressSource.Progress += tuple =>
                {
                    if(tuple.filesRestored == 0) return;
                    totalRestorePercentage = (int) Math.Ceiling((double) ++totalFilesRestored / totalFilesToRestore * 100);
                    progressSource?.RaiseRestoreProgress(totalRestorePercentage, totalFilesRestored, totalFilesToRestore);
                };

                progressSource?.RaiseRestoreProgress(0, 0, totalFilesToRestore);

                await releasesToReassemble.ForEachAsync(async x =>
                {
                    try
                    {
                        var (fullNupkgMemoryStream, _, fullSnapRelease) =
                            await _snapPack.RebuildPackageAsync(packagesDirectory, snapAppChannelReleases, x, compoundProgressSource, cancellationToken);

                        using (fullNupkgMemoryStream)
                        {
                            var fullNupkgAbsolutePath = _filesystem.PathCombine(packagesDirectory, fullSnapRelease.Filename);
                            await _filesystem.FileWriteAsync(fullNupkgMemoryStream, fullNupkgAbsolutePath, cancellationToken);

                            var releasesReassembledSoFarVolatile = Interlocked.Increment(ref releasesReassembled);
                            restoreSummary.ReassembleSummary.Add(new SnapPackageManagerReleaseStatus(fullSnapRelease, true));

                            logger?.Debug($"Successfully restored {releasesReassembledSoFarVolatile} of {releasesToReassemble.Count()}.");
                        }
                    }
                    catch (Exception e)
                    {
                        restoreSummary.ReassembleSummary.Add(new SnapPackageManagerReleaseStatus(x, false));
                        logger?.ErrorException($"Error reassembling full nupkg: {x.BuildNugetFullFilename()}", e);
                        success = false;
                    }
                }, restoreConcurrency);

                logger?.Info($"Reassembled {releasesToReassemble.Count()} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

                return success;
            }
        }

        async Task<bool> TryDownloadAsync([JetBrains.Annotations.NotNull] string packagesDirectory, [JetBrains.Annotations.NotNull] ISnapAppChannelReleases snapAppChannelReleases, [JetBrains.Annotations.NotNull] SnapRelease snapRelease,
            [JetBrains.Annotations.NotNull] PackageSource packageSource, INugetServiceProgressSource progressSource, ILog logger = null,
            CancellationToken cancellationToken = default)
        {
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));

            var restoreStopwatch = new Stopwatch();
            restoreStopwatch.Restart();

            var nupkgFileSize = snapRelease.IsFull ? snapRelease.FullFilesize : snapRelease.DeltaFilesize;
            var nupkgChecksum = snapRelease.IsFull ? snapRelease.FullSha256Checksum : snapRelease.DeltaSha256Checksum;

            logger?.Debug($"Downloading nupkg: {snapRelease.Filename}. " +
                          $"File size: {nupkgFileSize.BytesAsHumanReadable()}. " +
                          $"Package source: {packageSource.Name}.");

            try
            {
                var downloadContext = new DownloadContext(snapRelease);

                var downloadResult = await _nugetService
                    .DownloadAsyncWithProgressAsync(packageSource, downloadContext, progressSource, cancellationToken);

                using (downloadResult)
                {
                    if (!downloadResult.SuccessSafe())
                    {
                        logger?.Error($"Unknown error downloading nupkg: {snapRelease.Filename}.");
                        return false;
                    }
                                        
                    logger?.Debug($"Downloaded nupkg: {snapRelease.Filename}. Flushing to disk.");

                    var dstFilename = _filesystem.PathCombine(packagesDirectory, snapRelease.Filename);
                    await _filesystem.FileWriteAsync(downloadResult.PackageStream, dstFilename, cancellationToken);

                    downloadResult.PackageStream.Seek(0, SeekOrigin.Begin);            
                    
                    logger?.Debug("Nupkg flushed to disk. Verifying checksum!");

                    using (var packageArchiveReader = new PackageArchiveReader(downloadResult.PackageStream, true))
                    {
                        var downloadChecksum = _snapCryptoProvider.Sha256(snapRelease, packageArchiveReader, _snapPack);
                        if (downloadChecksum != nupkgChecksum)
                        {
                            logger?.Error($"Checksum mismatch for downloaded nupkg: {snapRelease.Filename}");
                            return false;
                        }                        
                    }
                    
                    logger?.Debug($"Successfully verified checksum for downloaded nupkg: {snapRelease.Filename}.");

                    return true;
                }
            }
            catch (Exception e)
            {
                logger?.ErrorException($"Unknown error downloading nupkg: {snapRelease.Filename}", e);
                return false;
            }
        }

        bool TryChecksum([JetBrains.Annotations.NotNull] SnapRelease snapRelease, string nupkgAbsoluteFilename, bool silent = false, ILog logger = null)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));

            try
            {
                var filename = _filesystem.PathGetFileName(nupkgAbsoluteFilename);
                if (!_filesystem.FileExists(nupkgAbsoluteFilename))
                {
                    logger?.Warn($"Checksum error - File does not exist: {filename}. This is not fatal! Package will be either downloaded from NuGet server or reassembled.");
                    return false;
                }

                using var packageArchiveReader = new PackageArchiveReader(nupkgAbsoluteFilename);
                if (!silent)
                {
                    logger?.Debug($"Starting to checksum: {filename}.");
                }

                var sha256Checksum = _snapCryptoProvider.Sha256(snapRelease, packageArchiveReader, _snapPack);
                if (snapRelease.IsFull)
                {
                    if (sha256Checksum == snapRelease.FullSha256Checksum)
                    {
                        if (!silent)
                        {
                            logger?.Debug($"Checksum success: {filename}.");
                        }
                        return true;
                    }
                }
                else if (snapRelease.IsDelta)
                {
                    if (sha256Checksum == snapRelease.DeltaSha256Checksum)
                    {
                        if (!silent)
                        {
                            logger?.Debug($"Checksum success: {filename}.");
                        }
                        return true;
                    }
                }
                else
                {
                    throw new NotSupportedException($"Unknown package type: {snapRelease.Filename}");
                }

                logger?.Error($"Checksum mismatch: {filename}.");
                    
                return false;
            }
            catch (Exception e)
            {
                logger?.ErrorException($"Unknown error checksumming file: {snapRelease.Filename}.", e);
            }
            return false;
        }
                
    }
}
