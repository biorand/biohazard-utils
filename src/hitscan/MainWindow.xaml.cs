using System.ComponentModel;
using System.Windows;

namespace IntelOrca.Biohazard.HitScan
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private IBinder _binder;

        public MainWindow()
        {
            InitializeComponent();
            SelectedBinder = Binders[0];
        }

        public IBinder SelectedBinder
        {
            get => _binder;
            set
            {
                if (_binder != value)
                {
                    _binder = value;
                    DataContext = _binder.Data;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBinder)));
                }
            }
        }

        public IBinder[] Binders { get; } = new IBinder[] {
            new Re1Binder(),
            new Re2Binder() };
    }
}
