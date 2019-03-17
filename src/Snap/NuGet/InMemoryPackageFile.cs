using System;
using System.IO;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using NuGet.Frameworks;
using NuGet.Packaging;
using Snap.Core.Models;

namespace Snap.NuGet
{
    internal sealed class InMemoryPackageFile : IPackageFile
    {
        readonly Stream _memoryStream;

        public string Path { get; }
        public string EffectivePath { get; }
        public FrameworkName TargetFramework { get; }
        public DateTimeOffset LastWriteTime { get; }
        public string Filename { get; }

        public InMemoryPackageFile([NotNull] Stream memoryStream, [NotNull] NuGetFramework nuGetFramework, [NotNull] string targetPath, [NotNull] string filename)
        {
            if (nuGetFramework == null) throw new ArgumentNullException(nameof(nuGetFramework));
            if (targetPath == null) throw new ArgumentNullException(nameof(targetPath));
            _memoryStream = memoryStream ?? throw new ArgumentNullException(nameof(memoryStream));

            TargetFramework = new FrameworkName(nuGetFramework.DotNetFrameworkName);
            Path = EffectivePath = targetPath ?? throw new ArgumentNullException(nameof(targetPath));
            Filename = filename ?? throw new ArgumentNullException(nameof(filename));
            LastWriteTime = DateTimeOffset.Now;
        }

        public Stream GetStream()
        {
            return _memoryStream;
        }
    }
}
