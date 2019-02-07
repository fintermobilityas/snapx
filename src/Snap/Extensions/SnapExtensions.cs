using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Configuration;
using NuGet.Versioning;
using Snap.Attributes;
using Snap.Core;
using Snap.Core.Models;
using Snap.NuGet;
using Snap.Reflection;

namespace Snap.Extensions
{
    public static class SnapExtensions
    {
        // https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageCreation/Utility/PackageIdValidator.cs#L14
        static readonly Regex AppIdRegex = new Regex(@"^\w+([._]\w+)*$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        static readonly Regex ChannelNameRegex = new Regex(@"^[a-zA-Z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        static readonly Regex NetFullFrameworkRegex = new Regex("^net[0-9]{2,3}$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        static readonly Regex NetCoreAppRegex = new Regex("^netcoreapp\\d{1}.\\d{1}$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        static readonly Regex ExpansionRegex = new Regex("((\\$[0-9A-Za-z\\\\_]*)\\$)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

        internal static string ExpandProperties([NotNull] this string value, [NotNull] Dictionary<string, string> properties)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            if (properties.Count == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(properties));

            foreach (var match in ExpansionRegex.Matches(value).Cast<Match>())
            {
                var key = match.Value.Replace("$", string.Empty);

                if (!properties.ContainsKey(key))
                {
                    throw new Exception($"Failed to expand key: {key}.");
                }

                value = value.Replace(match.Value, properties[key]);
            }

            return value;
        }

        internal static bool IsNetCoreAppSafe(this string framework)
        {
            return framework != null && NetCoreAppRegex.IsMatch(framework);
        }

        internal static bool IsNetFullFrameworkAppSafe(this string framework)
        {
            return framework != null && NetFullFrameworkRegex.IsMatch(framework);
        }

        internal static bool IsNetFrameworkValidSafe(this string framework)
        {
            return framework.IsNetCoreAppSafe() || framework.IsNetFullFrameworkAppSafe();
        }

        internal static SnapChannel GetCurrentChannelOrThrow([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var channel = snapApp.Channels.SingleOrDefault(x => x.Current);
            if (channel == null)
            {
                throw new Exception($"Current channel not found. Snap id: {snapApp.Id}");
            }
            return channel;
        }
        
        internal static bool IsValidAppId([NotNull] this string value)
        {
            return AppIdRegex.IsMatch(value);
        }
        
        internal static bool IsValidChannelName([NotNull] this string value)
        {
            return ChannelNameRegex.IsMatch(value);
        }
        
        internal static bool IsValidAppId([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return snapApp.Id.IsValidAppId();
        }
        
        internal static bool IsValidChannelName([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var channel = snapApp.GetCurrentChannelOrThrow();
            return channel.Name.IsValidChannelName();
        }
        
        internal static string BuildNugetUpstreamPackageId([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return snapApp.BuildNugetUpstreamPackageIdImpl(snapApp.Delta);
        }
        
        internal static string BuildFullNugetUpstreamPackageId([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return snapApp.BuildNugetUpstreamPackageIdImpl(false);
        }
        
        internal static string BuildDeltaNugetUpstreamPackageId([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return snapApp.BuildNugetUpstreamPackageIdImpl(true);
        }
        
        static string BuildNugetUpstreamPackageIdImpl([NotNull] this SnapApp snapApp, bool delta = false)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var channel = snapApp.GetCurrentChannelOrThrow();
            var fullOrDelta = delta ? "delta" : "full";
            return $"{snapApp.Id}_{fullOrDelta}_{snapApp.Target.Rid}_{channel.Name}".ToLowerInvariant();
        }

        internal static string BuildNugetLocalFilename([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var channel = snapApp.GetCurrentChannelOrThrow();
            var fullOrDelta = snapApp.Delta ? "delta" : "full";
            return $"{snapApp.Id}_{fullOrDelta}_{snapApp.Version.ToMajorMinorPatch()}_{snapApp.Target.Rid}_{channel.Name}.nupkg".ToLowerInvariant();
        }

        internal static PackageSource BuildPackageSource([NotNull] this SnapNugetFeed snapFeed, [NotNull] InMemorySettings inMemorySettings)
        {
            if (snapFeed == null) throw new ArgumentNullException(nameof(snapFeed));
            if (inMemorySettings == null) throw new ArgumentNullException(nameof(inMemorySettings));

            var packageSource = new PackageSource(snapFeed.SourceUri.ToString(), snapFeed.Name, true, true, false);

            var storePasswordInClearText = !inMemorySettings.IsPasswordEncryptionSupported();
            
            if (snapFeed.Username != null && snapFeed.Password != null)
            {
                snapFeed.Password = storePasswordInClearText ? snapFeed.Password : EncryptionUtility.EncryptString(snapFeed.Password);

                // Comma-delimited list of authentication types the credential is valid for as stored in the config file.
                // If null or empty, all authentication types are valid. Example: 'basic,negotiate'
                string validAuthenticationTypesText = null;
                
                packageSource.Credentials = new PackageSourceCredential(packageSource.Name,
                    snapFeed.Username, snapFeed.Password, storePasswordInClearText, validAuthenticationTypesText);

                inMemorySettings.AddOrUpdate(ConfigurationConstants.CredentialsSectionName, packageSource.Credentials.AsCredentialsItem());
            }

            if (snapFeed.ApiKey != null)
            {
                if (storePasswordInClearText)
                {
                    inMemorySettings.AddOrUpdate(ConfigurationConstants.ApiKeys, new AddItem(packageSource.Source, snapFeed.ApiKey));
                }
                else
                {
                    SettingsUtility.SetEncryptedValueForAddItem(inMemorySettings, ConfigurationConstants.ApiKeys, packageSource.Source, snapFeed.ApiKey);                    
                }
            }

            packageSource.ProtocolVersion = (int)snapFeed.ProtocolVersion;

            return packageSource;
        }

        internal static string GetDecryptedValue([NotNull] this PackageSource packageSource, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] string sectionName)
        {
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (sectionName == null) throw new ArgumentNullException(nameof(sectionName));

            var nuGetSupportsEncryption = nuGetPackageSources.IsPasswordEncryptionSupported();
            
            var decryptedValue = nuGetSupportsEncryption ? 
                SettingsUtility.GetDecryptedValueForAddItem(nuGetPackageSources.Settings, sectionName, packageSource.Source) : 
                SettingsUtility.GetValueForAddItem(nuGetPackageSources.Settings, sectionName, packageSource.Source);
            
            return string.IsNullOrWhiteSpace(decryptedValue) ? null : decryptedValue;
        }

        internal static SnapNugetFeed BuildSnapNugetFeed([NotNull] this PackageSource packageSource, [NotNull] INuGetPackageSources nuGetPackageSources)
        {
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));

            var apiKey = packageSource.GetDecryptedValue(nuGetPackageSources, ConfigurationConstants.ApiKeys);
            
            var snapFeed = new SnapNugetFeed
            {
                Name = packageSource.Name,
                SourceUri = packageSource.SourceUri,
                ProtocolVersion = (NuGetProtocolVersion)packageSource.ProtocolVersion,
                Username = packageSource.Credentials?.Username,
                Password = packageSource.Credentials?.Password,
                ApiKey = apiKey
            };

            return snapFeed;
        }

        internal static bool TryCreateSnapHttpFeed(this string value, out SnapHttpFeed snapHttpFeed)
        {
            snapHttpFeed = null;
            if (value == null)
            {
                return false;
            }

            const string snapHttpPrefix = "snap://";
            const string snapHttpsPrefix = "snaps://";

            var http = value.StartsWith(snapHttpPrefix);
            var https = !http && value.StartsWith(snapHttpsPrefix);

            if (!http && !https)
            {
                return false;
            }

            var substrLen = http ? snapHttpPrefix.Length : snapHttpsPrefix.Length;
            var sourceUrl = $"{(http ? "http://" : "https://")}{value.Substring(substrLen)}";

            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri))
            {
                return false;
            }

            snapHttpFeed = new SnapHttpFeed(sourceUri);
            return true;
        }

        internal static IEnumerable<SnapApp> BuildSnapApps([NotNull] this SnapApps snapApps, [NotNull] INuGetPackageSources nuGetPackageSources)
        {
            foreach (var snapsApp in snapApps.Apps)
            {
                foreach (var snapsTarget in snapsApp.Targets)
                {
                    yield return snapApps.BuildSnapApp(snapsApp.Id, snapsTarget.Rid, snapsApp.Version, nuGetPackageSources);
                }
            }
        }

        internal static SnapApp BuildSnapApp([NotNull] this SnapApps snapApps, string id, [NotNull] string rid, [NotNull] SemanticVersion releaseVersion,
            [NotNull] INuGetPackageSources nuGetPackageSources)
        {
            if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
            if (rid == null) throw new ArgumentNullException(nameof(rid));
            if (releaseVersion == null) throw new ArgumentNullException(nameof(releaseVersion));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));

            var snapApp = snapApps.Apps.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.InvariantCultureIgnoreCase));
            if (snapApp == null)
            {
                throw new Exception($"Unable to find snap with id: {id}");
            }

            var snapAppUniqueRuntimeIdentifiers = snapApp.Targets.Select(x => x.Rid).ToList();
            if (snapAppUniqueRuntimeIdentifiers.Distinct().Count() != snapApp.Targets.Count)
            {
                throw new Exception($"Target runtime identifiers (rids) must be unique: {string.Join(",", snapAppUniqueRuntimeIdentifiers)}. Snap id: {snapApp.Id}");
            }
            
            var snapAppTarget = snapApp.Targets.SingleOrDefault(x => string.Equals(x.Rid, rid, StringComparison.InvariantCultureIgnoreCase));
            if (snapAppTarget == null)
            {
                throw new Exception($"Unable to find target with rid: {rid}. Snap id: {snapApp.Id}");
            }

            if (!snapAppTarget.Framework.IsNetFrameworkValidSafe())
            {
                throw new Exception($"Unknown .NET framework: {snapAppTarget.Framework}");
            }

            var snapAppUniqueChannels = snapApp.Channels.Distinct().ToList();
            if (snapAppUniqueChannels.Count != snapApp.Channels.Count)
            {
                throw new Exception($"Channel list must be unique: {string.Join(",", snapApp.Channels)}. Snap id: {snapApp.Id}");
            }

            var snapAppsDefaultChannel = snapApps.Channels.First();
            var snapAppDefaultChannel = snapApp.Channels.First();

            if (!string.Equals(snapAppsDefaultChannel.Name, snapAppDefaultChannel, StringComparison.Ordinal))
            {
                throw new Exception($"Default channel must be {snapAppsDefaultChannel.Name}. Snap id: {snapApp.Id}");
            }

            var snapAppAvailableChannels = snapApps.Channels.Where(rhs => snapApp.Channels.Any(lhs => lhs.Equals(rhs.Name, StringComparison.InvariantCultureIgnoreCase))).ToList();
            if (!snapAppAvailableChannels.Any())
            {
                throw new Exception($"Could not find any global channels. Channel list: {string.Join(",", snapAppUniqueChannels)}. Snap id: {snapApp.Id}");
            }
                      
            var snapFeeds = new List<SnapFeed>();
            snapFeeds.AddRange(nuGetPackageSources.BuildSnapFeeds());
            snapFeeds.AddRange(snapApps.Channels.Select(x => x.UpdateFeed.TryCreateSnapHttpFeed(out var snapHttpFeed) ? snapHttpFeed : null).Where(x => x != null).DistinctBy(x => x.SourceUri));

            var snapNugetFeeds = snapFeeds.Where(x => x is SnapNugetFeed).Cast<SnapNugetFeed>().ToList();
            var snapHttpFeeds = snapFeeds.Where(x => x is SnapHttpFeed).Cast<SnapHttpFeed>().ToList();
            var snapAppChannels = new List<SnapChannel>();

            for (var i = 0; i < snapAppAvailableChannels.Count; i++)
            {
                var snapsChannel = snapAppAvailableChannels[i];
                var pushFeed = snapNugetFeeds.SingleOrDefault(x => x.Name == snapsChannel.PushFeed);
                if (pushFeed == null)
                {
                    throw new Exception($"Unable to resolve push feed: {snapsChannel.PushFeed}. Channel: {snapsChannel.Name}. Application id: {snapApp.Id}");
                }

                var updateFeed = (SnapFeed)
                                 snapNugetFeeds.SingleOrDefault(x => x.Name == snapsChannel.UpdateFeed)
                                 ?? snapHttpFeeds.SingleOrDefault(x =>
                                     snapsChannel.UpdateFeed.TryCreateSnapHttpFeed(out var snapHttpFeed)
                                     && x.ToStringSnapUrl() == snapHttpFeed.ToStringSnapUrl());

                if (updateFeed == null)
                {
                    throw new Exception($"Unable to resolve update feed: {snapsChannel.UpdateFeed}. Channel: {snapsChannel.Name}. Application id: {snapApp.Id}");
                }

                var currentChannel = i == 0; // Default snap channel is always the first one defined. 
                snapAppChannels.Add(new SnapChannel(snapsChannel.Name, currentChannel, pushFeed, updateFeed));
            }

            return new SnapApp
            {
                Id = snapApp.Id,
                Version = releaseVersion,
                Channels = snapAppChannels,
                Target = new SnapTarget(snapAppTarget)
            };
        }

