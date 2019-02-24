using System.Diagnostics.CodeAnalysis;
using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("pack", HelpText = "Create a new release for a given app")]
    [UsedImplicitly]
    internal class PackOptions : BaseSubOptions
    {
        [Option("id", HelpText = "Application id", Required = true)]
        public string AppId { get; [UsedImplicitly] set; }
        [Option('r', "rid", HelpText = "Runtime identifier target name, e.g win7-x64", Required = true)]
        public string Rid { get; [UsedImplicitly] set; }
        [Option('v', "version", HelpText = "New application version", Required = true)]
        public string Version { get; [UsedImplicitly] set; }
        [Option('y', "yes", HelpText = "Answer yes to all prompts")]
        public bool YesToAllPrompts { get; [UsedImplicitly] set; }
    }
}
