using System;
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp;

internal static class UiKit
{
    private sealed class GridSortState
    {
        public int LastColumnIndex = -1;
        public bool LastAscending;
    }

    private static readonly ConditionalWeakTable<DataGridView, GridSortState> GridSortStates = new();

    public static readonly Color Background = Color.FromArgb(244, 246, 249);
    public static readonly Color Surface = Color.White;
    public static readonly Color Text = Color.FromArgb(40, 50, 60);
    public static readonly Color Muted = Color.FromArgb(100, 110, 120);
    public static readonly Color Primary = Color.FromArgb(0, 122, 204);
    public static readonly Color Success = Color.FromArgb(40, 167, 69);
    // لون أخضر أغمق مخصص لنصوص على خلفية بيضاء (وليس كخلفية زر): اللون الأصلي
    // Success فاتح ومقروئيته ضعيفة كنص عادي، فنستخدم هذا البديل الأغمق في
    // أي تسمية/نص يحتاج تباينًا واضحًا مع الخلفية البيضاء.
    public static readonly Color SuccessText = Color.FromArgb(21, 118, 48);
    public static readonly Color Danger = Color.FromArgb(192, 57, 43);
    public static readonly Color Border = Color.FromArgb(220, 225, 230);

    public static void PrepareForm(Form form, string title, int width, int height)
    {
        form.Text = title;
        form.Size = new Size(width, height);
        form.MinimumSize = new Size(Math.Min(width, 900), Math.Min(height, 600));
        form.StartPosition = FormStartPosition.CenterScreen;
        form.RightToLeft = RightToLeft.Yes;
        form.RightToLeftLayout = true;
        // توحيد ترتيب عناصر كل الحاويات بعد تفعيل RTL على النافذة.
        // FlowLayoutPanel يرث RTL من الأب؛ الجمع بين RTL الموروث وFlowDirection.RightToLeft
        // كان يعكس الترتيب مرتين، فتظهر الأزرار والحقول من اليسار لليمين.
        NormalizeRtlLayoutTree(form, inheritedRtl: false);
        form.BackColor = Background;
        form.Font = new Font("Segoe UI", 10.5f);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        // أي محتوى يتجاوز المساحة المتاحة يصبح قابلًا للتمرير بدل أن يُقص بصمت.
        form.AutoScroll = true;
        form.MaximizeBox = true;
        form.Shown -= FitFormToContent;
        form.Shown += FitFormToContent;
        EnableEscape(form);
    }

    private static readonly ConditionalWeakTable<DataGridView, object> RtlGridStates = new();

    /// <summary>
    /// يطبّق RTL بصورة متسقة على الحاويات المضافة الآن أو لاحقًا.
    /// نترك FlowLayoutPanel ذات RightToLeft.No كما هي لأنها تضبط اتجاهها يدويًا.
    /// </summary>
    private static void NormalizeRtlLayoutTree(Control root, bool inheritedRtl)
    {
        bool effectiveRtl = root.RightToLeft switch
        {
            RightToLeft.Yes => true,
            RightToLeft.No => false,
            _ => inheritedRtl
        };

        if (root is FlowLayoutPanel flow && effectiveRtl && flow.FlowDirection == FlowDirection.RightToLeft)
            flow.FlowDirection = FlowDirection.LeftToRight;

        if (root is TabControl tabs && effectiveRtl)
        {
            tabs.RightToLeft = RightToLeft.Yes;
            tabs.RightToLeftLayout = true;
        }

        // DataGridView يعاني من خلل رسم عند RightToLeft.Yes داخل نافذة معكوسة.
        // نتركه No للرسم، ونرتب الأعمدة بصريًا من اليمين إلى اليسار عن طريق DisplayIndex.
        if (root is DataGridView grid && inheritedRtl)
            ApplyRightToLeftGridLayout(grid);

        root.ControlAdded -= RtlLayoutChildAdded;
        root.ControlAdded += RtlLayoutChildAdded;
        foreach (Control child in root.Controls)
            NormalizeRtlLayoutTree(child, effectiveRtl);
    }

