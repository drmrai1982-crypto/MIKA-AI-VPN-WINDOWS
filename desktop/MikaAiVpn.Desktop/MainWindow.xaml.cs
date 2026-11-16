using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace MikaAiVpn.Desktop;

public partial class MainWindow : Window
{
    private bool _isPersian;
    private readonly Random _radarRandom = new();
    private DispatcherTimer? _radarTimer;
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
        const int lonSteps = 96, latSteps = 48;
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

        var image = CreateNightTexture();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new ImageBrush(image) { Stretch = Stretch.Fill }));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(30, 32, 76, 112))));
        var sphere = new GeometryModel3D(mesh, material) { BackMaterial = material };
        var spin = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        sphere.Transform = new RotateTransform3D(spin);

        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(85, 91, 105)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(230, 237, 255), new Vector3D(-0.4, -0.2, -1)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(55, 130, 195), new Vector3D(0.7, 0.1, 0.6)));
        scene.Children.Add(sphere);
        GlobeViewport.Camera = new PerspectiveCamera(new Point3D(0, 0, 3.15), new Vector3D(0, 0, -3.15), new Vector3D(0, 1, 0), 38);
        GlobeViewport.Children.Add(new ModelVisual3D { Content = scene });
        spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(72)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private static ImageSource CreateNightTexture()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(3, 10, 22)), null, new Rect(0, 0, 1200, 600));
            var land = new SolidColorBrush(Color.FromRgb(18, 31, 43));
            var coast = new Pen(new SolidColorBrush(Color.FromArgb(150, 202, 183, 145)), 1.4);
            string[] shapes = {
                "M 75,125 L 125,85 205,82 265,125 275,170 235,210 210,255 170,270 135,230 95,210 Z",
                "M 190,275 L 250,280 280,330 260,390 230,475 205,530 175,475 165,405 155,350 Z",
                "M 515,105 L 565,75 630,82 675,120 655,165 610,170 585,195 545,160 Z",
                "M 550,185 L 615,170 670,205 690,260 665,325 630,400 595,460 565,405 545,335 525,265 Z",
                "M 660,100 L 725,70 805,82 875,125 935,185 915,225 870,225 835,255 790,220 745,185 700,160 Z",
                "M 825,300 L 875,280 920,315 925,365 890,395 855,360 Z",
                "M 965,355 L 1005,345 1035,375 1015,410 980,410 Z",
                "M 1080,220 L 1110,210 1125,240 1100,260 Z"
            };
            foreach (var s in shapes) dc.DrawGeometry(land, coast, Geometry.Parse(s));

            var border = new Pen(new SolidColorBrush(Color.FromArgb(48, 245, 220, 178)), 0.8);
            for (int i = 0; i < 15; i++) dc.DrawLine(border, new Point(100 + i * 65, 115), new Point(130 + i * 61, 475));

            var rnd = new Random(20261116);
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(250, 255, 235, 183), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(110, 255, 185, 85), 0.35));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 160, 60), 1));
            (double x,double y,double w,double h,int n)[] areas = {
                (90,100,190,145,560),(160,285,120,230,300),(525,90,155,105,390),
                (530,185,160,260,420),(670,85,270,165,900),(820,280,115,120,240),
                (950,345,100,75,110),(1050,215,75,70,100)
            };
            foreach (var a in areas)
                for (int i = 0; i < a.n; i++)
                {
                    double x = a.x + rnd.NextDouble() * a.w, y = a.y + rnd.NextDouble() * a.h;
                    double r = rnd.NextDouble() < 0.07 ? 2.5 : 0.7 + rnd.NextDouble() * 1.1;
                    dc.DrawEllipse(glow, null, new Point(x,y), r * 2.5, r * 2.5);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(225,255,230,174)), null, new Point(x,y), r * 0.4, r * 0.4);
                }
            var star = new SolidColorBrush(Color.FromArgb(85, 150, 190, 225));
            for (int i = 0; i < 300; i++) dc.DrawEllipse(star, null, new Point(rnd.NextDouble()*1200,rnd.NextDouble()*600),0.5,0.5);
        }
        return new DrawingImage(group);
    }

    private void StartRadar()
    {
        // Only the luminous sweep rotates; the range rings and axes stay fixed.
        var spin = new RotateTransform(0, 87, 87);
        RadarSweepCanvas.RenderTransform = spin;
        spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4.8)) { RepeatBehavior = RepeatBehavior.Forever });

        RefreshRadarNetwork();
        _radarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _radarTimer.Tick += (_, _) => RefreshRadarNetwork();
        _radarTimer.Start();
    }

    private void RefreshRadarNetwork()
    {
        const int pointCount = 7;
        const double center = 87;
        var points = new Point[pointCount];

        // Seven fresh signal nodes are laid out once per second inside the radar ring.
        for (int i = 0; i < pointCount; i++)
        {
            double angle = -Math.PI / 2 + (2 * Math.PI * i / pointCount)
                + (_radarRandom.NextDouble() - 0.5) * 0.16;
            double radius = 52 + _radarRandom.NextDouble() * 15;
            points[i] = new Point(
                center + Math.Cos(angle) * radius,
                center + Math.Sin(angle) * radius);
        }

        RadarNodesCanvas.Children.Clear();
        Color signal = _radarConnected
            ? Color.FromRgb(0, 255, 102)
            : Color.FromRgb(255, 23, 68);
        var coreBrush = new SolidColorBrush(signal);
        var glowBrush = new SolidColorBrush(Color.FromArgb(100, signal.R, signal.G, signal.B));

        // A 7-node ring with links to the next two nodes gives each node four links.
        // Each undirected connection is drawn once: 14 links, degree 4 for every node.
        for (int i = 0; i < pointCount; i++)
        {
            foreach (int offset in new[] { 1, 2 })
            {
                Point a = points[i];
                Point b = points[(i + offset) % pointCount];

                RadarNodesCanvas.Children.Add(new Line
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    Stroke = glowBrush, StrokeThickness = 5.5, Opacity = 0.85,
                    Effect = new BlurEffect { Radius = 7 }
                });
                RadarNodesCanvas.Children.Add(new Line
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    Stroke = coreBrush, StrokeThickness = 1.15, Opacity = 0.82
                });
            }
        }

        foreach (Point point in points)
        {
            RadarNodesCanvas.Children.Add(new Ellipse
            {
                Width = 13, Height = 13,
                Fill = glowBrush,
                Stroke = glowBrush,
                StrokeThickness = 1,
                Effect = new BlurEffect { Radius = 8 },
                RenderTransform = new TranslateTransform(point.X - center - 6.5, point.Y - center - 6.5)
            });
            RadarNodesCanvas.Children.Add(new Ellipse
            {
                Width = 5.5, Height = 5.5,
                Fill = coreBrush,
                Effect = new DropShadowEffect
                {
                    Color = signal, BlurRadius = 13, ShadowDepth = 0, Opacity = 1
                },
                RenderTransform = new TranslateTransform(point.X - center - 2.75, point.Y - center - 2.75)
            });
        }

        var centerNode = new Ellipse
        {
            Width = 8, Height = 8,
            Fill = new SolidColorBrush(Color.FromRgb(218, 255, 250)),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(80, 230, 255), BlurRadius = 18, ShadowDepth = 0, Opacity = 1
            }
        };
        Canvas.SetLeft(centerNode, center - 4);
        Canvas.SetTop(centerNode, center - 4);
        RadarNodesCanvas.Children.Add(centerNode);
    }

    // Call only after the real tunnel controller confirms an established VPN session.
    private void UpdateRadarState(bool connected)
    {
        _radarConnected = connected;
        RefreshRadarNetwork();
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e) { _isPersian = !_isPersian; SetLanguage(_isPersian); }

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsButton.IsEnabled = false;
        DiagnosticsText.Text = _isPersian ? "در حال بررسی هسته..." : "Inspecting native core...";
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "hev-engine");
            if (!File.Exists(Path.Combine(dir, "fcae_ffi.dll")))
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