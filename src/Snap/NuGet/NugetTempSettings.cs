using System;
using JetBrains.Annotations;
using NuGet.Configuration;

namespace Snap.NuGet
{
    internal sealed class NugetTempSettings : Settings
    {
        public NugetTempSettings([NotNull] string tempDirectory) :
            base(tempDirectory, $"snap_nuget_temp_settings{Guid.NewGuid()}.config", false)
        {
            if (tempDirectory == null) throw new ArgumentNullException(nameof(tempDirectory));
        }
    }
}
