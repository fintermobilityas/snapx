using System.Diagnostics.CodeAnalysis;
using CommandLine;

namespace Snap.Tool.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("releasify", HelpText = "Create a new release for a given snap app")]
    internal class ReleasifyOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Snap app name", Required = true)]
        public string App { get; set; }
        [Option('c', "channel", HelpText = "Snap channel name", Required = true)]
        public string Channel { get; set; }
        [Option('n', "nupkg", HelpText = "Nuget package to releasify", Required = true)]
        public string Nupkg { get; set; }
    }
}
