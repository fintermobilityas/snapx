using JetBrains.Annotations;

namespace Snap.NuGet
{
    public enum NuGetProtocolVersion
    {
        [UsedImplicitly] NotSupported = 0,
        V2 = 1,
        V3 = 2
    }
}
