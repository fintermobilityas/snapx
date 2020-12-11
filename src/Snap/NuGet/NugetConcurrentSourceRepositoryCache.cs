using System;
using System.Collections.Concurrent;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace Snap.NuGet
{
    internal class NugetConcurrentSourceRepositoryCache
    {
        readonly ConcurrentDictionary<PackageSource, SourceRepository> _packageSources = new();

        public SourceRepository GetOrAdd([NotNull] PackageSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return _packageSources.GetOrAdd(source, CreateSourceRepository);
        }

        static SourceRepository CreateSourceRepository([NotNull] PackageSource packageSource)
        {
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            return new SourceRepository(packageSource, Repository.Provider.GetCoreV3());
        }
    }
}
