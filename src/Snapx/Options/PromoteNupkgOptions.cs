using System.Diagnostics.CodeAnalysis;
using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("promote", HelpText = "Promotes a nupkg to the next release channel")]
    [UsedImplicitly]
    internal class PromoteNupkgOptions : BaseSubOptions
    {
        [Option("id", HelpText = "Application id", Required = true)]
        public string AppId { get; set; }
        [Option("all", HelpText = "Promotes the application to all channels")]
        public bool All { get; set; }
    }
}
