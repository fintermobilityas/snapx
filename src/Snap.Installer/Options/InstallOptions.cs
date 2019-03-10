using CommandLine;
using JetBrains.Annotations;

namespace Snap.Installer.Options
{
    internal sealed class InstallOptions : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename")]
        public string Filename { get; [UsedImplicitly] set; }
    }
}
