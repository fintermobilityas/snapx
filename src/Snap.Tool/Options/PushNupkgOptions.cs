using System.Diagnostics.CodeAnalysis;
using CommandLine;

namespace Snap.Tool.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("push", HelpText = "Pushes a nupkg to the default release channel.")]
    internal class PushNupkgOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Snap app name", Required = true)]
        public string App { get; set; }
        [Option('n', "nupkg", HelpText = "Nuget package to push", Required = true)]
        public string Nupkg { get; set; }
    }
}
