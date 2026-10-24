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

        /// <summary>
        /// Wraps the uncompressed data in a valid PRS stream without compressing
        /// it. This is faster than <see cref="Compress"/> and produces a stream
        /// about 12.5% larger than the input.
        /// </summary>
        public static PrsFile Store(ReadOnlyMemory<byte> uncompressed)
        {
            return new PrsFile(Prs.Store(uncompressed.Span));
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
