using System.Diagnostics.CodeAnalysis;
using CommandLine;

namespace Snap.Tool.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("promote", HelpText = "Promotes a nupkg to the next release channel")]
    internal class PromoteNupkgOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Application name", Required = true)]
        public string App { get; set; }
        [Option("all", HelpText = "Promotes the application to all release channels", Required = true)]
        public bool All { get; set; }
    }
}
