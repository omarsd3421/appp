using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// سطح عرض الورقة في شاشة المعاينة الكبيرة:
///  - بيرسم الصورة متوسّطة وبأقصى حجم (Zoom-fit) من غير فلكرة (Double buffer).
///  - الانتقال بين الأوراق فوري (من غير تلاشي أو انزلاق) عشان المعاينة تبقى أسرع.
///  - أسهم كبيرة على الجانبين بتظهر لما الماوس يقرّب منها.
///  - مؤشر «جاري التحميل» بيظهر بس لو التحميل أخد وقت.
/// الصور مِلك الفورم اللي بيستخدم الكنترول؛ هو بس بيرسمها.
/// </summary>
internal sealed class PageView : Control
{
    private const int DurationMs = 170;
    private const int ArrowRadius = 26;
    private const int ArrowMargin = 44;
    private const int ArrowZone = 120;

    private Image? current;
    private Image? previous;
    private float t = 1f;
    private int dir;
    private long animStart;
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private readonly System.Windows.Forms.Timer animTimer =
        new System.Windows.Forms.Timer { Interval = 12 };

    private readonly System.Windows.Forms.Timer busyTimer =
        new System.Windows.Forms.Timer { Interval = 140 };

    private bool busy;
    private int hoverSide;
    private int wheelAccumulator;
    private bool canGoPrev;
    private bool canGoNext;

    /// <summary> -1 = السابق، +1 = التالي. </summary>
    public event Action<int>? ArrowClicked;
    public event Action<int>? WheelStep;
    public event Action? PageDoubleClick;

