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
        // Fine polar scale: major bearings every 30 degrees, minor ticks between them.
        const double center = 110;
        for (int i = 0; i < 72; i++)
        {
            double angle = i * Math.PI / 36.0;
            double outer = 101;
            double inner = i % 6 == 0 ? 91 : 96;
            RadarCanvas.Children.Add(new Line
            {
                X1 = center + Math.Sin(angle) * inner,
                Y1 = center - Math.Cos(angle) * inner,
                X2 = center + Math.Sin(angle) * outer,
                Y2 = center - Math.Cos(angle) * outer,
                Stroke = new SolidColorBrush(Color.FromArgb(i % 6 == 0 ? (byte)125 : (byte)42, 142, 190, 211)),
                StrokeThickness = i % 6 == 0 ? 1.0 : 0.55
            });
        }

        var spin = new RotateTransform();
        RadarSweepCanvas.RenderTransform = spin;
        spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(7.5))
            { RepeatBehavior = RepeatBehavior.Forever });

        RefreshRadarNetwork();
    }

    private void RefreshRadarNetwork()
    {
        const int pointCount = 7;
        const double center = 110;
        // Irregularly distributed telemetry stations: deliberately not arranged
        // on a circle, so the display cannot form a decorative polygon.
        Point[] points =
        {
            new(91, 34), new(158, 51), new(181, 116),
            new(143, 174), new(61, 157), new(35, 91), new(103, 104)
        };

        RadarNodesCanvas.Children.Clear();
        Color signal = _radarConnected
            ? Color.FromRgb(50, 235, 153)
            : Color.FromRgb(255, 72, 105);

        // Each station selects its four nearest peers. Draw each physical link
        // once; close links are brighter and longer links remain subtle.
        var links = new HashSet<(int A, int B)>();
        for (int i = 0; i < pointCount; i++)
        {
            var nearest = Enumerable.Range(0, pointCount)
                .Where(j => j != i)
                .OrderBy(j => DistanceSquared(points[i], points[j]))
                .Take(4);
            foreach (int j in nearest)
                links.Add((Math.Min(i, j), Math.Max(i, j)));
        }

        foreach (var link in links)
        {
            Point a = points[link.A], b = points[link.B];
            double distance = Math.Sqrt(DistanceSquared(a, b));
            bool local = distance < 78;
            byte alpha = local ? (byte)92 : (byte)28;
            RadarNodesCanvas.Children.Add(new Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(alpha, signal.R, signal.G, signal.B)),
                StrokeThickness = local ? 1.05 : 0.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }

        // Small, restrained station markers with a soft halo; no oversized
        // glowing vertices or heavy polygon outline.
        foreach (Point point in points)
        {
            var halo = new Ellipse
            {
                Width = 15, Height = 15,
                Fill = new RadialGradientBrush(new GradientStopCollection {
                    new GradientStop(Color.FromArgb(72, signal.R, signal.G, signal.B), 0.0),
                    new GradientStop(Color.FromArgb(22, signal.R, signal.G, signal.B), 0.58),
                    new GradientStop(Color.FromArgb(0, signal.R, signal.G, signal.B), 1.0)
                })
            };
            Canvas.SetLeft(halo, point.X - 7.5);
            Canvas.SetTop(halo, point.Y - 7.5);
            RadarNodesCanvas.Children.Add(halo);

            var node = new Ellipse
            {
                Width = 5, Height = 5,
                Fill = new SolidColorBrush(Color.FromRgb(230, 246, 252)),
                Stroke = new SolidColorBrush(Color.FromArgb(210, signal.R, signal.G, signal.B)),
                StrokeThickness = 1,
                Effect = new DropShadowEffect
                {
                    Color = signal, BlurRadius = 7, ShadowDepth = 0, Opacity = 0.55
                }
            };
            Canvas.SetLeft(node, point.X - 2.5);
            Canvas.SetTop(node, point.Y - 2.5);
            RadarNodesCanvas.Children.Add(node);
        }
    }

    private static double DistanceSquared(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
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