using CommandLine;

namespace Snap.Tool.Options
{
    [Verb("install", HelpText = "Install app from a local nuget package.")]
    internal class InstallNupkgOptions : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Path to nupkg", Required = true)]
        public string Nupkg { get; set; }
    }
}
