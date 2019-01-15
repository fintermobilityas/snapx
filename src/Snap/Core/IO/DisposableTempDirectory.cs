using System;
using System.IO;

namespace Snap.Core.IO
{
    public sealed class DisposableTempDirectory : IDisposable
    {
        public string AbsolutePath { get; }

        public DisposableTempDirectory(string workingDirectory)
        {
            AbsolutePath = Path.Combine(workingDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(AbsolutePath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(AbsolutePath))
                {
                    Directory.Delete(AbsolutePath);
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
}
