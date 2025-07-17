using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace survey
{
    internal class GameState2(GameStateManifest manifest, Process process)
    {
        [DllImport("kernel32.dll")]
        private extern static bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private extern static bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesWritten);

        public Process Process => process;
        public GameStateManifest Manifest => manifest;

        private Dictionary<string, object> _values = [];
        private bool _updated;

        public bool Refresh()
        {
            _updated = false;
            foreach (var variableInfo in manifest.Variables)
            {
                var value = GetValueNow(variableInfo.Name);
                if (value != null)
                {
                    SetValue(variableInfo.Name, value);
                }
            }
            foreach (var group in manifest.FlagGroups)
            {
                var flagData = ReadFlagArray(group.Offset, group.Count);
                for (int i = 0; i < group.Count; i++)
                {
                    var flagName = $"flags.{group.Name}[{i}]";
                    SetValue(flagName, flagData[i] ? 1 : 0);
                }
            }
            return _updated;
        }

        public void Update(Dictionary<string, object> values)
        {
            Refresh();
            foreach (var kvp in values)
            {
                _values[kvp.Key] = kvp.Value;
            }

            foreach (var variableInfo in manifest.Variables)
            {
                if (_values.TryGetValue(variableInfo.Name, out var value))
                {
                }
            }

            foreach (var group in manifest.FlagGroups)
            {
                var flagData = new ReFlagArray(group.Count);
                for (int i = 0; i < group.Count; i++)
                {
                    var flagName = $"flags.{group.Name}[{i}]";
                    if (_values.TryGetValue(flagName, out var flagValue))
                    {
                        flagData[i] = !flagValue.Equals(0);
                    }
                }
                WriteFlagArray(group.Offset, flagData);
            }
        }

        private void SetValue(string name, object? value)
        {
            if (_values.TryGetValue(name, out var existingValue))
            {
                if (existingValue.Equals(value))
                {
                    return;
                }
            }

            if (value == null)
            {
                _values.Remove(name);
            }
            else
            {
                _values[name] = value;
            }
            _updated = true;
        }

        public object? GetValue(string name)
        {
            _values.TryGetValue(name, out var value);
            return value;
        }

        private object? GetValueNow(string name)
        {
            var variableInfo = manifest.Variables.FirstOrDefault(v => v.Name == name);
            if (variableInfo == null)
                return null;

            if (variableInfo.Type == "s16")
            {
                return ReadMemory(variableInfo.Offset, 2, false);
            }
            else if (variableInfo.Type == "u8")
            {
                return ReadMemory(variableInfo.Offset, 1, true);
            }

            return 0;
        }

        private unsafe object? ReadMemory(int offset, int size, bool unsigned)
        {
            var buffer = stackalloc byte[size];
            if (!ReadProcessMemory(process.Handle, offset, (nint)buffer, size, out var bytesRead))
                return null;

            var span = new ReadOnlySpan<byte>(buffer, size);
            if (size == 1)
            {
                return unsigned ? (byte)buffer[0] : (sbyte)buffer[0];
            }
            else if (size == 2)
            {
                return unsigned ? MemoryMarshal.Cast<byte, ushort>(span)[0] : MemoryMarshal.Cast<byte, short>(span)[0];
            }
            else if (size == 4)
            {
                return unsigned ? MemoryMarshal.Cast<byte, uint>(span)[0] : MemoryMarshal.Cast<byte, int>(span)[0];
            }
            else if (size == 8)
            {
                return unsigned ? MemoryMarshal.Cast<byte, ulong>(span)[0] : MemoryMarshal.Cast<byte, long>(span)[0];
            }
            else
            {
                return null;
            }
        }

        private unsafe ReFlagArray ReadFlagArray(int offset, int count)
        {
            var numWords = (count + 31) / 32;
            var words = new int[numWords];
            fixed (int* pBytes = words)
            {
                ReadProcessMemory(process.Handle, offset, (nint)pBytes, numWords, out var bytesRead);
                return new ReFlagArray(new Memory<int>(words));
            }
        }

        private unsafe void WriteFlagArray(int offset, ReFlagArray value)
        {
            var data = value.Data.Span;
            var numWords = data.Length;
            fixed (int* pBytes = data)
            {
                WriteProcessMemory(process.Handle, offset, (nint)pBytes, numWords, out var bytesWritten);
            }
        }
    }

    internal struct ReFlagArray
    {
        public Memory<int> Data { get; }

        public ReFlagArray(int count)
        {
            Data = new Memory<int>(new int[count / 32]);
        }

        public ReFlagArray(Memory<int> data)
        {
            Data = data;
        }

        public bool this[int index]
        {
            get
            {
                var wordIndex = index / 32;
                var wordMask = 1 << (31 - (index % 32));
                if (wordIndex >= Data.Length)
                    return false;
                return (Data.Span[wordIndex] & wordMask) != 0;
            }
            set
            {
                var wordIndex = index / 32;
                var wordMask = 1 << (31 - (index % 32));
                if (wordIndex >= Data.Length)
                    return;

                var oldValue = Data.Span[wordIndex];
                var newValue = value
                    ? oldValue | wordMask
                    : oldValue & ~wordMask;
                Data.Span[wordIndex] = newValue;
            }
        }
    }
}
