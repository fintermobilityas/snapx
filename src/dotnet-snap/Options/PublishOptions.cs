using System.Diagnostics.CodeAnalysis;
using CommandLine;

namespace Snap.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("publish", HelpText = "Publish a new release for a given snap app")]
    class PublishOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Snap app name", Required = true)]
        public string App { get; set; }
        [Option('c', "channel", HelpText = "Snap channel name", Required = true)]
        public string Channel { get; set; }
        [Option('n', "nupkg", HelpText = "Nuget package to publish", Required = true)]
        public string Nupkg { get; set; }
    }
}
