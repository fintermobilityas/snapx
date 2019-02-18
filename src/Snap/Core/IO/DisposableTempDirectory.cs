using System;

namespace Snap.Core.IO
{
    internal sealed class DisposableTempDirectory : IDisposable
    {
        readonly ISnapFilesystem _filesystem;

        public string WorkingDirectory { get; }
        
        public DisposableTempDirectory(string workingDirectory, ISnapFilesystem filesystem, bool createRandomSubdirectory = true)
        {
            _filesystem = filesystem;
            if (!createRandomSubdirectory)
            {
                WorkingDirectory = workingDirectory;
                return;
            }
            WorkingDirectory = filesystem.PathCombine(workingDirectory, Guid.NewGuid().ToString());
            filesystem.DirectoryCreate(WorkingDirectory);
        }

        public void Dispose()
        {
            _filesystem.DirectoryDeleteOrJustGiveUpAsync(WorkingDirectory).Wait();
        }
    }
}
