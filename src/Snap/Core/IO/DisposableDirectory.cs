using System;
using System.Threading.Tasks;

namespace Snap.Core.IO;

internal sealed class DisposableDirectory : IAsyncDisposable
{
    readonly ISnapFilesystem _filesystem;

    public string WorkingDirectory { get; }
        
    public static implicit operator string(DisposableDirectory directory) => directory.WorkingDirectory;

    public DisposableDirectory(string workingDirectory, ISnapFilesystem filesystem, bool createRandomSubdirectory = true)
    {
        _filesystem = filesystem;
        WorkingDirectory = !createRandomSubdirectory ? workingDirectory : filesystem.PathCombine(workingDirectory, Guid.NewGuid().ToString());

        filesystem.DirectoryCreateIfNotExists(WorkingDirectory);
    }
        
    public async ValueTask DisposeAsync()
    {
        await _filesystem.DirectoryDeleteAsync(WorkingDirectory);
    }
}