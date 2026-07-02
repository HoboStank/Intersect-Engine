using System;
using System.IO;
using System.Linq;

class Program {
    static void Main(string[] args) {
        if (args.Length == 0) {
            Console.Error.WriteLine("Usage: inspect-key <path>");
            Environment.Exit(2);
        }
        var path = args[0];
        if (!File.Exists(path)) {
            Console.Error.WriteLine($"File not found: {path}");
            Environment.Exit(2);
        }
        var bytes = File.ReadAllBytes(path);
        Console.WriteLine($"File: {path}");
        Console.WriteLine($"Length: {bytes.Length}");
        int n = Math.Min(bytes.Length, 512);
        for (int i = 0; i < n; i += 16) {
            var slice = bytes.Skip(i).Take(Math.Min(16, n - i)).ToArray();
            Console.Write(i.ToString("X4") + ": ");
            Console.WriteLine(string.Join(" ", slice.Select(b => b.ToString("X2"))) + "  " +
                System.Text.Encoding.ASCII.GetString(slice).Replace('\r',' ').Replace('\n',' '));
        }
    }
}
