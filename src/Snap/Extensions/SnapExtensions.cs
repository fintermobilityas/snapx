using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    internal static class SnapExtensions
    {
        static readonly Regex AppIdRegex = new Regex(@"^\w+([._]\w+)*$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        static readonly Regex ChannelNameRegex = new Regex(@"^[a-zA-Z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        static readonly Regex NetFullFrameworkRegex = new Regex("^net[0-9]{2,3}$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        static readonly Regex NetCoreAppRegex = new Regex("^netcoreapp\\d{1}.\\d{1}$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        static readonly Regex ExpansionRegex = new Regex("((\\$[0-9A-Za-z\\\\_]*)\\$)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);

        internal static string ExpandProperties([NotNull] this string value, [NotNull] Dictionary<string, string> properties)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            if (properties.Count == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(properties));

            // ReSharper disable once RedundantEnumerableCastCall
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

        internal static bool IsRuntimeIdentifierValidSafe(this string runtimeIdentifier)
        {
            return runtimeIdentifier != null && (runtimeIdentifier == "win-x64" || runtimeIdentifier == "linux-x64");
        }

        internal static SnapChannel GetDefaultChannelOrThrow([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var channel = snapApp.Channels.FirstOrDefault();
            if (channel == null)
            {
                throw new Exception($"Default channel not found. Snap id: {snapApp.Id}");
            }
            return channel;
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
        
        internal static SnapChannel GetNextChannel([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            var channelsCount = snapApp.Channels.Count;
            
            for (var index = 0; index < channelsCount; index++)
            {
                var currentSnapChannel = snapApp.Channels[index];
                var nextSnapChannel = index + 1 < channelsCount ? snapApp.Channels[index + 1] : null;
                if (currentSnapChannel.Current && nextSnapChannel != null)
                {
                    return nextSnapChannel;
                }
            }

            return null;
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
        
        internal static string BuildNugetUpstreamId([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return snapApp.IsFull ? snapApp.BuildNugetFullUpstreamId() : snapApp.BuildNugetDeltaUpstreamId();
        }
                
        internal static string BuildNugetFullUpstreamId([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return $"{snapApp.Id}_full_{snapApp.Target.Rid}_snapx".ToLowerInvariant();
        }
        
        internal static string BuildNugetDeltaUpstreamId([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return $"{snapApp.Id}_delta_{snapApp.Target.Rid}_snapx".ToLowerInvariant();
        }

        internal static string BuildNugetUpstreamId([NotNull] this SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            return snapRelease.IsFull ? snapRelease.BuildNugetFullUpstreamId() : snapRelease.BuildNugetDeltaUpstreamId();
        }

        internal static string BuildNugetFullUpstreamId([NotNull] this SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            return $"{snapRelease.Id}_full_{snapRelease.Target.Rid}_snapx".ToLowerInvariant();
        }
        
        internal static string BuildNugetDeltaUpstreamId([NotNull] this SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            return $"{snapRelease.Id}_delta_{snapRelease.Target.Rid}_snapx".ToLowerInvariant();
        }

        internal static string BuildNugetFilename([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return snapApp.IsFull ? snapApp.BuildNugetFullFilename() : snapApp.BuildNugetDeltaFilename();
        }
        
        internal static string BuildNugetFullFilename([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return $"{snapApp.Id}_full_{snapApp.Target.Rid}_snapx.{snapApp.Version.ToMajorMinorPatch()}.nupkg".ToLowerInvariant();
        }

        internal static string BuildNugetDeltaFilename([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return $"{snapApp.Id}_delta_{snapApp.Target.Rid}_snapx.{snapApp.Version.ToMajorMinorPatch()}.nupkg".ToLowerInvariant();
        }
        
        internal static string BuildNugetFilename([NotNull] this SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            return snapRelease.IsFull ? snapRelease.BuildNugetFullFilename() : snapRelease.BuildNugetDeltaFilename();
        }
        
        internal static string BuildNugetFullFilename([NotNull] this SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            return $"{snapRelease.Id}_full_{snapRelease.Target.Rid}_snapx.{snapRelease.Version.ToMajorMinorPatch()}.nupkg".ToLowerInvariant();
        }

        internal static string BuildNugetDeltaFilename([NotNull] this SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            return $"{snapRelease.Id}_delta_{snapRelease.Target.Rid}_snapx.{snapRelease.Version.ToMajorMinorPatch()}.nupkg".ToLowerInvariant();
        }

        internal static string BuildNugetReleasesUpstreamId([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return $"{snapApp.Id.ToLowerInvariant()}_snapx";
        }
        
        internal static string BuildNugetReleasesUpstreamId([NotNull] this SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            return $"{snapRelease.Id.ToLowerInvariant()}_snapx";
        }
        
        internal static string BuildNugetReleasesFilename([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return $"{snapApp.BuildNugetReleasesUpstreamId()}.nupkg".ToLowerInvariant();
        }

        public static SnapApp AsFullSnapApp([NotNull] this SnapApp snapApp, bool isGenesis)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            var fullSnapApp = new SnapApp(snapApp)
            {
                IsFull = true,
                IsGenesis = isGenesis
            };
            
            return fullSnapApp;
        }
        
        public static SnapApp AsDeltaSnapApp([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            var fullSnapApp = new SnapApp(snapApp)
            {
                IsFull = false,
                IsGenesis = false
            };

            return fullSnapApp;
        }

        public static SnapRelease AsFullRelease([NotNull] this SnapRelease snapRelease, bool isGenesis)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            
            var fullSnapRelease = new SnapRelease(snapRelease)
            {
                Filename = snapRelease.BuildNugetFullFilename(),
                UpstreamId = snapRelease.BuildNugetFullUpstreamId(),
                IsGenesis = isGenesis,
                IsFull = true,
                Gc = isGenesis && snapRelease.Gc
            };

            fullSnapRelease.Files.Clear();
            fullSnapRelease.New.Clear();
            fullSnapRelease.Modified.Clear();
            fullSnapRelease.Unmodified.Clear();
            fullSnapRelease.Deleted.Clear();
            
            fullSnapRelease.Files.AddRange(snapRelease.Files.Select(x => new SnapReleaseChecksum(x)));

            return fullSnapRelease;
        }

        public static SnapRelease AsDeltaRelease([NotNull] this SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            var deltaSnapRelease = new SnapRelease(snapRelease).AsFullRelease(false);
            deltaSnapRelease.IsFull = false;
            deltaSnapRelease.Filename = deltaSnapRelease.BuildNugetDeltaFilename();
            deltaSnapRelease.UpstreamId = deltaSnapRelease.BuildNugetDeltaUpstreamId();
            
            return deltaSnapRelease; 
        }
        
        internal static (bool valid, string id, string fullOrDelta, SemanticVersion semanticVersion, string rid) ParseNugetFilename([NotNull] this string filename, 
            StringComparison stringComparisonType)
        {
            string id = default;
            string fullOrDelta = default;
            SemanticVersion semanticVersion = default;
            string rid = default;
            int nupkgExtensionPos = -1;

            if (string.IsNullOrWhiteSpace(filename))
            {
                goto done;
            }

            var values = filename.Split('_').ToList();
            if (values.Count != 4)
            {
                goto done;
            }

            nupkgExtensionPos = filename.LastIndexOf(".nupkg", stringComparisonType);
            if (nupkgExtensionPos == -1)
            {
                goto done;
            }

            id = string.IsNullOrWhiteSpace(values[0]) ? null : values[0];
            fullOrDelta = values[1] == "delta" ? "delta" : values[1] == "full" ? "full" : null;
            rid = string.IsNullOrWhiteSpace(values[2]) ? null : values[2];            

            if (!values[3].StartsWith("snapx.", stringComparisonType))
            {
                goto done;
            }
            
            var semanticVersionStr = nupkgExtensionPos == -1 || 
                                     string.IsNullOrWhiteSpace(values[3]) ? null : values[3].Replace(".nupkg", string.Empty);
            semanticVersionStr = semanticVersionStr?.Replace("snapx.", string.Empty);
            if (!SemanticVersion.TryParse(semanticVersionStr, out semanticVersion))
            {
                semanticVersion = null;
            }

            done:
            var valid = id != null && fullOrDelta != null && semanticVersion != null && rid != null && nupkgExtensionPos != -1;
            return (valid, id, fullOrDelta, semanticVersion, rid);
        }

        internal static PackageSource BuildPackageSource([NotNull] this SnapNugetFeed snapFeed, [NotNull] NugetTempSettings nugetTempSettings)
        {
            if (snapFeed == null) throw new ArgumentNullException(nameof(snapFeed));
            if (nugetTempSettings == null) throw new ArgumentNullException(nameof(nugetTempSettings));

            var packageSource = new PackageSource(snapFeed.Source.ToString(), snapFeed.Name, true, true, false);

            var storePasswordInClearText = !nugetTempSettings.IsPasswordEncryptionSupported();
            
            if (snapFeed.Username != null && snapFeed.Password != null)
            {
                snapFeed.Password = storePasswordInClearText ? snapFeed.Password : EncryptionUtility.EncryptString(snapFeed.Password);

                // Comma-delimited list of authentication types the credential is valid for as stored in the config file.
                // If null or empty, all authentication types are valid. Example: 'basic,negotiate'
                string validAuthenticationTypesText = null;
                
                packageSource.Credentials = new PackageSourceCredential(packageSource.Name,
                    // ReSharper disable once ExpressionIsAlwaysNull
                    snapFeed.Username, snapFeed.Password, storePasswordInClearText, validAuthenticationTypesText);

                nugetTempSettings.AddOrUpdate(ConfigurationConstants.CredentialsSectionName, packageSource.Credentials.AsCredentialsItem());
            }

            if (snapFeed.ApiKey != null)
            {
                if (storePasswordInClearText)
                {
                    nugetTempSettings.AddOrUpdate(ConfigurationConstants.ApiKeys, new AddItem(packageSource.Source, snapFeed.ApiKey));
                }
                else
                {
                    SettingsUtility.SetEncryptedValueForAddItem(nugetTempSettings, ConfigurationConstants.ApiKeys, packageSource.Source, snapFeed.ApiKey);                    
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
                Source = packageSource.SourceUri,
                ProtocolVersion = (NuGetProtocolVersion)packageSource.ProtocolVersion,
                Username = packageSource.Credentials?.Username,
                Password = packageSource.Credentials?.Password,
                ApiKey = apiKey
            };

            return snapFeed;
        }

        internal static IEnumerable<SnapApp> BuildSnapApps([NotNull] this SnapApps snapApps, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] ISnapFilesystem snapFilesystem)
        {
            foreach (var snapsApp in snapApps.Apps)
            {
                foreach (var snapsTarget in snapsApp.Targets)
                {
                    yield return snapApps.BuildSnapApp(snapsApp.Id, snapsTarget.Rid, nuGetPackageSources, snapFilesystem);
                }
            }
        }

        internal static SnapApp BuildSnapApp([NotNull] this SnapApps snapApps, string id, [NotNull] string rid,
            [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] ISnapFilesystem snapFilesystem)
        {
            if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
            if (rid == null) throw new ArgumentNullException(nameof(rid));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));

            var snapApp = snapApps.Apps.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (snapApp == null)
            {
                throw new Exception($"Unable to find snap with id: {id}");
            }

            var snapAppUniqueRuntimeIdentifiers = snapApp.Targets.Select(x => x.Rid).ToList();
            if (snapAppUniqueRuntimeIdentifiers.Distinct().Count() != snapApp.Targets.Count)
            {
                throw new Exception($"Target runtime identifiers (rids) must be unique: {string.Join(",", snapAppUniqueRuntimeIdentifiers)}. Snap id: {snapApp.Id}");
            }
                        
            var snapAppTarget = snapApp.Targets.SingleOrDefault(x => string.Equals(x.Rid, rid, StringComparison.OrdinalIgnoreCase));
            if (snapAppTarget == null)
            {
                throw new Exception($"Unable to find target with rid: {rid}. Snap id: {snapApp.Id}");
            }

            snapAppTarget.Installers = snapAppTarget.Installers.Distinct().ToList();

            if (!snapAppTarget.Rid.IsRuntimeIdentifierValidSafe())
            {
                throw new Exception($"Unsupported rid: {rid}. Snap id: {snapApp.Id}");
            }

            if (!snapAppTarget.Framework.IsNetFrameworkValidSafe())
            {
                throw new Exception($"Unknown .NET framework: {snapAppTarget.Framework}");
            }
            
            var snapAppTargetUniqueShortcuts = snapAppTarget.Shortcuts.Select(x => x).ToList();
            if (snapAppTargetUniqueShortcuts.Distinct().Count() != snapAppTarget.Shortcuts.Count)
            {
                throw new Exception($"Target shortcut locations must be unique: {string.Join(", ", snapAppTargetUniqueShortcuts)}. Snap id: {snapApp.Id}");
            }
            
            var snapAppTargetUniqueInstallers = snapAppTarget.Installers.Select(x => x).ToList();
            if (snapAppTargetUniqueInstallers.Distinct().Count() != snapAppTarget.Installers.Count)
            {
                throw new Exception($"Target installer types must be unique: {string.Join(", ", snapAppTargetUniqueInstallers)}. Snap id: {snapApp.Id}");
            }
            
            if (snapAppTarget.Icon != null)
            {
                snapAppTarget.Icon = snapFilesystem.PathGetFullPath(snapAppTarget.Icon);

                if (!snapFilesystem.FileExists(snapAppTarget.Icon))
                {                    
                    throw new Exception($"Unable to find icon: {snapAppTarget.Icon}.");
                }
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

            var snapAppAvailableChannels = snapApps.Channels.Where(rhs => snapApp.Channels.Any(lhs => lhs.Equals(rhs.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            if (!snapAppAvailableChannels.Any())
            {
                throw new Exception($"Could not find any global channels. Channel list: {string.Join(",", snapAppUniqueChannels)}. Snap id: {snapApp.Id}");
            }
                      
            var snapFeeds = new List<SnapFeed>();
            snapFeeds.AddRange(nuGetPackageSources.BuildSnapFeeds());
            snapFeeds.AddRange(snapApps.Channels.Select(x => x.UpdateFeed).OfType<SnapsHttpFeed>().DistinctBy(x => x.Source).Select(x => new SnapHttpFeed(x)));

            var snapNugetFeeds = snapFeeds.Where(x => x is SnapNugetFeed).Cast<SnapNugetFeed>().ToList();
            var snapHttpFeeds = snapFeeds.Where(x => x is SnapHttpFeed).Cast<SnapHttpFeed>().ToList();
            var snapAppChannels = new List<SnapChannel>();

            for (var i = 0; i < snapAppAvailableChannels.Count; i++)
            {
                var snapsChannel = snapAppAvailableChannels[i];
                var pushFeed = snapNugetFeeds.SingleOrDefault(x => x.Name == snapsChannel.PushFeed.Name);
                if (pushFeed == null)
                {
                    throw new Exception($"Unable to resolve push feed: {snapsChannel.PushFeed}. Channel: {snapsChannel.Name}. Application id: {snapApp.Id}");
                }

                SnapFeed updateFeed = null;

                switch (snapsChannel.UpdateFeed)
                {
                    case SnapsNugetFeed snapsNugetFeed:
                        updateFeed = snapNugetFeeds.SingleOrDefault(x => x.Name == snapsNugetFeed.Name);
                        break;
                    case SnapsHttpFeed snapsHttpFeed:
                        updateFeed = snapHttpFeeds.SingleOrDefault(x => x.Source == snapsHttpFeed.Source);
                        break;
                }

                if (updateFeed == null)
                {
                    throw new Exception($"Unable to resolve update feed type: {snapsChannel.UpdateFeed?.GetType().Name}. Channel: {snapsChannel.Name}. Application id: {snapApp.Id}");
                }

                var currentChannel = i == 0; // Default snap channel is always the first one defined. 
                snapAppChannels.Add(new SnapChannel(snapsChannel.Name, currentChannel, pushFeed, updateFeed));
            }

            if (snapAppTarget.PersistentAssets.Any(x => x.StartsWith("app-", StringComparison.OrdinalIgnoreCase)))
            {
                throw new Exception("Fatal error! A persistent asset starting with 'app-' was detected in manifest. This is a reserved keyword.");
            }

            return new SnapApp
            {
                Id = snapApp.Id,
                Channels = snapAppChannels,
                Target = new SnapTarget(snapAppTarget),
                ReleaseNotes = snapApp.ReleaseNotes
            };
        }

        internal static INuGetPackageSources BuildNugetSources([NotNull] this SnapApp snapApp, [NotNull] string tempDirectory)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (tempDirectory == null) throw new ArgumentNullException(nameof(tempDirectory));
            var inMemorySettings = new NugetTempSettings(tempDirectory);

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

                    var pushFeed = nuGetPackageSources.Items.SingleOrDefault(packageSource => packageSource.Name == x.PushFeed.Name);
                    if (pushFeed != null)
                    {
                        packageSources.Add(pushFeed);
                    }

                    if (x.UpdateFeed is SnapsNugetFeed snapsNugetFeed)
                    {
                        var updateFeed = nuGetPackageSources.Items.SingleOrDefault(packageSource => packageSource.Name == snapsNugetFeed.Name);
                        if (updateFeed != null)
                        {
                            packageSources.Add(updateFeed);
                        }
                    }


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

        internal static SnapApp GetSnapApp([NotNull] this AssemblyDefinition assemblyDefinition, [NotNull] ISnapAppReader snapAppReader)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));

            var assemblyReflector = new CecilAssemblyReflector(assemblyDefinition);

            var snapReleaseDetailsAttribute = assemblyReflector.GetAttribute<SnapAppReleaseDetailsAttribute>();
            if (snapReleaseDetailsAttribute == null)
            {
                throw new Exception($"Unable to find {nameof(SnapAppReleaseDetailsAttribute)} in assembly {assemblyReflector.FullName}");
            }

            var snapSpecResource = assemblyReflector.MainModule.Resources.SingleOrDefault(x => x.Name == SnapConstants.SnapAppLibraryName);
            if (!(snapSpecResource is EmbeddedResource snapSpecEmbeddedResource))
            {
                throw new Exception($"Unable to find resource {SnapConstants.SnapAppLibraryName} in assembly {assemblyReflector.FullName}");
            }

            using (var resourceStream = snapSpecEmbeddedResource.GetResourceStream())
            using (var snapAppMemoryStream = new MemoryStream())
            {
                resourceStream.CopyTo(snapAppMemoryStream);
                snapAppMemoryStream.Seek(0, SeekOrigin.Begin);

                return snapAppReader.BuildSnapAppFromStream(snapAppMemoryStream);
            }
        }

        internal static SnapApp GetSnapAppFromDirectory([NotNull] this string workingDirectory, [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader snapAppReader)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));

            var snapAppDll = filesystem.PathCombine(workingDirectory, SnapConstants.SnapAppDllFilename);
            if (!filesystem.FileExists(snapAppDll))
            {
                throw new FileNotFoundException(snapAppDll);
            }

            using (var snapAppDllFileStream = new FileStream(snapAppDll, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var snapAppDllAssemblyDefinition = AssemblyDefinition.ReadAssembly(snapAppDllFileStream))
            {
                return snapAppDllAssemblyDefinition.GetSnapApp(snapAppReader);
            }
        }
    
        internal static void GetCoreRunExecutableFullPath([NotNull] this Assembly assembly, [NotNull] ISnapFilesystem snapFilesystem,
            [NotNull] ISnapAppReader snapAppReader, out string coreRunFullPath)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));

            var assemblyLocationDirectory = snapFilesystem.PathGetDirectoryName(assembly.Location);
            var snapApp = assemblyLocationDirectory.GetSnapAppFromDirectory(snapFilesystem, snapAppReader);
            var parentDirectory = snapFilesystem.DirectoryGetParent(assemblyLocationDirectory);            
            
            var exeName = snapApp.Id;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                exeName += ".exe";
            }
            
            coreRunFullPath = snapFilesystem.PathCombine(parentDirectory, exeName);
        }

        internal static bool IsSupportedOsVersion(this OSPlatform oSPlatform)
        {
            return oSPlatform == OSPlatform.Linux || oSPlatform == OSPlatform.Windows;
        }
    }
}
