using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("install", HelpText = "Install app from a local nuget package")]
    [UsedImplicitly]
    internal class InstallNupkgOptions : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Path to nupkg", Required = true)]
        public string Nupkg { get; set; }
    }
}
