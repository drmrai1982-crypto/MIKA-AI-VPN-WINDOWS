using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MikaAiVpn.Desktop;

public partial class MainWindow : Window
{
    private bool _isPersian;
    private bool _radarConnected;
    private SolidColorBrush? _networkLineBrush;
    private SolidColorBrush? _networkGlowBrush;
    private SolidColorBrush? _networkNodeCoreBrush;
    private SolidColorBrush? _networkNodeGlowBrush;
    private SolidColorBrush? _radarTargetBrush;
    private SolidColorBrush? _radarTargetGlowBrush;
    private Model3DGroup? _globeNetworkLines;
    private Point3D[] _globeStations = Array.Empty<Point3D>();
    private DispatcherTimer? _networkShuffleTimer;

    public MainWindow()
    {
        InitializeComponent();
        BuildGlobe();
        StartGlobeNetworkShuffle();
        StartRadar();
        Closed += (_, _) => _networkShuffleTimer?.Stop();
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
        var spin = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        var globeTransform = new RotateTransform3D(spin);
        var sphere = new GeometryModel3D(mesh, material) { BackMaterial = material, Transform = globeTransform };

        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(190, 195, 205)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(255, 250, 238), new Vector3D(-0.4, -0.2, -1)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(100, 168, 235), new Vector3D(0.7, 0.1, 0.6)));
        scene.Children.Add(sphere);
        scene.Children.Add(BuildGlobeNetwork(globeTransform));
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

        // Seven independent radar returns, matching the Android radar's fixed targets.
        // These are decorative radar echoes, not the seven globe network stations.
        float[][] radarTargets =
        {
            new[] { -0.48f, -0.31f }, new[] { 0.19f, -0.57f },
            new[] { 0.57f, -0.10f }, new[] { 0.35f, 0.43f },
            new[] { -0.28f, 0.49f }, new[] { -0.63f, 0.12f },
            new[] { 0.04f, 0.28f }
        };
        _radarTargetBrush = new SolidColorBrush(Color.FromRgb(245, 76, 101));
        _radarTargetGlowBrush = new SolidColorBrush(Color.FromArgb(90, 245, 76, 101));
        foreach (float[] target in radarTargets)
        {
            double x = center + target[0] * 91;
            double y = center + target[1] * 91;
            var glow = new Ellipse
            {
                Width = 13, Height = 13,
                Fill = _radarTargetGlowBrush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(glow, x - 6.5);
            Canvas.SetTop(glow, y - 6.5);
            RadarTargetsCanvas.Children.Add(glow);

            var dot = new Ellipse
            {
                Width = 4.2, Height = 4.2,
                Fill = _radarTargetBrush,
                Stroke = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                StrokeThickness = 0.55,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, x - 2.1);
            Canvas.SetTop(dot, y - 2.1);
            RadarTargetsCanvas.Children.Add(dot);
        }

        var spin = new RotateTransform();
        RadarSweepCanvas.RenderTransform = spin;
        spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(7.5))
            { RepeatBehavior = RepeatBehavior.Forever });

    }

    // A dense, evenly distributed set of fixed globe nodes. The nodes never
    // jump; only their links are rewired once per second.
    private Model3DGroup BuildGlobeNetwork(Transform3D sharedRotation)
    {
        const int stationCount = 32;
        _globeStations = CreateDistributedStations(stationCount);

        Color signal = _radarConnected
            ? Color.FromRgb(48, 218, 145)
            : Color.FromRgb(245, 76, 101);
        _networkLineBrush = new SolidColorBrush(Color.FromArgb(205, signal.R, signal.G, signal.B));
        _networkGlowBrush = new SolidColorBrush(Color.FromArgb(72, signal.R, signal.G, signal.B));
        var lineMaterial = new MaterialGroup();
        lineMaterial.Children.Add(new DiffuseMaterial(_networkLineBrush));
        lineMaterial.Children.Add(new EmissiveMaterial(_networkGlowBrush));

        _globeNetworkLines = new Model3DGroup();
        var nodeMaterial = new MaterialGroup();
        _networkNodeCoreBrush = new SolidColorBrush(Color.FromArgb(245, signal.R, signal.G, signal.B));
        _networkNodeGlowBrush = new SolidColorBrush(Color.FromArgb(220, signal.R, signal.G, signal.B));
        nodeMaterial.Children.Add(new DiffuseMaterial(_networkNodeCoreBrush));
        nodeMaterial.Children.Add(new EmissiveMaterial(_networkNodeGlowBrush));

        var nodes = new Model3DGroup();
        foreach (Point3D station in _globeStations)
        {
            // Keep every node fixed to the globe surface; the globe depth buffer
            // naturally hides nodes on the far side of the rotating planet.
            var nodeCenter = new Point3D(station.X * 1.014, station.Y * 1.014, station.Z * 1.014);
            var marker = CreateSphere(nodeCenter, 0.016, 14, 10);
            nodes.Children.Add(new GeometryModel3D(marker, nodeMaterial) { BackMaterial = nodeMaterial });
        }

        var group = new Model3DGroup();
        group.Children.Add(_globeNetworkLines);
        group.Children.Add(nodes);
        group.Transform = sharedRotation;
        RebuildGlobeConnections();
        return group;
    }

    private static Point3D[] CreateDistributedStations(int count)
    {
        // Fibonacci-sphere placement avoids clusters and leaves well over seven
        // nodes on the viewer-facing hemisphere at every rotation angle.
        const double goldenAngle = Math.PI * (3.0 - 2.23606797749979);
        var points = new Point3D[count];
        for (int i = 0; i < count; i++)
        {
            double y = 1.0 - 2.0 * (i + 0.5) / count;
            double radius = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
            double longitude = goldenAngle * i;
            points[i] = new Point3D(
                Math.Cos(longitude) * radius,
                y,
                Math.Sin(longitude) * radius);
        }
        return points;
    }

    private void StartGlobeNetworkShuffle()
    {
        _networkShuffleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _networkShuffleTimer.Tick += (_, _) => RebuildGlobeConnections();
        _networkShuffleTimer.Start();
    }

    private void RebuildGlobeConnections()
    {
        if (_globeNetworkLines is null || _globeStations.Length < 6 ||
            _networkLineBrush is null || _networkGlowBrush is null) return;

        _globeNetworkLines.Children.Clear();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(_networkLineBrush));
        material.Children.Add(new EmissiveMaterial(_networkGlowBrush));

        // Shuffle a permutation, then connect offsets 1 and 2. This produces
        // 64 unique links for 32 nodes, with exactly four links per node.
        int[] order = Enumerable.Range(0, _globeStations.Length).ToArray();
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var edges = new HashSet<(int A, int B)>();
        for (int i = 0; i < order.Length; i++)
            foreach (int offset in new[] { 1, 2 })
            {
                int a = order[i], b = order[(i + offset) % order.Length];
                edges.Add((Math.Min(a, b), Math.Max(a, b)));
            }

        foreach (var edge in edges)
        {
            var arc = CreateSurfaceArc(_globeStations[edge.A], _globeStations[edge.B],
                1.014, 0.0024, 36, 7);
            _globeNetworkLines.Children.Add(
                new GeometryModel3D(arc, material) { BackMaterial = material });
        }
    }

    private static Point3D GeoPoint(double latitudeDegrees, double longitudeDegrees)
    {
        double latitude = latitudeDegrees * Math.PI / 180.0;
        double longitude = longitudeDegrees * Math.PI / 180.0;
        return new Point3D(Math.Cos(latitude) * Math.Sin(longitude),
            Math.Sin(latitude), Math.Cos(latitude) * Math.Cos(longitude));
    }

    private static MeshGeometry3D CreateSurfaceArc(Point3D start, Point3D end,
        double surfaceRadius, double tubeRadius, int segments, int sides)
    {
        Vector3D a = new(start.X, start.Y, start.Z);
        Vector3D b = new(end.X, end.Y, end.Z);
        a.Normalize(); b.Normalize();
        double dot = Math.Clamp(Vector3D.DotProduct(a, b), -0.9999, 0.9999);
        double angle = Math.Acos(dot);
        var mesh = new MeshGeometry3D();

        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            Vector3D radial = angle < 0.0001 ? a :
                a * (Math.Sin((1 - t) * angle) / Math.Sin(angle)) +
                b * (Math.Sin(t * angle) / Math.Sin(angle));
            radial.Normalize();
            Vector3D tangent = b - a * dot;
            if (tangent.LengthSquared < 1e-8) tangent = new Vector3D(0, 1, 0);
            tangent.Normalize();
            Vector3D side = Vector3D.CrossProduct(radial, tangent);
            if (side.LengthSquared < 1e-8) side = Vector3D.CrossProduct(radial, new Vector3D(1, 0, 0));
            side.Normalize();

            for (int j = 0; j < sides; j++)
            {
                double around = 2 * Math.PI * j / sides;
                Vector3D offset = side * (Math.Cos(around) * tubeRadius) +
                                  radial * (Math.Sin(around) * tubeRadius);
                // Sinusoidal lift is zero at both endpoints and strongest at mid-span,
                // giving each connection the raised orbital-arc profile used on Android.
                double arcRadius = surfaceRadius + 0.085 * Math.Sin(Math.PI * t);
                mesh.Positions.Add(new Point3D(radial.X * arcRadius + offset.X,
                    radial.Y * arcRadius + offset.Y, radial.Z * arcRadius + offset.Z));
                mesh.Normals.Add(offset);
            }
        }

        for (int i = 0; i < segments; i++)
            for (int j = 0; j < sides; j++)
            {
                int current = i * sides + j, nextSide = i * sides + (j + 1) % sides;
                int nextRing = (i + 1) * sides + j, nextRingSide = (i + 1) * sides + (j + 1) % sides;
                mesh.TriangleIndices.Add(current); mesh.TriangleIndices.Add(nextRing); mesh.TriangleIndices.Add(nextSide);
                mesh.TriangleIndices.Add(nextSide); mesh.TriangleIndices.Add(nextRing); mesh.TriangleIndices.Add(nextRingSide);
            }
        return mesh;
    }

    private static MeshGeometry3D CreateSphere(Point3D center, double radius, int longitudeSteps, int latitudeSteps)
    {
        var mesh = new MeshGeometry3D();
        for (int y = 0; y <= latitudeSteps; y++)
        {
            double latitude = Math.PI * (0.5 - (double)y / latitudeSteps);
            for (int x = 0; x <= longitudeSteps; x++)
            {
                double longitude = 2 * Math.PI * x / longitudeSteps;
                Vector3D normal = new(Math.Cos(latitude) * Math.Sin(longitude),
                    Math.Sin(latitude), Math.Cos(latitude) * Math.Cos(longitude));
                mesh.Positions.Add(new Point3D(center.X + normal.X * radius,
                    center.Y + normal.Y * radius, center.Z + normal.Z * radius));
                mesh.Normals.Add(normal);
            }
        }
        for (int y = 0; y < latitudeSteps; y++)
            for (int x = 0; x < longitudeSteps; x++)
            {
                int a = y * (longitudeSteps + 1) + x, b = a + longitudeSteps + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1);
            }
        return mesh;
    }

    // This is only a visual state hook; call it after a real tunnel state change.
    private void UpdateRadarState(bool connected, bool connecting = false)
    {
        // This visual hook must only be called from verified tunnel-state events.
        // Connecting is blue, disconnected is red, and connected is green.
        _radarConnected = connected;
        Color signal = connected
            ? Color.FromRgb(48, 218, 145)
            : connecting ? Color.FromRgb(0, 170, 255) : Color.FromRgb(245, 76, 101);
        if (_networkLineBrush is not null) _networkLineBrush.Color = Color.FromArgb(205, signal.R, signal.G, signal.B);
        if (_networkGlowBrush is not null) _networkGlowBrush.Color = Color.FromArgb(72, signal.R, signal.G, signal.B);
        if (_networkNodeCoreBrush is not null) _networkNodeCoreBrush.Color = Color.FromArgb(245, signal.R, signal.G, signal.B);
        if (_networkNodeGlowBrush is not null) _networkNodeGlowBrush.Color = Color.FromArgb(220, signal.R, signal.G, signal.B);
        if (_radarTargetBrush is not null) _radarTargetBrush.Color = signal;
        if (_radarTargetGlowBrush is not null) _radarTargetGlowBrush.Color = Color.FromArgb(90, signal.R, signal.G, signal.B);
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