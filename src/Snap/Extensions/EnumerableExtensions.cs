using System;
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

        public static async Task ForEachAsync<T>([NotNull] this IEnumerable<T> source, [NotNull] Func<T, Task> body, int concurrency = 0)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (concurrency < 0) throw new ArgumentOutOfRangeException(nameof(concurrency));

            const int maxConcurrency = 8;
            
            if (concurrency == 0 
                || concurrency > maxConcurrency)
            {
                var processorCount = Environment.ProcessorCount;
                concurrency = processorCount > maxConcurrency ? maxConcurrency : processorCount;
            }
            
            using (var semaphore = new SemaphoreSlim(concurrency, concurrency))
            {                
                var threads = source.Select(x =>
                {
                    var tcs = new TaskCompletionSource<bool>();
                    
                    new Thread(async () =>
                    {
                        await semaphore.WaitAsync();
                        
                        try
                        {
                            await body.Invoke(x);
                        }
                        finally
                        {
                            semaphore.Release();
                            tcs.TrySetResult(true);
                        }
                    }).Start();
                    
                    return tcs.Task;
                });

                await Task.WhenAll(threads);
            }
            
        }    
    }
}
