using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// تعريف اختصار واحد في البرنامج: رقمه الداخلي، الشاشة التابع لها، اسمه بالعربي، والزر الافتراضي.
/// Context: لو اختصارين ليهم نفس الزر لكن في سياقين مختلفين (مثلاً «داخل الجدول» و«خارج الجدول»)
/// ما بنعتبرهمش متعارضين.
/// </summary>
internal sealed class ShortcutDef
{
    public string Id { get; }
    public string Group { get; }
    public string Title { get; }
    public Keys Default { get; }
    public string? Context { get; }

    public ShortcutDef(string id, string group, string title, Keys defaultKey, string? context = null)
    {
        Id = id;
        Group = group;
        Title = title;
        Default = defaultKey;
        Context = context;
    }
}

internal static class ShortcutGroups
{
    public const string Global = "global";
    public const string Dashboard = "dashboard";
    public const string Print = "print";
    public const string Inventory = "inventory";
    public const string Operation = "operation";
    public const string Audit = "audit";

    /// <summary> ترتيب ظهور المجموعات في صفحة التخصيص. </summary>
    public static readonly (string Key, string Title)[] Ordered =
    {
        (Global, "كل الشاشات"),
        (Dashboard, "لوحة التحكم"),
        (Print, "شاشة الطباعة"),
        (Inventory, "المخزن"),
        (Operation, "شاشة إتمام العملية"),
        (Audit, "سجل تدقيق العمليات"),
    };

    public static string TitleOf(string key)
    {
        foreach (var (k, t) in Ordered)
            if (k == key) return t;
        return key;
    }

    /// <summary> مجموعة الاختصارات الخاصة بالشاشة دي (فاضية لو الشاشة ملهاش اختصارات خاصة). </summary>
    public static string ForForm(Form form) => form switch
    {
        DashboardForm => Dashboard,
        Form1 => Print,
        InventoryForm => Inventory,
        OperationDoneForm => Operation,
        AuditLogForm => Audit,
        _ => string.Empty
    };
}

/// <summary>
/// المخزن المركزي لكل اختصارات لوحة المفاتيح في البرنامج.
/// القيم المتغيرة بتتحفظ في shortcuts.json (جنب options.json) وبتدخل في النسخ الاحتياطي،
/// وأي شاشة بتسأل هنا وقت الضغط فالتغيير بيشتغل فورًا من غير ما تقفل الشاشات المفتوحة.
/// </summary>
internal static class Shortcuts
{
    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibraryPrintApp");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "shortcuts.json");

    private static readonly object Gate = new();
    private static Dictionary<string, int>? overrides;