    public PageView()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        TabStop = false;
        animTimer.Tick += (s, e) => AnimTick();
        busyTimer.Tick += (s, e) =>
        {
            busyTimer.Stop();
            busy = true;
            Invalidate();
        };
    }

    public bool CanGoPrev
    {
        get => canGoPrev;
        set { canGoPrev = value; Invalidate(); }
    }

    public bool CanGoNext
    {
        get => canGoNext;
        set { canGoNext = value; Invalidate(); }
    }

    public bool UsesImage(Image? img) =>
        img != null && (ReferenceEquals(img, current) || ReferenceEquals(img, previous));

    public void SetImage(Image? img, int direction, bool animate)
    {
        if (ReferenceEquals(img, current))
            return;

        // المعاينة من غير أي أنيميشن (لتسريعها): الصورة الجديدة بتظهر فورًا.
        // البارامتر animate متسايب للتوافق بس ومبقاش له تأثير.
        if (false && animate && current != null && img != null && Width > 0 && Height > 0)
        {
            previous = current;
            current = img;
            dir = direction;
            t = 0f;
            animStart = clock.ElapsedMilliseconds;
            animTimer.Start();
        }
        else
        {
            previous = null;
            current = img;
            t = 1f;
            animTimer.Stop();
        }

        Invalidate();
    }

    public void BeginBusy()
    {
        if (busy)
            return;

        busyTimer.Stop();
        busyTimer.Start();
    }

    public void EndBusy()
    {
        busyTimer.Stop();
        if (busy)
        {
            busy = false;
            Invalidate();
        }
    }

    private void AnimTick()
    {
        t = Math.Min(1f, (clock.ElapsedMilliseconds - animStart) / (float)DurationMs);
        if (t >= 1f)
        {
            previous = null;
            animTimer.Stop();
        }

        Invalidate();
    }

    private Rectangle FitRect(Image img)
    {
        int cw = Math.Max(1, ClientSize.Width);
        int ch = Math.Max(1, ClientSize.Height);
        float s = Math.Min((float)cw / img.Width, (float)ch / img.Height);
        int w = Math.Max(1, (int)Math.Round(img.Width * s));
        int h = Math.Max(1, (int)Math.Round(img.Height * s));
        return new Rectangle((cw - w) / 2, (ch - h) / 2, w, h);
    }

    private void DrawFit(Graphics g, Image img, float alpha, float offsetX)
    {
        Rectangle r = FitRect(img);
        r.Offset((int)offsetX, 0);

        if (alpha >= 0.995f)
        {
            g.DrawImage(img, r);
            return;
        }

        using var attrs = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = Math.Clamp(alpha, 0f, 1f) };
        attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(img, r, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        try
        {
            if (t < 1f && previous != null && current != null)
            {
                float k = 1f - (float)Math.Pow(1f - t, 3);
                g.InterpolationMode = InterpolationMode.Bilinear;
                DrawFit(g, previous, 1f - k, -dir * 30f * k);
                DrawFit(g, current, k, dir * 30f * (1f - k));
            }
            else if (current != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                DrawFit(g, current, 1f, 0f);
            }
        }
        catch (Exception ex) when (
            ex is ArgumentException ||
            ex is InvalidOperationException ||
            ex is ExternalException)
        {
            // صورة اتشالت أثناء الرسم — الإطار اللي بعده هيرسم صح.
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawArrows(g);
        DrawBusy(g);
    }

    private Point ArrowCenter(int side) =>
        new Point(side < 0 ? ArrowMargin : Width - ArrowMargin, Height / 2);

    private void DrawArrows(Graphics g)
    {
        if (hoverSide < 0 && canGoPrev)
            DrawArrow(g, -1);
        else if (hoverSide > 0 && canGoNext)
            DrawArrow(g, 1);
    }

    private void DrawArrow(Graphics g, int side)
    {
        Point c = ArrowCenter(side);

        using (var fill = new SolidBrush(Color.FromArgb(150, 30, 40, 50)))
            g.FillEllipse(fill, c.X - ArrowRadius, c.Y - ArrowRadius, ArrowRadius * 2, ArrowRadius * 2);

        // السهم باتجاه الورقة: اليسار = السابق، اليمين = التالي.
        int s = side < 0 ? 1 : -1;
        using var pen = new Pen(Color.White, 4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        g.DrawLines(pen, new[]
        {
            new Point(c.X + 5 * s, c.Y - 11),
            new Point(c.X - 5 * s, c.Y),
            new Point(c.X + 5 * s, c.Y + 11)
        });
    }

    private void DrawBusy(Graphics g)
    {
        if (!busy)
            return;

        const string text = "جاري تحميل الورقة…";
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
        SizeF ts = g.MeasureString(text, font);
        float w = ts.Width + 28f;
        float h = ts.Height + 10f;
        float x = (Width - w) / 2f;
        float y = Height - h - 24f;

        using (var path = new GraphicsPath())
        {
            float d = h;
            path.AddArc(x, y, d, d, 90, 180);
            path.AddArc(x + w - d, y, d, d, 270, 180);
            path.CloseFigure();

            using var brush = new SolidBrush(Color.FromArgb(200, 30, 40, 50));
            g.FillPath(brush, path);
        }

        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, font, Brushes.White, new RectangleF(x, y, w, h), sf);
    }

    private bool InArrow(Point p, out int side)
    {
        side = 0;

        if (canGoPrev && Distance(p, ArrowCenter(-1)) <= ArrowRadius + 6)
        {
            side = -1;
            return true;
        }

        if (canGoNext && Distance(p, ArrowCenter(1)) <= ArrowRadius + 6)
        {
            side = 1;
            return true;
        }

        return false;
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int side = e.X < ArrowZone ? -1 : (e.X > Width - ArrowZone ? 1 : 0);
        if (side != hoverSide)
        {
            hoverSide = side;
            Invalidate();
        }

        Cursor = InArrow(e.Location, out _) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverSide != 0)
        {
            hoverSide = 0;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button == MouseButtons.Left && InArrow(e.Location, out int side))
            ArrowClicked?.Invoke(side);
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);

        Point p = PointToClient(Cursor.Position);
        if (!InArrow(p, out _))
            PageDoubleClick?.Invoke();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled)
            handled.Handled = true;

        wheelAccumulator += e.Delta;
        while (Math.Abs(wheelAccumulator) >= 120)
        {
            int step = wheelAccumulator > 0 ? -1 : 1;
            wheelAccumulator -= wheelAccumulator > 0 ? 120 : -120;
            WheelStep?.Invoke(step);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animTimer.Dispose();
            busyTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
