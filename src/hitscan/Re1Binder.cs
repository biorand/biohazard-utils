using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace IntelOrca.Biohazard.HitScan
{
    class Re1Binder
    {
        private DispatcherTimer _refreshTimer = new DispatcherTimer();
        private ReProcess _reProcess;
        private WeaponListData _data;

        public Re1Binder()
        {
            _refreshTimer.Tick += (s, e) => CheckProcess();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(100);
            _refreshTimer.Start();
        }

        public WeaponListData Data
        {
            get => _data;
            set
            {
                if (_data != value)
                {
                    if (_data != null)
                    {
                        _data.PropertyChanged -= OnDataPropertyChanged;
                    }
                    _data = value;
                    _data.PropertyChanged += OnDataPropertyChanged;
                }
            }
        }

        private void OnDataPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WeaponData.LowCap) ||
                e.PropertyName == nameof(WeaponData.HighCap))
            {
                if (_reProcess == null || _reProcess.HasExited)
                    return;

                WriteWeaponNumbers();
            }
        }

        private void CheckProcess()
        {
            try
            {
                if (_data == null)
                    return;

                if (_reProcess == null || _reProcess.HasExited)
                {
                    _reProcess = null;
                }
                if (_reProcess == null)
                {
                    _reProcess = ReProcess.FromName("Bio");
                    if (_reProcess != null)
                    {
                        InitProcess();
                    }
                }
                if (_reProcess == null)
                {
                    _data.ProcessStatus = "Searching for game...";
                    _data.SelectedWeapon = null;
                    _data.Weapons.Clear();
                }
                else
                {
                    UpdateProcess();
                }
            }
            catch
            {
            }
        }

        private void InitProcess()
        {
            _data.Weapons.Clear();
            _data.Weapons.Add(CreateWeapon("Shotgun"));
            _data.Weapons.Add(CreateWeapon("Handgun / Magnum"));
            _data.Weapons.Add(CreateWeapon("Uzi"));
            _data.SelectedWeapon = _data.Weapons[1];

            // Write our thunk (@ 0x400268)
            _reProcess.WriteMemory(0x0040AD37, 0xE9, 0x2C, 0x55, 0xFF, 0xFF); // jmp
            _reProcess.WriteMemory(0x00400268,
                0x50, // push eax
                0xA1, 0xA8, 0x00, 0xC3, 0x00, // mov eax, [0C300A8h]
                0xA3, 0x38, 0x89, 0xC3, 0x00, // mov [0C38938h], eax
                0xA0, 0xB4, 0x51, 0xC3, 0x00, // mov al, [0C351B4h]
                0xA2, 0x3C, 0x89, 0xC3, 0x00, // mov [0C3893Ch], al
                0x66, 0xA1, 0x10, 0x87, 0xC3, 0x00, // mov ax, [0C38710h]
                0x66, 0xA3, 0x3E, 0x89, 0xC3, 0x00, // mov [0C3893Eh], ax
                0x58, // pop eax
                0xC3 // ret
            );
        }

        private WeaponData CreateWeapon(string name)
        {
            var result = new WeaponData() { Name = name };
            result.PropertyChanged += OnDataPropertyChanged;
            return result;
        }

        private unsafe void UpdateProcess()
        {
            var input = _reProcess.ReadMemory16(0x00C38710);
            var playerIndex = _reProcess.ReadMemory8(0xC351B5);
            var playerWeapon = _reProcess.ReadMemory8(0xC351B6);

            var lastHitValue = _reProcess.ReadMemory32(0x00C38938);
            var lastHitFlags = _reProcess.ReadMemory8(0x00C3893C);
            var lastInput = _reProcess.ReadMemory16(0x00C3893E);

            var hitScanData = new HitScanData[2];
            fixed (HitScanData* p = hitScanData)
            {
                _reProcess.ReadMemory(0x4AAD98, (IntPtr)p, 24);
            }
            var hitScanPlayerData = hitScanData[playerIndex];

            _data.ProcessStatus = $"Game found [PID: {_reProcess.Pid}]";

            _data.Weapons[0].LowCap = hitScanPlayerData.Shotgun.Lo;
            _data.Weapons[0].HighCap = hitScanPlayerData.Shotgun.Hi;
            _data.Weapons[1].LowCap = hitScanPlayerData.Handgun.Lo;
            _data.Weapons[1].HighCap = hitScanPlayerData.Handgun.Hi;
            _data.Weapons[2].LowCap = hitScanPlayerData.Uzi.Lo;
            _data.Weapons[2].HighCap = hitScanPlayerData.Uzi.Hi;

            var wpIndex = -1;
            switch (playerWeapon)
            {
                case 2:
                case 4:
                case 5:
                    wpIndex = 1;
                    break;
                case 3:
                    wpIndex = 0;
                    break;
                case 0x6F:
                case 0x70:
                    wpIndex = 2;
                    break;
            }

            if (wpIndex != -1)
            {
                if ((lastInput & 0x0001) != 0)
                    _data.Weapons[wpIndex].RecentHigh = lastHitValue;
                else if ((lastInput & 0x0004) != 0)
                    _data.Weapons[wpIndex].RecentLow = lastHitValue;
                else
                    _data.Weapons[wpIndex].RecentNeutral = lastHitValue;
            }
        }

        private unsafe void WriteWeaponNumbers()
        {
            var playerIndex = _reProcess.ReadMemory8(0xC351B5);

            var hitScanData = new HitScanData[2];
            fixed (HitScanData* p = hitScanData)
            {
                _reProcess.ReadMemory(0x4AAD98, (IntPtr)p, 24);
            }
            var hitScanPlayerData = hitScanData[playerIndex];
            hitScanPlayerData.Shotgun.Lo = (short)_data.Weapons[0].LowCap;
            hitScanPlayerData.Shotgun.Hi = (short)_data.Weapons[0].HighCap;
            hitScanPlayerData.Handgun.Lo = (short)_data.Weapons[1].LowCap;
            hitScanPlayerData.Handgun.Hi = (short)_data.Weapons[1].HighCap;
            hitScanPlayerData.Uzi.Lo = (short)_data.Weapons[2].LowCap;
            hitScanPlayerData.Uzi.Hi = (short)_data.Weapons[2].HighCap;
            hitScanData[playerIndex] = hitScanPlayerData;
            fixed (HitScanData* p = hitScanData)
            {
                _reProcess.WriteMemory(0x4AAD98, (IntPtr)p, 24);
            }
        }
    }

    class ReProcess
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
