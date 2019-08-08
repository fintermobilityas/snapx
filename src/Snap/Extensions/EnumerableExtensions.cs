using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

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

        public static Task ForEachAsync<T>([NotNull] this IEnumerable<T> source, [NotNull] Func<T, Task> body, int concurrency = 0)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (concurrency < 0) throw new ArgumentOutOfRangeException(nameof(concurrency));

            const int maxConcurrency = 8;
            
            if (concurrency == 0)
            {
                concurrency = Environment.ProcessorCount;
            }

            if (concurrency > maxConcurrency)
            {
                concurrency = maxConcurrency;
            }
            
            // https://devblogs.microsoft.com/pfxteam/implementing-a-simple-foreachasync-part-2/
            return Task.WhenAll(
                Partitioner
                .Create(source)
                .GetPartitions(concurrency)
                .Select(partition => Task.Run(async delegate
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