    private static void RtlLayoutChildAdded(object? sender, ControlEventArgs e)
    {
        if (sender is not Control parent) return;
        bool parentRtl = parent.RightToLeft switch
        {
            RightToLeft.Yes => true,
            RightToLeft.No => false,
            _ => parent.Parent != null && IsEffectivelyRightToLeft(parent.Parent)
        };
        NormalizeRtlLayoutTree(e.Control, parentRtl);
    }

    private static bool IsEffectivelyRightToLeft(Control control)
    {
        for (Control? current = control; current != null; current = current.Parent)
        {
            if (current.RightToLeft == RightToLeft.Yes) return true;
            if (current.RightToLeft == RightToLeft.No) return false;
        }
        return false;
    }

    /// <summary>
    /// يجعل أول عمود مضاف هو العمود الأيمن في الجداول العربية، مع الإبقاء على
    /// RightToLeft.No لتفادي مشكلة رسم DataGridView المعروفة في WinForms.
    /// </summary>
    public static void ApplyRightToLeftGridLayout(DataGridView grid)
    {
        if (grid.IsDisposed) return;
        grid.RightToLeft = RightToLeft.No;
        if (!RtlGridStates.TryGetValue(grid, out _))
        {
            RtlGridStates.Add(grid, new object());
            grid.ColumnAdded += (_, _) => ReorderRtlGridColumns(grid);
            grid.DataBindingComplete += (_, _) => ReorderRtlGridColumns(grid);
        }
        ReorderRtlGridColumns(grid);
    }

