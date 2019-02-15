using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("sha1", HelpText = "Calculate SHA-1 for a given file")]
    [UsedImplicitly]
    internal class Sha1Options : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename", Required = true)]
        public string Filename { get; [UsedImplicitly] set; }
    }
}
