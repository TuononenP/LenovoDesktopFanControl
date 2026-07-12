using System.Windows.Controls;
using System.Windows.Media.Animation;
using LenovoDesktopFanControl.Services;

namespace LenovoDesktopFanControl.Views.Controls;

public partial class FanCard : System.Windows.Controls.UserControl
{
    public FanCard()
    {
        InitializeComponent();
    }

    private void CardRoot_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!MotionPreferences.AnimationsEnabled)
        {
            CardRoot.Opacity = 1;
            return;
        }

        CardRoot.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }
}
