using System;
using System.Threading;
using System.Threading.Tasks;
using Snap.Core.Packaging;

namespace Snap.Update
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var a = new SnapExtractor(@"C:\Users\peters\Documents\GitHub\snap\src\Snap.Update\bin\Debug\netcoreapp2.2\Youpark-428.0.0-full.nupkg");
            await a.ExtractAsync(@"C:\Users\peters\Documents\GitHub\snap\src\Snap.Update\bin\Debug\netcoreapp2.2\test", CancellationToken.None);

            Console.WriteLine("Hello World!");
        }
    }
}
