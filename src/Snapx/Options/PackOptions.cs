using System.Diagnostics.CodeAnalysis;
using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("pack", HelpText = "Publish a new release")]
    [UsedImplicitly]
    internal class PackOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Application id", Required = true)]
        public string AppId { get; [UsedImplicitly] set; }
        [Option('r', "rid", HelpText = "Runtime identifier target name, e.g win-x64", Required = true)]
        public string Rid { get; [UsedImplicitly] set; }
        [Option('v', "version", HelpText = "New application version", Required = true)]
        public string Version { get; [UsedImplicitly] set; }
        [Option('y', "yes", HelpText = "Yes (y) to all prompts")]
        public bool YesToAllPrompts { get; [UsedImplicitly] set; }
    }
}
