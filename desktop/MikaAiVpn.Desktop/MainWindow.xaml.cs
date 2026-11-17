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
        var points = new Point[pointCount];

        // Stable, slightly irregular orbital positions avoid the rigid star-shaped look.
        double[] angleOffsets = { -90, -37, 18, 69, 126, 181, 231 };
        double[] radii = { 69, 76, 68, 77, 70, 76, 67 };
        for (int i = 0; i < pointCount; i++)
        {
            double angle = angleOffsets[i] * Math.PI / 180.0;
            points[i] = new Point(center + Math.Cos(angle) * radii[i],
                                  center + Math.Sin(angle) * radii[i]);
        }

        RadarNodesCanvas.Children.Clear();
        Color signal = _radarConnected
            ? Color.FromRgb(50, 235, 153)
            : Color.FromRgb(255, 72, 105);

        // Four links per node: two clear local links and two deliberately subdued
        // long-range links. Each undirected link is drawn once.
        for (int i = 0; i < pointCount; i++)
        {
            for (int offset = 1; offset <= 2; offset++)
            {
                Point a = points[i];
                Point b = points[(i + offset) % pointCount];
                bool primary = offset == 1;
                var lineColor = Color.FromArgb(primary ? (byte)118 : (byte)42,
                                                signal.R, signal.G, signal.B);
                RadarNodesCanvas.Children.Add(new Line
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    Stroke = new SolidColorBrush(lineColor),
                    StrokeThickness = primary ? 1.35 : 0.75,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
                if (primary)
                {
                    RadarNodesCanvas.Children.Add(new Line
                    {
                        X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                        Stroke = new SolidColorBrush(Color.FromArgb(24, signal.R, signal.G, signal.B)),
                        StrokeThickness = 5,
                        Effect = new BlurEffect { Radius = 5 }
                    });
                }
            }
        }

        foreach (Point point in points)
        {
            var halo = new Ellipse
            {
                Width = 20, Height = 20,
                Fill = new RadialGradientBrush(new GradientStopCollection {
                    new GradientStop(Color.FromArgb(95, signal.R, signal.G, signal.B), 0.0),
                    new GradientStop(Color.FromArgb(35, signal.R, signal.G, signal.B), 0.55),
                    new GradientStop(Color.FromArgb(0, signal.R, signal.G, signal.B), 1.0)
                })
            };
            Canvas.SetLeft(halo, point.X - 10);
            Canvas.SetTop(halo, point.Y - 10);
            RadarNodesCanvas.Children.Add(halo);

            var node = new Ellipse
            {
                Width = 6.5, Height = 6.5,
                Fill = new SolidColorBrush(Color.FromRgb(235, 249, 255)),
                Stroke = new SolidColorBrush(signal),
                StrokeThickness = 1.35,
                Effect = new DropShadowEffect
                {
                    Color = signal, BlurRadius = 11, ShadowDepth = 0, Opacity = 0.75
                }
            };
            Canvas.SetLeft(node, point.X - 3.25);
            Canvas.SetTop(node, point.Y - 3.25);
            RadarNodesCanvas.Children.Add(node);
        }
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