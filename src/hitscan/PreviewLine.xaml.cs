using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntelOrca.Biohazard.HitScan
{
    /// <summary>
    /// Interaction logic for PreviewLine.xaml
    /// </summary>
    public partial class PreviewLine : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _text;
        private int _value;
        private bool _solidLine;

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    RaisePropertyChanged(nameof(DisplayText));
                }
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    RaisePropertyChanged(nameof(DisplayText));
                }
            }
        }

        public bool SolidLine
        {
            get => _solidLine;
            set
            {
                if (_solidLine != value)
                {
                    _solidLine = value;
                    if (value)
                    {
                        line.Fill = Foreground;
                        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                        grid.ColumnDefinitions[1].Width = GridLength.Auto;
                        Grid.SetColumn(line, 0);
                        Grid.SetColumn(text, 1);
                    }
                    else
                    {
                        line.Fill = (Brush)FindResource("DottedBrush");
                        grid.ColumnDefinitions[0].Width = GridLength.Auto;
                        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                        Grid.SetColumn(text, 0);
                        Grid.SetColumn(line, 1);
                    }
                }
            }
        }

        public PreviewLine()
        {
            InitializeComponent();
        }

        public string DisplayText => $"{_text} [{_value}]";

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