    private static readonly ShortcutDef[] Defs =
    {
        // ----- كل الشاشات -----
        new("global.help", ShortcutGroups.Global, "عرض قائمة اختصارات الشاشة الحالية", Keys.F1),
        new("global.close", ShortcutGroups.Global, "إغلاق الشاشة الحالية", Keys.Escape),
        new("global.back", ShortcutGroups.Global, "رجوع وإغلاق الشاشة (خارج خانات الكتابة والجداول)", Keys.Back, "form"),
        new("global.gridDelete", ShortcutGroups.Global, "حذف الصف المحدد في الجداول", Keys.Delete, "grid"),
        new("global.gridDelete2", ShortcutGroups.Global, "حذف الصف المحدد في الجداول (مفتاح بديل)", Keys.Back, "grid"),

        // ----- لوحة التحكم -----
        new("dash.print", ShortcutGroups.Dashboard, "فتح شاشة الطباعة والنسخ", Keys.NumPad1),
        new("dash.cash", ShortcutGroups.Dashboard, "السحب والإيداع / خدمات الكاش", Keys.NumPad2),
        new("dash.sales", ShortcutGroups.Dashboard, "المبيعات", Keys.NumPad3),
        new("dash.inventory", ShortcutGroups.Dashboard, "المخزن", Keys.NumPad4),
        new("dash.customers", ShortcutGroups.Dashboard, "العملاء والديون", Keys.NumPad5),
        new("dash.treasury", ShortcutGroups.Dashboard, "الخزينة", Keys.NumPad6),
        new("dash.bee", ShortcutGroups.Dashboard, "ماكينة باب رزق", Keys.NumPad7),
        new("dash.opay", ShortcutGroups.Dashboard, "ماكينة OPay", Keys.NumPad8),
        new("dash.admin", ShortcutGroups.Dashboard, "إعدادات الإدارة (تطلب كلمة المرور)", Keys.NumPad9),
        new("dash.management", ShortcutGroups.Dashboard, "المشتريات والمرتجعات والتقارير", Keys.Control | Keys.M),
        new("dash.invoice", ShortcutGroups.Dashboard, "إصدار فاتورة", Keys.NumPad0),
        new("dash.closing", ShortcutGroups.Dashboard, "اعتماد وإغلاق اليومية", Keys.Decimal),
        new("dash.refresh", ShortcutGroups.Dashboard, "تحديث أرصدة الخزينة", Keys.F5),

        // ----- شاشة الطباعة -----
        new("print.addFiles", ShortcutGroups.Print, "إضافة ملفات", Keys.NumPad1),
        new("print.paste", ShortcutGroups.Print, "إضافة الملفات المنسوخة من الحافظة", Keys.NumPad2),
        new("print.selectAll", ShortcutGroups.Print, "تحديد كل الملفات في القائمة", Keys.NumPad3),
        new("print.printSelected", ShortcutGroups.Print, "طباعة الملفات المحددة", Keys.NumPad4),
        new("print.printAll", ShortcutGroups.Print, "طباعة كل الملفات", Keys.NumPad5),
        new("print.options", ShortcutGroups.Print, "فتح إعدادات الطباعة", Keys.NumPad6),
        new("print.delete", ShortcutGroups.Print, "حذف الملفات المحددة", Keys.NumPad7),
        new("print.clear", ShortcutGroups.Print, "مسح قائمة الملفات بالكامل", Keys.Shift | Keys.NumPad8),
        new("print.recalc", ShortcutGroups.Print, "إعادة حساب الإجماليات", Keys.NumPad9),
        new("print.recalcAlt", ShortcutGroups.Print, "إعادة حساب الإجماليات (مفتاح بديل)", Keys.F5),
        new("print.editImage", ShortcutGroups.Print, "تعديل الصورة المحددة في Picture Manager", Keys.NumPad0),
        new("print.merge", ShortcutGroups.Print, "دمج الملفات المحددة في ملف PDF واحد", Keys.None),
        new("print.copyService", ShortcutGroups.Print, "فتح شاشة التصوير", Keys.None),
        new("print.reports", ShortcutGroups.Print, "السجل والملخصات", Keys.None),
        new("print.adminSettings", ShortcutGroups.Print, "إعدادات الإدارة", Keys.None),
        new("print.cancelPrint", ShortcutGroups.Print, "إلغاء الطباعة الجارية وحذف المهام", Keys.None),
        new("print.minimize", ShortcutGroups.Print, "تصغير شاشة الطباعة", Keys.None),
        new("print.logout", ShortcutGroups.Print, "تسجيل خروج", Keys.None),
        new("print.addCard", ShortcutGroups.Print, "إضافة بطاقة / تحويل الملفين المحددين لبطاقة", Keys.None),
        new("print.gridPaste", ShortcutGroups.Print, "لصق الملفات من الحافظة (داخل جدول الملفات)", Keys.Control | Keys.V, "grid"),

        // ----- المخزن -----
        new("inv.add", ShortcutGroups.Inventory, "إضافة صنف جديد", Keys.F2),
        new("inv.edit", ShortcutGroups.Inventory, "تعديل الصنف المحدد", Keys.F3),
        new("inv.search", ShortcutGroups.Inventory, "الانتقال لخانة البحث", Keys.F4),
        new("inv.refresh", ShortcutGroups.Inventory, "تحديث القائمة", Keys.F5),
        new("inv.rowEdit", ShortcutGroups.Inventory, "تعديل الصنف (وأنت واقف على الجدول)", Keys.Return, "grid"),
        new("inv.doSearch", ShortcutGroups.Inventory, "تنفيذ البحث (وأنت في خانة البحث)", Keys.Return, "search"),

        // ----- شاشة إتمام العملية -----
        new("op.extra", ShortcutGroups.Operation, "إضافة شيء آخر (مسطرة)", Keys.Space),

        // ----- سجل التدقيق -----
        new("audit.refresh", ShortcutGroups.Audit, "تحديث السجل", Keys.F5),
    };