    private static void ReorderRtlGridColumns(DataGridView grid)
    {
        if (grid.IsDisposed || grid.Columns.Count < 2) return;
        var ordered = grid.Columns.Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .Reverse()
            .Concat(grid.Columns.Cast<DataGridViewColumn>().Where(column => !column.Visible))
            .ToList();

        for (int index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].DisplayIndex != index)
                ordered[index].DisplayIndex = index;
        }
    }

    /// <summary> يجعل زر Esc يغلق الشاشة الحالية بسرعة. </summary>
    public static void EnableEscape(Form form)
    {
        form.KeyPreview = true;
        form.KeyDown -= EscapeKeyDown;
        form.KeyDown += EscapeKeyDown;
        form.KeyDown -= BackspaceKeyDown;
        form.KeyDown += BackspaceKeyDown;
        EnableHelpKey(form);
    }

    /// <summary> زر المساعدة (F1 افتراضيًا) يعرض اختصارات الشاشة الحالية. الزر بيتغير من إعدادات الإدارة. </summary>
    public static void EnableHelpKey(Form form)
    {
        form.KeyPreview = true;
        form.KeyDown -= HelpKeyDown;
        form.KeyDown += HelpKeyDown;
    }

    private static void HelpKeyDown(object? sender, KeyEventArgs e)
    {
        if (!Shortcuts.Is("global.help", e)) return;
        if (sender is not Form form || form.IsDisposed) return;

        e.SuppressKeyPress = true;
        e.Handled = true;
        ShortcutHelpForm.ShowFor(form);
    }

    /// <summary>
    /// يظبط حجم النافذة بعد ما تظهر على حسب محتواها: لو الجدول أو شريط الأزرار أو المحتوى
    /// محتاج مساحة أكبر من حجم النافذة الأصلي بتكبر النافذة بالمقدار المطلوب بس (من غير شريط
    /// تحريك)، ولو المحتوى أكبر من الشاشة كلها بتتفتح Maximized. النوافذ الصغيرة اللي محتواها
    /// داخل حجمها بتفضل صغيرة زي ما هي.
    /// </summary>
    private static void FitFormToContent(object? sender, EventArgs e)
    {
        if (sender is not Form form || form.IsDisposed) return;
        if (!form.IsHandleCreated) return;
        // نستنى لحد ما الـ Layout يخلص عشان قياس المحتوى يبقى صحيح.
        form.BeginInvoke((Action)(() => ApplyContentFit(form)));
    }

    private static readonly ConditionalWeakTable<Form, object> NoMaximizeForms = new();

    /// <summary> النافذة دي ما تتفتحش Maximized حتى لو المحتوى كبير (بتتكبر لحد حدود الشاشة بس). </summary>
    public static void NeverMaximize(Form form)
    {
        if (!NoMaximizeForms.TryGetValue(form, out _)) NoMaximizeForms.Add(form, new object());
    }

    private static void ApplyContentFit(Form form)
    {
        if (form.IsDisposed || form.Disposing || form.WindowState != FormWindowState.Normal) return;

        Rectangle work = Screen.FromControl(form).WorkingArea;
        bool resizable = form.FormBorderStyle == FormBorderStyle.Sizable || form.FormBorderStyle == FormBorderStyle.SizableToolWindow;

        if (!resizable)
        {
            // النوافذ ثابتة الحجم: نكتفي بالتأكد إنها مش أكبر من الشاشة.
            if (form.MaximizeBox && (form.Width > work.Width || form.Height > work.Height))
                form.WindowState = FormWindowState.Maximized;
            return;
        }

        int extraW = 0, extraH = 0;

        if (form.AutoScroll)
        {
            extraW = Math.Max(extraW, form.DisplayRectangle.Width - form.ClientSize.Width);
            extraH = Math.Max(extraH, form.DisplayRectangle.Height - form.ClientSize.Height);
        }
        MeasureHorizontalOverflow(form, ref extraW);

        if (extraW <= 4 && extraH <= 4)
        {
            if ((form.Width > work.Width || form.Height > work.Height) && !NoMaximizeForms.TryGetValue(form, out _))
                form.WindowState = FormWindowState.Maximized;
            return;
        }

        int newW = form.Width + (extraW > 4 ? extraW + 16 : 0);
        int newH = form.Height + (extraH > 4 ? extraH + 16 : 0);

        if (NoMaximizeForms.TryGetValue(form, out _))
        {
            newW = Math.Min(newW, work.Width);
            newH = Math.Min(newH, work.Height);
        }
        else if (newW > work.Width || newH > work.Height)
        {
            form.WindowState = FormWindowState.Maximized;
            return;
        }

        // نكبّر حوالين المنتصف (زي ما كانت متوسّطة) ونتأكد إنها جوه الشاشة.
        int left = form.Left - (newW - form.Width) / 2;
        int top = form.Top - (newH - form.Height) / 2;
        left = Math.Max(work.Left, Math.Min(left, work.Right - newW));
        top = Math.Max(work.Top, Math.Min(top, work.Bottom - newH));
        form.SetBounds(left, top, newW, newH);
    }

    private static void MeasureHorizontalOverflow(Control root, ref int extraW)
    {
        foreach (Control c in root.Controls)
        {
            if (!c.Visible) continue;

            if (c is DataGridView grid)
            {
                int visibleCols = grid.Columns.GetColumnCount(DataGridViewElementStates.Visible);
                if (visibleCols > 0 && grid.DisplayedColumnCount(false) < visibleCols)
                {
                    bool vScroll = grid.Controls.OfType<VScrollBar>().Any(v => v.Visible);
                    int need = grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible)
                        + (grid.RowHeadersVisible ? grid.RowHeadersWidth : 0)
                        + (vScroll ? SystemInformation.VerticalScrollBarWidth : 0)
                        + 4;
                    extraW = Math.Max(extraW, need - grid.ClientSize.Width);
                }
                continue; // مفيش داعي ندخل جوه الجدول
            }

            if (c is FlowLayoutPanel flow && !flow.WrapContents &&
                (flow.FlowDirection == FlowDirection.LeftToRight || flow.FlowDirection == FlowDirection.RightToLeft))
            {
                int need = flow.Padding.Horizontal + 4;
                foreach (Control child in flow.Controls)
                    if (child.Visible) need += child.Width + child.Margin.Horizontal;
                extraW = Math.Max(extraW, need - flow.ClientSize.Width);
            }
            else if (c is ScrollableControl sc && sc.AutoScroll)
            {
                extraW = Math.Max(extraW, sc.DisplayRectangle.Width - sc.ClientSize.Width);
            }

            MeasureHorizontalOverflow(c, ref extraW);
        }
    }

    /// <summary>
    /// Backspace يقفل النافذة زي Esc، بس بأمان: لو بتمسح كلام في خانة كتابة مش بيقفل،
    /// ولو ماسك الزر (تكرار تلقائي) مش بيقفل، وفي الجداول سايبه لاختصار الحذف، وفي
    /// لوحة التحكم وشاشة الدخول مش بيقفل خالص (عشان البرنامج ما يتقفلش بالغلط).
    /// </summary>
    private static DateTime lastBackspaceDown = DateTime.MinValue;

    private static void BackspaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (!Shortcuts.Is("global.back", e)) return;
        if (sender is not Form form || form.IsDisposed) return;
        if (form is DashboardForm || form is LoginForm) return;

        DateTime now = DateTime.UtcNow;
        bool freshPress = (now - lastBackspaceDown).TotalMilliseconds > 600;
        lastBackspaceDown = now;
        if (!freshPress) return;

        Control? focused = GetFocusedControl(form);
        if (focused is DataGridView || focused is DateTimePicker) return;
        if (HasTypedText(focused)) return;

        e.SuppressKeyPress = true;
        e.Handled = true;
        if (form.Modal) form.DialogResult = DialogResult.Cancel;
        form.Close();
    }

    private static Control? GetFocusedControl(Form form)
    {
        Control c = form;
        while (c is ContainerControl cc && cc.ActiveControl != null)
            c = cc.ActiveControl;
        return ReferenceEquals(c, form) ? null : c;
    }

    private static bool HasTypedText(Control? c)
    {
        switch (c)
        {
            case TextBoxBase tb: return !tb.ReadOnly && tb.TextLength > 0;
            case NumericUpDown nud: return nud.Text.Length > 0;
            case ComboBox cb: return cb.DropDownStyle != ComboBoxStyle.DropDownList && cb.Text.Length > 0;
            default: return false;
        }
    }

    /// <summary> يدوّر أركان العنصر بنصف قطر صغير (ويفضل شغال لو الحجم اتغير). </summary>
    public static void RoundCorners(Control control, int radius = 10)
    {
        void Apply()
        {
            if (control.IsDisposed || control.Width < 2 || control.Height < 2) return;
            int r = Math.Min(radius, Math.Min(control.Width, control.Height) / 2);
            int d = r * 2;
            using var path = new GraphicsPath();
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(control.Width - d, 0, d, d, 270, 90);
            path.AddArc(control.Width - d, control.Height - d, d, d, 0, 90);
            path.AddArc(0, control.Height - d, d, d, 90, 90);
            path.CloseFigure();
            Region? old = control.Region;
            control.Region = new Region(path);
            old?.Dispose();
        }
        control.SizeChanged += (_, _) => Apply();
        Apply();
    }

    private static void EscapeKeyDown(object? sender, KeyEventArgs e)
    {
        if (!Shortcuts.Is("global.close", e)) return;
        if (sender is not Form form || form.IsDisposed) return;

        e.SuppressKeyPress = true;
        e.Handled = true;
        if (form.Modal) form.DialogResult = DialogResult.Cancel;
        form.Close();
    }

    public static Label Title(string text, int size = 18)
        => new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", size, FontStyle.Bold), ForeColor = Text };

    public static Button Button(string text, Color color, int width = 130, int height = 40)
    {
        var b = new Button
        {
            Text = text, Width = width, Height = height, MinimumSize = new Size(width, 36),
            BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Cursor = Cursors.Hand,
            Margin = new Padding(5), TextAlign = ContentAlignment.MiddleCenter,
            // UseCompatibleTextRendering=false (الافتراضي) يجعل الرسم يمر عبر GDI
            // بدل GDI+، وهو المسار الوحيد الذي يفعّل استبدال الخط التلقائي
            // (Font Linking) لرموز الإيموجي غير الموجودة في خط "Segoe UI" نفسه
            // (مثل 📦 💸 🖨️ 🏪 👥 💰 📟 📲 🚪 🔒). مع GDI+ (true القديمة) كانت هذه
            // الرموز تظهر كمربعات فارغة (tofu) لأن GDI+ لا يبحث عن خط بديل
            // (Segoe UI Emoji) للحروف غير الموجودة في الخط المحدد صراحة.
            UseCompatibleTextRendering = false, AutoEllipsis = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, .12f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color, .08f);
        return b;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoGenerateColumns = false;
        // إصلاح مهم: DataGridView في WinForms له مشكلة رسم معروفة عندما يرث
        // RightToLeft=Yes من الفورم الأب (يحدث معه أحيانًا ألا يرسم أي محتوى
        // إطلاقًا رغم امتلائه ببيانات صحيحة وحجم ومرئية طبيعيين). نضبطها صراحة
        // على No هنا لتفادي هذا الخلل تمامًا، بينما باقي عناصر الفورم تبقى RTL
        // بشكل طبيعي.
        grid.RightToLeft = RightToLeft.No;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.ScrollBars = ScrollBars.Both;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.BackgroundColor = Background;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Border;
        grid.ColumnHeadersVisible = true;
        grid.ColumnHeadersHeight = 44;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = 40;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(52, 73, 94), ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            Padding = new Padding(4)
        };
        grid.RowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White, ForeColor = Text,
            SelectionBackColor = Color.FromArgb(214, 234, 248), SelectionForeColor = Text,
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(248, 250, 252), ForeColor = Text,
            SelectionBackColor = Color.FromArgb(214, 234, 248), SelectionForeColor = Text
        };
        grid.AutoSize = false;
        grid.Dock = DockStyle.Fill;
        ApplyRightToLeftGridLayout(grid);
        EnableColumnSorting(grid);
    }

    /// <summary>
    /// يتيح فرز الجداول بالضغط على عنوان العمود. أول ضغطة على التاريخ أو الأرقام
    /// تعرض الأحدث/الأكبر أولًا، والضغطة التالية تعكس الترتيب. الأعمدة النصية تبدأ تصاعديًا.
    /// </summary>
    public static void EnableColumnSorting(DataGridView grid)
    {
        if (GridSortStates.TryGetValue(grid, out _)) return;
        GridSortStates.Add(grid, new GridSortState());
        grid.ColumnAdded += GridColumnAdded;
        grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
        foreach (DataGridViewColumn column in grid.Columns)
            ConfigureSortableColumn(column);
    }

    private static void GridColumnAdded(object? sender, DataGridViewColumnEventArgs e)
        => ConfigureSortableColumn(e.Column);

    private static void ConfigureSortableColumn(DataGridViewColumn column)
    {
        // لا معنى لفرز الصور أو أزرار الجدول، بينما الأعمدة النصية والرقمية كلها قابلة للفرز.
        column.SortMode = column is DataGridViewImageColumn || column is DataGridViewButtonColumn
            ? DataGridViewColumnSortMode.NotSortable
            : DataGridViewColumnSortMode.Programmatic;
    }

    private static void GridColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (sender is not DataGridView grid || e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count) return;
        var column = grid.Columns[e.ColumnIndex];
        if (column.SortMode == DataGridViewColumnSortMode.NotSortable || grid.Rows.Count < 2) return;

        var state = GridSortStates.GetValue(grid, _ => new GridSortState());
        bool ascending = state.LastColumnIndex == e.ColumnIndex
            ? !state.LastAscending
            : !IsNumericOrDateColumn(grid, e.ColumnIndex); // الأكبر/الأحدث أول ضغطة

        try
        {
            grid.Sort(new GridValueComparer(e.ColumnIndex, ascending));
            foreach (DataGridViewColumn col in grid.Columns)
                col.HeaderCell.SortGlyphDirection = SortOrder.None;
            column.HeaderCell.SortGlyphDirection = ascending ? SortOrder.Ascending : SortOrder.Descending;
            state.LastColumnIndex = e.ColumnIndex;
            state.LastAscending = ascending;
        }
        catch (InvalidOperationException)
        {
            // الجداول غير القابلة للفرز (مثل بعض الجداول المرتبطة مباشرة بمصدر بيانات) تبقى سليمة.
        }
        catch (ArgumentException)
        {
            // لا نوقف الشاشة بسبب عمود غير مدعوم للفرز.
        }
    }

    private static bool IsNumericOrDateColumn(DataGridView grid, int columnIndex)
    {
        bool found = false, allNumeric = true, allDate = true;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            object? value = row.Cells[columnIndex].Value;
            if (IsEmptySortValue(value)) continue;
            found = true;
            if (!TryParseSortNumber(value, out _)) allNumeric = false;
            if (!TryParseSortDate(value, out _)) allDate = false;
            if (!allNumeric && !allDate) return false;
        }
        return found && (allNumeric || allDate);
    }

    private sealed class GridValueComparer : IComparer
    {
        private readonly int columnIndex;
        private readonly bool ascending;
        public GridValueComparer(int columnIndex, bool ascending) { this.columnIndex = columnIndex; this.ascending = ascending; }

        public int Compare(object? x, object? y)
        {
            var rowX = x as DataGridViewRow;
            var rowY = y as DataGridViewRow;
            object? a = rowX == null ? null : rowX.Cells[columnIndex].Value;
            object? b = rowY == null ? null : rowY.Cells[columnIndex].Value;
            bool emptyA = IsEmptySortValue(a);
            bool emptyB = IsEmptySortValue(b);
            if (emptyA || emptyB) return emptyA == emptyB ? 0 : emptyA ? 1 : -1; // الخانات الفارغة في الآخر دائمًا

            int result;
            if (TryParseSortNumber(a, out var numberA) && TryParseSortNumber(b, out var numberB))
                result = numberA.CompareTo(numberB);
            else if (TryParseSortDate(a, out var dateA) && TryParseSortDate(b, out var dateB))
                result = dateA.CompareTo(dateB);
            else
                result = StringComparer.CurrentCultureIgnoreCase.Compare(Convert.ToString(a), Convert.ToString(b));
            return ascending ? result : -result;
        }
    }

    private static bool IsEmptySortValue(object? value)
    {
        if (value == null || value == DBNull.Value) return true;
        string text = Convert.ToString(value)?.Trim() ?? string.Empty;
        return text.Length == 0 || text == "—" || text == "-" || text.Equals("N/A", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSortDate(object? value, out DateTime result)
    {
        if (value is DateTime date) { result = date; return true; }
        string text = Convert.ToString(value)?.Trim() ?? string.Empty;
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out result)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out result);
    }

    private static bool TryParseSortNumber(object? value, out decimal result)
    {
        if (value is decimal dec) { result = dec; return true; }
        if (value is int i) { result = i; return true; }
        if (value is long l) { result = l; return true; }
        if (value is double d) { result = (decimal)d; return true; }
        string text = Convert.ToString(value)?.Trim() ?? string.Empty;
        text = text.Replace('٠','0').Replace('١','1').Replace('٢','2').Replace('٣','3').Replace('٤','4')
                   .Replace('٥','5').Replace('٦','6').Replace('٧','7').Replace('٨','8').Replace('٩','9')
                   .Replace('٬', ',').Replace('٫', '.').Replace(",", string.Empty)
                   .Replace("ج.م", string.Empty).Replace("جنيه", string.Empty).Replace("EGP", string.Empty)
                   .Replace("ر.س", string.Empty).Replace("$", string.Empty).Replace("€", string.Empty).Trim();
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out result)
            || decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out result);
    }

    /// <summary>
    /// يمنع تراكب/قص عناصر شريط أدوات أفقي (FlowLayoutPanel لا يلتف على عدة أسطر،
    /// WrapContents=false) عند تصغير النافذة أو الشاشة: بدل أن تتراكب العناصر فوق
    /// بعضها أو تختفي خارج حدود الشاشة بصمت، يظهر سكرول أفقي يتيح الوصول لكل
    /// عنصر دائمًا مهما صغر عرض النافذة.
    /// </summary>
    public static void EnableToolbarScroll(FlowLayoutPanel panel)
    {
        panel.WrapContents = false;
        panel.AutoScroll = true;
        panel.AutoSize = true;
        panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        panel.MinimumSize = new Size(0, 44);
        panel.Padding = new Padding(4, 4, 4, 4);
        // يمنع قص النص داخل زر/ComboBox عند تكبير Windows؛ ارتفاع الشريط
        // يتبع أعلى عنصر داخله بدل رقم ثابت لا يناسب كل DPI.
        panel.SizeChanged -= ToolbarSizeChanged;
        panel.SizeChanged += ToolbarSizeChanged;

        void ToolbarSizeChanged(object? sender, EventArgs e)
        {
            if (panel.IsDisposed) return;
            foreach (Control child in panel.Controls)
            {
                if (child is Button b)
                    b.AutoSize = false;
            }
        }
    }

    /// <summary>
    /// ينشئ عمود جدول نصي بعرض أولي ثابت. نضبط MinimumWidth دائمًا حتى لا يقدر
    /// المستخدم على سحب حد العمود ليصغّره لدرجة يختفي فيها العنوان أو يتراكب
    /// المحتوى (المشكلة التي كانت تحدث سابقًا لعدم وجود أي حد أدنى للعرض).
    /// يمكن تمرير minWidth صراحة، وإلا يُحسب تلقائيًا كنسبة معقولة من العرض الأصلي.
    /// </summary>
    public static DataGridViewTextBoxColumn GridText(string name, string header, int width, bool fill = false, int minWidth = 0)
    {
        var col = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.True
        };
        col.MinimumWidth = minWidth > 0 ? minWidth : Math.Max(45, Math.Min(width, 70));
        return col;
    }

    /// <summary>
    /// يختصر حذف العنصر المحدد في الجداول باستخدام Delete أو Backspace.
    /// لا يعمل أثناء تحرير خلية حتى لا يتدخل في كتابة النص.
    /// </summary>
    public static void EnableKeyboardDelete(DataGridView grid, Action deleteAction)
    {
        grid.KeyDown -= KeyboardDeleteKeyDown;
        grid.KeyDown += KeyboardDeleteKeyDown;

        void KeyboardDeleteKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not DataGridView dgv) return;
            if (dgv.IsCurrentCellInEditMode) return;
            if (!Shortcuts.Is("global.gridDelete", e) && !Shortcuts.Is("global.gridDelete2", e)) return;

            e.SuppressKeyPress = true;
            e.Handled = true;
            deleteAction();
        }
    }

    public static void SelectAll(Control control)
    {
        if (control is TextBox tb)
        {
            tb.Enter -= SelectAllTextBox;
            tb.Enter += SelectAllTextBox;
            tb.MouseDown -= SelectAllMouseDown;
            tb.MouseDown += SelectAllMouseDown;
        }
        else if (control is NumericUpDown num)
        {
            num.Enter -= SelectAllNumeric;
            num.Enter += SelectAllNumeric;
            num.MouseDown -= SelectAllNumericMouseDown;
            num.MouseDown += SelectAllNumericMouseDown;
        }
        foreach (Control child in control.Controls) SelectAll(child);
    }

    private static void SelectAllTextBox(object? s, EventArgs e) => ((TextBox)s!).SelectAll();
    private static void SelectAllMouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) BeginInvokeSelect((Control)s!, true);
    }
    private static void SelectAllNumeric(object? s, EventArgs e) => BeginInvokeSelect((Control)s!, true);
    private static void SelectAllNumericMouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) BeginInvokeSelect((Control)s!, true);
    }

    private static void BeginInvokeSelect(Control c, bool all)
    {
        if (c.IsDisposed || c.Disposing) return;

        void SelectNow()
        {
            if (c.IsDisposed || c.Disposing) return;
            if (c is TextBox tb) tb.SelectAll();
            else if (c is NumericUpDown num)
            {
                TextBoxBase? edit = num.Controls.Count > 0
                    ? FindTextEditor(num)
                    : null;
                edit?.SelectAll();
            }
        }

        if (!c.IsHandleCreated)
        {
            SelectNow();
            return;
        }

        try
        {
            c.BeginInvoke((Action)SelectNow);
        }
        catch (ObjectDisposedException)
        {
            // تم إغلاق الكنترول أثناء التنفيذ.
        }
        catch (InvalidOperationException)
        {
            // الـHandle غير جاهز/يُغلق؛ التحديد تجميلي فقط.
        }
    }

    private static TextBoxBase? FindTextEditor(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TextBoxBase textBox) return textBox;
            TextBoxBase? nested = FindTextEditor(child);
            if (nested != null) return nested;
        }
        return null;
    }
}

