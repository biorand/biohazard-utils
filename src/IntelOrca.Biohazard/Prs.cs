// Based on the PRS compression/decompression algorithm by Sewer56
// https://github.com/Sewer56/dlang-prs

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IntelOrca.Biohazard
{
    internal static class Prs
    {
        public static byte[] Decompress(ReadOnlySpan<byte> source)
        {
            var ctx = new PrsDecompressContext();
            return ctx.Decompress(source);
        }

        public static byte[] Compress(ReadOnlySpan<byte> source, int searchBufferSize = 0x1FFF)
        {
            var ctx = new PrsCompressContext();
            return ctx.Compress(source, searchBufferSize);
        }

        private ref struct PrsDecompressContext
        {
            private ReadOnlySpan<byte> _src;
            private int _srcPosition;

            private byte[] _dst;
            private int _dstPosition;
            private Span<byte> _dstSpan;

            private byte _controlByte;
            private int _currentBitPosition;

#if NET
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
            public byte[] Decompress(ReadOnlySpan<byte> src)
            {
                _src = src;
                _srcPosition = 0;

                _dst = new byte[_src.Length * 8]; // Safe estimate.
                _dstPosition = 0;
                _dstSpan = new Span<byte>(_dst);

                _controlByte = ReadByte();
                _currentBitPosition = 0;

                // Endlessly iterate over the file until the end of file signature/opcode special combo is hit.
                while (true)
                {
                    // If it was not safe then, well, we have to :/
                    if (_dstPosition + 256 > _dst.Length)
                    {
                        Array.Resize(ref _dst, _dst.Length * 2);
                        _dstSpan = new Span<byte>(_dst);
                    }

                    // Test for Direct Byte (Opcode 1)
                    if (RetrieveControlBit() == 1)
                    {
                        // Write direct byte.
                        _dstSpan[_dstPosition++] = ReadByte();
                        continue;
                    }

                    // Opcode 1 failed, now testing for Opcode 0X
                    // Test for Opcode 01
                    if (RetrieveControlBit() == 1)
                    {
                        // Write long copy, break if it's end of file.
                        if (WriteLongCopy())
                            break;
                    }
                    // Do Opcode 00
                    else
                    {
                        WriteShortCopy();
                    }
                }

                Array.Resize(ref _dst, _dstPosition);
                return _dst;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private byte ReadByte()
            {
                return _src[_srcPosition++];
            }

            /// <summary>
            /// Retrieves the next control bit inside of the currently
            /// set controlByte. Fetches the next controlByte and reads
            /// the first bit if the current controlByte is exhausted.
            /// </summary>
            /// <returns></returns>
#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private int RetrieveControlBit()
            {
                // Once we are exhausted out of bits, we need to read the next one from our stream.
                if (_currentBitPosition >= 8)
                {
                    // Get new controlByte and reset bit position.
                    _controlByte = ReadByte();
                    _currentBitPosition = 0;
                }

                // Retrieve our return value and pre-shift control byte for our next return value.
                var returnValue = _controlByte & 0x01; // Select first bit.
                _controlByte = (byte)(_controlByte >> 1);

                // Read the next bit next time.
                _currentBitPosition += 1;

                return returnValue;
            }

            /// <summary>
            /// Decodes a PRS encoded shortjump (after the opcode)
            /// and writes the result into the destination array.
            /// 
            /// Params:
            /// source = Source array to PRS shortcopy from.
            /// destination = Destination array to perform the copy operation to.
            /// </summary>
#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private void WriteShortCopy()
            {
                // Use a shorter variable name for simplification. (Compiler will optimize this out in release mode)
                var length = 0;

                // Get our length for the jump.
                length = length | RetrieveControlBit(); // The second bit comes first.
                length = length << 1;                   // Small hint to the compiler.
                length = length | RetrieveControlBit(); // Then the first bit.

                // Offset the value back by 2.
                length += 2;

                // Obtain the offset.
                var offset = ReadByte() | -0x100; // -0x100 converts from `256 - positive offset` to `negative offset`
                                                  // We lost our sign when we originally wrote the offset.

                // LZ77 Write to Destination
                LZ77Copy(length, offset);
            }

            /// <summary>
            /// Copies bytes from the source array to the destination array with the specified length
            /// and offset. The final byte index of the destination is used for declaring the position from which
            /// the look behind operation is performed.
            /// </summary>
#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private void LZ77Copy(int length, int offset)
            {
                // Contains the pointer to the destination array from which to perform the look back for bytes to copy from.
                var copyStartPosition = _dstPosition + offset; // offset is negative

                // Minimal vector optimizations.
                if (copyStartPosition + length < _dstPosition)
                {
                    var src = _dstSpan.Slice(copyStartPosition, length);
                    var dst = _dstSpan.Slice(_dstPosition, length);
                    src.CopyTo(dst);
                }
                else
                {
                    for (var x = 0; x < length; x++)
                    {
                        _dstSpan[_dstPosition + x] = _dstSpan[copyStartPosition + x];
                    }
                }

                _dstPosition += length;
            }

            /// <summary>
            /// Decodes a PRS encoded longjump(after the opcode)
            /// and writes the result into the destination array.
            /// 
            /// Returns true if this is the special end of file
            /// opcode, else false.
            /// 
            /// Params:
            /// source = Source array to PRS shortcopy from.
            /// destination = Destination array to perform the copy operation to.
            /// </summary>
#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private bool WriteLongCopy()
            {
                // Obtain the offset and size packed combination.
                int offset = ReadByte(); // Dlang will return negative number, e.g. ff ff ff 88 when only 88 is read because of signed bytes being default.
                                         // Readbyte has been modified to return unsigned only
                offset |= ReadByte() << 8;

                // Check for decompression end condition.
                if (offset == 0)
                {
                    return true;
                }

                // Separate the size from the offset and calculate the actual offset.
                var length = offset & 0b111;
                offset = (offset >> 3) | -0x2000; // When packing, we have lost the contents of the initial bits when left shifting which make
                                                  // our offset negative (8192 - offset = actual offset)
                                                  // Here we simply re-add those bits back to get our actual offset.

                // Check if Mode 3 (Long Copy Large)
                if (length == 0)
                {
                    // Get length from next byte and increment.
                    length = ReadByte();
                    length += 1;
                }
                else // Otherwise Mode 2 (Long Copy Short)
                {
                    length += 2; // Offset length by 2 a packed.
                }

                // LZ77 Write to Destination
                LZ77Copy(length, offset);
                return false;
            }
        }

        private ref struct PrsCompressContext
        {
            /// <summary>
            // Defines the maximum allowed length of matching/equivalent sequential bytes in a found pattern
            // within the search buffer as part of the LZ77 compression algorithm PRS bases on.
            // This value is inclusive, i.e.maxLength + 1 is the first disallowed value.
            /// </summary>
            public const int maxLength = 0x100;
            private const int shortCopyMaxLength = 0x100;
            private const int shortCopyMaxOffset = 5;
            private const int shortCopyMinOffset = 2;

            private int _searchBufferSize;

            private ReadOnlySpan<byte> _src;
            private List<byte> _dst;

            private int _srcPosition;
            private int _controlByteOffset;
            private int _currentBitPosition;

#if NET
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
            public byte[] Compress(ReadOnlySpan<byte> src, int searchBufferSize = 0x1FFF)
            {
                _searchBufferSize = searchBufferSize;
                _src = src;

                // Assume our compressed file will be at least of equivalent length.
                _dst = new List<byte>
                {
                    Capacity = (int)((_src.Length * 1.15f) + 3)
                };

                // Theoretical worst scenario for PRS compression is 9/8, 112.5% size + 3 bytes
                // This is when every byte cannot get a copy back.
                // Just in case, I will be very generous and give 115%, + 3 bytes

                // Setup control byte.
                _controlByteOffset = _dst.Count;
                _dst.Add(0);

                // Reset variables.
                _srcPosition = 0;
                _currentBitPosition = 0;

                // Begin compression.
                while (_srcPosition < _src.Length)
                {
                    // Find the longest match of repeating bytes.
                    var lz77Match = LZ77GetLongestMatch();

                    // Pack into the archive as direct byte if there is no match.
                    if (lz77Match.offset >= -shortCopyMaxLength && lz77Match.length >= shortCopyMinOffset && lz77Match.length <= shortCopyMaxOffset)
                    {
                        _srcPosition += lz77Match.length;
                        WriteShortCopy(lz77Match);
                    }
                    else if (lz77Match.length <= 2)
                    {
                        WriteDirectByte(_src[_srcPosition]);
                    }
                    else
                    {
                        // Encode LZ77 Match
                        _srcPosition += lz77Match.length;
                        if (lz77Match.length >= 3 && lz77Match.length <= 9)
                            WriteLongCopySmall(lz77Match);
                        else
                            WriteLongCopyLarge(lz77Match);
                    }
                }

                // Add finisher to PRS file.
                AppendControlBit(0);
                AppendControlBit(1);
                _dst.Add(0x00);
                _dst.Add(0x00);

                // Return back
                return _dst.ToArray();
            }

            /// <summary>
            /// Places an individual bit into the current index of the current control byte,
            /// then increments the current bit position denoted by variable currentBitPosition.
            /// </summary>
            /// <param name="bit">The either 0 or 1 bit to be appended onto the control byte.</param>
#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private void AppendControlBit(int bit)
            {
                // All of the bit positions have been used up, we need to write the bit 
                // and its the data for the block down.
                //
                // In this reference encoder we do the exhaustion check and flushing/writing
                // of the block of opcodes (control byte) and data before writing the next
                // opcode rather than after.
                //
                // This automatically ensures that the opcode and the next control byte lie
                // in the same block as long as the order or writing the opcodes before their
                // data block counterparts (which makes the source code cleaner and easier to 
                // understand anyway) is enforced.
                if (_currentBitPosition >= 8)
                {
                    // Setup next control byte.
                    _controlByteOffset = _dst.Count;
                    _dst.Add(0x00);

                    // Reset offset.
                    _currentBitPosition = 0;
                }

                // Append the current bit position and go to next position.
                var oldControlByte = _dst[_controlByteOffset];
                _dst[_controlByteOffset] = (byte)(oldControlByte | (bit << _currentBitPosition));
                _currentBitPosition++;
            }

#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private void WriteDirectByte(byte byteToWrite)
            {
                AppendControlBit(1);
                _dst.Add(byteToWrite);
                _srcPosition += 1;
            }

#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private void WriteShortCopy(LZ77Properties lz77Match)
            {
                // Offset the size as required for this mode (pack 2-5 as 0-3)
                lz77Match.length -= 2;

                // Write opcode 00.
                AppendControlBit(0);
                AppendControlBit(0);

                // Pack the size with the second byte first.
                AppendControlBit((lz77Match.length >> 1) & 1);
                AppendControlBit(lz77Match.length & 1);

                // Write the offset as 256 - (offset * - 1) as required by the format.
                _dst.Add((byte)(lz77Match.offset & 0xFF));
            }

#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private void WriteLongCopySmall(LZ77Properties lz77Match)
            {
                // Offset the size as required for this mode (pack 3-9 as 1-7)
                lz77Match.length -= 2;

                // Write opcode 01.
                AppendControlBit(0);
                AppendControlBit(1);

                // Pack the size into the short offset and write.
                short packed = (short)(((lz77Match.offset << 3) & 0xFFF8) | lz77Match.length);

                // Write the packed size and offset in Big Endian
                _dst.Add((byte)packed);
                _dst.Add((byte)(packed >> 8));
            }

#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private void WriteLongCopyLarge(LZ77Properties lz77Match)
            {
                // Offset the size as required for this mode.
                lz77Match.length -= 1;

                // Write opcode 01.
                AppendControlBit(0);
                AppendControlBit(1);

                // Pack the size into the short offset and write.
                short packed = (short)((lz77Match.offset << 3) & 0xFFF8);

                // Write the packed size and offset in Big Endian
                _dst.Add((byte)packed);
                _dst.Add((byte)(packed >> 8));

                // Write the offset.
                _dst.Add((byte)lz77Match.length);
            }

            /// <summary>
            /// Digs through the search buffer and finds the longest match
            /// of repeating bytes which match the bytes at the current pointer
            /// onward.
            /// </summary>
#if NET
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            private readonly LZ77Properties LZ77GetLongestMatch()
            {
                // The source bytes are a reference in order to prevent copying.
                // The other parameters are value type in order to take advantage of locality of reference.

                // Stores the details of the best found LZ77 match up till a point.
                var bestLZ77Match = new LZ77Properties();

                // Set the minimum position the pointer can access.
                int minimumPointerPosition = Math.Max(0, _srcPosition - _searchBufferSize);

                // Speedup: If cannot exceed source length, do not check it on every loop iteration. (else clause) 
                if (_srcPosition + maxLength + sizeof(int) >= _src.Length) // length is 1 indexed, our reads are not.
                {
                    for (int currentPointer = _srcPosition - 1; currentPointer >= minimumPointerPosition; currentPointer--)
                    {
                        if (_src[currentPointer] == _src[_srcPosition])
                        {
                            // We've matched a symbol: Count matching symbols.
                            var currentLength = 1;
                            while ((_srcPosition + currentLength < _src.Length) && (_src[currentPointer + currentLength] == _src[_srcPosition + currentLength]))
                                currentLength++;

                            // Cap at the limit of repeated bytes if it's over the limit of what PRS allows.
                            // We can also stop our search here.
                            if (currentLength > maxLength)
                            {
                                currentLength = maxLength;
                                bestLZ77Match.length = currentLength;
                                bestLZ77Match.offset = currentPointer - _srcPosition;
                                return bestLZ77Match;
                            }

                            // Set the best match if acquired.
                            if (currentLength > bestLZ77Match.length)
                            {
                                bestLZ77Match.length = currentLength;
                                bestLZ77Match.offset = currentPointer - _srcPosition;
                            }
                        }
                    }
                }
                else
                {
                    var longInitialMatch = MemoryMarshal.Read<int>(_src.Slice(_srcPosition, 4));
                    int initialMatch = longInitialMatch & 0x00FFFFFF;

                    // Iterate over each individual byte backwards to find the longest match.
                    for (int currentPointer = _srcPosition - 1; currentPointer >= minimumPointerPosition; currentPointer--)
                    {
                        var match = MemoryMarshal.Read<int>(_src.Slice(currentPointer, 4));
                        if ((match & 0x00FFFFFF) == initialMatch)
                        {
                            // We've matched a symbol: Count matching symbols.
                            var currentLength = 3;
                            while (_src[currentPointer + currentLength] == _src[_srcPosition + currentLength])
                            {
                                currentLength++;

                                // This check needs to be here, otherwise the search might go into unitialized memory as 
                                // the loop will not cap before maxLength
                                if (currentLength > maxLength)
                                {
                                    currentLength = maxLength;
                                    bestLZ77Match.length = currentLength;
                                    bestLZ77Match.offset = currentPointer - _srcPosition;
                                    return bestLZ77Match;
                                }
                            }

                            // Set the best match if acquired.
                            if (currentLength > bestLZ77Match.length)
                            {
                                bestLZ77Match.length = currentLength;
                                bestLZ77Match.offset = currentPointer - _srcPosition;
                            }
                        }
                    }

                    // If no match found, check for possible missed short copy (2-5 bytes).
                    if (bestLZ77Match.length == 0)
                    {
                        short shortInitialMatch = MemoryMarshal.Read<short>(_src.Slice(_srcPosition, 2));
                        minimumPointerPosition = _srcPosition - Math.Min(_searchBufferSize, shortCopyMaxLength);
                        if (minimumPointerPosition < 0)
                            minimumPointerPosition = 0;

                        for (int currentPointer = _srcPosition - 1; currentPointer >= minimumPointerPosition; currentPointer--)
                        {
                            var match = MemoryMarshal.Read<short>(_src.Slice(currentPointer, 2));
                            if (match == shortInitialMatch)
                            {
                                // We've matched a symbol: Count matching symbols.
                                var currentLength = 2;
                                bestLZ77Match.length = currentLength;
                                bestLZ77Match.offset = currentPointer - _srcPosition;
                                break;
                            }
                        }
                    }
                }

                return bestLZ77Match;
            }

            private struct LZ77Properties
            {
                public int offset;
                public int length;
            }
        }
    }
}