    private static readonly Dictionary<string, ShortcutDef> ById = Defs.ToDictionary(d => d.Id);

    public static IReadOnlyList<ShortcutDef> All => Defs;

    public static ShortcutDef? Find(string id) => ById.TryGetValue(id, out var d) ? d : null;

    public static IEnumerable<ShortcutDef> InGroup(string group) => Defs.Where(d => d.Group == group);

    // ------------------------------------------------------------------
    // القراءة والمطابقة
    // ------------------------------------------------------------------

    /// <summary> الزر الحالي للاختصار (Keys.None = ملهوش اختصار). </summary>
    public static Keys Get(string id)
    {
        if (!ById.TryGetValue(id, out var def)) return Keys.None;
        lock (Gate)
        {
            overrides ??= LoadFromDisk();
            return overrides.TryGetValue(id, out int value) ? (Keys)value : def.Default;
        }
    }

    public static bool Is(string id, KeyEventArgs e) => Matches(id, e.KeyData);

    /// <summary> keyData = الزر + أي Ctrl/Alt/Shift (نفس شكل KeyEventArgs.KeyData و ProcessCmdKey). </summary>
    public static bool Matches(string id, Keys keyData)
    {
        Keys key = Get(id);
        return key != Keys.None && keyData == key;
    }

    /// <summary>
    /// أثناء الكتابة داخل خانة نص بنسمح بس بالاختصارات اللي فيها Ctrl أو Alt أو مفاتيح F1..F24،
    /// عشان زر عادي (رقم أو حرف) ما يتسرقش من الكتابة.
    /// </summary>
    public static bool SafeWhileTyping(Keys keyData)
    {
        if ((keyData & (Keys.Control | Keys.Alt)) != 0) return true;
        Keys code = keyData & Keys.KeyCode;
        return code >= Keys.F1 && code <= Keys.F24;
    }

    public static Dictionary<string, Keys> Snapshot()
        => Defs.ToDictionary(d => d.Id, d => Get(d.Id));

    public static Dictionary<string, Keys> DefaultMap()
        => Defs.ToDictionary(d => d.Id, d => d.Default);

    // ------------------------------------------------------------------
    // الحفظ
    // ------------------------------------------------------------------

    /// <summary> بنحفظ بس الاختصارات اللي اتغيرت عن الافتراضي. </summary>
    public static void SaveAll(IReadOnlyDictionary<string, Keys> map)
    {
        var diff = new Dictionary<string, int>();
        foreach (var def in Defs)
        {
            if (map.TryGetValue(def.Id, out Keys key) && key != def.Default)
                diff[def.Id] = (int)key;
        }

        Directory.CreateDirectory(DirectoryPath);
        string json = JsonSerializer.Serialize(diff, new JsonSerializerOptions { WriteIndented = true });
        string temp = FilePath + ".part";
        File.WriteAllText(temp, json);
        File.Move(temp, FilePath, true);
        lock (Gate) overrides = diff;
    }

    /// <summary> إعادة تحميل الاختصارات من القرص بعد استعادة نسخة احتياطية. </summary>
    public static void Reload()
    {
        lock (Gate) overrides = LoadFromDisk();
    }

