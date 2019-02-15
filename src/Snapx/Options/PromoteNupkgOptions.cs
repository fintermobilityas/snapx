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
        public string AppId { get; [UsedImplicitly] set; }
        [Option('r', "rid", HelpText = "Runtime identifier target name, e.g win7-x64", Required = true)]
        public string Rid { get; [UsedImplicitly] set; }
        [Option('c', "channel", HelpText = "Channel name")]
        public string Channel { get; [UsedImplicitly] set; }
        [Option("all", HelpText = "Promotes the application to all channels")]
        public bool All { get; set; }
    }
}
