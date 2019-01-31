using System;
using System.IO;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using NuGet.Frameworks;
using NuGet.Packaging;

namespace Snap.NuGet
{
    internal sealed class InMemoryPackageFile : IPackageFile
    {
        readonly MemoryStream _memoryStream;

        public string Path { get; }
        public string EffectivePath { get; }
        public FrameworkName TargetFramework { get; }
        public DateTimeOffset LastWriteTime { get; }

        public InMemoryPackageFile([NotNull] MemoryStream memoryStream, [NotNull] string effectivePath, [NotNull] NuGetFramework nuGetFramework)
        {
            if (nuGetFramework == null) throw new ArgumentNullException(nameof(nuGetFramework));
            _memoryStream = memoryStream ?? throw new ArgumentNullException(nameof(memoryStream));

            Path = EffectivePath = effectivePath ?? throw new ArgumentNullException(nameof(effectivePath));
            LastWriteTime = DateTimeOffset.Now;
            TargetFramework = new FrameworkName(nuGetFramework.DotNetFrameworkName);
        }

        public Stream GetStream()
        {
            return _memoryStream;
        }
    }
}
