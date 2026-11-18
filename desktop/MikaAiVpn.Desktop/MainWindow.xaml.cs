using System.Windows.Threading;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace MikaAiVpn.Desktop;

public partial class MainWindow : Window
{
    private bool _isPersian;
    private SolidColorBrush? _radarTargetBrush;
    private SolidColorBrush? _radarTargetGlowBrush;
    private RotateTransform? _radarRotation;
    private readonly DispatcherTimer _radarTimer = new(DispatcherPriority.Render);
    private double _radarAngle;
    private bool _animationsPaused = true;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateAnimationState();
        Activated += (_, _) => UpdateAnimationState();
        Deactivated += (_, _) => UpdateAnimationState();
        Loaded += (_, _) => UpdateAnimationState();
        Closed += (_, _) => StopAnimations();

        _radarTimer.Interval = TimeSpan.FromMilliseconds(33);
        _radarTimer.Tick += (_, _) =>
        {
            if (_radarRotation is null) return;
            _radarAngle = (_radarAngle + 2.0) % 360.0;
            _radarRotation.Angle = _radarAngle;
        };

        StartRadar();
        UpdateRadarState(connected: false);
        SetLanguage(false);
    }

    private void StartRadar()
    {
        const double center = 110;
        for (int i = 0; i < 36; i++)
        {
            double angle = i * Math.PI / 18.0;
            double outer = 101;
            double inner = i % 6 == 0 ? 91 : 96;
            RadarCanvas.Children.Add(new Line
            {
                X1 = center + Math.Sin(angle) * inner,
                Y1 = center - Math.Cos(angle) * inner,
                X2 = center + Math.Sin(angle) * outer,
                Y2 = center - Math.Cos(angle) * outer,
                Stroke = new SolidColorBrush(Color.FromArgb(i % 6 == 0 ? (byte)205 : (byte)105, 178, 211, 230)),
                StrokeThickness = i % 6 == 0 ? 1.35 : 0.85
            });
        }

        float[][] radarTargets =
        {
            new[] { -0.48f, -0.31f }, new[] { 0.19f, -0.57f },
            new[] { 0.57f, -0.10f }, new[] { 0.35f, 0.43f },
            new[] { -0.28f, 0.49f }, new[] { -0.63f, 0.12f },
            new[] { 0.04f, 0.28f }
        };
        _radarTargetBrush = new SolidColorBrush(Color.FromRgb(255, 23, 68));
        _radarTargetGlowBrush = new SolidColorBrush(Color.FromArgb(55, 255, 23, 68));
        foreach (float[] target in radarTargets)
        {
            double x = center + target[0] * 91;
            double y = center + target[1] * 91;
            var glow = new Ellipse { Width = 11, Height = 11, Fill = _radarTargetGlowBrush, IsHitTestVisible = false };
            Canvas.SetLeft(glow, x - 5.5); Canvas.SetTop(glow, y - 5.5);
            RadarTargetsCanvas.Children.Add(glow);
            var dot = new Ellipse
            {
                Width = 4.2, Height = 4.2, Fill = _radarTargetBrush,
                Stroke = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                StrokeThickness = 0.55, IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, x - 2.1); Canvas.SetTop(dot, y - 2.1);
            RadarTargetsCanvas.Children.Add(dot);
        }

        _radarRotation = new RotateTransform(0);
        RadarSweepCanvas.RenderTransformOrigin = new Point(0.5, 0.5);
        RadarSweepCanvas.RenderTransform = _radarRotation;
    }

    private void UpdateAnimationState()
    {
        bool shouldRun = WindowState != WindowState.Minimized && IsActive;
        if (shouldRun)
        {
            if (!_animationsPaused) return;
            _radarTimer.Start();
            WorldMap.ResumeAnimation();
            _animationsPaused = false;
        }
        else
        {
            if (_animationsPaused) return;
            _radarTimer.Stop();
            WorldMap.PauseAnimation();
            _animationsPaused = true;
        }
    }

    private void StopAnimations()
    {
        _radarTimer.Stop();
        WorldMap.PauseAnimation();
    }

    // Visual-only status hook. Call only after a verified tunnel-state event.
    private void UpdateRadarState(bool connected, bool connecting = false, bool degraded = false)
    {
        Color signal = connected
            ? Color.FromRgb(0, 255, 102)
            : degraded ? Color.FromRgb(255, 214, 0)
            : connecting ? Color.FromRgb(0, 170, 255)
            : Color.FromRgb(255, 23, 68);
        WorldMap.SetConnectionColor(signal);
        if (_radarTargetBrush is not null) _radarTargetBrush.Color = signal;
        if (_radarTargetGlowBrush is not null) _radarTargetGlowBrush.Color = Color.FromArgb(75, signal.R, signal.G, signal.B);

        // Keep the sweep core, its halo, and the radar targets on the same status hue.
        RadarSweepGlowLine.Stroke = new SolidColorBrush(Color.FromArgb(178, signal.R, signal.G, signal.B));
        RadarSweepCoreLine.Stroke = new SolidColorBrush(Color.FromArgb(245, signal.R, signal.G, signal.B));
        RadarSweepCenter.Fill = new SolidColorBrush(Color.FromArgb(245, signal.R, signal.G, signal.B));
        RadarSweepCenter.Stroke = new SolidColorBrush(Color.FromArgb(255, signal.R, signal.G, signal.B));
        RadarGlowStop0.Color = Color.FromArgb(105, signal.R, signal.G, signal.B);
        RadarGlowStop1.Color = Color.FromArgb(76, signal.R, signal.G, signal.B);
        RadarGlowStop2.Color = Color.FromArgb(48, signal.R, signal.G, signal.B);
        RadarGlowStop3.Color = Color.FromArgb(24, signal.R, signal.G, signal.B);
        RadarGlowStop4.Color = Color.FromArgb(0, signal.R, signal.G, signal.B);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (WindowState == WindowState.Normal)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void LanguageButton_Click(object sender, RoutedEventArgs e) { _isPersian = !_isPersian; SetLanguage(_isPersian); }

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsButton.IsEnabled = false;
        DiagnosticsText.Text = _isPersian ? "در حال بررسی هسته..." : "Inspecting native core...";
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "hev-engine");
            if (!File.Exists(System.IO.Path.Combine(dir, "fcae_ffi.dll")))
            {
                DiagnosticsText.Text = _isPersian ? "فایل‌های هسته پیدا نشدند." : "Native core files were not found.";
                return;
            }
            var result = await Task.Run(() => NativeBridge.Inspect(dir));
            DiagnosticsText.Text = result.Message;
            ConnectionState.Text = result.Result == 0
                ? (_isPersian ? "هسته بررسی شد؛ تونل شروع نشده است" : "CORE INSPECTED · TUNNEL NOT STARTED")
                : (_isPersian ? $"بررسی ناموفق ({result.Result})" : $"Inspection failed ({result.Result})");
        }
        catch (Exception ex) { DiagnosticsText.Text = (_isPersian ? "خطا: " : "Error: ") + ex.Message; }
        finally { DiagnosticsButton.IsEnabled = true; }
    }

    private void SetLanguage(bool fa)
    {
        FlowDirection = fa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        LanguageButton.Content = fa ? "English" : "فارسی";
        Title = fa ? "مایکا ای‌آی وی‌پی‌ان" : "MIKA AI VPN";
        WorldTitle.Text = fa ? "شبکهٔ جهانی" : "GLOBAL NETWORK";
        WorldSubtitle.Text = fa ? "نقشهٔ تخت جهان · شبکهٔ جهانی" : "Flat world map · Global network";
        ConnectionState.Text = fa ? "هسته آماده است · تونل شروع نشده" : "CORE READY · TUNNEL NOT STARTED";
        ConnectionHint.Text = fa ? "نقشه و رادار به‌صورت محلی اجرا می‌شوند؛ ترافیک VPN فعال نیست." : "The map and radar run locally. VPN traffic is not active.";
        SessionLabel.Text = fa ? "وضعیت نشست" : "SESSION";
        SessionValue.Text = fa ? "شروع نشده" : "Not started";
        BackendLabel.Text = fa ? "موتور فعال" : "ACTIVE BACKEND";
        BackendValue.Text = fa ? "در دسترس نیست" : "Not available";
        TunLabel.Text = fa ? "موتور تونل" : "TUN ENGINE";
        TunValue.Text = fa ? "در دسترس نیست" : "Not available";
        TrafficLabel.Text = fa ? "ترافیک شبکه" : "NETWORK TRAFFIC";
        DownloadLabel.Text = fa ? "↓ دانلود" : "↓ DOWNLOAD";
        UploadLabel.Text = fa ? "↑ آپلود" : "↑ UPLOAD";
        DiagnosticsLabel.Text = fa ? "ابزارهای تشخیصی" : "DIAGNOSTICS";
        DiagnosticsButton.Content = fa ? "بررسی هسته و موتورها" : "Inspect core and engines";
        if (DiagnosticsText.Text.StartsWith("Inspection has not", StringComparison.Ordinal) || DiagnosticsText.Text.StartsWith("بررسی هنوز", StringComparison.Ordinal))
            DiagnosticsText.Text = fa ? "بررسی هنوز اجرا نشده است. این آزمایش تونل را روشن نمی‌کند." : "Inspection has not been run. This test does not start a tunnel.";
    }
}