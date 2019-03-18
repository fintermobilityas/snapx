using System;

namespace Snap.Core.IO
{
    internal sealed class DisposableDirectory : IDisposable
    {
        readonly ISnapFilesystem _filesystem;

        public string WorkingDirectory { get; }
        
        public DisposableDirectory(string workingDirectory, ISnapFilesystem filesystem, bool createRandomSubdirectory = true)
        {
            _filesystem = filesystem;
            WorkingDirectory = !createRandomSubdirectory ? workingDirectory : filesystem.PathCombine(workingDirectory, Guid.NewGuid().ToString());

            filesystem.DirectoryCreateIfNotExists(WorkingDirectory);
        }
        
        public void Dispose()
        {
            _filesystem.DirectoryDeleteAsync(WorkingDirectory).GetAwaiter().GetResult();
        }
    }
}
