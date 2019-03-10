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
        [Option("id", HelpText = "Application id to restore, if empty all applications will be restored")]
        public string AppId { get; [UsedImplicitly] set; }
    }
}
