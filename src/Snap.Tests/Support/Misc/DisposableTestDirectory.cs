using System;
using System.IO;

namespace Snap.Tests.Support.Misc
{
    internal class DisposableTestDirectory : IDisposable
    {
        readonly string _testDirectory;

        public DisposableTestDirectory(string workingDirectory, string testDirectory = null)
        {
            _testDirectory = Path.Combine(workingDirectory, testDirectory ?? Guid.NewGuid().ToString());

            if (!Directory.Exists(_testDirectory))
            {
                Directory.CreateDirectory(_testDirectory);
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}