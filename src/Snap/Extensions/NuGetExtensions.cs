using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using JetBrains.Annotations;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Snap.Core.Models;
using Snap.NuGet;

namespace Snap.Extensions;

internal static class NuGetExtensions
{
    public static bool IsUncPathSafe(this PackageSource packageSource)
    {
        try
        {
            return packageSource != null 
                   && packageSource.SourceUri != null
                   && packageSource.SourceUri.IsUnc
                   && packageSource.SourceUri.IsAbsoluteUri;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsLocalPathSafe(this PackageSource packageSource)
    {
        try
        {
            return packageSource != null 
                   && packageSource.SourceUri != null
                   && packageSource.SourceUri.IsAbsoluteUri
                   && packageSource.IsLocal;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsLocalOrUncPath(this PackageSource packageSource)
    {
        return packageSource.IsLocalPathSafe() || packageSource.IsUncPathSafe();
    }

    public static XElement SingleOrDefault([NotNull] this XDocument xDocument, [NotNull] XName name, bool ignoreCase = true)
    {
        if (xDocument == null) throw new ArgumentNullException(nameof(xDocument));
        if (name == null) throw new ArgumentNullException(nameof(name));
        return xDocument.Descendants().SingleOrDefault(name, ignoreCase);
    }

    public static XElement SingleOrDefault([NotNull] this IEnumerable<XElement> xElements, [NotNull] XName name, bool ignoreCase = true)
    {
        if (xElements == null) throw new ArgumentNullException(nameof(xElements));
        if (name == null) throw new ArgumentNullException(nameof(name));
        return (
                from node in xElements
                let comperator = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
                where
                    string.Equals(node.Name.LocalName, name.LocalName, comperator)
                    && string.Equals(node.Name.NamespaceName, name.NamespaceName, comperator)
                select node)
            .FirstOrDefault();
    }

    internal static async Task<Uri> BuildDownloadUrlV3Async([NotNull] this DownloadResourceV3 downloadResourceV3, [NotNull] PackageIdentity identity,
        [NotNull] ILogger logger, CancellationToken token)
    {
        if (downloadResourceV3 == null) throw new ArgumentNullException(nameof(downloadResourceV3));
        if (identity == null) throw new ArgumentNullException(nameof(identity));
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        if (downloadResourceV3 == null) throw new ArgumentNullException(nameof(downloadResourceV3));

        var type = downloadResourceV3.GetType();

        const string getDownloadUrlMethodName = "GetDownloadUrl";
        var getDownloadUrlMethod = type.GetMethod(getDownloadUrlMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (getDownloadUrlMethod == null)
        {
            throw new MissingMethodException(getDownloadUrlMethodName);
        }

        var getDownloadUrlTask = (dynamic) getDownloadUrlMethod.Invoke(downloadResourceV3, [identity, logger, token]);
        if (getDownloadUrlTask != null)
        {
            var downloadUri = await getDownloadUrlTask;

            return downloadUri as Uri;
        }

        return null;
    }

    internal static HttpSource BuildHttpSource([NotNull] this DownloadResourceV3 downloadResourceV3)
    {
        if (downloadResourceV3 == null) throw new ArgumentNullException(nameof(downloadResourceV3));

        var type = downloadResourceV3.GetType();

        const string httpSourcePrivateReadonlyFieldName = "_client";
        var httpSource = type.GetField(httpSourcePrivateReadonlyFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (httpSource == null)
        {
            throw new MissingFieldException(httpSourcePrivateReadonlyFieldName);
        }

        return httpSource.GetValue(downloadResourceV3) as HttpSource;
    }

    internal static HttpSource BuildHttpSource([NotNull] this V2FeedParser v2FeedParser)
    {
        if (v2FeedParser == null) throw new ArgumentNullException(nameof(v2FeedParser));

        var type = v2FeedParser.GetType();

        const string httpSourcePrivateReadonlyFieldName = "_httpSource";
        var httpSource = type.GetField(httpSourcePrivateReadonlyFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (httpSource == null)
        {
            throw new MissingFieldException(httpSourcePrivateReadonlyFieldName);
        }

        return httpSource.GetValue(v2FeedParser) as HttpSource;
    }

    internal static V2FeedParser BuildV2FeedParser([NotNull] this DownloadResourceV2Feed downloadResourceV2Feed)
    {
        if (downloadResourceV2Feed == null) throw new ArgumentNullException(nameof(downloadResourceV2Feed));

        var type = downloadResourceV2Feed.GetType();

        const string feedParserPrivateReadonlyFieldName = "_feedParser";
        var v2FeedParser = type.GetField(feedParserPrivateReadonlyFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (v2FeedParser == null)
        {
            throw new MissingFieldException(feedParserPrivateReadonlyFieldName);
        }

        return v2FeedParser.GetValue(downloadResourceV2Feed) as V2FeedParser;
    }

    internal static PackageIdentity BuildPackageIdentity([NotNull] this SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        return new PackageIdentity(snapRelease.UpstreamId, snapRelease.Version.ToNuGetVersion());
    }

    internal static PackageIdentity BuildPackageIdentity([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return new PackageIdentity(snapApp.BuildNugetUpstreamId(), snapApp.Version.ToNuGetVersion());
    }

    internal static NuGetPackageSearchMedatadata BuildPackageSearchMetadata([NotNull] this SnapApp snapApp,
        [NotNull] INuGetPackageSources nugetSources)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        if (nugetSources == null) throw new ArgumentNullException(nameof(nugetSources));

        var channel = snapApp.GetCurrentChannelOrThrow();
        var updateFeed = (SnapNugetFeed) channel.UpdateFeed;
        var packageSource = nugetSources.Items.Single(x => x.Name == updateFeed.Name && x.SourceUri == updateFeed.Source);

        return new NuGetPackageSearchMedatadata(snapApp.BuildPackageIdentity(), packageSource, DateTimeOffset.Now, new List<PackageDependency>());
    }

    internal static DownloadResourceResult BuildDownloadResourceResult([NotNull] this SnapApp snapApp,
        [NotNull] MemoryStream packageStream, [NotNull] INuGetPackageSources nugetSources)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));
        if (nugetSources == null) throw new ArgumentNullException(nameof(nugetSources));

        var channel = snapApp.GetCurrentChannelOrThrow();
        var updateFeed = (SnapNugetFeed) channel.UpdateFeed;
        var packageSource = nugetSources.Items.Single(x => x.Name == updateFeed.Name && x.SourceUri == updateFeed.Source);

        return new DownloadResourceResult(new MemoryStream(packageStream.ToArray()), new PackageArchiveReader(packageStream), packageSource.Name);
    }

    internal static bool SuccessSafe(this DownloadResourceResult downloadResourceResult)
    {
        return downloadResourceResult != null && downloadResourceResult.Status == DownloadResourceResultStatus.Available;
    }

    public static bool RemovePackageFile([NotNull] this PackageBuilder packageBuilder, [NotNull] string targetPath, StringComparison stringComparisonType)
    {
        if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
        if (targetPath == null) throw new ArgumentNullException(nameof(targetPath));
        var packageFile = packageBuilder.GetPackageFile(targetPath, stringComparisonType);
        return packageFile != null && packageBuilder.Files.Remove(packageFile);
    }

    public static IPackageFile GetPackageFile([NotNull] this PackageBuilder packageBuilder, [NotNull] string filename, StringComparison stringComparisonType)
    {
        if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
        if (filename == null) throw new ArgumentNullException(nameof(filename));

        return packageBuilder.Files.SingleOrDefault(x => string.Equals(
            filename.ForwardSlashesSafe(),
            x.Path.ForwardSlashesSafe(),
            stringComparisonType));
    }

    internal static async Task<ManifestMetadata> GetManifestMetadataAsync([NotNull] this IAsyncPackageCoreReader asyncPackageCoreReader,
        CancellationToken cancellationToken)
    {
        if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
        await using var nuspecStream = await asyncPackageCoreReader.GetNuspecAsync(cancellationToken).ReadToEndAsync(cancellationToken: cancellationToken);
        return Manifest.ReadFrom(nuspecStream, false)?.Metadata;
    }

    static bool IsPasswordEncryptionSupportedImpl()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    internal static bool IsPasswordEncryptionSupported([NotNull] this SnapNugetFeed nugetFeed)
    {
        if (nugetFeed == null) throw new ArgumentNullException(nameof(nugetFeed));
        return IsPasswordEncryptionSupportedImpl();
    }

    internal static bool IsPasswordEncryptionSupported([NotNull] this ISettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        return IsPasswordEncryptionSupportedImpl();
    }

    internal static bool IsPasswordEncryptionSupported([NotNull] this INuGetPackageSources packageSources)
    {
        if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));
        return IsPasswordEncryptionSupportedImpl();
    }
}