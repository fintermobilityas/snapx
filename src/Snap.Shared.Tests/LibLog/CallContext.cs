using System.Collections.Concurrent;
using System.Threading;

namespace Snap.Shared.Tests.LibLog;

// https://www.cazzulino.com/callcontext-netstandard-netcore.html
public static class CallContext<T>
{
    static readonly ConcurrentDictionary<string, AsyncLocal<T>> State = new();

    /// <summary>
    /// Stores a given object and associates it with the specified name.
    /// </summary>
    /// <param name="name">The name with which to associate the new item in the call context.</param>
    /// <param name="data">The object to store in the call context.</param>
    public static void SetData(string name, T data) =>
        State.GetOrAdd(name, _ => new AsyncLocal<T>()).Value = data;

    /// <summary>
    /// Retrieves an object with the specified name from the <see cref="CallContext{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the data being retrieved. Must match the type used when the <paramref name="name"/> was set via <see cref="SetData"/>.</typeparam>
    /// <param name="name">The name of the item in the call context.</param>
    /// <returns>The object in the call context associated with the specified name, or a default value for <typeparamref name="T"/> if none is found.</returns>
    public static T GetData(string name) =>
        State.TryGetValue(name, out var data) ? data.Value : default;
}