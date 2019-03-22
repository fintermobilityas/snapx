using System.Diagnostics.CodeAnalysis;
using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("restore", HelpText = "Restore missing or corrupt packages")]
    [UsedImplicitly]
    internal class RestoreOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Application id. If empty then all applications will be restored")]
        public string AppId { get; [UsedImplicitly] set; }
        [Option('r', "rid", HelpText = "Runtime identifier target name, e.g win-x64. If empty all rid's will be restored")]
        public string Rid { get; [UsedImplicitly] set; }
        [Option('i', "installers", HelpText = "Rebuild installers")]
        public bool BuildInstallers { get; set; } = true;
    }
}
