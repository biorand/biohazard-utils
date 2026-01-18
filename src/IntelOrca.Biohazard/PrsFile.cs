using System;

namespace IntelOrca.Biohazard
{
    /// <summary>
    /// Wraps or compresses a buffer to a PRS-compressed buffer with capability to decompress it back.
    /// PRS is a LZ77 based compression algorithm by Sega designed for the Saturn, and Dreamcast. It was used in
    /// Resident Evil Code: Veronica which debuted on the Sega Dreamcast.
    /// </summary>
    public sealed class PrsFile
    {
        private readonly ReadOnlyMemory<byte> _compressed;
        private ReadOnlyMemory<byte>? _uncompressed;
        private readonly object _sync = new object();

        public ReadOnlyMemory<byte> Data => _compressed;

        public static PrsFile Compress(ReadOnlyMemory<byte> uncompressed)
        {
            // var bufferSize = 8192 - 1;
            var bufferSize = 256 - 1;
            return new PrsFile(Prs.Compress(uncompressed.ToArray(), bufferSize));
        }

        public PrsFile(ReadOnlyMemory<byte> compressed)
        {
            _compressed = compressed;
        }

        public unsafe ReadOnlyMemory<byte> Uncompressed
        {
            get
            {
                if (_uncompressed == null)
                {
                    lock (_sync)
                    {
                        if (_uncompressed == null)
                        {
                            _uncompressed = Prs.Decompress(_compressed.Span);
                        }
                    }
                }
                return _uncompressed.Value;
            }
        }
    }
}
