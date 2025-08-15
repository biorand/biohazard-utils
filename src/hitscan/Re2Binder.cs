using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace IntelOrca.Biohazard.HitScan
{
    internal class Re2Binder : IBinder
    {
        private readonly DispatcherTimer _refreshTimer = new DispatcherTimer();
        private ReProcess _reProcess;

        public string Name => "RE 2";

        public Re2Binder()
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
                    _reProcess = ReProcess.FromName("bio2 1.10");
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
            foreach (var wpName in _weaponNames)
            {
                Data.Weapons.Add(CreateWeapon(wpName));
            }
            Data.SelectedWeapon = Data.Weapons[1];

            // Write our thunk (@ 0x40018F)
            _reProcess.WriteMemory(0x40018F,
                0xFF, 0x74, 0x24, 0x08, // push [esp+08]
                0x8F, 0x05, 0x50, 0xEE, 0x98, 0x00, // pop [098EE50h]
                0xFF, 0x35, 0x04, 0x86, 0x98, 0x00, // push [0988604h]
                0x8F, 0x05, 0x58, 0xEE, 0x98, 0x00, // pop [098EE58h]
                0xE9, 0xF6, 0x42, 0x0D, 0x00 // jmp 4D44A0
            );

            _reProcess.WriteMemory(0x004D55AC, 0xE8, 0xDE, 0xAB, 0xF2, 0xFF); // call thunk
            _reProcess.WriteMemory(0x004D86C6, 0xE8, 0xC4, 0x7A, 0xF2, 0xFF); // call thunk
            _reProcess.WriteMemory(0x004D8F3B, 0xE8, 0x4F, 0x72, 0xF2, 0xFF); // call thunk
        }

        private WeaponData CreateWeapon(string name)
        {
            var result = new WeaponData() { Name = name };
            result.PropertyChanged += OnDataPropertyChanged;
            return result;
        }

        private unsafe void UpdateProcess()
        {
            var input = _reProcess.ReadMemory16(0x00988604);
            var playerIndex = _reProcess.ReadMemory8(0x98E9A6);
            var playerWeapon = _reProcess.ReadMemory8(0x989FFE);

            var lastHitValue = _reProcess.ReadMemory32(0x0098EE50);
            var lastHitFlags = _reProcess.ReadMemory8(0x0098EE54);
            var lastInput = _reProcess.ReadMemory16(0x0098EE58);

            var hitScanData = ReadHitScanData();

            Data.ProcessStatus = $"Game found [PID: {_reProcess.Pid}]";

            for (var i = 0; i < hitScanData.Length; i++)
            {
                Data.Weapons[i].LowCap = hitScanData[i].Value[playerIndex];
            }

            if ((lastInput & 0x0001) != 0)
                Data.Weapons[playerWeapon].RecentHigh = lastHitValue;
            else if ((lastInput & 0x0004) != 0)
                Data.Weapons[playerWeapon].RecentLow = lastHitValue;
            else
                Data.Weapons[playerWeapon].RecentNeutral = lastHitValue;
        }

        private unsafe void WriteWeaponNumbers()
        {
            var playerIndex = _reProcess.ReadMemory8(0x98E9A6);
            var hitScanData = ReadHitScanData();
            for (var i = 0; i < hitScanData.Length; i++)
            {
                hitScanData[i].Value[playerIndex] = (short)Data.Weapons[i].LowCap;
            }
            fixed (HitScanValues* p = hitScanData)
            {
                _reProcess.WriteMemory(0x53A358, (IntPtr)p, hitScanData.Length * sizeof(HitScanValues));
            }
        }

        private unsafe HitScanValues[] ReadHitScanData()
        {
            var hitScanData = new HitScanValues[_weaponNames.Length];
            fixed (HitScanValues* p = hitScanData)
            {
                _reProcess.ReadMemory(0x53A358, (IntPtr)p, hitScanData.Length * sizeof(HitScanValues));
            }
            return hitScanData;
        }

        private static readonly string[] _weaponNames = new string[] {
            "None",
            "Knife",
            "HandgunLeon",
            "HandgunClaire",
            "CustomHandgun",
            "Magnum",
            "CustomMagnum",
            "Shotgun",
            "CustomShotgun",
            "GrenadeLauncherExplosive",
            "GrenadeLauncherFlame",
            "GrenadeLauncherAcid",
            "Bowgun",
            "ColtSAA",
            "Sparkshot",
            "SMG",
            "Flamethrower",
            "RocketLauncher",
            "GatlingGun",
            "Beretta",
        };

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct HitScanValues
        {
            public short Var0;
            public short Var1;
            public fixed short Value[2];
        }
    }
}
