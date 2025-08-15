using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace IntelOrca.Biohazard.HitScan
{
    internal class ReProcess
    {
        private readonly Process _process;

        public int Pid => _process.Id;
        public bool HasExited => _process.HasExited;

        private ReProcess(Process process)
        {
            _process = process;
        }

        public static ReProcess FromName(string processName)
        {
            var process = Process.GetProcessesByName(processName).FirstOrDefault();
            if (process == null)
                return null;

            return new ReProcess(process);
        }

        public unsafe void ReadMemory(int address, IntPtr buffer, int bufferSize)
        {
            ReadProcessMemory(_process.Handle, (IntPtr)address, buffer, bufferSize, out _);
        }

        public unsafe void WriteMemory(int address, IntPtr buffer, int bufferSize)
        {
            WriteProcessMemory(_process.Handle, (IntPtr)address, buffer, bufferSize, out _);
        }

        public unsafe byte ReadMemory8(int address)
        {
            byte result = 0;
            ReadProcessMemory(_process.Handle, (IntPtr)address, (IntPtr)(&result), sizeof(byte), out _);
            return result;
        }

        public unsafe short ReadMemory16(int address)
        {
            short result = 0;
            ReadProcessMemory(_process.Handle, (IntPtr)address, (IntPtr)(&result), sizeof(short), out _);
            return result;
        }

        public unsafe int ReadMemory32(int address)
        {
            int result = 0;
            ReadProcessMemory(_process.Handle, (IntPtr)address, (IntPtr)(&result), sizeof(int), out _);
            return result;
        }

        public unsafe void WriteMemory(int address, params byte[] data)
        {
            fixed (byte* p = data)
            {
                WriteProcessMemory(_process.Handle, (IntPtr)address, (IntPtr)p, data.Length, out _);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            IntPtr lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            IntPtr lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesWritten);
    }
}
