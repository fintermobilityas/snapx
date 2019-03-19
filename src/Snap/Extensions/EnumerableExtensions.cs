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

        // https://devblogs.microsoft.com/pfxteam/implementing-a-simple-foreachasync-part-2/
        public static Task ForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> body, int concurrency = 0) 
        {
            if (concurrency == 0)
            {
                concurrency = Environment.ProcessorCount;
            }
            return Task.WhenAll( 
                from partition in Partitioner.Create(source).GetPartitions(concurrency) 
                select Task.Run(async delegate { 
                    using (partition) 
                        while (partition.MoveNext()) 
                            await body(partition.Current); 
                })); 
        }    
    }
}
