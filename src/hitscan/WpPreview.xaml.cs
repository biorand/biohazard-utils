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

            grid.Children.Add(new PreviewLine()
            {
                Foreground = Brushes.Blue,
                Text = "LO",
                Value = wp.LowCap,
                Margin = GetMargin(wp.LowCap)
            });
            grid.Children.Add(new PreviewLine()
            {
                Foreground = Brushes.Red,
                Text = "HI",
                Value = wp.HighCap,
                Margin = GetMargin(wp.HighCap)
            });

            grid.Children.Add(new PreviewLine()
            {
                Foreground = Brushes.Green,
                Text = "LAST HIGH",
                Value = wp.RecentHigh,
                Margin = GetMargin(wp.RecentHigh),
                SolidLine = true
            });
            grid.Children.Add(new PreviewLine()
            {
                Foreground = Brushes.Green,
                Text = "LAST NEUTRAL",
                Value = wp.RecentNeutral,
                Margin = GetMargin(wp.RecentNeutral),
                SolidLine = true
            });
            grid.Children.Add(new PreviewLine()
            {
                Foreground = Brushes.Green,
                Text = "LAST LOW",
                Value = wp.RecentLow,
                Margin = GetMargin(wp.RecentLow),
                SolidLine = true
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
