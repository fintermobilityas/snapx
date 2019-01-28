using System;
using System.Collections.Generic;
using System.IO;
using NuGet.Frameworks;
using NuGet.Packaging;

namespace Snap.Core
{
    internal interface ISnapPackageDetails
    {
        SnapAppSpec Spec { get; set; }
        string NuspecFilename { get; }
        string NuspecBaseDirectory { get; }
        ISnapProgressSource SnapProgressSource {get; set; }
        IReadOnlyDictionary<string, string> NuspecProperties { get; set; }
    }

    internal sealed class SnapPackageDetails : ISnapPackageDetails
    {
        public SnapAppSpec Spec { get; set; }
        public string NuspecFilename { get; set; }
        public string NuspecBaseDirectory { get; set; }
        public ISnapProgressSource SnapProgressSource { get; set; }
        public IReadOnlyDictionary<string, string> NuspecProperties { get; set; }
    }

    internal interface ISnapPack
    {
        MemoryStream Pack(ISnapPackageDetails packageDetails);
    }

    internal sealed class SnapPack : ISnapPack
    {
        readonly ISnapFilesystem _snapFilesystem;

        public SnapPack(ISnapFilesystem snapFilesystem)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
        }

        public MemoryStream Pack(ISnapPackageDetails packageDetails)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));  

            if (packageDetails.NuspecBaseDirectory == null || !_snapFilesystem.DirectoryExists(packageDetails.NuspecBaseDirectory))
            {
                throw new DirectoryNotFoundException($"Unable to find base directory: {packageDetails.NuspecBaseDirectory}.");
            }

            if (!_snapFilesystem.FileExists(packageDetails.NuspecFilename))
            {
                throw new FileNotFoundException($"Unable to find nuspec filename: {packageDetails.NuspecFilename}.");
            }

            if (packageDetails.Spec == null)
            {
                throw new Exception($"Snap spec cannot be null.");
            }

            if (packageDetails.Spec.Version == null)
            {
                throw new Exception($"Snap spec package version cannot be null.");
            }

            var properties = new Dictionary<string, string>
            {
                {"version", packageDetails.Spec.Version.ToFullString()},
                {"nuspecbasedirectory", packageDetails.NuspecBaseDirectory}
            };

            if (packageDetails.NuspecProperties != null)
            {
                foreach (var pair in packageDetails.NuspecProperties)
                {
                    if (!properties.ContainsKey(pair.Key.ToLowerInvariant()))
                    {
                        properties.Add(pair.Key, pair.Value);
                    }
                }
            }

            var progressSource = packageDetails.SnapProgressSource;

            progressSource?.Raise(0);

            var outputStream = new MemoryStream();

            using (var nuspecStream = _snapFilesystem.OpenReadOnly(packageDetails.NuspecFilename))
            {
                string GetPropertyValue(string propertyName)
                {
                    return properties.TryGetValue(propertyName, out var value) ? value : null;
                }

                progressSource?.Raise(50);

                var packageBuilder = new PackageBuilder(nuspecStream, packageDetails.NuspecBaseDirectory, GetPropertyValue);
                packageBuilder.Save(outputStream);

                progressSource?.Raise(100);

                return outputStream;
            }
        }
    }
}
