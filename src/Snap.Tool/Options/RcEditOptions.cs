using CommandLine;

namespace Snap.Tool.Options
{
    [Verb("rcedit", HelpText = "Resource manipulation for .NET executables")]
    internal class RcEditOptions : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename", Required = true)]
        public string Filename { get; set; }
        [Option("gui-app", HelpText = "Change subsystem to for console application to Windows GUI")]
        public bool ConvertSubSystemToWindowsGui { get; set; }
    }
}
