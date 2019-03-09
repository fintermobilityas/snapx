using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Snap.Extensions
{
    internal static class EnumerableExtensions
    {
        public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource> onNext)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            foreach (var item in source) onNext(item);
        }

        public static Task ForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> body, int degreeOfParallelism = 0)
        {
            degreeOfParallelism = degreeOfParallelism <= 0 ? Environment.ProcessorCount : degreeOfParallelism;
            return Task.WhenAll(
                Partitioner
                .Create(source)
                .GetPartitions(degreeOfParallelism)
                .Select(partition => Task.Run(async () =>
                {
                    using (partition)
                    {
                        while (partition.MoveNext())
                        {
                            await body(partition.Current);
                        }
                    }
                })));
        }
    }
}
