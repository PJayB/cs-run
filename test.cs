using System;
using System.IO;

static class Program 
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        foreach(var argument in args)
        {
            Console.WriteLine(argument);
        }
    }
};
