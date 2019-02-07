using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("rcedit", HelpText = "Manipulate resources for either Windows or Linux binaries")]
    [UsedImplicitly]
    internal class RcEditOptions : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename", Required = true)]
        public string Filename { get; set; }
        [Option("gui-app", HelpText = "Change console application subsystem Windows GUI")]
        public bool ConvertSubSystemToWindowsGui { get; set; }
    }
}
