using System.ComponentModel;

namespace IntelOrca.Biohazard.HitScan
{

    public class WeaponData : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _name;
        private int _lowCap;
        private int _highCap;
        private int _recentLow;
        private int _recentNeutral;
        private int _recentHigh;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    RaisePropertyChanged(nameof(Name));
                }
            }
        }

        public int LowCap
        {
            get => _lowCap;
            set
            {
                if (_lowCap != value)
                {
                    _lowCap = value;
                    RaisePropertyChanged(nameof(LowCap));
                }
            }
        }

        public int HighCap
        {
            get => _highCap;
            set
            {
                if (_highCap != value)
                {
                    _highCap = value;
                    RaisePropertyChanged(nameof(HighCap));
                }
            }
        }

        public int RecentLow
        {
            get => _recentLow;
            set
            {
                if (_recentLow != value)
                {
                    _recentLow = value;
                    RaisePropertyChanged(nameof(RecentLow));
                }
            }
        }

        public int RecentNeutral
        {
            get => _recentNeutral;
            set
            {
                if (_recentNeutral != value)
                {
                    _recentNeutral = value;
                    RaisePropertyChanged(nameof(RecentNeutral));
                }
            }
        }

        public int RecentHigh
        {
            get => _recentHigh;
            set
            {
                if (_recentHigh != value)
                {
                    _recentHigh = value;
                    RaisePropertyChanged(nameof(RecentHigh));
                }
            }
        }

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
