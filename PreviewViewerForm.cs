using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// شاشة المعاينة الكبيرة (ملء الشاشة) — بتتفتح بدبل كليك على المعاينة الجانبية.
///
/// السرعة:
///  - بتظهر فورًا بصورة المعاينة الجانبية (مكبّرة) وبعدها بتتبدّل بنسخة عالية الدقة بانتقال ناعم.
///  - كاش للأوراق + تحميل مسبق (Prefetch) للأوراق اللي حوالين الورقة الحالية، فالتنقل المتتالي فوري.
///  - أثناء سحب السلايدر الرسم بيتم بمعدل محدود ويتخطى الأوراق اللي المستخدم عداها.
///
/// طرق التنقل:
///  - السلايدر، ومربع رقم الورقة (اكتب رقم + Enter)، وأزرار ‹ ›
///  - الأسهم / PageUp / PageDown / Space / Backspace / Home / End
///  - عجلة الماوس، والأسهم الكبيرة على جانبي الورقة
///  - اكتب أي رقم مباشرة (من غير ما تضغط على المربع) ثم Enter
///  - دبل كليك على الورقة أو Esc للرجوع
/// </summary>
internal sealed class PreviewViewerForm : Form
{
    internal sealed record Rendered(Image? Image, int SheetIndex, int SheetCount);

    /// <summary>
    /// بترسم ورقة بالمقاس المطلوب. لازم ترجّع null لو isStale() بقت true قبل ما تبدأ.
    /// </summary>
    internal delegate Task<Rendered?> RenderDelegate(
        int sheetIndex, int longSide, int shortSide, Func<bool> isStale);

    private const int CacheCapacity = 10;
    private static readonly Color PageBack = Color.FromArgb(235, 237, 240);
    private static readonly Color BarBack = Color.FromArgb(248, 249, 250);

    private readonly string fileName;
    private readonly Image? placeholder;
    private readonly RenderDelegate render;
    private readonly Dictionary<int, Image> cache = new();

    private readonly PageView pageView;
    private readonly SheetNavigator nav;
    private readonly Label titleLabel;
    private readonly System.Windows.Forms.Timer renderTimer;

    private int count;
    private volatile int target;
    private int shownIndex = -1;
    private bool firstReal = true;
    private volatile bool closed;
    private bool prefetching;
    private int longSide = 1400;
    private int shortSide = 994;

    /// <summary> رقم الورقة (0-based) اللي المستخدم كان واقف عندها لما قفل. </summary>
    public int CurrentIndex => target;

    public PreviewViewerForm(
        string fileName,
        int startIndex,
        int sheetCount,
        Image? placeholder,
        RenderDelegate render)
    {
        this.fileName = fileName;
        this.placeholder = placeholder;
        this.render = render;
        count = Math.Max(1, sheetCount);
        target = Math.Clamp(startIndex, 0, count - 1);

        Text = "معاينة — " + fileName;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        RightToLeft = RightToLeft.No;
        BackColor = PageBack;
        Font = new Font("Segoe UI", 10f);
        DoubleBuffered = true;

        pageView = new PageView { Dock = DockStyle.Fill, BackColor = PageBack };

        nav = new SheetNavigator(large: true)
        {
            Dock = DockStyle.Bottom,
            BackColor = BarBack
        };

        var top = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = BarBack };

