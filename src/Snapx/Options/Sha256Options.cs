using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("sha256", HelpText = "Calculate SHA-256 checksum for a given file")]
    [UsedImplicitly]
    internal class Sha256Options : BaseSubOptions
    {
        [Value(0,
            HelpText = "Input file to be processed.",
            MetaName = "input file",
            Required = true)]
        public string Filename { get; [UsedImplicitly] set; }

        [Usage(ApplicationAlias = "snapx")]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("Calculate SHA-256 checksum for a given file", new Sha256Options
                {
                    Filename = "test.txt"
                });
            }
        }
    }
}
