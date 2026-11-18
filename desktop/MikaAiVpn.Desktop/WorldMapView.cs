using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MikaAiVpn.Desktop;

/// <summary>
/// Offline Natural Earth world map with a lightweight dimensional treatment
/// and animated network overlay. Decorative only; it never controls VPN state.
/// </summary>
public sealed class WorldMapView : FrameworkElement
{
    private static readonly (double X, double Y)[] Nodes =
    {
        (.09,.29),(.14,.23),(.20,.30),(.25,.39),(.29,.28),(.31,.49),(.37,.35),(.43,.27),(.47,.36),(.51,.30),
        (.55,.42),(.59,.28),(.63,.35),(.68,.27),(.72,.38),(.77,.29),(.82,.34),(.87,.27),(.91,.36),(.23,.60),
        (.30,.69),(.50,.59),(.55,.72),(.81,.63),(.86,.70),(.67,.55),(.40,.55),(.12,.48),(.94,.49),(.74,.52)
    };
    private static readonly int[] LinkOffsets = { 2, 5, 9, 14 };
    private readonly List<StreamGeometry> _countries = new();
    private readonly DispatcherTimer _batchTimer;
    private int _batchStart;
    private Color _signal = Color.FromRgb(255, 23, 68);
    private bool _running;

    public WorldMapView()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        LoadCountries();
        _batchTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _batchTimer.Tick += (_, _) => { _batchStart = (_batchStart + 7) % Nodes.Length; InvalidateVisual(); };
        Loaded += (_, _) => ResumeAnimation();
        Unloaded += (_, _) => PauseAnimation();
    }

    private void LoadCountries()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "assets", "ne_110m_admin_0_countries.geojson");
        if (!File.Exists(file)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind == JsonValueKind.Null) continue;
                string type = geometry.GetProperty("type").GetString() ?? "";
                var coordinates = geometry.GetProperty("coordinates");
                if (type == "Polygon") AddPolygon(coordinates);
                else if (type == "MultiPolygon")
                    foreach (var polygon in coordinates.EnumerateArray()) AddPolygon(polygon);
            }
        }
        catch (Exception)
        {
            _countries.Clear();
        }
    }

    private void AddPolygon(JsonElement rings)
    {
        var stream = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = stream.Open())
        {
            foreach (var ring in rings.EnumerateArray())
            {
                bool begun = false;
                double previousLon = 0;
                foreach (var coordinate in ring.EnumerateArray())
                {
                    var values = coordinate.EnumerateArray();
                    if (!values.MoveNext()) continue;
                    double lon = values.Current.GetDouble();
                    if (!values.MoveNext()) continue;
                    double lat = Math.Clamp(values.Current.GetDouble(), -85.0, 85.0);
                    double px = (lon + 180.0) / 360.0;
                    double radians = lat * Math.PI / 180.0;
                    double py = 0.5 - Math.Log(Math.Tan(Math.PI / 4.0 + radians / 2.0)) / (2.0 * Math.PI);
                    var point = new Point(px, Math.Clamp(py, 0, 1));
                    if (!begun || Math.Abs(lon - previousLon) > 180.0)
                    {
                        context.BeginFigure(point, true, true);
                        begun = true;
                    }
                    else context.LineTo(point, true, false);
                    previousLon = lon;
                }
            }
        }
        stream.Freeze();
        _countries.Add(stream);
    }

    public void SetConnectionColor(Color color) { _signal = color; InvalidateVisual(); }
    public void ResumeAnimation() { if (!_running) { _running = true; _batchTimer.Start(); } }
    public void PauseAnimation() { _running = false; _batchTimer.Stop(); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Stable 2:1 equirectangular map, centered without distorting geography.
        double mapWidth = Math.Min(w * .98, h * .90 * 2.0);
        double mapHeight = mapWidth / 2.0;
        double left = (w - mapWidth) / 2.0;
        double top = (h - mapHeight) / 2.0;
        dc.PushTransform(new MatrixTransform(new Matrix(mapWidth, 0, 0, mapHeight, left, top)));

        // Agreed Android palette: deep navy ocean and restrained warm-gold graticule.
        var ocean = new SolidColorBrush(Color.FromRgb(0x17, 0x2B, 0x49));
        dc.DrawRoundedRectangle(ocean, null, new Rect(.012, .035, .976, .93), .025, .025);

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(22, 0xE6, 0xD7, 0x7A)), .00065);
        for (int i = 1; i <= 5; i++)
            dc.DrawLine(gridPen, new Point(.025, i / 6.0), new Point(.975, i / 6.0));
        for (int i = 1; i <= 11; i++)
            dc.DrawLine(gridPen, new Point(i / 12.0, .02), new Point(i / 12.0, .98));

        // Flat 2D warm cream-gold land, matching the agreed Android map palette.
        // Keep the fill calm and readable; network colors remain independent.
        var landFill = new SolidColorBrush(Color.FromRgb(0xE6, 0xD7, 0x7A));
        var coast = new Pen(new SolidColorBrush(Color.FromRgb(0xF3, 0xE8, 0xA8)), .00125);
        var coastHighlight = new Pen(new SolidColorBrush(Color.FromArgb(95, 0xFF, 0xF2, 0xC2)), .00055);
        foreach (var country in _countries)
        {
            dc.DrawGeometry(landFill, coast, country);
            dc.DrawGeometry(null, coastHighlight, country);
        }

        // Four curved routes per node. Seven nodes brighten each second.
        for (int i = 0; i < Nodes.Length; i++)
        {
            foreach (int offset in LinkOffsets)
            {
                var a = Nodes[i];
                var b = Nodes[(i + offset) % Nodes.Length];
                double cx = (a.X + b.X) / 2.0;
                double lift = .10 + ArrayIndex(offset) * .065;
                double cy = Math.Min(a.Y, b.Y) - lift;
                var geometry = new StreamGeometry();
                using (var c = geometry.Open())
                {
                    c.BeginFigure(new Point(a.X, a.Y), false, false);
                    c.QuadraticBezierTo(new Point(cx, cy), new Point(b.X, b.Y), true, false);
                }
                geometry.Freeze();
                bool active = (i - _batchStart + Nodes.Length) % Nodes.Length < 7;
                var routePen = new Pen(new SolidColorBrush(active
                    ? Color.FromArgb(175, _signal.R, _signal.G, _signal.B)
                    : Color.FromArgb(17, 115, 205, 190)), active ? .00135 : .00065);
                dc.DrawGeometry(null, routePen, geometry);
            }
        }

        for (int i = 0; i < Nodes.Length; i++)
        {
            var p = Nodes[i];
            bool active = (i - _batchStart + Nodes.Length) % Nodes.Length < 7;
            var color = active ? _signal : Color.FromRgb(91, 196, 176);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(active ? (byte)65 : (byte)35, color.R, color.G, color.B)),
                null, new Point(p.X, p.Y), active ? .007 : .004, active ? .014 : .008);
            dc.DrawEllipse(new SolidColorBrush(color), null, new Point(p.X, p.Y),
                active ? .0028 : .0017, active ? .0056 : .0034);
        }

        dc.Pop();
    }

    private static int ArrayIndex(int offset) => offset switch { 2 => 0, 5 => 1, 9 => 2, _ => 3 };
}