using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace IntelOrca.Biohazard.HitScan
{
    internal class Re1Binder : IBinder
    {
        private readonly DispatcherTimer _refreshTimer = new DispatcherTimer();
        private ReProcess _reProcess;

        public string Name => "RE 1";

        public Re1Binder()
        {
            Data.PropertyChanged += OnDataPropertyChanged;

            _refreshTimer.Tick += (s, e) => CheckProcess();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(100);
            _refreshTimer.Start();
        }

        public WeaponListData Data { get; } = new WeaponListData();

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
                if (Data == null)
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
                    Data.ProcessStatus = "Searching for game...";
                    Data.SelectedWeapon = null;
                    Data.Weapons.Clear();
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
            Data.Weapons.Clear();
            Data.Weapons.Add(CreateWeapon("Shotgun"));
            Data.Weapons.Add(CreateWeapon("Handgun / Magnum"));
            Data.Weapons.Add(CreateWeapon("Uzi"));
            Data.SelectedWeapon = Data.Weapons[1];

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

            Data.ProcessStatus = $"Game found [PID: {_reProcess.Pid}]";

            Data.Weapons[0].LowCap = hitScanPlayerData.Shotgun.Lo;
            Data.Weapons[0].HighCap = hitScanPlayerData.Shotgun.Hi;
            Data.Weapons[1].LowCap = hitScanPlayerData.Handgun.Lo;
            Data.Weapons[1].HighCap = hitScanPlayerData.Handgun.Hi;
            Data.Weapons[2].LowCap = hitScanPlayerData.Uzi.Lo;
            Data.Weapons[2].HighCap = hitScanPlayerData.Uzi.Hi;

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
                    Data.Weapons[wpIndex].RecentHigh = lastHitValue;
                else if ((lastInput & 0x0004) != 0)
                    Data.Weapons[wpIndex].RecentLow = lastHitValue;
                else
                    Data.Weapons[wpIndex].RecentNeutral = lastHitValue;
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
            hitScanPlayerData.Shotgun.Lo = (short)Data.Weapons[0].LowCap;
            hitScanPlayerData.Shotgun.Hi = (short)Data.Weapons[0].HighCap;
            hitScanPlayerData.Handgun.Lo = (short)Data.Weapons[1].LowCap;
            hitScanPlayerData.Handgun.Hi = (short)Data.Weapons[1].HighCap;
            hitScanPlayerData.Uzi.Lo = (short)Data.Weapons[2].LowCap;
            hitScanPlayerData.Uzi.Hi = (short)Data.Weapons[2].HighCap;
            hitScanData[playerIndex] = hitScanPlayerData;
            fixed (HitScanData* p = hitScanData)
            {
                _reProcess.WriteMemory(0x4AAD98, (IntPtr)p, 24);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HitScanData
        {
            public HitScanValues Shotgun;
            public HitScanValues Handgun;
            public HitScanValues Uzi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HitScanValues
        {
            public short Hi;
            public short Lo;
        }
    }
}