/// <summary>
/// DataGridView آمن للاستخدام داخل نوافذ RTL (RightToLeftLayout = true).
///
/// السبب الجذري لمشكلة "الجدول يظهر فارغًا تمامًا" (بدون رؤوس أعمدة ولا صفوف
/// رغم وجود بيانات صحيحة وحجم/مكان طبيعيين، وهي بالضبط مشكلة شاشة سجل اليومية
/// المتكررة عبر عدة إصلاحات سابقة): عندما يكون للفورم الأب RightToLeftLayout=true
/// فإن نظام التشغيل يفعّل النمط الموسّع WS_EX_LAYOUTRTL على نافذة الفورم، وهذا
/// النمط يُورَّث تلقائيًا (على مستوى نظام التشغيل، وليس WinForms) إلى كل نافذة
/// فرعية بما فيها المقبض (HWND) الداخلي لـ DataGridView، ما لم تُستثنَ صراحةً
/// بالنمط WS_EX_NOINHERITLAYOUT. وبما أن DataGridView يرسم كل محتواه يدويًا
/// (owner-drawn عبر GDI+)، فإن هذا التوريث يجعل الرسم يفشل بالكامل بصمت.
///
/// ضبط الخاصية الإدارية DataGridView.RightToLeft = No (كما كانت محاولات
/// الإصلاح السابقة تفعل) لا يمنع هذا التوريث إطلاقًا لأنها خاصية WinForms
/// إدارية لا علاقة لها بنمط النافذة الأصلي (native) في نظام التشغيل.
///
/// الحل الجذري هنا: استثناء نافذة الجدول نفسها من توريث تخطيط الفورم الأب،
/// وإزالة WS_EX_LAYOUTRTL منها هي نفسها، عند إنشاء المقبض مباشرة (CreateParams)
/// — بصرف النظر عمّا يفعله الفورم الأب أو أي إعداد آخر. هذا يضمن أن الجدول
/// سيُرسم بشكل طبيعي دائمًا مهما كان الفورم المُستضيف RTL أو LTR.
/// </summary>
internal class SafeDataGridView : DataGridView
{
    private const int WS_EX_LAYOUTRTL = 0x00400000;
    private const int WS_EX_NOINHERITLAYOUT = 0x00100000;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle &= ~WS_EX_LAYOUTRTL;
            cp.ExStyle |= WS_EX_NOINHERITLAYOUT;
            return cp;
        }
    }
}
