using System;
using System.IO;

static class Program 
{
    static void Main(string[] args)
    {
#if DEBUG
        Console.WriteLine("Hello, World!");
#endif
        int counter = 0;
        foreach(var argument in args)
        {
            Console.WriteLine(string.Format("{0}: {1}", counter++, argument));
        }
    }
};
