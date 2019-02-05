using CommandLine;

namespace Snap.Tool.Options
{
    [Verb("sha512", HelpText = "Calculate SHA-512 for a given file.")]
    internal class Sha512Options : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename", Required = true)]
        public string Filename { get; set; }
    }
}
