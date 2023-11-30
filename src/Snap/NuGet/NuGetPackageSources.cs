using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Configuration;
using Snap.Core;
using Snap.Logging;

namespace Snap.NuGet;

internal class NuGetMachineWideSettings : IMachineWideSettings
{
    readonly Lazy<ISettings> _settings;

    public ISettings Settings => _settings.Value;

    public NuGetMachineWideSettings([NotNull] ISnapFilesystem filesystem, [NotNull] string workingDirectory, ILog logger = null)
    {
        if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
        if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

        logger ??= LogProvider.For<NuGetMachineWideSettings>();

        // https://github.com/NuGet/NuGet.Client/blob/8cb7886a7e9052308cfa51308f6f901c7caf5004/src/NuGet.Core/NuGet.Commands/SourcesCommands/SourceRunners.cs#L102

        _settings = new Lazy<ISettings>(() =>
        {
            ISettings settings;
            try
            {
                settings = global::NuGet.Configuration.Settings.LoadDefaultSettings(workingDirectory,
                    configFileName: null,
                    machineWideSettings: new XPlatMachineWideSetting());
            }
            catch (NuGetConfigurationException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                logger.ErrorException("Error loading machine wide settings", ex.InnerException ?? ex);
                return new NullSettings();
            }

            return settings;
        });
    }
}

internal sealed class NugetOrgOfficialV2PackageSources()
    : NuGetPackageSources(new NullSettings(), new List<PackageSource> { PackageSourceV2 })
{
    static readonly PackageSource PackageSourceV2 =
        new(NuGetConstants.V2FeedUrl, "nuget.org", true, true, false)
        {
            ProtocolVersion = (int) NuGetProtocolVersion.V2,
            IsMachineWide = true
        };
}

internal sealed class NugetOrgOfficialV3PackageSources()
    : NuGetPackageSources(new NullSettings(), new List<PackageSource> { PackageSourceV3 })
{
    static readonly PackageSource PackageSourceV3 =
        new(NuGetConstants.V3FeedUrl, "nuget.org", true, true, false)
        {
            ProtocolVersion = (int) NuGetProtocolVersion.V3,
            IsMachineWide = true
        };
}

internal class NuGetMachineWidePackageSources : NuGetPackageSources
{
    public NuGetMachineWidePackageSources([NotNull] ISnapFilesystem filesystem, [NotNull] string workingDirectory)
    {
        if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
        if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

        var nugetMachineWideSettings = new NuGetMachineWideSettings(filesystem, workingDirectory);
        var packageSources = new List<PackageSource>();

        var nugetConfigReader = new NuGetConfigFileReader();
        foreach (var packageSource in nugetConfigReader.ReadNugetSources(workingDirectory).Where(x => x.IsEnabled))
        {
            if (!packageSources.Contains(packageSource))
            {
                packageSources.Add(packageSource);
            }
        }

        Items = packageSources;
        Settings = nugetMachineWideSettings.Settings;
    }
}

internal class NuGetInMemoryPackageSources(string tempDirectory, IReadOnlyCollection<PackageSource> packageSources)
    : NuGetPackageSources(new NugetInMemorySettings(tempDirectory), packageSources)
{
    public NuGetInMemoryPackageSources(string tempDirectory, PackageSource packageSource) : this(tempDirectory, new List<PackageSource> { packageSource })
    {
            
    }
}

internal interface INuGetPackageSources : IEnumerable<PackageSource>
{
    ISettings Settings { get; }
    IReadOnlyCollection<PackageSource> Items { get; }
}

internal class NuGetPackageSources([NotNull] ISettings settings, [NotNull] IReadOnlyCollection<PackageSource> sources)
    : INuGetPackageSources
{
    public ISettings Settings { get; protected set; } = settings ?? throw new ArgumentNullException(nameof(settings));
    public IReadOnlyCollection<PackageSource> Items { get; protected set; } = sources ?? throw new ArgumentNullException(nameof(sources));

    public static NuGetPackageSources Empty => new();

    protected NuGetPackageSources() : this(new NullSettings(), new List<PackageSource>())
    {
    }

    [UsedImplicitly]
    public NuGetPackageSources([NotNull] ISettings settings) : this(settings,
        settings.GetConfigFilePaths().Select(x => new PackageSource(x)).ToList())
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
    }

    public IEnumerator<PackageSource> GetEnumerator()
    {
        return Items.GetEnumerator();
    }

    public override string ToString()
    {
        return string.Join(",", Items.Select(s => s.SourceUri.ToString()));
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}