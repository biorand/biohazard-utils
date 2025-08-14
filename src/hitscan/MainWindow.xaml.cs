using System.Runtime.InteropServices;
using System.Windows;

namespace IntelOrca.Biohazard.HitScan
{
    public partial class MainWindow : Window
    {
        private Re1Binder _binder;

        public WeaponListData WeaponListData { get; } = new WeaponListData();

        public MainWindow()
        {
            InitializeComponent();

            DataContext = WeaponListData;

            _binder = new Re1Binder
            {
                Data = WeaponListData
            };
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
