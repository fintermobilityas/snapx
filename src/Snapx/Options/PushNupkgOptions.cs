using System.Diagnostics.CodeAnalysis;
using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("push", HelpText = "Pushes a nupkg to the default release channel")]
    [UsedImplicitly]
    internal class PushNupkgOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Snap app name", Required = true)]
        public string App { get; set; }
        [Option('n', "nupkg", HelpText = "Nuget package to push", Required = true)]
        public string Nupkg { get; set; }
    }
}
