using CommandLine;
using JetBrains.Annotations;

namespace Snap.Installer.Options
{
    [Verb("install", HelpText = "Install a nupkg")]
    [UsedImplicitly]
    internal sealed class InstallOptions : BaseSubOptions
    {
        [Option('f', "filename", HelpText = "Input filename")]
        public string Filename { get; [UsedImplicitly] set; }

        [Option('c', "channel", HelpText = "Default channel")]
        public string Channel { get; [UsedImplicitly] set; }
    }
}
