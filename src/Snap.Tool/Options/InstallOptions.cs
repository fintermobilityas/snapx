using CommandLine;

namespace Snap.Tool.Options
{
    [Verb("install-nupkg", HelpText = "Install app from a local nuget package")]
    internal class InstallNupkgOptions : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Path to nupkg", Required = true)]
        public string Filename { get; set; }
    }
}
