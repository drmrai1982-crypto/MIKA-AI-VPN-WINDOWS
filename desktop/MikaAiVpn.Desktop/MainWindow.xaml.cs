using System.Windows.Threading;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Threading;
using System.Diagnostics;

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
    private readonly SemaphoreSlim _nativeGate = new(1, 1);
    private readonly DispatcherTimer _telemetryTimer = new(DispatcherPriority.Background);
    private bool _sessionInitialized;
    private bool _sessionStarted;
    private bool _stopRequested;
    private bool _polling;
    private bool _closing;
    private bool _allowClose;
    private readonly string _ffiDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "hev-engine");
    private readonly string _dataDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MIKA AI VPN");

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateAnimationState();
        Activated += (_, _) => UpdateAnimationState();
        Deactivated += (_, _) => UpdateAnimationState();
        Loaded += (_, _) => UpdateAnimationState();
        Closed += (_, _) => StopAnimations();
        Closing += MainWindow_Closing;
        _telemetryTimer.Interval = TimeSpan.FromSeconds(1);
        _telemetryTimer.Tick += async (_, _) => await PollTelemetryAsync();

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
        Loaded += (_, _) =>
        {
            if (Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--connect", StringComparison.OrdinalIgnoreCase)))
                ConnectButton_Click(this, new RoutedEventArgs());
        };
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
        finally { DiagnosticsButton.IsEnabled = !_sessionInitialized; }
    }


    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionStarted || _closing) return;
        ConnectButton.IsEnabled = false;
        DiagnosticsButton.IsEnabled = false;
        UpdateRadarState(false, connecting: true);
        ConnectionState.Text = _isPersian ? "در حال آماده‌سازی اتصال..." : "PREPARING VPN SESSION...";
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            if (!File.Exists(System.IO.Path.Combine(_ffiDirectory, "fcae_ffi.dll")))
                throw new FileNotFoundException("fcae_ffi.dll was not found in the runtime folder.");
            await _nativeGate.WaitAsync();
            try
            {
                if (!_sessionInitialized)
                {
                    var init = await Task.Run(() => NativeBridge.Initialize(_ffiDirectory));
                    DiagnosticsText.Text = init.Message;
                    if (init.Result != 0) throw new InvalidOperationException(init.Message);
                    _sessionInitialized = true;
                }
                var start = await Task.Run(() => NativeBridge.Start(_dataDirectory, 2, 0));
                DiagnosticsText.Text = start.Message;
                if (start.Result == 7)
                {
                    await Task.Run(() => NativeBridge.Shutdown());
                    _sessionInitialized = false;
                    var choice = MessageBox.Show(
                        _isPersian ? "برای ایجاد رابط TUN دسترسی مدیر لازم است. برنامه با درخواست UAC دوباره اجرا شود؟" :
                        "Administrator permission is required for the VPN TUN interface. Restart with a UAC prompt?",
                        "MIKA AI VPN", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (choice == MessageBoxResult.Yes)
                    {
                        var exe = Environment.ProcessPath;
                        if (!string.IsNullOrWhiteSpace(exe))
                        {
                            Process.Start(new ProcessStartInfo(exe, "--connect") { UseShellExecute = true, Verb = "runas" });
                            _allowClose = true;
                            Close();
                            return;
                        }
                    }
                    throw new InvalidOperationException(start.Message);
                }
                if (start.Result != 0) throw new InvalidOperationException(start.Message);
                _sessionStarted = true;
            }
            finally { _nativeGate.Release(); }
            DisconnectButton.IsEnabled = true;
            _telemetryTimer.Start();
            await PollTelemetryAsync();
        }
        catch (Exception ex)
        {
            DiagnosticsText.Text = ex.Message;
            ConnectionState.Text = _isPersian ? "اتصال ناموفق" : "CONNECTION FAILED";
            ConnectionHint.Text = ex.Message;
            UpdateRadarState(false);
            ConnectButton.IsEnabled = true;
            if (_sessionInitialized)
            {
                try { await _nativeGate.WaitAsync(); try { await Task.Run(() => NativeBridge.Shutdown()); } finally { _nativeGate.Release(); } }
                catch { }
                _sessionInitialized = false;
            }
        }
        finally { DiagnosticsButton.IsEnabled = !_sessionInitialized; }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_sessionInitialized) return;
        DisconnectButton.IsEnabled = false;
        _stopRequested = true;
        try
        {
            await _nativeGate.WaitAsync();
            try
            {
                var result = await Task.Run(() => NativeBridge.Stop());
                DiagnosticsText.Text = result.Message;
                if (result.Result != 0) throw new InvalidOperationException(result.Message);
            }
            finally { _nativeGate.Release(); }
            await PollTelemetryAsync();
        }
        catch (Exception ex) { DiagnosticsText.Text = ex.Message; }
    }

    private async Task PollTelemetryAsync()
    {
        if (!_sessionInitialized || _polling || _closing) return;
        _polling = true;
        try
        {
            await _nativeGate.WaitAsync();
            (int Result, NativeTelemetry Telemetry) result;
            try { result = await Task.Run(() => NativeBridge.Telemetry()); }
            finally { _nativeGate.Release(); }
            if (result.Result != 0) return;
            var t = result.Telemetry;
            DownloadValue.Text = FormatRate(t.DownloadBytesPerSecond);
            UploadValue.Text = FormatRate(t.UploadBytesPerSecond);
            BackendValue.Text = t.Backend == 0 ? "Aether" : t.Backend == 1 ? "Psiphon" : "—";
            TunValue.Text = "HEV";
            bool connected = t.State == 4;
            bool waitingForNativeStart = _sessionStarted && t.State == 0 && !_stopRequested;
            bool connecting = t.State == 1 || t.State == 2 || t.State == 3 || t.State == 6 || waitingForNativeStart;
            bool failed = t.State == 5;
            UpdateRadarState(connected, connecting);
            SessionValue.Text = connected ? (_isPersian ? "متصل" : "Connected") :
                connecting ? (_isPersian ? "در حال اتصال" : "Connecting") :
                failed ? (_isPersian ? "خطا" : "Error") : (_isPersian ? "قطع‌شده" : "Disconnected");
            ConnectionState.Text = connected ? (_isPersian ? "اتصال VPN برقرار است" : "VPN CONNECTED") :
                connecting ? (_isPersian ? "در حال برقراری تونل..." : "TUNNEL CONNECTING") :
                failed ? (_isPersian ? "خطای موتور VPN" : "VPN ENGINE ERROR") :
                (_isPersian ? "VPN قطع است" : "VPN DISCONNECTED");
            string detail = !string.IsNullOrWhiteSpace(t.LastError) ? t.LastError : t.StatusMessage;
            ConnectionHint.Text = string.IsNullOrWhiteSpace(detail) ? " " : detail;
            if (t.State == 0 && _stopRequested)
            {
                _sessionStarted = false;
                _stopRequested = false;
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
            }
            else if (!_sessionStarted && t.State == 0)
            {
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
            }
            else
            {
                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = true;
            }
        }
        catch (Exception ex) { DiagnosticsText.Text = ex.Message; }
        finally { _polling = false; }
    }

    private static string FormatRate(ulong bytes)
    {
        double value = bytes;
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return string.Format("{0:0.##} {1}", value, units[i]);
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        if (!_sessionInitialized) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _telemetryTimer.Stop();
        StopAnimations();
        try
        {
            await _nativeGate.WaitAsync();
            try
            {
                if (_sessionStarted) await Task.Run(() => NativeBridge.Stop());
                await Task.Run(() => NativeBridge.Shutdown());
            }
            finally { _nativeGate.Release(); }
        }
        catch { }
        _sessionInitialized = false;
        _sessionStarted = false;
        _stopRequested = false;
        _allowClose = true;
        Close();
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
        ConnectButton.Content = fa ? "اتصال" : "Connect";
        DisconnectButton.Content = fa ? "قطع اتصال" : "Disconnect";
        if (DiagnosticsText.Text.StartsWith("Inspection has not", StringComparison.Ordinal) || DiagnosticsText.Text.StartsWith("بررسی هنوز", StringComparison.Ordinal))
            DiagnosticsText.Text = fa ? "بررسی هنوز اجرا نشده است. این آزمایش تونل را روشن نمی‌کند." : "Inspection has not been run. This test does not start a tunnel.";
    }
}