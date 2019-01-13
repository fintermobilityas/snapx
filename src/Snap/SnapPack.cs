using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;
using Snap.Update;

namespace Snap
{
    internal sealed class SnapPackDetails
    {
        public SemanticVersion CurrentVersion { get; set; }
        public string AppName { get; set; }
        public string ChannelName { get; set; }
        public SnapTargetFramework TargetFramework { get; set; }
        public SnapFeed Feed { get; set; }
        public string NuspecBaseDirectory { get; set; }
        public string NuspecFilename { get; set; }
        public IReadOnlyDictionary<string, string> MsBuildProperties { get; set; }
        public IReadOnlyDictionary<string, string> NuspecProperties { get; set; }
        public IProgressSource ProgressSource {get; set; }
    }

    internal interface ISnapPack
    {
        Task<string> PackAsync(SnapPackDetails snapPackDetails);
    }

    internal sealed class SnapPack : ISnapPack
    {
        readonly ISnapFilesystem _snapFilesystem;

        public SnapPack(ISnapFilesystem snapFilesystem)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
        }

        public Task<string> PackAsync(SnapPackDetails snapPackDetails)
        {
            if (snapPackDetails == null) throw new ArgumentNullException(nameof(snapPackDetails));  

            if (snapPackDetails.NuspecBaseDirectory == null || !_snapFilesystem.DirectoryExists(snapPackDetails.NuspecBaseDirectory))
            {
                throw new DirectoryNotFoundException($"Unable to find base directory: {snapPackDetails.NuspecBaseDirectory}.");
            }

            if (!_snapFilesystem.FileExists(snapPackDetails.NuspecFilename))
            {
                throw new FileNotFoundException($"Unable to find nuspec filename: {snapPackDetails.NuspecFilename}.");
            }

            var properties = new Dictionary<string, string>
            {
                {"version", snapPackDetails.CurrentVersion.ToFullString()},
                {"snapfolder", snapPackDetails.NuspecBaseDirectory}
            };

            if (snapPackDetails.NuspecProperties != null)
            {
                foreach (var pair in snapPackDetails.NuspecProperties)
                {
                    if (!properties.ContainsKey(pair.Key.ToLowerInvariant()))
                    {
                        properties.Add(pair.Key, pair.Value);
                    }
                }
            }

            var progressSource = snapPackDetails.ProgressSource;

            progressSource?.Raise(0);

            using (var nuspecStream = _snapFilesystem.OpenReadOnly(snapPackDetails.NuspecFilename))
            {
                string GetPropertyValue(string propertyName)
                {
                    return properties.TryGetValue(propertyName, out var value) ? value : null;
                }

                var targetFramework = NuGetFramework.Parse("net45");

                var packageBuilder = new PackageBuilder(nuspecStream, snapPackDetails.NuspecBaseDirectory, GetPropertyValue);

                packageBuilder.TargetFrameworks.Clear();
                packageBuilder.TargetFrameworks.Add(targetFramework);

                Debugger.Break();
            }

            //packagear
            throw new NotImplementedException();
        }

        Task BuildAsync()
        {
            return Task.CompletedTask;
        }

        Task PackAsync()
        {
            return Task.CompletedTask;
        }

    }
}