        internal static INuGetPackageSources BuildNugetSources([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var inMemorySettings = new InMemorySettings();

            var nugetFeeds = snapApp.Channels
                .SelectMany(x => new List<SnapNugetFeed> { x.PushFeed, x.UpdateFeed as SnapNugetFeed })
                .Where(x => x != null)
                .DistinctBy(x => x.Name)
                .Select(x => x.BuildPackageSource(inMemorySettings))
                .ToList();

            return new NuGetPackageSources(inMemorySettings, nugetFeeds);
        }

        internal static INuGetPackageSources BuildNugetSources([NotNull] this SnapApps snapApps, INuGetPackageSources nuGetPackageSources)
        {
            var allPackageSources = snapApps.Channels
                .SelectMany(x =>
                {
                    var packageSources = new List<PackageSource>();

                    var pushFeed = nuGetPackageSources.Items.SingleOrDefault(packageSource => packageSource.Name == x.PushFeed);
                    if (pushFeed != null)
                    {
                        packageSources.Add(pushFeed);
                    }

                    if (x.UpdateFeed.TryCreateSnapHttpFeed(out _))
                    {
                        goto done;
                    }

                    var updateFeed = nuGetPackageSources.Items.SingleOrDefault(packageSource => packageSource.Name == x.UpdateFeed);
                    if (updateFeed != null)
                    {
                        packageSources.Add(updateFeed);
                    }

                    done:
                    return packageSources;
                })
                .DistinctBy(x => x.Name)
                .ToList();

            return !allPackageSources.Any() ? NuGetPackageSources.Empty : new NuGetPackageSources(nuGetPackageSources.Settings, allPackageSources);
        }

        internal static List<SnapNugetFeed> BuildSnapFeeds([NotNull] this INuGetPackageSources nuGetPackageSources)
        {
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            var snapFeeds = nuGetPackageSources.Items.Select(x => x.BuildSnapNugetFeed(nuGetPackageSources)).ToList();
            return snapFeeds;
        }

        internal static SnapApp GetSnapApp([NotNull] this AssemblyDefinition assemblyDefinition, [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));

            var assemblyReflector = new CecilAssemblyReflector(assemblyDefinition);

            var snapReleaseDetailsAttribute = assemblyReflector.GetAttribute<SnapAppReleaseDetailsAttribute>();
            if (snapReleaseDetailsAttribute == null)
            {
                throw new Exception($"Unable to find {nameof(SnapAppReleaseDetailsAttribute)} in assembly {assemblyReflector.FullName}");
            }

            var snapSpecResource = assemblyReflector.MainModule.Resources.SingleOrDefault(x => x.Name == snapAppWriter.SnapAppLibraryName);
            if (!(snapSpecResource is EmbeddedResource snapSpecEmbeddedResource))
            {
                throw new Exception($"Unable to find resource {snapAppWriter.SnapAppLibraryName} in assembly {assemblyReflector.FullName}");
            }

            using (var resourceStream = snapSpecEmbeddedResource.GetResourceStream())
            using (var snapAppMemoryStream = new MemoryStream())
            {
                resourceStream.CopyTo(snapAppMemoryStream);
                snapAppMemoryStream.Seek(0, SeekOrigin.Begin);

                return snapAppReader.BuildSnapAppFromStream(snapAppMemoryStream);
            }
        }

