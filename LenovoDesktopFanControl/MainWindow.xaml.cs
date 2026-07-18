using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using LenovoDesktopFanControl.Services;
using LenovoDesktopFanControl.ViewModels;
using LenovoDesktopFanControl.Views;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace LenovoDesktopFanControl;

public partial class MainWindow : Window
{
    private readonly Drawing.Icon _applicationIcon;
    private readonly IWmiFanControlService _fanControlService;
    private readonly MainViewModel _viewModel;
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ContextMenuStrip? _trayMenu;
    private WinForms.ToolStripItem? _trayTitleItem;
    private WinForms.ToolStripItem? _trayShowItem;
    private WinForms.ToolStripItem? _trayExitItem;
    private Task _initializationTask = Task.CompletedTask;
    private WindowState _restoreWindowState = WindowState.Normal;
    private bool _isInitialized;
    private bool _isClosing;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();

        _applicationIcon = CreateApplicationIcon();
        ApplyApplicationIcon();

        _fanControlService = VisualTestFanControlService.TryCreate(out var visualTestService)
            ? visualTestService!
            : new WmiFanControlService();
        var settingsService = new SettingsService();
        var autoStartService = new AutoStartService();
        _viewModel = new MainViewModel(
            _fanControlService,
            settingsService,
            autoStartService,
            new LampArrayLightingService());
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loc.Instance.PropertyChanged += OnLocalizationChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

