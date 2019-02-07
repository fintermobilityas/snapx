using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("rcedit", HelpText = "Manipulate resources for windows or linux binaries. Supports: PE/ELF binaries.")]
    [UsedImplicitly]
    internal class RcEditOptions : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename", Required = true)]
        public string Filename { get; set; }
        [Option("gui-app", HelpText = "Change subsystem to for console application to Windows GUI")]
        public bool ConvertSubSystemToWindowsGui { get; set; }
    }
}
