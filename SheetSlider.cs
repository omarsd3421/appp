using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// سلايدر مخصص (مرسوم يدويًا) للتنقل السريع بين أوراق المعاينة:
/// سحب، ضغط في أي مكان للقفز، عجلة الماوس، حركة ناعمة للمقبض، وفقاعة بتعرض رقم الورقة أثناء السحب.
/// القيم هنا 0-based؛ الفقاعة بتعرض (القيمة + 1).
/// </summary>
internal sealed class SheetSlider : Control
{
    private const float Pad = 15f;

    private int minimum;
    private int maximum;
    private int current;
    private float displayPos;
    private bool dragging;
    private bool hover;
    private float hoverAmount;

    private readonly System.Windows.Forms.Timer animTimer =
        new System.Windows.Forms.Timer { Interval = 15 };

    /// <summary> (القيمة الجديدة، هل المستخدم لسه بيسحب؟) — بتتنادى من المستخدم فقط، مش من الكود. </summary>
    public event Action<int, bool>? UserValueChanged;

    public SheetSlider()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);

        Height = 46;
        Cursor = Cursors.Hand;
        animTimer.Tick += (s, e) => AnimStep();
    }

    public int Minimum
    {
        get => minimum;
        set
        {
            minimum = value;
            if (maximum < minimum)
                maximum = minimum;
            current = Math.Clamp(current, minimum, maximum);
            displayPos = Math.Clamp(displayPos, minimum, maximum);
            Invalidate();
        }
    }

    public int Maximum
    {
        get => maximum;
        set
        {
            maximum = Math.Max(value, minimum);
            current = Math.Clamp(current, minimum, maximum);
            displayPos = Math.Clamp(displayPos, minimum, maximum);
            Invalidate();
        }
    }

    /// <summary> تغيير برمجي: بيحرّك المقبض بنعومة ومبيطلعش حدث. بيتجاهل لو المستخدم ماسك المقبض. </summary>
    public int Value
    {
        get => current;
        set
        {
            if (dragging)
                return;

            int v = Math.Clamp(value, minimum, maximum);
            if (v == current && Math.Abs(displayPos - v) < 0.001f)
                return;

            current = v;
            StartAnim();
        }
    }

    private void StartAnim()
    {
        // من غير أنيميشن في المعاينة: المقبض وتأثير الـHover بيتغيروا فورًا.
        if (!dragging)
            displayPos = current;
        hoverAmount = (hover || dragging) ? 1f : 0f;
        Invalidate();
    }

    private void AnimStep()
    {
        bool busy = false;

        if (!dragging)
        {
            float d = current - displayPos;
            if (Math.Abs(d) > 0.005f)
            {
                displayPos += d * 0.35f;
                busy = true;
            }
            else
            {
                displayPos = current;
            }
        }

        float target = (hover || dragging) ? 1f : 0f;
        float dh = target - hoverAmount;
        if (Math.Abs(dh) > 0.01f)
        {
            hoverAmount += dh * 0.3f;
            busy = true;
        }
        else
        {
            hoverAmount = target;
        }

        Invalidate();

        if (!busy)
            animTimer.Stop();
    }

    private float TrackY => Height - 17f;

    private void UpdateFromMouse(int x)
    {
        float x0 = Pad;
        float x1 = Math.Max(Pad + 1f, Width - Pad);
        float frac = Math.Clamp((x - x0) / (x1 - x0), 0f, 1f);
        float pos = minimum + frac * (maximum - minimum);
        displayPos = pos;

        int v = (int)Math.Round(pos);
        if (v != current)
        {
            current = v;
            UserValueChanged?.Invoke(v, true);
        }

        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled || maximum <= minimum)
            return;

        dragging = true;
        Capture = true;
        UpdateFromMouse(e.X);
        StartAnim();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!hover)
        {
            hover = true;
            StartAnim();
        }

        if (dragging)
            UpdateFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging)
            return;

        dragging = false;
        Capture = false;
        UserValueChanged?.Invoke(current, false);
        StartAnim();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hover = true;
        StartAnim();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = false;
        StartAnim();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled)
            handled.Handled = true;

        if (!Enabled || maximum <= minimum || dragging)
            return;

        int step = e.Delta > 0 ? -1 : 1;
        int v = Math.Clamp(current + step, minimum, maximum);
        if (v == current)
            return;

        current = v;
        StartAnim();
        UserValueChanged?.Invoke(v, false);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        bool active = Enabled && maximum > minimum;
        float y = TrackY;
        float x0 = Pad;
        float x1 = Math.Max(Pad + 1f, Width - Pad);
        float frac = maximum > minimum
            ? Math.Clamp((displayPos - minimum) / (maximum - minimum), 0f, 1f)
            : 0f;
        float tx = x0 + (x1 - x0) * frac;

        Color trackColor = Color.FromArgb(214, 219, 226);
        Color fillColor = active ? UiKit.Primary : Color.FromArgb(170, 178, 188);

        using (var pen = new Pen(trackColor, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(pen, x0, y, x1, y);

        if (tx > x0)
        {
            using var pen = new Pen(fillColor, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, x0, y, tx, y);
        }

        // نقط صغيرة لكل ورقة لما العدد قليل
        int steps = maximum - minimum;
        if (active && steps >= 1 && steps <= 30)
        {
            using var dot = new SolidBrush(Color.FromArgb(170, 255, 255, 255));
            for (int i = 0; i <= steps; i++)
            {
                float px = x0 + (x1 - x0) * i / steps;
                g.FillEllipse(dot, px - 1.5f, y - 1.5f, 3f, 3f);
            }
        }

        float r = 8f + 2.5f * hoverAmount + (dragging ? 1f : 0f);

        using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            g.FillEllipse(shadow, tx - r, y - r + 1.5f, r * 2, r * 2);

        g.FillEllipse(Brushes.White, tx - r, y - r, r * 2, r * 2);
        using (var border = new Pen(fillColor, 2.5f))
            g.DrawEllipse(border, tx - r, y - r, r * 2, r * 2);

        if (dragging && active)
            DrawBubble(g, tx, y - r, (current + 1).ToString());
    }

    private void DrawBubble(Graphics g, float cx, float thumbTop, string text)
    {
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        SizeF ts = g.MeasureString(text, font);
        float w = Math.Max(32f, ts.Width + 14f);
        float h = 20f;
        float x = Math.Clamp(cx - w / 2f, 2f, Math.Max(2f, Width - w - 2f));
        float yy = Math.Max(0f, thumbTop - h - 6f);

        using var path = new GraphicsPath();
        float d = 8f;
        path.AddArc(x, yy, d, d, 180, 90);
        path.AddArc(x + w - d, yy, d, d, 270, 90);
        path.AddArc(x + w - d, yy + h - d, d, d, 0, 90);
        path.AddArc(x, yy + h - d, d, d, 90, 90);
        path.CloseFigure();

        using (var brush = new SolidBrush(UiKit.Primary))
            g.FillPath(brush, path);

        using (var brush = new SolidBrush(UiKit.Primary))
        {
            PointF[] tri =
            {
                new PointF(cx - 5f, yy + h - 0.5f),
                new PointF(cx + 5f, yy + h - 0.5f),
                new PointF(cx, yy + h + 5f)
            };
            g.FillPolygon(brush, tri);
        }

        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, font, Brushes.White, new RectangleF(x, yy, w, h), sf);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            animTimer.Dispose();

        base.Dispose(disposing);
    }
}
