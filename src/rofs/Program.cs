using System;
using System.IO;
using IntelOrca.Biohazard;

namespace rofs
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: rofs <input.rofs> [output_dir]");
                return;
            }

            string inputPath = args[0];
            string outputDir = args.Length > 1 ? args[1] : Path.GetFileNameWithoutExtension(inputPath);

            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"File not found: {inputPath}");
                return;
            }

            try
            {
                using var archive = new RE3Archive(inputPath);
                archive.Extract(outputDir);
                Console.WriteLine($"Extracted to {outputDir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
