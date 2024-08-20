using System;
using System.IO;

namespace Skatech.Monolith.Utilities;

internal static class UtilitySwap {
    public const string Name = "swap";
    public const string Description = "Swaps file initial and final blocks";

    public static void Run(string[] args) {
        if (args.Length > 0) {
            string path = args[0];
            using var stream = File.Open(path, FileMode.Open);
            int size = Math.Min(512, (int)(stream.Length / 2));
            var buffer1 = new byte[size];
            var buffer2 = new byte[size];
            
            Console.WriteLine($"Swapping file parts: {Path.GetFileName(path)}, block-size: {size}");
            
            if (buffer1.Length != stream.Read(buffer1, 0, buffer1.Length)) {
                throw new InvalidOperationException("Failed to read block from file.");
            }                
            
            stream.Seek(-size, SeekOrigin.End);
            if (buffer2.Length != stream.Read(buffer2, 0, buffer2.Length)) {
                throw new InvalidOperationException("Failed to read block from file.");
            }

            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(buffer2, 0, buffer2.Length);
            
            stream.Seek(-size, SeekOrigin.End);
            stream.Write(buffer1, 0, buffer1.Length);
        }
        else Console.WriteLine("Usage: swp file");
    }
}