        var closeButton = new Button
        {
            Text = "✕  رجوع (Esc)",
            Dock = DockStyle.Right,
            Width = 150,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiKit.Danger,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.Click += (s, e) => Close();

        titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 50, 60)
        };

        var hint = new Label
        {
            Dock = DockStyle.Left,
            Width = 470,
            RightToLeft = RightToLeft.Yes,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(100, 110, 120),
            Text = "الأسهم أو عجلة الماوس للتنقل  •  Home / End  •  اكتب رقم الورقة ثم Enter  •  دبل كليك للرجوع"
        };

        var border = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = UiKit.Border
        };

        top.Controls.Add(titleLabel);
        top.Controls.Add(hint);
        top.Controls.Add(closeButton);
        top.Controls.Add(border);

        Controls.Add(pageView);
        Controls.Add(nav);
        Controls.Add(top);

        renderTimer = new System.Windows.Forms.Timer { Interval = 35 };
        renderTimer.Tick += (s, e) =>
        {
            renderTimer.Stop();
            _ = RenderAsync(target);
        };

        nav.IndexRequested += (idx, dragging) => GoTo(idx, dragging);
        nav.EscapePressed += () => pageView.Focus();
        pageView.ArrowClicked += d => Step(d);
        pageView.WheelStep += d => Step(d);
        pageView.PageDoubleClick += () => Close();
    }

    protected override void OnLoad(EventArgs e)
    {
        Screen screen = Owner != null ? Screen.FromControl(Owner) : Screen.PrimaryScreen ?? Screen.AllScreens[0];
        Bounds = screen.Bounds;
        base.OnLoad(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // مقاس الرسم حسب مساحة العرض الفعلية (وبزيادة بسيطة عشان الوضوح).
        Size vp = pageView.ClientSize;
        int minSide = Math.Max(1, Math.Min(vp.Width, vp.Height));
        int maxSide = Math.Max(vp.Width, vp.Height);
        longSide = Math.Clamp(Math.Min(maxSide, (int)(minSide * 1.45)), 1000, 2600);
        shortSide = (int)Math.Round(longSide * 0.71);

        if (placeholder != null)
        {
            pageView.SetImage(placeholder, 0, false);
            shownIndex = target;
        }

        GoTo(target, false);
        pageView.Focus();
    }

    // ------------------------------------------------------------------
    // التنقل
    // ------------------------------------------------------------------
    private void Step(int delta) => GoTo(target + delta, false);

    private void GoTo(int idx, bool dragging)
    {
        if (closed || count <= 0)
            return;

        idx = Math.Clamp(idx, 0, count - 1);
        target = idx;

        nav.SetState(idx, count);
        UpdateChrome();

        if (cache.TryGetValue(idx, out Image? img))
        {
            renderTimer.Stop();
            pageView.EndBusy();
            ShowImage(idx, img);
            _ = PrefetchLoopAsync();
            return;
        }

        pageView.BeginBusy();

        if (dragging)
        {
            // أثناء السحب: نرسم كل 35ms (Throttle) مش بعد ما المستخدم يقف.
            if (!renderTimer.Enabled)
                renderTimer.Start();
        }
        else
        {
            renderTimer.Stop();
            _ = RenderAsync(idx);
        }
    }

    private void UpdateChrome()
    {
        titleLabel.Text = $"{fileName}   —   ورقة {target + 1} من {count}";
        pageView.CanGoPrev = target > 0;
        pageView.CanGoNext = target < count - 1;
    }

    private void ShowImage(int idx, Image img)
    {
        int dir = (firstReal || shownIndex == idx) ? 0 : (idx > shownIndex ? 1 : -1);
        firstReal = false;
        shownIndex = idx;
        pageView.SetImage(img, dir, true);
    }

    // ------------------------------------------------------------------
    // الرسم والكاش
    // ------------------------------------------------------------------
    private async Task RenderAsync(int idx)
    {
        Rendered? r = null;

        try
        {
            r = await render(idx, longSide, shortSide, () => closed || idx != target);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Viewer render error: " + ex);
        }

        if (closed || IsDisposed)
        {
            r?.Image?.Dispose();
            return;
        }

        if (r?.Image == null)
        {
            // اتخطّت (المستخدم عدّى الورقة دي) أو فشلت.
            if (idx == target)
                pageView.EndBusy();
            return;
        }

        if (r.SheetCount > 0 && r.SheetCount != count)
        {
            count = r.SheetCount;
            target = Math.Clamp(target, 0, count - 1);
            nav.SetState(target, count);
            UpdateChrome();
        }

        AddToCache(r.SheetIndex, r.Image);

        if (r.SheetIndex == target)
        {
            pageView.EndBusy();
            ShowImage(r.SheetIndex, r.Image);
        }

        _ = PrefetchLoopAsync();
    }

    private async Task PrefetchLoopAsync()
    {
        if (prefetching)
            return;

        prefetching = true;
        try
        {
            while (!closed && !IsDisposed)
            {
                int next = FindNextToPrefetch();
                if (next < 0)
                    break;

                int n = next;
                Rendered? r = null;

                try
                {
                    r = await render(n, longSide, shortSide, () => closed || Math.Abs(n - target) > 3);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Viewer prefetch error: " + ex);
                }

                if (closed || IsDisposed)
                {
                    r?.Image?.Dispose();
                    break;
                }

                if (r?.Image == null)
                    break;

                AddToCache(r.SheetIndex, r.Image);

                if (r.SheetIndex == target && shownIndex != target)
                {
                    pageView.EndBusy();
                    ShowImage(r.SheetIndex, r.Image);
                }
            }
        }
        finally
        {
            prefetching = false;
        }
    }

    private int FindNextToPrefetch()
    {
        int t = target;
        foreach (int off in new[] { 1, -1, 2, -2, 3 })
        {
            int idx = t + off;
            if (idx >= 0 && idx < count && !cache.ContainsKey(idx))
                return idx;
        }

        return -1;
    }

    private void AddToCache(int idx, Image img)
    {
        if (cache.TryGetValue(idx, out Image? old))
        {
            cache.Remove(idx);
            if (!ReferenceEquals(old, img))
                SafeDispose(old);
        }

        cache[idx] = img;

        while (cache.Count > CacheCapacity)
        {
            // نشيل أبعد ورقة عن الورقة الحالية.
            int victim = cache.Keys
                .OrderByDescending(k => Math.Abs(k - target))
                .First();

            if (cache.TryGetValue(victim, out Image? v))
            {
                cache.Remove(victim);
                SafeDispose(v);
            }
        }
    }

    private void SafeDispose(Image img)
    {
        // لو الصورة لسه بتتعرض/بتتأنيميت، نسيبها للـGC بدل ما نكسر الرسم.
        if (!pageView.UsesImage(img))
            img.Dispose();
    }

    // ------------------------------------------------------------------
    // الكيبورد
    // ------------------------------------------------------------------
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (nav.BoxFocused)
        {
            if (keyData == Keys.Escape)
            {
                nav.CancelEdit();
                pageView.Focus();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        if (TryGetDigit(keyData, out char digit))
        {
            nav.FocusBox(digit);
            return true;
        }

        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;

            case Keys.Right:
            case Keys.Down:
            case Keys.PageDown:
            case Keys.Space:
                Step(1);
                return true;

            case Keys.Left:
            case Keys.Up:
            case Keys.PageUp:
            case Keys.Back:
                Step(-1);
                return true;

            case Keys.Home:
                GoTo(0, false);
                return true;

            case Keys.End:
                GoTo(count - 1, false);
                return true;

            case Keys.G | Keys.Control:
                nav.FocusBox();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static bool TryGetDigit(Keys keyData, out char digit)
    {
        digit = '\0';

        if (keyData >= Keys.D0 && keyData <= Keys.D9)
            digit = (char)('0' + (keyData - Keys.D0));
        else if (keyData >= Keys.NumPad0 && keyData <= Keys.NumPad9)
            digit = (char)('0' + (keyData - Keys.NumPad0));

        return digit != '\0';
    }

    // ------------------------------------------------------------------
    // الإغلاق والتنظيف
    // ------------------------------------------------------------------
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closed = true;
        renderTimer.Stop();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        pageView.SetImage(null, 0, false);

        foreach (Image img in cache.Values)
        {
            try { img.Dispose(); } catch { }
        }

        cache.Clear();

        try { placeholder?.Dispose(); } catch { }
        renderTimer.Dispose();
    }
}
