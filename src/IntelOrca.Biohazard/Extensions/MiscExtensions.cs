using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IntelOrca.Biohazard
{
    public static class MiscExtensions
    {
        public static ulong CalculateFnv1a(this byte[] data) => CalculateFnv1a(new ReadOnlySpan<byte>(data));
        public static ulong CalculateFnv1a(this ReadOnlyMemory<byte> data) => CalculateFnv1a(data.Span);
        public static ulong CalculateFnv1a(this ReadOnlySpan<byte> data)
        {
            var hash = 0x0CBF29CE484222325UL;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= 0x100000001B3UL;
            }
            return hash;
        }

        public static byte PeekByte(this BinaryReader br)
        {
            var pos = br.BaseStream.Position;
            var b = br.ReadByte();
            br.BaseStream.Position = pos;
            return b;
        }

        public static int ReadUInt24(this BinaryReader br)
        {
            var a = br.ReadUInt16();
            var b = br.ReadByte();
            return a | (b << 16);
        }

        public static void Write(this BinaryWriter bw, byte value, int count)
        {
            if (count == 0)
                return;

            var buffer = new byte[Math.Min(4096, count)];
            Unsafe.InitBlock(ref buffer[0], value, (uint)buffer.Length);
            for (var i = 0; i < count; i += buffer.Length)
            {
                var left = count - i;
                var amount = Math.Min(left, buffer.Length);
                bw.Write(buffer, 0, amount);
            }
        }

        public static void Write(this BinaryWriter bw, ReadOnlySpan<byte> data)
        {
            var buffer = new byte[4096];
            for (var i = 0; i < data.Length; i += buffer.Length)
            {
                var left = data.Length - i;
                var view = data.Slice(i, Math.Min(left, buffer.Length));
                view.CopyTo(buffer);
                bw.Write(buffer, 0, view.Length);
            }
        }

        public static void Write(this BinaryWriter bw, ReadOnlyMemory<byte> data) => Write(bw, data.Span);

        public static void WriteASCII(this BinaryWriter bw, string s)
        {
            foreach (var c in s)
            {
                bw.Write((byte)c);
            }
        }

        public static void Align(this BinaryWriter bw, int value)
        {
            var stream = bw.BaseStream;
            while ((stream.Position % value) != 0)
            {
                bw.Write((byte)0);
            }
        }

        public static unsafe T ReadStruct<T>(this BinaryReader br) where T : struct
        {
            var bytes = br.ReadBytes(Marshal.SizeOf<T>());
            fixed (byte* b = bytes)
            {
                return Marshal.PtrToStructure<T>((IntPtr)b);
            }
        }

        public static unsafe T Write<T>(this BinaryWriter bw, T structure) where T : struct
        {
            var result = default(T);
            var bytes = new byte[Marshal.SizeOf<T>()];
            fixed (byte* b = bytes)
            {
                Marshal.StructureToPtr(structure, (IntPtr)b, false);
            }
            bw.Write(bytes);
            return result;
        }

        public static void CopyAmountTo(this Stream source, Stream destination, long length)
        {
            var buffer = new byte[4096];
            long count = 0;
            while (count < length)
            {
                var readLen = (int)Math.Min(buffer.Length, length - count);
                var read = source.Read(buffer, 0, readLen);
                if (read == 0)
                    break;

                destination.Write(buffer, 0, read);
                count += read;
            }
            if (count != length)
                throw new IOException($"Unable to copy {length} bytes to new stream.");
        }

        internal static ForkedBinaryReader Fork(this BinaryReader br)
        {
            return new ForkedBinaryReader(br);
        }

        internal static string Namify(this string[] table, string prefix, byte value)
        {
            var name = "";
            if (value < table.Length)
                name = table[value];
            if (string.IsNullOrEmpty(name))
                return $"{prefix}{value:X2}";
            return $"{prefix}{name}"
                .Replace(" ", "_")
                .Replace("(", "")
                .Replace(")", "")
                .ToUpperInvariant();
        }

        public static void SetElement<T>(this List<T> list, int index, T value)
        {
            while (list.Count <= index)
            {
                list.Add(default!);
            }
            list[index] = value;
        }
    }
}
