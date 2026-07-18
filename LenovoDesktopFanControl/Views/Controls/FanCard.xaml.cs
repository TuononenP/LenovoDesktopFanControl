using System.Windows.Media.Animation;
using LenovoDesktopFanControl.Services;
using LenovoDesktopFanControl.ViewModels;
using WpfTextBox = System.Windows.Controls.TextBox;

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

    private void FanNameEditor_IsVisibleChanged(
        object sender,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (sender is not WpfTextBox editor || e.NewValue is not true)
            return;

        _ = editor.Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.SelectAll();
        });
    }

    private void FanNameEditor_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not WpfTextBox editor || editor.DataContext is not FanViewModel fan)
            return;

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            editor.GetBindingExpression(WpfTextBox.TextProperty)?.UpdateSource();
            fan.IsEditingName = false;
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            editor.GetBindingExpression(WpfTextBox.TextProperty)?.UpdateTarget();
            fan.IsEditingName = false;
            e.Handled = true;
        }
    }

    private void FanNameEditor_LostKeyboardFocus(
        object sender,
        System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is not WpfTextBox editor ||
            editor.DataContext is not FanViewModel fan ||
            !fan.IsEditingName)
            return;

        editor.GetBindingExpression(WpfTextBox.TextProperty)?.UpdateSource();
        fan.IsEditingName = false;
    }
}
