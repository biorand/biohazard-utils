using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IntelOrca.Biohazard.HitScan
{
    public class WeaponListData : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private WeaponData _selectedWeapon;
        private string _processStatus;

        public ObservableCollection<WeaponData> Weapons { get; } = new ObservableCollection<WeaponData>();

        public WeaponData SelectedWeapon
        {
            get => _selectedWeapon;
            set
            {
                if (_selectedWeapon != value)
                {
                    _selectedWeapon = value;
                    OnPropertyChanged(nameof(SelectedWeapon));
                }
            }
        }

        public string ProcessStatus
        {
            get => _processStatus;
            set
            {
                if (_processStatus != value)
                {
                    _processStatus = value;
                    OnPropertyChanged(nameof(ProcessStatus));
                }
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
