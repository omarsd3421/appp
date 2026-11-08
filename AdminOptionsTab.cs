using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// تبويب «القوائم والاختيارات» في إعدادات الإدارة: إضافة / تعديل / حذف / ترتيب عناصر
/// القوائم المنسدلة، وبيانات المكتبة اللي بتظهر في الفاتورة.
/// </summary>
internal sealed class OptionsEditorControl : UserControl
{
    private sealed class ListDef
    {
        public string Title = string.Empty;
        /// <summary> «OPay» أو «BEE» للقوائم اللي ليها فئات، وnull لباقي القوائم. </summary>
        public string? MachineKey;
        public string MachineName = string.Empty;
        public Func<AppOptionsData, List<string>> Get = null!;
        public Action<AppOptionsData, List<string>> Set = null!;
        public Func<List<string>> Defaults = null!;
    }

    private readonly AppOptionsData data = AppOptions.CloneForEdit();

    private readonly ListDef[] lists;

    // ----- محرر الفئات -----
    private readonly DataGridView grid = new();
    private readonly GroupBox tiersBox = new();
    private readonly List<Button> tierButtons = new();
    private string? gridKey;   // مفتاح الخدمة اللي الجدول بيعرض فئاتها دلوقتي (مثلاً «OPay|كارت شحن»)

    private readonly ComboBox listPicker = new();
    private readonly ListBox listBox = new();
    private readonly TextBox nameBox = new();
    private readonly TextBox shopName = new();
    private readonly TextBox shopPhones = new();
    private readonly TextBox shopAddress = new();

    public OptionsEditorControl()
    {
        // ملاحظة: من غير إيموجي في العناوين، لأن القايمة المنسدلة بترسمها بخط مفيهوش إيموجي (كانت بتطلع مربعات).
        lists = new[]
        {
            new ListDef { Title = "خدمات ماكينة OPay", MachineKey = "OPay", MachineName = "OPay",
                Get = d => d.MachineServicesOPay, Set = (d, v) => d.MachineServicesOPay = v, Defaults = AppOptions.DefaultMachineServices },
            new ListDef { Title = "خدمات ماكينة باب رزق", MachineKey = "BEE", MachineName = "باب رزق",
                Get = d => d.MachineServicesBee, Set = (d, v) => d.MachineServicesBee = v, Defaults = AppOptions.DefaultMachineServices },
            new ListDef { Title = "خدمات الدفع",
                Get = d => d.PaymentServices, Set = (d, v) => d.PaymentServices = v, Defaults = AppOptions.DefaultPaymentServices },
            new ListDef { Title = "تصنيفات حركة الخزينة (إضافة سيولة / سحب)",
                Get = d => d.TreasuryCategories, Set = (d, v) => d.TreasuryCategories = v, Defaults = AppOptions.DefaultTreasuryCategories },
        };
        BuildUi();
        listPicker.SelectedIndex = 0;
        ReloadList();
        UpdateTiersPanel();
    }

    private ListDef CurrentDef => lists[Math.Max(0, listPicker.SelectedIndex)];
    private List<string> Current => CurrentDef.Get(data);

