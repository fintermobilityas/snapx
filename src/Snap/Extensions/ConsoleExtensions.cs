using System;
using System.Linq;
using JetBrains.Annotations;

namespace Snap.Extensions
{
    internal static class ConsoleExtensions
    {
        public static bool Prompt([NotNull] this string verbsStr, [NotNull] string question, char delimeter = '|')
        {
            if (verbsStr == null) throw new ArgumentNullException(nameof(verbsStr));
            if (question == null) throw new ArgumentNullException(nameof(question));
            if (delimeter <= 0) throw new ArgumentOutOfRangeException(nameof(delimeter));
            Console.WriteLine(question);
            var verbs = verbsStr.Split(delimeter).ToList();
            var value = Console.ReadLine();
            return verbs.Any(verb => string.Equals(value, verb, StringComparison.InvariantCultureIgnoreCase));
        }
    }
}
