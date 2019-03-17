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
            if (!createRandomSubdirectory)
            {
                WorkingDirectory = workingDirectory;
                return;
            }
            WorkingDirectory = filesystem.PathCombine(workingDirectory, Guid.NewGuid().ToString());
        }
        
        public void Dispose()
        {
            _filesystem.DirectoryDeleteAsync(WorkingDirectory).GetAwaiter().GetResult();
        }
    }
}
