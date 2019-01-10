using CommandLine;

namespace Snap.Options
{
    [Verb("install-nupkg", HelpText = "Install app from a local nuget package")]
    class InstallNupkgOptions : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename", Required = true)]
        public string Filename { get; set; }
    }
}
