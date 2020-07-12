#if NETFULLFRAMEWORKAPP || NETSTANDARD
using System.Collections.Generic;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace System.Linq
{
    internal static class ShimExtensions
    {
        public static IEnumerable<TSource> SkipLast<TSource>([NotNull] this IEnumerable<TSource> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return source.SkipLast(0);
        }

        public static IEnumerable<TSource> SkipLast<TSource>([NotNull] this IEnumerable<TSource> source, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var enumerator = count <= 0 ? source.Skip(0).GetEnumerator() : source.GetEnumerator();
            var queue = new Queue<TSource>();
            using var innerEnumerator = enumerator;
            while (innerEnumerator.MoveNext())
            {
                if (queue.Count != count)
                {
                    queue.Enqueue(innerEnumerator.Current);
                    continue;
                }
                do
                {
                    yield return queue.Dequeue();
                    queue.Enqueue(innerEnumerator.Current);
                } while (innerEnumerator.MoveNext());
                break;
            }
        }
    }
}
#endif
