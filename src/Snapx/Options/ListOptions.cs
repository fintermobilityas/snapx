using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("list", true, HelpText = "Show current releases")]
    [UsedImplicitly]
    internal class ListOptions : BaseSubOptions
    {
        [Value(0, 
            HelpText = "The application id.",
            Required = false)]
        public string Id { get; [UsedImplicitly] set; }

        [Usage(ApplicationAlias = "snapx")]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("Show releases for all applications", new ListOptions());
                yield return new Example("Show releases for demoapp application", new ListOptions
                {
                    Id = "demoapp"
                });
            }
        }
    }
}
