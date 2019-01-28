using System;
using System.Collections.Generic;
using System.IO;
using NuGet.Frameworks;
using NuGet.Packaging;

namespace Snap.Core
{
    internal interface ISnapPackDetails
    {
        SnapAppSpec Spec { get; set; }
        string NuspecFilename { get; }
        string NuspecBaseDirectory { get; }
        ISnapProgressSource SnapProgressSource {get; set; }
        IReadOnlyDictionary<string, string> NuspecProperties { get; set; }
        Func<MemoryStream, string> FileResolverFunc { get; set; }
    }

    internal sealed class SnapPackDetails : ISnapPackDetails
    {
        public SnapAppSpec Spec { get; set; }
        public string NuspecFilename { get; set; }
        public string NuspecBaseDirectory { get; set; }
        public ISnapProgressSource SnapProgressSource { get; set; }
        public IReadOnlyDictionary<string, string> NuspecProperties { get; set; }
        public Func<MemoryStream, string> FileResolverFunc { get; set; }
    }

    internal interface ISnapPack
    {
        MemoryStream Pack(ISnapPackDetails snapPackDetails);
    }

    internal sealed class SnapPack : ISnapPack
    {
        readonly ISnapFilesystem _snapFilesystem;

        public SnapPack(ISnapFilesystem snapFilesystem)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
        }

        public MemoryStream Pack(ISnapPackDetails snapPackDetails)
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
                {"version", snapPackDetails.Spec.Version.ToFullString()},
                {"nuspecbasedirectory", snapPackDetails.NuspecBaseDirectory}
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

            var progressSource = snapPackDetails.SnapProgressSource;

            progressSource?.Raise(0);

            var outputStream = new MemoryStream();

            using (var nuspecStream = _snapFilesystem.OpenReadOnly(snapPackDetails.NuspecFilename))
            {
                string GetPropertyValue(string propertyName)
                {
                    return properties.TryGetValue(propertyName, out var value) ? value : null;
                }

                var targetFramework = NuGetFramework.Parse("net45");
                var packageBuilder = new PackageBuilder(nuspecStream, snapPackDetails.NuspecBaseDirectory, GetPropertyValue);
                packageBuilder.Save(outputStream);

                return outputStream;
            }
        }
    }
}
