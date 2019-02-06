using CommandLine;

namespace Snap.Tool.Options
{
    [Verb("sha1", HelpText = "Calculate SHA-1 for a given file")]
    internal class Sha1Options : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename", Required = true)]
        public string Filename { get; set; }
    }
}