        Loaded += OnLoaded;
        Closing += OnClosing;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
        Activated += OnWindowActivated;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (!_isInitialized)
            return;
        _ = _viewModel.Lighting.ReapplyAsync();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        NativeWindowTheme.Apply(this);
    }

    private void ApplyApplicationIcon()
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                _applicationIcon.Handle,
                Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            Icon = source;
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to apply application icon: {ex.Message}");
        }
    }

    private static Drawing.Icon CreateApplicationIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/AppIcon.ico"));
            if (resource != null)
            {
                using var stream = resource.Stream;
                using var embeddedIcon = new Drawing.Icon(stream, 32, 32);
                return (Drawing.Icon)embeddedIcon.Clone();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to load embedded application icon: {ex.Message}");
        }

        return CreateFallbackApplicationIcon();
    }

    private static Drawing.Icon CreateFallbackApplicationIcon()
    {
        try
        {
            using var bitmap = new Drawing.Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = Drawing.Graphics.FromImage(bitmap);
            graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
            graphics.Clear(Drawing.Color.Transparent);

            using var backgroundBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 18, 25, 35));
            using var accentBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 91, 157, 255));
            using var accentDeepBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 56, 110, 220));
            using var hubBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 232, 240, 255));
            using var ringPen = new Drawing.Pen(accentBrush, 3)
            {
                StartCap = Drawing2D.LineCap.Round,
                EndCap = Drawing2D.LineCap.Round
            };
            using var hubRingPen = new Drawing.Pen(accentDeepBrush, 2.5f);

            graphics.FillEllipse(backgroundBrush, 2, 2, 60, 60);
            graphics.DrawEllipse(ringPen, 4, 4, 56, 56);

            var bladePts = new[]
            {
                new Drawing.PointF(33f, 27f),
                new Drawing.PointF(41f, 23f),
                new Drawing.PointF(46f, 15f),
                new Drawing.PointF(43f, 7f),
                new Drawing.PointF(35f, 5f),
                new Drawing.PointF(28f, 9f),
                new Drawing.PointF(27f, 18f),
                new Drawing.PointF(30f, 24f)
            };

            for (var i = 0; i < 3; i++)
            {
                var state = graphics.Save();
                graphics.TranslateTransform(32, 32, Drawing2D.MatrixOrder.Prepend);
                graphics.RotateTransform(120 * i, Drawing2D.MatrixOrder.Prepend);
                graphics.TranslateTransform(-32, -32, Drawing2D.MatrixOrder.Prepend);
                graphics.FillClosedCurve(accentBrush, bladePts, Drawing2D.FillMode.Alternate, 0.6f);
                graphics.Restore(state);
            }

            graphics.FillEllipse(hubBrush, 26, 26, 12, 12);
            graphics.DrawEllipse(hubRingPen, 26, 26, 12, 12);
            graphics.FillEllipse(accentDeepBrush, 29.5f, 29.5f, 5, 5);

            var nativeIcon = bitmap.GetHicon();
            try
            {
                using var temporaryIcon = Drawing.Icon.FromHandle(nativeIcon);
                return (Drawing.Icon)temporaryIcon.Clone();
            }
            finally
            {
                DestroyIcon(nativeIcon);
            }
        }
        catch
        {
            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
            return;

        _isInitialized = true;
        if (App.StartMinimized)
            HideToTray();
        _initializationTask = InitializeViewModelAsync();
    }

    private async Task InitializeViewModelAsync()
    {
        try
        {
            await _viewModel.InitializeAsync();
            if (VisualScaleVerifier.IsRequested)
            {
                await Dispatcher.InvokeAsync(() => VisualScaleVerifier.Render(this));
                _ = Dispatcher.BeginInvoke(RequestExit);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Application initialization failed", ex);
            if (!_isClosing)
            {
                System.Windows.MessageBox.Show(
                    this,
                    LocalizationService.Get("MsgInitializationError", ex.Message),
                    LocalizationService.Get("AppTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async void ShutdownConflicts_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = System.Windows.MessageBox.Show(
            this,
            LocalizationService.Get("ConfirmShutdownConflicts"),
            LocalizationService.Get("ConfirmShutdownConflictsTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await _viewModel.ShutdownConflictingSoftwareAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;

        if (_isClosing)
            return;

        if (ShouldHideOnClose(_exitRequested, _viewModel.MinimizeToTray))
        {
            HideToTray();
            return;
        }

        _exitRequested = true;
        _isClosing = true;
        IsEnabled = false;
        _ = ShutdownAndCloseAsync();
    }

    internal static bool ShouldHideOnClose(bool exitRequested, bool minimizeToTray) =>
        !exitRequested && minimizeToTray;

    private async Task ShutdownAndCloseAsync()
    {
        try
        {
            await _initializationTask;
            await _viewModel.ShutdownAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Application shutdown failed", ex);
        }
        finally
        {
            Loaded -= OnLoaded;
            SourceInitialized -= OnSourceInitialized;
            StateChanged -= OnStateChanged;
            Closing -= OnClosing;
            Activated -= OnWindowActivated;
            Loc.Instance.PropertyChanged -= OnLocalizationChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            DisposeTrayIcon();

            try
            {
                _applicationIcon.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to dispose application icon", ex);
            }

            try
            {
                _fanControlService.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to dispose fan-control service", ex);
            }

            System.Windows.Application.Current.Shutdown();

            // The application mixes WPF and WinForms notification-area components.
            // On affected systems the dispatcher shuts down but an elevated process
            // can remain alive and keep the executable locked. All app cleanup and
            // settings persistence have completed above, so explicitly finish the
            // process after WPF has raised its shutdown events.
            Environment.Exit(0);
        }
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == System.Windows.Data.Binding.IndexerName)
            RunOnDispatcher(UpdateTrayText);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainViewModel.StatusMessage) or nameof(MainViewModel.EffectiveStatusKind)))
            return;

        RunOnDispatcher(() => AnimateStatusChange());
    }

    private void AnimateStatusChange()
    {
        if (!MotionPreferences.AnimationsEnabled)
        {
            StatusText.Opacity = 1;
            StatusDot.Opacity = 1;
            return;
        }

        var animation = new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        StatusText.BeginAnimation(OpacityProperty, animation);
        StatusDot.BeginAnimation(OpacityProperty, animation);
    }

    private void LoadingOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || !MotionPreferences.AnimationsEnabled)
        {
            LoadingOverlay.Opacity = 1;
            return;
        }

        LoadingOverlay.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void LightNameEditor_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox editor || e.NewValue is not true)
            return;

        _ = editor.Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.SelectAll();
        });
    }

    private void LightNameEditor_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox editor ||
            editor.DataContext is not LightingZoneViewModel zone)
            return;

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            editor.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            zone.IsEditingName = false;
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            editor.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            zone.IsEditingName = false;
            e.Handled = true;
        }
    }

    private void LightNameEditor_LostKeyboardFocus(
        object sender,
        System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox editor ||
            editor.DataContext is not LightingZoneViewModel zone ||
            !zone.IsEditingName)
            return;

        editor.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        zone.IsEditingName = false;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_isClosing)
            return;

        if (WindowState == WindowState.Minimized)
        {
            if (_viewModel.MinimizeToTray)
            {
                ShowInTaskbar = false;
                EnsureTrayIcon();
            }
            else
            {
                ShowInTaskbar = true;
                DisposeTrayIcon();
            }
        }
        else
        {
            _restoreWindowState = WindowState;
            ShowInTaskbar = true;
            DisposeTrayIcon();
        }
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null)
            return;

        WinForms.NotifyIcon? trayIcon = null;
        WinForms.ContextMenuStrip? trayMenu = null;
        try
        {
            trayMenu = new WinForms.ContextMenuStrip
            {
                AutoSize = true,
                MinimumSize = new Drawing.Size(168, 0),
                Padding = new WinForms.Padding(6),
                ShowCheckMargin = false,
                ShowImageMargin = false
            };
            ApplyTrayMenuTheme(trayMenu);
            _trayTitleItem = trayMenu.Items.Add(LocalizationService.Get("AppTitle"));
            _trayTitleItem.Enabled = false;
            _ = trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayShowItem = trayMenu.Items.Add(
                LocalizationService.Get("TrayShow"),
                null,
                (_, _) => RunOnDispatcher(RestoreFromTray));
            _trayExitItem = trayMenu.Items.Add(
                LocalizationService.Get("TrayExit"),
                null,
                (_, _) => RunOnDispatcher(RequestExit));
            foreach (WinForms.ToolStripMenuItem item in trayMenu.Items.OfType<WinForms.ToolStripMenuItem>())
                item.Padding = new WinForms.Padding(8, 5, 8, 5);

            trayIcon = new WinForms.NotifyIcon
            {
                Text = LocalizationService.Get("AppTitle"),
                Icon = _applicationIcon,
                ContextMenuStrip = trayMenu
            };
            trayIcon.DoubleClick += (_, _) => RunOnDispatcher(RestoreFromTray);

            _trayMenu = trayMenu;
            _trayIcon = trayIcon;
            trayIcon.Visible = true;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to create notification-area icon", ex);
            trayIcon?.Dispose();
            trayMenu?.Dispose();
            _trayIcon = null;
            _trayMenu = null;
            _trayTitleItem = null;
            _trayShowItem = null;
            _trayExitItem = null;
            ShowInTaskbar = true;
        }
    }

    private void UpdateTrayText()
    {
        if (_trayIcon == null || _trayTitleItem == null || _trayShowItem == null || _trayExitItem == null)
            return;

        try
        {
            _trayIcon.Text = LocalizationService.Get("AppTitle");
            _trayTitleItem.Text = LocalizationService.Get("AppTitle");
            _trayShowItem.Text = LocalizationService.Get("TrayShow");
            _trayExitItem.Text = LocalizationService.Get("TrayExit");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to update notification-area text", ex);
        }
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            RunOnDispatcher(UpdateTrayTheme);
    }

    private void UpdateTrayTheme()
    {
        if (_trayMenu != null)
            ApplyTrayMenuTheme(_trayMenu);
    }

    private static void ApplyTrayMenuTheme(WinForms.ContextMenuStrip menu)
    {
        if (SystemParameters.HighContrast)
        {
            menu.Renderer = new WinForms.ToolStripSystemRenderer();
            menu.BackColor = Drawing.SystemColors.Menu;
            menu.ForeColor = Drawing.SystemColors.MenuText;
            menu.Font = Drawing.SystemFonts.MenuFont;
            return;
        }

        menu.Renderer = new TrayMenuRenderer();
        menu.BackColor = Drawing.Color.FromArgb(18, 25, 35);
        menu.ForeColor = Drawing.Color.FromArgb(169, 181, 197);
        menu.Font = Drawing.SystemFonts.MessageBoxFont;
    }

    private void RestoreFromTray()
    {
        if (_isClosing)
            return;

        Show();
        ShowInTaskbar = true;
        WindowState = _restoreWindowState;
        Activate();
    }

    private void HideToTray()
    {
        if (_isClosing)
            return;

        if (WindowState != WindowState.Minimized)
            _restoreWindowState = WindowState;
        ShowInTaskbar = false;
        EnsureTrayIcon();
        Hide();
    }

    private void RequestExit()
    {
        _exitRequested = true;
        Close();
    }

    private void RunOnDispatcher(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        if (Dispatcher.CheckAccess())
            action();
        else
            _ = Dispatcher.BeginInvoke(action);
    }

    private void DisposeTrayIcon()
    {
        var trayIcon = _trayIcon;
        var trayMenu = _trayMenu;
        _trayIcon = null;
        _trayMenu = null;
        _trayTitleItem = null;
        _trayShowItem = null;
        _trayExitItem = null;

        if (trayIcon != null)
        {
            try
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to dispose notification-area icon", ex);
            }
        }

        try
        {
            trayMenu?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to dispose notification-area menu", ex);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
