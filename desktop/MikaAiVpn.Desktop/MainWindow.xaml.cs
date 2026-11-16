using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace MikaAiVpn.Desktop;

public partial class MainWindow : Window
{
    private bool _isPersian;
    private bool _radarConnected;

    public MainWindow()
    {
        InitializeComponent();
        BuildGlobe();
        StartRadar();
        SetLanguage(false);
    }

    private void BuildGlobe()
    {
        const int lonSteps = 192, latSteps = 96;
        var mesh = new MeshGeometry3D();
        for (int y = 0; y <= latSteps; y++)
        {
            double lat = Math.PI * (0.5 - (double)y / latSteps);
            for (int x = 0; x <= lonSteps; x++)
            {
                double lon = 2 * Math.PI * x / lonSteps;
                mesh.Positions.Add(new Point3D(Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat), Math.Cos(lat) * Math.Cos(lon)));
                mesh.TextureCoordinates.Add(new Point((double)x / lonSteps, (double)y / latSteps));
            }
        }
        for (int y = 0; y < latSteps; y++)
            for (int x = 0; x < lonSteps; x++)
            {
                int a = y * (lonSteps + 1) + x, b = a + lonSteps + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1);
            }

        var image = LoadEarthTexture();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new ImageBrush(image) { Stretch = Stretch.Fill }));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(18, 38, 52, 68))));
        var sphere = new GeometryModel3D(mesh, material) { BackMaterial = material };
        var spin = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        sphere.Transform = new RotateTransform3D(spin);

        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(190, 195, 205)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(255, 250, 238), new Vector3D(-0.4, -0.2, -1)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(100, 168, 235), new Vector3D(0.7, 0.1, 0.6)));
        scene.Children.Add(sphere);
        GlobeViewport.Camera = new PerspectiveCamera(new Point3D(0, 0, 3.15), new Vector3D(0, 0, -3.15), new Vector3D(0, 1, 0), 38);
        GlobeViewport.Children.Add(new ModelVisual3D { Content = scene });
        spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(72)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private static ImageSource LoadEarthTexture()
    {
        // NASA Blue Marble daytime equirectangular satellite map is downloaded by CI
        // and bundled with the app, so the globe works offline after installation.
        string texturePath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "earth-day.jpg");
        if (!File.Exists(texturePath))
            throw new FileNotFoundException("The bundled NASA Earth night texture is missing.", texturePath);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(texturePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void StartRadar()
    {
        // Professional polar ticks: fixed bearings around the range ring.
        for (int i = 0; i < 72; i++)
        {
            double angle = i * Math.PI / 36.0;
            double outer = 102;
            double inner = i % 6 == 0 ? 92 : 97;
            var tick = new Line
            {
                X1 = 105 + Math.Sin(angle) * inner,
                Y1 = 105 - Math.Cos(angle) * inner,
                X2 = 105 + Math.Sin(angle) * outer,
                Y2 = 105 - Math.Cos(angle) * outer,
                Stroke = new SolidColorBrush(Color.FromArgb(i % 6 == 0 ? (byte)150 : (byte)65, 133, 191, 220)),
                StrokeThickness = i % 6 == 0 ? 1.3 : 0.7
            };
            RadarCanvas.Children.Add(tick);
        }

        // The canvas already has RenderTransformOrigin=0.5,0.5 in XAML.
        // Use a zero-origin RotateTransform here to avoid applying the pivot twice.
        var spin = new RotateTransform();
        RadarSweepCanvas.RenderTransform = spin;
        spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(5.6)) { RepeatBehavior = RepeatBehavior.Forever });

        // Build the network once. Its seven nodes never jump or regenerate on a timer.
        RefreshRadarNetwork();
    }

    private void RefreshRadarNetwork()
    {
        const int pointCount = 7;
        const double center = 125;
        var points = new Point[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            double angle = -Math.PI / 2 + (2 * Math.PI * i / pointCount);
            double radius = 82 + 4 * Math.Sin(i * 1.73);
            points[i] = new Point(
                center + Math.Cos(angle) * radius,
                center + Math.Sin(angle) * radius);
        }

        RadarNodesCanvas.Children.Clear();
        Color signal = _radarConnected
            ? Color.FromRgb(0, 255, 102)
            : Color.FromRgb(255, 23, 68);
        var coreBrush = new SolidColorBrush(signal);
        var glowBrush = new SolidColorBrush(Color.FromArgb(190, signal.R, signal.G, signal.B));

        // Seven nodes; each node connects to four distinct peers (14 undirected links).
        for (int i = 0; i < pointCount; i++)
        {
            foreach (int offset in new[] { 1, 2 })
            {
                Point a = points[i];
                Point b = points[(i + offset) % pointCount];

                RadarNodesCanvas.Children.Add(new Line
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    Stroke = glowBrush, StrokeThickness = 10, Opacity = 1,
                    Effect = new BlurEffect { Radius = 9 }
                });
                RadarNodesCanvas.Children.Add(new Line
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    Stroke = coreBrush, StrokeThickness = 1.55, Opacity = 0.92
                });
            }
        }

        foreach (Point point in points)
        {
            var halo = new Ellipse
            {
                Width = 26, Height = 26,
                Fill = new RadialGradientBrush(new GradientStopCollection {
                    new GradientStop(Color.FromArgb(230, signal.R, signal.G, signal.B), 0.0),
                    new GradientStop(Color.FromArgb(170, signal.R, signal.G, signal.B), 0.30),
                    new GradientStop(Color.FromArgb(55, signal.R, signal.G, signal.B), 0.70),
                    new GradientStop(Color.FromArgb(0, signal.R, signal.G, signal.B), 1.0)
                }),
                Effect = new BlurEffect { Radius = 6 }
            };
            Canvas.SetLeft(halo, point.X - 15);
            Canvas.SetTop(halo, point.Y - 15);
            RadarNodesCanvas.Children.Add(halo);

            var node = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(255, 246, 235)),
                Stroke = coreBrush,
                StrokeThickness = 2,
                Effect = new DropShadowEffect
                {
                    Color = signal, BlurRadius = 25, ShadowDepth = 0, Opacity = 1
                }
            };
            Canvas.SetLeft(node, point.X - 5.5);
            Canvas.SetTop(node, point.Y - 5.5);
            RadarNodesCanvas.Children.Add(node);
        }

        var centerNode = new Ellipse
        {
            Width = 10, Height = 10,
            Fill = new SolidColorBrush(Color.FromRgb(218, 255, 250)),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(80, 230, 255), BlurRadius = 18, ShadowDepth = 0, Opacity = 1
            }
        };
        Canvas.SetLeft(centerNode, center - 6);
        Canvas.SetTop(centerNode, center - 6);
        RadarNodesCanvas.Children.Add(centerNode);
    }

    // Call only after the real tunnel controller confirms an established VPN session.
    private void UpdateRadarState(bool connected)
    {
        _radarConnected = connected;
        RefreshRadarNetwork();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (WindowState == WindowState.Normal)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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
        WorldSubtitle.Text = fa ? "نمای سه‌بعدی زمین · چراغ‌های شب" : "3D planet view · Night lights";
        ConnectionState.Text = fa ? "هسته آماده است · تونل شروع نشده" : "CORE READY · TUNNEL NOT STARTED";
        ConnectionHint.Text = fa ? "کره و رادار به‌صورت محلی اجرا می‌شوند؛ ترافیک VPN فعال نیست." : "The globe and radar run locally. VPN traffic is not active.";
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