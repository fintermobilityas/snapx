using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Snap.Extensions
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal static class TplExtensions
    {
        // https://blogs.msdn.microsoft.com/pfxteam/2012/10/05/how-do-i-cancel-non-cancelable-async-operations/
        public static async Task<T> WithCancellation<T>([NotNull] this Task<T> task, CancellationToken cancellationToken)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            
            // The tasck completion source. 
            var tcs = new TaskCompletionSource<bool>();

            // Register with the cancellation token.
            using (cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetResult(true), tcs))
            {
                // If the task waited on is the cancellation token...
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            // Wait for one or the other to complete.
            return await task;
        }

        public static async Task WithCancellation([NotNull] this Task task, CancellationToken cancellationToken)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            
            // The tasck completion source. 
            var tcs = new TaskCompletionSource<bool>();

            // Register with the cancellation token.
            using (cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetResult(true), tcs))
            {
                // If the task waited on is the cancellation token...
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            // Wait for one or the other to complete.
            await task;
        }
    }
}
