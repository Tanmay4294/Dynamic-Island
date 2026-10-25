using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;

namespace Spotifly.App.Controls;

/// <summary>
/// Reusable vector-rendered semicircular progress control designed to contour cleanly
/// along the lower curved dome/border of the Spotifly dynamic island.
/// Renders a background track and a foreground progress arc based on normalized Progress (0.0 to 1.0).
/// </summary>
public class SemicircleProgressControl : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(SemicircleProgressControl),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnProgressChanged,
                CoerceProgress));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(SemicircleProgressControl),
            new FrameworkPropertyMetadata(2.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InsetProperty =
        DependencyProperty.Register(
            nameof(Inset),
            typeof(double),
            typeof(SemicircleProgressControl),
            new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(
            nameof(TrackBrush),
            typeof(Brush),
            typeof(SemicircleProgressControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty =
        DependencyProperty.Register(
            nameof(ProgressBrush),
            typeof(Brush),
            typeof(SemicircleProgressControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowBrushProperty =
        DependencyProperty.Register(
            nameof(GlowBrush),
            typeof(Brush),
            typeof(SemicircleProgressControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(
            nameof(IsCompact),
            typeof(bool),
            typeof(SemicircleProgressControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double Inset
    {
        get => (double)GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? ProgressBrush
    {
        get => (Brush?)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public Brush? GlowBrush
    {
        get => (Brush?)GetValue(GlowBrushProperty);
        set => SetValue(GlowBrushProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private static object CoerceProgress(DependencyObject d, object baseValue)
    {
        if (baseValue is double val)
        {
            if (double.IsNaN(val) || val < 0.0) return 0.0;
            if (val > 1.0) return 1.0;
            return val;
        }
        return 0.0;
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Re-render handled by FrameworkPropertyMetadataOptions.AffectsRender
    }

    /// <summary>
    /// Calculates the curve geometry for a given width, height, and completion ratio (0.0 to 1.0).
    /// Returns the PathGeometry corresponding to the arc running along the lower dome contour from left to right.
    /// </summary>
    public static PathGeometry CalculateArcGeometry(double width, double height, double progress, double inset, bool isCompact)
    {
        var geom = new PathGeometry();
        if (width <= (inset * 2) || height <= (inset * 2) || progress <= 0.0001)
        {
            return geom;
        }

        // Available dimensions inside inset
        double w = width - (2 * inset);
        double h = height - (2 * inset);
        double drop = isCompact ? Math.Max(0, h * 0.15) : Math.Max(0, h * 0.10);

        double rx = w / 2.0;
        double ry = Math.Max(1.0, h - drop);
        double cx = inset + rx;
        double cy = inset + drop;

        // The island bottom arc sweeps clockwise from right side (w, drop) to left side (0, drop) in island coordinates:
        // Angle at right: theta = 0 rad (or 0 deg). Point = (cx + rx, cy) = (width - inset, drop + inset).
        // Angle at bottom: theta = pi/2 rad (90 deg). Point = (cx, cy + ry).
        // Angle at left: theta = pi rad (180 deg). Point = (cx - rx, cy) = (inset, drop + inset).
        //
        // Standard playback progression feels most natural left-to-right (theta from pi down to 0, or sweeping clockwise from left to right).
        // Let's trace from Left (theta = pi) to Right (theta = pi - progress * pi):
        // At progress = 0: point is at Left (cx - rx, cy).
        // At progress = 0.5: point is at Bottom (cx, cy + ry).
        // At progress = 1.0: point is at Right (cx + rx, cy).

        double clampedProgress = Math.Clamp(progress, 0.0, 1.0);
        double startAngle = Math.PI; // 180 degrees (left)
        double currentAngle = startAngle - (clampedProgress * Math.PI); // sweeps to 0 rad (right)

        Point startPoint = new Point(cx + rx * Math.Cos(startAngle), cy + ry * Math.Sin(startAngle));
        Point endPoint = new Point(cx + rx * Math.Cos(currentAngle), cy + ry * Math.Sin(currentAngle));

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };

        // If progress is greater than 0, construct the arc segment
        if (clampedProgress > 0.0001)
        {
            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(rx, ry),
                RotationAngle = 0,
                IsLargeArc = false, // angle delta is at most PI (180 deg), so never large arc
                SweepDirection = SweepDirection.Counterclockwise // In WPF screen coords (Y downwards), decreasing theta from PI to 0 moves Y down then up, sweeping counterclockwise in screen space.
            });
        }

        geom.Figures.Add(figure);
        return geom;
    }

    /// <summary>
    /// Calculates the full track geometry (progress = 1.0) along the bottom curve.
    /// </summary>
    public static PathGeometry CalculateTrackGeometry(double width, double height, double inset, bool isCompact)
    {
        return CalculateArcGeometry(width, height, 1.0, inset, isCompact);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0) return;

        double inset = Inset;
        double thickness = StrokeThickness;

        // 1. Render Background Track if brush is present
        if (TrackBrush != null)
        {
            var trackGeom = CalculateTrackGeometry(width, height, inset, IsCompact);
            var trackPen = new Pen(TrackBrush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            trackPen.Freeze();
            dc.DrawGeometry(null, trackPen, trackGeom);
        }

        // 2. Render Progress Arc
        double progress = Progress;
        if (progress > 0.0005)
        {
            var progressGeom = CalculateArcGeometry(width, height, progress, inset, IsCompact);

            // Optional Glow Stroke (e.g. for Modern theme neon accent or Glassmorphism ambient sheen)
            if (GlowBrush != null && GlowBrush != Brushes.Transparent)
            {
                var glowPen = new Pen(GlowBrush, thickness * 2.2)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                glowPen.Freeze();
                dc.DrawGeometry(null, glowPen, progressGeom);
            }

            // Main Foreground Arc
            if (ProgressBrush != null)
            {
                var progressPen = new Pen(ProgressBrush, thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                progressPen.Freeze();
                dc.DrawGeometry(null, progressPen, progressGeom);
            }
        }
    }
}
