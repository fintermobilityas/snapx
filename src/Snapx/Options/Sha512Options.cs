using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("sha512", HelpText = "Calculate SHA-512 checksum for a given file")]
    [UsedImplicitly]
    internal class Sha512Options : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename", Required = true)]
        public string Filename { get; [UsedImplicitly] set; }
    }
}
