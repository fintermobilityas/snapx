using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("gc", HelpText = "Remove releases that are no longer required")]
    [UsedImplicitly]
    internal class GcOptions : BaseSubOptions
    {
 
    }
}