    private static Dictionary<string, int> LoadFromDisk()
    {
        var result = new Dictionary<string, int>();
        try
        {
            if (!File.Exists(FilePath)) return result;
            var raw = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath));
            if (raw == null) return result;
            foreach (var (id, value) in raw)
                if (ById.ContainsKey(id)) result[id] = value;
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر قراءة shortcuts.json — هيتم استخدام الاختصارات الافتراضية", ex);
            result.Clear();
        }
        return result;
    }

    // ------------------------------------------------------------------
    // التعارض
    // ------------------------------------------------------------------

    /// <summary> الاختصارات التانية اللي بتستخدم نفس الزر في نفس النطاق (الشاشة نفسها أو «كل الشاشات»). </summary>
    public static List<ShortcutDef> FindConflicts(IReadOnlyDictionary<string, Keys> map, string id, Keys key)
    {
        var result = new List<ShortcutDef>();
        if (key == Keys.None || !ById.TryGetValue(id, out var me)) return result;

        foreach (var other in Defs)
        {
            if (other.Id == id) continue;
            if (!map.TryGetValue(other.Id, out Keys otherKey) || otherKey != key) continue;
            if (!OverlapScope(me, other)) continue;
            result.Add(other);
        }
        return result;
    }

    private static bool OverlapScope(ShortcutDef a, ShortcutDef b)
    {
        bool related = a.Group == b.Group
            || a.Group == ShortcutGroups.Global
            || b.Group == ShortcutGroups.Global;
        if (!related) return false;
        if (a.Context != null && b.Context != null && a.Context != b.Context) return false;
        return true;
    }

    // ------------------------------------------------------------------
    // العرض
    // ------------------------------------------------------------------

    public static string Describe(Keys keys)
    {
        if (keys == Keys.None) return "بدون اختصار";

        var parts = new List<string>();
        if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keys & Keys.Alt) != 0) parts.Add("Alt");
        if ((keys & Keys.Shift) != 0) parts.Add("Shift");
        parts.Add(KeyName(keys & Keys.KeyCode));
        return string.Join(" + ", parts);
    }

    public static string DescribeId(string id) => Describe(Get(id));

    private static string KeyName(Keys key)
    {
        if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            return "NumPad " + (key - Keys.NumPad0);
        if (key >= Keys.D0 && key <= Keys.D9)
            return (key - Keys.D0).ToString();

        return key switch
        {
            Keys.Decimal => "NumPad .",
            Keys.Add => "NumPad +",
            Keys.Subtract => "NumPad -",
            Keys.Multiply => "NumPad *",
            Keys.Divide => "NumPad /",
            Keys.Back => "Backspace",
            Keys.Escape => "Esc",
            Keys.Return => "Enter",
            Keys.Space => "Space",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Prior => "Page Up",
            Keys.Next => "Page Down",
            Keys.Up => "↑",
            Keys.Down => "↓",
            Keys.Left => "←",
            Keys.Right => "→",
            Keys.OemSemicolon => ";",
            Keys.Oemplus => "=",
            Keys.Oemcomma => ",",
            Keys.OemMinus => "-",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.Oemtilde => "`",
            Keys.OemOpenBrackets => "[",
            Keys.OemPipe => "\\",
            Keys.OemCloseBrackets => "]",
            Keys.OemQuotes => "'",
            _ => key.ToString()
        };
    }
}

/// <summary>
/// مربع تسجيل اختصار: تدوس عليه وبعدين تدوس الزر (أو التوليفة زي Ctrl+P) فيتسجل.
/// </summary>
internal sealed class KeyRecorderBox : TextBox
{
    private Keys current;

    /// <summary> بيتنفذ لما المستخدم يسجل ضغطة جديدة (مش لما الكود يغير القيمة). </summary>
    public event EventHandler? Recorded;

    public Keys Value
    {
        get => current;
        set => SetValueSilently(value);
    }

