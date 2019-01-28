using System.Diagnostics.CodeAnalysis;

namespace Snap.Tool.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal class MainOptions
    {
        public Sha512Options Sha512Verb { get; set; }
        public InstallNupkgOptions InstallNupkgVerb { get; set; }
        public ReleasifyOptions ReleasifyVerb { get; set; }
    }
}
