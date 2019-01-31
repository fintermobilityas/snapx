using System;

namespace Snap.Update
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            Console.WriteLine($"Arguments: {args.Length}");
            foreach (var arg in args)
            {
                Console.WriteLine(arg);
            }
            Console.WriteLine("Finished.");
            return -22;
        }
    }
}
