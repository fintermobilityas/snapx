using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("sha256", HelpText = "Calculate SHA-256 checksum for a given file")]
    [UsedImplicitly]
    internal class Sha256Options : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename", Required = true)]
        public string Filename { get; [UsedImplicitly] set; }
    }
}