        internal static SnapApp GetSnapAppFromDirectory([NotNull] this string workingDirectory, [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader snapAppReader,
            [NotNull] ISnapAppWriter snapAppWriter)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));

            var snapAppDll = filesystem.PathCombine(workingDirectory, snapAppWriter.SnapAppDllFilename);
            if (!filesystem.FileExists(snapAppDll))
            {
                throw new FileNotFoundException(snapAppDll);
            }

            using (var snapAppDllFileStream = new FileStream(snapAppDll, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var snapAppDllAssemblyDefinition = AssemblyDefinition.ReadAssembly(snapAppDllFileStream))
            {
                return snapAppDllAssemblyDefinition.GetSnapApp(snapAppReader, snapAppWriter);
            }
        }

        [UsedImplicitly]
        internal static string GetSnapStubExecutableFullPath([NotNull] this string stubExecutableWorkingDirectory, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapAppReader snapAppReader,
            [NotNull] ISnapAppWriter snapAppWriter, out string stubExecutableExeName)
        {
            if (stubExecutableWorkingDirectory == null) throw new ArgumentNullException(nameof(stubExecutableWorkingDirectory));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));

            var snapApp = stubExecutableWorkingDirectory.GetSnapAppFromDirectory(snapFilesystem, snapAppReader, snapAppWriter);

            stubExecutableExeName = $"{snapApp.Id}.exe";

            return snapFilesystem.PathCombine(stubExecutableWorkingDirectory, $"..\\{stubExecutableExeName}");
        }

        internal static SnapApp GetSnapApp([NotNull] this Assembly assembly, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapAppReader snapAppReader,
            [NotNull] ISnapAppWriter snapAppWriter)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));

            var snapSpecDllDirectory = snapFilesystem.PathGetDirectoryName(assembly.Location);
            if (snapSpecDllDirectory == null)
            {
                throw new Exception($"Unable to find snap app dll: {snapAppWriter.SnapAppDllFilename}. Assembly location: {assembly.Location}. Assembly name: {assembly.FullName}");
            }

            return snapSpecDllDirectory.GetSnapAppFromDirectory(snapFilesystem, snapAppReader, snapAppWriter);
        }

        [UsedImplicitly]
        internal static string GetSnapStubExecutableFullPath([NotNull] this Assembly assembly, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapAppReader snapAppReader,
            [NotNull] ISnapAppWriter snapAppWriter, out string stubExecutableExeName)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));

            return snapFilesystem.PathGetDirectoryName(assembly.Location).GetSnapStubExecutableFullPath(snapFilesystem, snapAppReader, snapAppWriter, out stubExecutableExeName);
        }


        internal static bool IsSupportedOsVersion(this OSPlatform oSPlatform)
        {
            return oSPlatform == OSPlatform.Linux || oSPlatform == OSPlatform.Windows;
        }
    }
}
