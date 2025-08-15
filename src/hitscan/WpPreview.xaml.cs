using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntelOrca.Biohazard.HitScan
{
    /// <summary>
    /// Interaction logic for WpPreview.xaml
    /// </summary>
    public partial class WpPreview : UserControl
    {
        public static readonly DependencyProperty WeaponDataProperty =
            DependencyProperty.Register(
                nameof(WeaponData),
                typeof(WeaponData),
                typeof(WpPreview),
                new PropertyMetadata(null, OnWeaponDataChanged));

        public WeaponData WeaponData
        {
            get => (WeaponData)GetValue(WeaponDataProperty);
            set => SetValue(WeaponDataProperty, value);
        }

        private static void OnWeaponDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WpPreview control)
            {
                if (e.OldValue is INotifyPropertyChanged oldWp)
                {
                    oldWp.PropertyChanged -= control.OnWpPropertyChanged;
                }
                if (e.NewValue is INotifyPropertyChanged newWp)
                {
                    newWp.PropertyChanged += control.OnWpPropertyChanged;
                }
                control.Refresh();
            }
        }

        public WpPreview()
        {
            InitializeComponent();
        }

        private void OnWpPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Refresh();
        }

        private void Refresh()
        {
            grid.Children.Clear();

            var wp = WeaponData;
            if (wp == null)
                return;

            AddLine(false, Brushes.Blue, "LO", wp.LowCap);
            AddLine(false, Brushes.Red, "HI", wp.HighCap);

            AddLine(true, Brushes.Green, "LAST HIGH", wp.RecentHigh);
            AddLine(true, Brushes.Green, "LAST NEUTRAL", wp.RecentNeutral);
            AddLine(true, Brushes.Green, "LAST LOW", wp.RecentLow);
        }

        private void AddLine(bool solid, Brush color, string text, int value)
        {
            if (value == 0)
                return;

            grid.Children.Add(new PreviewLine()
            {
                Foreground = color,
                Text = text,
                Value = value,
                Margin = GetMargin(value),
                SolidLine = solid
            });
        }

        private Thickness GetMargin(int value)
        {
            return new Thickness(0, GetPreviewY(value), 0, 0);
        }

        private int GetPreviewY(int value)
        {
            return -value / 8;
        }
    }
}
