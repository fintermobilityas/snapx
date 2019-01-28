using System.Diagnostics.CodeAnalysis;
using CommandLine;

namespace Snap.Tool.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("releasify", HelpText = "Create a new release for a given snap app")]
    internal class ReleasifyOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Snap app name.", Required = true)]
        public string App { get; set; }
        [Option('d', "base-directory", HelpText = "Base directory for where binaries for current app can be found.", Required = true)]
        public string BaseDirectory { get; set; }
    }
}