    public KeyRecorderBox()
    {
        ReadOnly = true;
        BackColor = Color.White;
        TextAlign = HorizontalAlignment.Center;
        RightToLeft = RightToLeft.No;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 11F);
        ShowValue();
    }

    public void SetValueSilently(Keys newValue)
    {
        current = newValue;
        ShowValue();
    }

    // true من أول ما المربع ياخد الفوكس لحد أول ضغطة مسجلة: بنعرض «اضغط الاختصار الجديد…».
    private bool prompting;

    private void ShowValue()
    {
        Text = prompting ? "اضغط الاختصار الجديد…" : Shortcuts.Describe(current);
        ForeColor = !prompting && current == Keys.None ? UiKit.Muted : UiKit.Text;
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        prompting = true;
        BackColor = Color.FromArgb(255, 249, 196);
        ShowValue();
        SelectionLength = 0;
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        prompting = false;
        BackColor = Color.White;
        ShowValue();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        SelectionLength = 0;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Focused) return base.ProcessCmdKey(ref msg, keyData);

        Keys code = keyData & Keys.KeyCode;

        // مفاتيح التعديل لوحدها (Ctrl/Shift/Alt/Win): ننتظر الزر الأساسي.
        if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu
            || code == Keys.LWin || code == Keys.RWin)
            return true;

        // Tab للتنقل بين الخانات، وAlt+F4 للإغلاق: نسيبهم يشتغلوا عادي.
        if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift) || keyData == (Keys.Alt | Keys.F4))
            return base.ProcessCmdKey(ref msg, keyData);

        current = keyData;
        prompting = false;
        ShowValue();
        SelectionLength = 0;

        // نأجل الحدث لبعد انتهاء معالجة الضغطة (فيه ممكن تظهر رسالة تأكيد).
        if (IsHandleCreated)
            BeginInvoke((Action)(() => Recorded?.Invoke(this, EventArgs.Empty)));
        return true;
    }
}

/// <summary> نافذة عرض الاختصارات (F1): اختصارات الشاشة الحالية ثم الاختصارات العامة. </summary>
internal sealed class ShortcutHelpForm : Form
{
    public ShortcutHelpForm(string groupKey)
    {
        Text = "اختصارات لوحة المفاتيح";
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10.5F);
        BackColor = UiKit.Background;
        ClientSize = new Size(640, 580);
        MinimumSize = new Size(520, 420);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        var close = UiKit.Button("إغلاق", Color.FromArgb(52, 73, 94), 120, 38);
        close.Dock = DockStyle.Bottom;
        close.Margin = new Padding(0);
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;

        var note = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            Padding = new Padding(14, 6, 14, 6),
            ForeColor = UiKit.Muted,
            TextAlign = ContentAlignment.MiddleRight,
            Text = "لازم NumLock يكون شغال عشان أزرار الأرقام الجانبية تشتغل. " +
                   "تقدر تغيّر أي اختصار من: إعدادات الإدارة ← ⌨️ الاختصارات."
        };

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 52,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = UiKit.Text,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = groupKey.Length > 0
                ? "⌨️ اختصارات " + ShortcutGroups.TitleOf(groupKey)
                : "⌨️ اختصارات لوحة المفاتيح"
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            RightToLeft = RightToLeft.Yes,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false,
            RowHeadersVisible = false,
            ColumnHeadersVisible = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = UiKit.Border,
            RowTemplate = { Height = 36 },
            EnableHeadersVisualStyles = false
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", Width = 190, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Action",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 234, 248);
        grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.Columns["Key"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.Columns["Key"]!.DefaultCellStyle.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        grid.Columns["Action"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        void AddSection(string sectionKey)
        {
            var items = Shortcuts.InGroup(sectionKey)
                .Where(d => Shortcuts.Get(d.Id) != Keys.None)
                .ToList();
            if (items.Count == 0) return;

            int h = grid.Rows.Add(string.Empty, ShortcutGroups.TitleOf(sectionKey));
            var headerRow = grid.Rows[h];
            headerRow.DefaultCellStyle.BackColor = Color.FromArgb(52, 73, 94);
            headerRow.DefaultCellStyle.ForeColor = Color.White;
            headerRow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(52, 73, 94);
            headerRow.DefaultCellStyle.SelectionForeColor = Color.White;
            headerRow.DefaultCellStyle.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);

            foreach (var d in items)
                grid.Rows.Add(Shortcuts.Describe(Shortcuts.Get(d.Id)), d.Title);
        }

        if (groupKey.Length > 0) AddSection(groupKey);
        AddSection(ShortcutGroups.Global);
        grid.ClearSelection();

        Controls.Add(grid);
        Controls.Add(title);
        Controls.Add(note);
        Controls.Add(close);

        // F1 تاني يقفل النافذة.
        KeyDown += (_, e) =>
        {
            if (Shortcuts.Is("global.help", e))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                Close();
            }
        };
    }

    public static void ShowFor(Form owner)
    {
        using var form = new ShortcutHelpForm(ShortcutGroups.ForForm(owner));
        form.ShowDialog(owner);
    }
}
