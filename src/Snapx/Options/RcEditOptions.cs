using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("rcedit", HelpText = "Manipulate resources for either Windows or Linux binaries")]
    [UsedImplicitly]
    internal class RcEditOptions : BaseSubOptions
    {
        [Option("gui-app",
            HelpText = "Change Windows Subsystem from Console to WindowsGui")]
        public bool ConvertSubSystemToWindowsGui { get; set; }

        [Option("icon",
            HelpText = "Set icon for a windows executable")]
        public string IconFilename { get; set; }

        [Value(0,
            HelpText = "The input filename.",
            Required = true)]
        public string Filename { get; set; }

        [Usage(ApplicationAlias = "snapx")]
        [UsedImplicitly]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("Change Windows Subsystem from Console to WindowsGui", new RcEditOptions
                {
                    Filename = "demoapp.exe",
                    ConvertSubSystemToWindowsGui = true
                });
                yield return new Example("Set icon for windows executable", new RcEditOptions
                {
                    Filename = "demoapp.exe",
                    IconFilename = "path/to/my/my.ico"
                });
            }
        }
    }
}