    private void BuildUi()
    {
        Dock = DockStyle.Fill;
        BackColor = UiKit.Background;
        RightToLeft = RightToLeft.Yes;
        AutoScroll = true;

        // الصفحة بقت أطول (قوائم + فئات + بيانات المكتبة) فبتتحرك بالسكرول بدل ما تتضغط.
        const int headerH = 54, listsH = 430, tiersH = 340, shopH = 205, pad = 20;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 1, RowCount = 4, Padding = new Padding(pad),
            Height = headerH + listsH + tiersH + shopH + pad * 2 + 24,
            BackColor = UiKit.Background, RightToLeft = RightToLeft.Yes
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, headerH));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, listsH));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, tiersH));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, shopH));

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), ForeColor = Color.FromArgb(52, 73, 94),
            Text = "اختار القايمة، وزوّد / عدّل / امسح / رتّب عناصرها. وللماكينات: اختار الخدمة من القايمة وظبّط فئاتها وأسعارها من الجدول اللي تحت. بعدين دوس «حفظ». التغيير بيظهر في النوافذ اللي تتفتح بعد الحفظ، والعمليات القديمة ما بتتغيرش."
        }, 0, 0);

        // ----- محرر القوائم -----
        var listsBox = new GroupBox
        {
            Dock = DockStyle.Fill, Text = "القوائم المنسدلة", BackColor = Color.White,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold), Padding = new Padding(14, 24, 14, 12)
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, RightToLeft = RightToLeft.Yes, BackColor = Color.White
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White, RightToLeft = RightToLeft.Yes
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        listPicker.Dock = DockStyle.Fill;
        listPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        listPicker.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        listPicker.Margin = new Padding(2, 4, 2, 6);
        foreach (var l in lists) listPicker.Items.Add(l.Title);
        ComboBoxStyler.ApplyModernStyle(listPicker);
        listPicker.SelectedIndexChanged += (_, _) =>
        {
            CommitGrid();
            nameBox.Clear();
            ReloadList();
            UpdateTiersPanel();
        };
        left.Controls.Add(listPicker, 0, 0);

        listBox.Dock = DockStyle.Fill;
        listBox.Font = new Font("Segoe UI", 11.5F);
        listBox.IntegralHeight = false;
        listBox.ItemHeight = 28;
        listBox.DrawMode = DrawMode.OwnerDrawFixed;
        listBox.DrawItem += DrawListItem;
        listBox.SelectedIndexChanged += (_, _) =>
        {
            if (listBox.SelectedItem is string s) nameBox.Text = s;
            CommitGrid();          // نحفظ فئات الخدمة اللي كنا واقفين عليها
            UpdateTiersPanel();    // ونعرض فئات الخدمة الجديدة
        };
        left.Controls.Add(listBox, 0, 1);
        inner.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(14, 0, 0, 0),
            BackColor = Color.White, RightToLeft = RightToLeft.Yes
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        for (int i = 0; i < 6; i++) right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        right.Controls.Add(new Label
        {
            Text = "اسم الاختيار", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomRight,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
        }, 0, 0);

        nameBox.Dock = DockStyle.Fill;
        nameBox.Font = new Font("Segoe UI", 12F);
        nameBox.Margin = new Padding(0, 6, 0, 6);
        nameBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddItem(); }
        };
        right.Controls.Add(nameBox, 0, 1);

        AddButton(right, 2, "➕ إضافة كاختيار جديد", UiKit.Success, AddItem);
        AddButton(right, 3, "✏️ تعديل الاختيار المحدد", UiKit.Primary, EditItem);
        AddButton(right, 4, "🗑️ حذف الاختيار المحدد", UiKit.Danger, DeleteItem);
        AddButton(right, 5, "⬆ تحريك لفوق", Color.FromArgb(52, 73, 94), () => MoveItem(-1));
        AddButton(right, 6, "⬇ تحريك لتحت", Color.FromArgb(52, 73, 94), () => MoveItem(1));
        AddButton(right, 7, "↩ رجّع القايمة للافتراضي", Color.FromArgb(120, 128, 136), ResetList);
        right.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopRight, ForeColor = UiKit.Muted,
            Font = new Font("Segoe UI", 9.5F), Margin = new Padding(0, 8, 0, 0),
            Text = "ملاحظة: الاختيار اللي اسمه «أخرى» بيفتح خانة كتابة اسم العملية بإيدك."
        }, 0, 8);
        inner.Controls.Add(right, 1, 0);
        listsBox.Controls.Add(inner);
        root.Controls.Add(listsBox, 0, 1);

        // ----- الفئات والأسعار (للماكينات بس) -----
        BuildTiersBox();
        root.Controls.Add(tiersBox, 0, 2);

        // ----- بيانات المكتبة (الفاتورة) -----
        var shopBox = new GroupBox
        {
            Dock = DockStyle.Fill, Text = "بيانات المكتبة (بتظهر في الفاتورة)", BackColor = Color.White,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold), Padding = new Padding(14, 24, 14, 10), Margin = new Padding(0, 12, 0, 0)
        };
        var shopGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Color.White, RightToLeft = RightToLeft.Yes
        };
        shopGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        shopGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) shopGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        shopGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        shopName.Text = data.ShopName;
        shopPhones.Text = data.Phones;
        shopAddress.Text = data.Address;
        AddShopRow(shopGrid, 0, "اسم المكتبة", shopName);
        AddShopRow(shopGrid, 1, "أرقام الهاتف", shopPhones);
        AddShopRow(shopGrid, 2, "العنوان", shopAddress);

        var save = UiKit.Button("💾 حفظ كل التغييرات", UiKit.Success, 240, 44);
        save.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
        save.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        save.Margin = new Padding(0, 6, 0, 0);
        save.Click += (_, _) => SaveAll();
        shopGrid.Controls.Add(save, 1, 3);
        shopBox.Controls.Add(shopGrid);
        root.Controls.Add(shopBox, 0, 3);

        Controls.Add(root);
    }

    private static Button AddButton(TableLayoutPanel p, int row, string text, Color color, Action action)
    {
        var b = UiKit.Button(text, color, 100, 40);
        b.Dock = DockStyle.Fill;
        b.Margin = new Padding(0, 3, 0, 3);
        b.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        b.Click += (_, _) => action();
        p.Controls.Add(b, 0, row);
        return b;
    }

    // ======================= الفئات والأسعار =======================

    private void BuildTiersBox()
    {
        tiersBox.Dock = DockStyle.Fill;
        tiersBox.BackColor = Color.White;
        tiersBox.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        tiersBox.Padding = new Padding(14, 24, 14, 10);
        tiersBox.Margin = new Padding(0, 12, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.White, RightToLeft = RightToLeft.Yes
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var hint = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiKit.Muted,
            Font = new Font("Segoe UI", 9.5F),
            Text = "الفئة = الاسم اللي يظهر في الشاشة (مثلاً 25).  السعر = اللي بيخرج من الماكينة (22.5).  سعر البيع = اللي العميل بيدفعه (25).  " +
                   "العمولة بتتحسب لوحدها = سعر البيع − السعر. لو سبت سعر البيع فاضي يبقى زي السعر. الخدمة اللي ملهاش فئات مبتظهرلهاش خانة «الفئة»."
        };
        layout.Controls.Add(hint, 0, 0);
        layout.SetColumnSpan(hint, 2);

        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = true;
        grid.AllowUserToDeleteRows = true;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.RowHeadersVisible = false;
        grid.RightToLeft = RightToLeft.No; // نفس إصلاح UiKit.StyleGrid (مشكلة رسم الجداول مع RTL)
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Color.FromArgb(225, 230, 236);
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.MultiSelect = false;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = 40;
        grid.RowTemplate.Height = 34;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(52, 73, 94), ForeColor = Color.White,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold), Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White, ForeColor = UiKit.Text, Font = new Font("Segoe UI", 11.5F),
            SelectionBackColor = Color.FromArgb(214, 234, 248), SelectionForeColor = UiKit.Text,
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "label", HeaderText = "الفئة (اللي بتظهر)", FillWeight = 36, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "amount", HeaderText = "السعر (من الماكينة)", FillWeight = 32, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sell", HeaderText = "سعر البيع (للعميل)", FillWeight = 32, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        UiKit.ApplyRightToLeftGridLayout(grid);
        grid.CellValidating += Grid_CellValidating;
        grid.CellEndEdit += Grid_CellEndEdit;
        layout.Controls.Add(grid, 0, 1);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(14, 0, 0, 0),
            BackColor = Color.White, RightToLeft = RightToLeft.Yes
        };
        for (int i = 0; i < 5; i++) buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tierButtons.Add(AddButton(buttons, 0, "➕ إضافة فئة", UiKit.Success, AddTier));
        tierButtons.Add(AddButton(buttons, 1, "🗑️ حذف الفئة المحددة", UiKit.Danger, DeleteTier));
        tierButtons.Add(AddButton(buttons, 2, "⬆ تحريك لفوق", Color.FromArgb(52, 73, 94), () => MoveTier(-1)));
        tierButtons.Add(AddButton(buttons, 3, "⬇ تحريك لتحت", Color.FromArgb(52, 73, 94), () => MoveTier(1)));
        tierButtons.Add(AddButton(buttons, 4, "مسح كل فئات الخدمة", Color.FromArgb(120, 128, 136), ClearTiers));
        layout.Controls.Add(buttons, 1, 1);

        tiersBox.Controls.Add(layout);
    }

    private void SetTiersEnabled(bool enabled)
    {
        grid.Enabled = enabled;
        foreach (var b in tierButtons) b.Enabled = enabled;
    }

    /// <summary> يعرض فئات الخدمة المحددة في القايمة (لو القايمة دي ماكينة والخدمة مش «أخرى»). </summary>
    private void UpdateTiersPanel()
    {
        var def = CurrentDef;
        string? service = listBox.SelectedItem as string;

        if (def.MachineKey == null || service == null || service == AppOptions.OtherService)
        {
            gridKey = null;
            grid.CancelEdit();
            grid.Rows.Clear();
            SetTiersEnabled(false);
            tiersBox.Text = def.MachineKey == null
                ? "الفئات والأسعار (للماكينات بس — اختار قايمة خدمات OPay أو باب رزق)"
                : service == AppOptions.OtherService
                    ? "الفئات والأسعار — «أخرى» اسمها وسعرها بيتكتبوا بإيدك، مفيهاش فئات"
                    : "الفئات والأسعار — اختار خدمة من القايمة اللي فوق";
            return;
        }

        gridKey = AppOptions.TierKey(def.MachineKey, service);
        grid.CancelEdit();
        grid.Rows.Clear();
        if (data.PriceTiers.TryGetValue(gridKey, out var tiers))
        {
            foreach (var t in tiers)
                grid.Rows.Add(t.Label, AppOptions.FormatNumber(t.Amount), AppOptions.FormatNumber(t.SellPrice));
        }
        SetTiersEnabled(true);
        tiersBox.Text = $"فئات وأسعار «{service}» — ماكينة {def.MachineName}";
    }

    /// <summary> يحفظ اللي في الجدول جوه data (لسه مش على الديسك لحد ما تدوس «حفظ»). </summary>
    private void CommitGrid()
    {
        if (gridKey == null) return;
        if (!grid.EndEdit()) grid.CancelEdit();

        var tiers = new List<PriceTier>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            if (!TryParseNumber(CellText(row, 1), out decimal amount) || amount <= 0) continue; // صف من غير سعر = بيتتجاهل
            decimal sell = TryParseNumber(CellText(row, 2), out decimal s) && s > 0 ? s : amount;
            string label = CellText(row, 0).Trim();
            if (label.Length == 0) label = AppOptions.FormatNumber(sell);
            tiers.Add(new PriceTier { Label = label, Amount = amount, SellPrice = sell });
        }

        if (tiers.Count == 0) data.PriceTiers.Remove(gridKey);
        else data.PriceTiers[gridKey] = tiers;
    }

    private static string CellText(DataGridViewRow row, int column)
        => Convert.ToString(row.Cells[column].Value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static bool TryParseNumber(string text, out decimal value)
    {
        text = (text ?? string.Empty).Trim()
            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
            .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9')
            .Replace('٫', '.').Replace(',', '.').Replace('،', '.').Replace(" ", string.Empty);
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private void Grid_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex == 0) return;
        string text = Convert.ToString(e.FormattedValue)?.Trim() ?? string.Empty;
        if (text.Length == 0) return;
        if (!TryParseNumber(text, out decimal v) || v <= 0)
        {
            e.Cancel = true;
            grid.Rows[e.RowIndex].ErrorText = "اكتب رقم صحيح أكبر من صفر (مثال: 22.5) — أو دوس Esc للتراجع";
        }
    }

    private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = grid.Rows[e.RowIndex];
        row.ErrorText = string.Empty;

        if (e.ColumnIndex > 0 && TryParseNumber(CellText(row, e.ColumnIndex), out decimal v) && v > 0)
            row.Cells[e.ColumnIndex].Value = AppOptions.FormatNumber(v);

        if (TryParseNumber(CellText(row, 1), out decimal amount) && TryParseNumber(CellText(row, 2), out decimal sell) && sell < amount)
            row.ErrorText = "سعر البيع لازم يكون أكبر من أو يساوي السعر";
    }

    private void AddTier()
    {
        if (!grid.Enabled) return;
        if (!grid.EndEdit()) return;
        int i = grid.Rows.Add();
        grid.CurrentCell = grid.Rows[i].Cells[0];
        grid.Focus();
        grid.BeginEdit(true);
    }

    private void DeleteTier()
    {
        int i = grid.CurrentCell?.RowIndex ?? -1;
        if (i < 0 || grid.Rows[i].IsNewRow) { Warn("اختار الفئة اللي عايز تمسحها من الجدول الأول."); return; }
        grid.CancelEdit();
        grid.Rows.RemoveAt(i);
    }

    private void MoveTier(int delta)
    {
        if (!grid.EndEdit() || grid.CurrentCell == null) return;
        int i = grid.CurrentCell.RowIndex, col = grid.CurrentCell.ColumnIndex, t = i + delta;
        if (i < 0 || t < 0 || t >= grid.Rows.Count || grid.Rows[i].IsNewRow || grid.Rows[t].IsNewRow) return;
        for (int c = 0; c < grid.Columns.Count; c++)
            (grid.Rows[i].Cells[c].Value, grid.Rows[t].Cells[c].Value) = (grid.Rows[t].Cells[c].Value, grid.Rows[i].Cells[c].Value);
        grid.CurrentCell = grid.Rows[t].Cells[col];
    }

    private void ClearTiers()
    {
        if (grid.Rows.Count <= 1) return;
        if (MessageBox.Show(this, "تمسح كل فئات الخدمة دي؟ (الخانة هتختفي من شاشة الماكينة لحد ما تضيف فئات تاني)", "تأكيد",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != DialogResult.Yes) return;
        grid.CancelEdit();
        grid.Rows.Clear();
    }

    /// <summary> فحص صارم قبل الحفظ؛ بيرجّع رسالة الخطأ أو null لو كله تمام. </summary>
    private string? ValidateTiers()
    {
        foreach (var def in lists.Where(l => l.MachineKey != null))
        {
            foreach (var service in def.Get(data))
            {
                if (!data.PriceTiers.TryGetValue(AppOptions.TierKey(def.MachineKey!, service), out var tiers)) continue;
                foreach (var t in tiers)
                {
                    if (t.SellPrice < t.Amount)
                        return $"«{service}» (ماكينة {def.MachineName}): الفئة «{t.Label}» سعر بيعها ({AppOptions.FormatNumber(t.SellPrice)}) أقل من سعرها ({AppOptions.FormatNumber(t.Amount)}).";
                }
                var dup = tiers.GroupBy(t => t.Label, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
                if (dup != null)
                    return $"«{service}» (ماكينة {def.MachineName}): الفئة «{dup.Key}» مكررة. غيّر اسم واحدة منهم.";
            }
        }
        return null;
    }

    private static void AddShopRow(TableLayoutPanel grid, int row, string label, TextBox box)
    {
        grid.Controls.Add(new Label
        {
            Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
        }, 0, row);
        box.Dock = DockStyle.Fill;
        box.Font = new Font("Segoe UI", 11F);
        box.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(box, 1, row);
    }

    private void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var back = new SolidBrush(selected ? Color.FromArgb(0, 122, 204) : (e.Index % 2 == 0 ? Color.White : Color.FromArgb(248, 250, 252)));
        e.Graphics.FillRectangle(back, e.Bounds);
        string text = listBox.Items[e.Index]?.ToString() ?? string.Empty;
        TextRenderer.DrawText(e.Graphics, text, listBox.Font, e.Bounds, selected ? Color.White : UiKit.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.NoPrefix);
    }

    private void ReloadList(int? select = null)
    {
        listBox.Items.Clear();
        foreach (var item in Current) listBox.Items.Add(item);
        if (select is int i && i >= 0 && i < listBox.Items.Count) listBox.SelectedIndex = i;
    }

    private bool Exists(string text, int ignoreIndex = -1)
        => Current.Where((x, i) => i != ignoreIndex).Any(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase));

    private void AddItem()
    {
        string text = nameBox.Text.Trim();
        if (text.Length == 0) { Warn("اكتب اسم الاختيار الأول."); nameBox.Focus(); return; }
        if (Exists(text)) { Warn("الاختيار ده موجود بالفعل في القايمة."); return; }
        // «أخرى» تفضل آخر عنصر لو موجودة.
        int otherIndex = Current.FindIndex(x => x == "أخرى");
        if (otherIndex >= 0) Current.Insert(otherIndex, text); else Current.Add(text);
        ReloadList(Current.IndexOf(text));
    }

    private void EditItem()
    {
        int index = listBox.SelectedIndex;
        string text = nameBox.Text.Trim();
        if (index < 0) { Warn("اختار العنصر اللي عايز تعدله من القايمة الأول."); return; }
        if (text.Length == 0) { Warn("اكتب الاسم الجديد."); nameBox.Focus(); return; }
        if (Exists(text, index)) { Warn("الاسم ده موجود بالفعل في القايمة."); return; }

        // لو الخدمة ليها فئات، بننقلها للاسم الجديد.
        CommitGrid();
        string? machine = CurrentDef.MachineKey;
        if (machine != null)
        {
            string oldKey = AppOptions.TierKey(machine, Current[index]);
            if (data.PriceTiers.Remove(oldKey, out var moved) && text != AppOptions.OtherService)
                data.PriceTiers[AppOptions.TierKey(machine, text)] = moved;
        }
        gridKey = null;
        Current[index] = text;
        ReloadList(index);
    }

    private void DeleteItem()
    {
        int index = listBox.SelectedIndex;
        if (index < 0) { Warn("اختار العنصر اللي عايز تمسحه الأول."); return; }
        if (Current.Count <= 1) { Warn("لازم تفضل عنصر واحد على الأقل في القايمة."); return; }
        string name = Current[index];
        if (MessageBox.Show(this, $"مسح «{name}» من القايمة؟\n(العمليات القديمة المسجلة بيه ما بتتأثرش)", "تأكيد المسح",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != DialogResult.Yes) return;
        if (CurrentDef.MachineKey != null)
            data.PriceTiers.Remove(AppOptions.TierKey(CurrentDef.MachineKey, name));
        gridKey = null;
        Current.RemoveAt(index);
        nameBox.Clear();
        ReloadList(Math.Min(index, Current.Count - 1));
    }

    private void MoveItem(int delta)
    {
        int index = listBox.SelectedIndex;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Current.Count) return;
        (Current[index], Current[target]) = (Current[target], Current[index]);
        ReloadList(target);
    }

    private void ResetList()
    {
        if (MessageBox.Show(this, "ترجّع القايمة دي للاختيارات الأصلية؟ التعديلات اللي عملتها عليها هتتمسح.", "تأكيد",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != DialogResult.Yes) return;
        CommitGrid();
        gridKey = null;
        var def = lists[listPicker.SelectedIndex];
        def.Set(data, def.Defaults());
        nameBox.Clear();
        ReloadList();
        UpdateTiersPanel();
    }

    private void SaveAll()
    {
        if (string.IsNullOrWhiteSpace(shopName.Text)) { Warn("اسم المكتبة مينفعش يبقى فاضي."); shopName.Focus(); return; }
        if (grid.IsCurrentCellInEditMode && !grid.EndEdit())
        {
            Warn("في خانة في جدول الفئات فيها قيمة مش صح. صحّحها أو دوس Esc للتراجع، وبعدين احفظ.");
            return;
        }
        CommitGrid();
        string? tierError = ValidateTiers();
        if (tierError != null) { Warn(tierError); return; }
        try
        {
            data.ShopName = shopName.Text.Trim();
            data.Phones = shopPhones.Text.Trim();
            data.Address = shopAddress.Text.Trim();
            AppOptions.Save(data);
            MessageBox.Show(this, "تم الحفظ ✅\nالقوائم والفئات الجديدة هتظهر في أي نافذة ماكينة تفتحها من دلوقتي.", "تم الحفظ",
                MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر حفظ القوائم والاختيارات", ex);
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
}
