using System;

namespace IntelOrca.Biohazard.Rdt
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                foreach (var inputPath in args)
                {
                    if (inputPath.EndsWith(".rdt", StringComparison.OrdinalIgnoreCase))
                    {
                        var outputPath = inputPath.Remove(inputPath.Length - 4);
                        var unpacker = new RdtUnpacker(BioVersion.Biohazard3, inputPath, outputPath);
                        unpacker.Unpack();
                    }
                    else if (inputPath.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase))
                    {
                        var packer = new RdtPacker(BioVersion.Biohazard3, inputPath);
                        packer.Pack();
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
