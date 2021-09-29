using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace Snap.Extensions;

internal static class ListExtensions
{
    public static IEnumerable<T> DistinctBy<T>([NotNull] this IEnumerable<T> list, [NotNull] Func<T, object> keySelector)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        return list.GroupBy(keySelector).Select(x => x.First());
    }
}