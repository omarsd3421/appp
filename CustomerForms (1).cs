using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

namespace LibraryPrintApp;

internal sealed class CustomersForm : Form
{
    private readonly string currentUser;
    private readonly SafeDataGridView grid = new();
    private readonly TextBox search = new();
    private readonly Label countLabel = new();
    private readonly Label totalDebtLabel = new();
    private readonly Label emptyState = new();
    private readonly ComboBox sortBox = new();
    private readonly CheckBox debtorsOnly = new();

    private sealed record CustomerRow(long Id, string Name, string Phone, decimal Debt, DateTime? LastPayment, int? DaysWithoutPayment);

    public CustomersForm(string userName)
    {
        currentUser = userName;
        BuildUi();
        LoadCustomers();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "العملاء والديون", 1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 650);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = UiKit.Background;
        Font = new Font("Segoe UI", 10.5f);
        // بدون شريط تمرير للشاشة كلها: الجدول وحده هو اللي بيتمرر
        AutoScroll = false;

        // جدول رئيسي واحد: الرأس والأدوات بحجم محتواهم (AutoSize) والجدول ياخد الباقي،
        // فمفيش ارتفاعات ثابتة بتتقص مع تكبير الـDPI.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(20), BackColor = UiKit.Background, RightToLeft = RightToLeft.Yes
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        // ---------- الرأس: العنوان + عدد العملاء يمين، وإجمالي الديون على شكل شارة يسار ----------
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true,
            BackColor = Color.White, Padding = new Padding(24, 16, 24, 16),
            Margin = new Padding(0, 0, 0, 14), RightToLeft = RightToLeft.Yes
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true,
            BackColor = Color.White, RightToLeft = RightToLeft.Yes, Margin = Padding.Empty
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = "👥 العملاء والديون", AutoSize = true, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = UiKit.Text
        };
        countLabel.AutoSize = true; countLabel.Dock = DockStyle.Fill;
        countLabel.TextAlign = ContentAlignment.MiddleLeft;
        countLabel.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        countLabel.ForeColor = UiKit.Muted;
        titleStack.Controls.Add(title, 0, 0);
        titleStack.Controls.Add(countLabel, 0, 1);

        totalDebtLabel.AutoSize = true;
        totalDebtLabel.Anchor = AnchorStyles.None;
        totalDebtLabel.TextAlign = ContentAlignment.MiddleCenter;
        totalDebtLabel.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        totalDebtLabel.Padding = new Padding(18, 10, 18, 10);
        totalDebtLabel.BackColor = Color.FromArgb(238, 242, 246);
        totalDebtLabel.ForeColor = UiKit.Muted;

        header.Controls.Add(titleStack, 0, 0);
        header.Controls.Add(totalDebtLabel, 1, 0);
        root.Controls.Add(header, 0, 0);

        // ---------- شريط الأدوات: بحث + أزرار في صف واحد ----------
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, AutoSize = true,
            BackColor = Color.White, Padding = new Padding(16, 14, 16, 14),
            Margin = new Padding(0, 0, 0, 10), RightToLeft = RightToLeft.Yes
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // مربع البحث: إطار رفيع بلون التطبيق + خانة نص عادية بدون حدّ داخلي
        var searchHost = new Panel
        {
            Dock = DockStyle.Fill, Margin = new Padding(4), BackColor = UiKit.Primary,
            Padding = new Padding(2), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        var searchInner = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 10, 14, 10),
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        search.Dock = DockStyle.Fill;
        search.Multiline = false;
        search.BorderStyle = BorderStyle.None;
        search.Font = new Font("Segoe UI", 13f);
        search.RightToLeft = RightToLeft.Yes;
        search.PlaceholderText = "🔍  ابحث بالاسم أو الرقم";
        searchInner.Controls.Add(search);
        searchHost.Controls.Add(searchInner);
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadCustomers(); } };
        search.TextChanged += (_, _) => LoadCustomers(); // بحث فوري أثناء الكتابة

        var find = UiKit.Button("🔍 بحث", UiKit.Primary, 100, 48);
        var addCustomer = UiKit.Button("➕ إضافة عميل", UiKit.Success, 145, 48);
        var oldDebt = UiKit.Button("📒 إضافة دين قديم", Color.FromArgb(230, 126, 34), 170, 48);
        var editCustomer = UiKit.Button("✏️ تعديل", Color.FromArgb(52, 120, 190), 100, 48);
        var deleteCustomer = UiKit.Button("🗑 حذف", Color.FromArgb(192, 57, 43), 100, 48);
        var refresh = UiKit.Button("↻ تحديث", Color.FromArgb(90, 98, 107), 100, 48);
        foreach (var b in new[] { find, addCustomer, oldDebt, editCustomer, deleteCustomer, refresh })
        {
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(4);
        }
        find.Click += (_, _) => LoadCustomers();
        addCustomer.Click += (_, _) => AddCustomer();
        oldDebt.Click += (_, _) => AddOldDebtToSelected();
        editCustomer.Click += (_, _) => EditSelectedCustomer();
        deleteCustomer.Click += (_, _) => DeleteSelectedCustomer();
        refresh.Click += (_, _) => LoadCustomers();

        toolbar.Controls.Add(searchHost, 0, 0);
        toolbar.Controls.Add(find, 1, 0);
        toolbar.Controls.Add(addCustomer, 2, 0);
        toolbar.Controls.Add(oldDebt, 3, 0);
        toolbar.Controls.Add(editCustomer, 4, 0);
        toolbar.Controls.Add(deleteCustomer, 5, 0);
        toolbar.Controls.Add(refresh, 6, 0);
        root.Controls.Add(toolbar, 0, 1);

        // ---------- صف الترتيب والفلتر ----------
        var filterBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, AutoSize = true,
            BackColor = Color.White, Padding = new Padding(16, 10, 16, 10),
            Margin = new Padding(0, 0, 0, 14), RightToLeft = RightToLeft.Yes
        };
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var sortLabel = new Label
        {
            Text = "الترتيب:", AutoSize = true, Anchor = AnchorStyles.None,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = UiKit.Text, Margin = new Padding(6, 0, 6, 0)
        };
        sortBox.DropDownStyle = ComboBoxStyle.DropDownList;
        sortBox.Dock = DockStyle.Fill;
        sortBox.Margin = new Padding(4);
        sortBox.RightToLeft = RightToLeft.Yes;
        sortBox.Font = new Font("Segoe UI", 11f);
        sortBox.Items.AddRange(new object[] { "بالاسم (أ ← ي)", "الأكثر دينًا أولًا", "الأقدم تأخرًا في السداد أولًا" });
        sortBox.SelectedIndex = 0;
        sortBox.SelectedIndexChanged += (_, _) => LoadCustomers();

        debtorsOnly.Text = "المدينين فقط";
        debtorsOnly.AutoSize = true;
        debtorsOnly.Anchor = AnchorStyles.None;
        debtorsOnly.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        debtorsOnly.Margin = new Padding(24, 0, 6, 0);
        debtorsOnly.RightToLeft = RightToLeft.Yes;
        debtorsOnly.CheckedChanged += (_, _) => LoadCustomers();

        filterBar.Controls.Add(sortLabel, 0, 0);
        filterBar.Controls.Add(sortBox, 1, 0);
        filterBar.Controls.Add(debtorsOnly, 2, 0);
        root.Controls.Add(filterBar, 0, 2);

        // ---------- الجدول ----------
        var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1), Margin = Padding.Empty };
        root.Controls.Add(gridHost, 0, 3);

        UiKit.StyleGrid(grid);
        grid.Dock = DockStyle.Fill; grid.RightToLeft = RightToLeft.No; grid.AutoGenerateColumns = false;
        grid.AllowUserToResizeRows = false; grid.AllowUserToResizeColumns = true; grid.MultiSelect = false;
        grid.ReadOnly = true; grid.RowHeadersVisible = false; grid.ColumnHeadersVisible = true;
        grid.ColumnHeadersHeight = 46; grid.RowTemplate.Height = 46;
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.Columns.Clear();

        var id = UiKit.GridText("Id", "#", 70, fill: true, minWidth: 55);
        var name = UiKit.GridText("Name", "الاسم", 360, fill: true, minWidth: 220);
        var phone = UiKit.GridText("Phone", "الرقم", 240, fill: true, minWidth: 160);
        var debt = UiKit.GridText("Debt", "الدين الحالي", 240, fill: true, minWidth: 130);
        var lastPay = UiKit.GridText("LastPay", "آخر دفعة", 160, fill: true, minWidth: 110);
        var days = UiKit.GridText("Days", "بدون سداد منذ", 150, fill: true, minWidth: 110);
        id.MinimumWidth = 45; name.MinimumWidth = 150; phone.MinimumWidth = 120;
        id.FillWeight = 6; name.FillWeight = 24; phone.FillWeight = 18; debt.FillWeight = 18; lastPay.FillWeight = 16; days.FillWeight = 18;
        debt.DefaultCellStyle.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        grid.Columns.Add(id); grid.Columns.Add(name); grid.Columns.Add(phone); grid.Columns.Add(debt);
        grid.Columns.Add(lastPay); grid.Columns.Add(days);
        // الجدول LTR داخليًا (تفاديًا لمشكلة رسم معروفة)، فنعكس ترتيب العرض ليبدأ # من اليمين
        // مع بقاء أرقام الأعمدة في الكود كما هي.
        days.DisplayIndex = 0; lastPay.DisplayIndex = 1; debt.DisplayIndex = 2; phone.DisplayIndex = 3; name.DisplayIndex = 4; id.DisplayIndex = 5;
        grid.CellDoubleClick += Grid_CellDoubleClick;

        emptyState.Dock = DockStyle.Fill; emptyState.TextAlign = ContentAlignment.MiddleCenter;
        emptyState.Font = new Font("Segoe UI", 13f, FontStyle.Bold); emptyState.ForeColor = Color.FromArgb(130, 138, 146);
        emptyState.BackColor = Color.White; emptyState.Text = "لا يوجد عملاء مطابقون للبحث."; emptyState.Visible = false;
        gridHost.Controls.Add(emptyState);
        gridHost.Controls.Add(grid);
    }

    private void LoadCustomers()
    {
        try
        {
            var list = new List<CustomerRow>();
            using (var c = AppDatabase.OpenConnection())
            {
                using var cmd = c.CreateCommand();
                var q = search.Text.Trim();
                // الأعمدة: الرقم، الاسم، التليفون، الدين الحالي، آخر دفعة، أول قيد دين (لحساب مدة التأخير)
                cmd.CommandText = @"
SELECT c.Id, c.Name, COALESCE(c.Phone,''),
       MAX(0,
         COALESCE((SELECT SUM(CASE WHEN d.Type='DEBT' THEN d.Amount ELSE -d.Amount END)
                   FROM Debts_History d WHERE d.CustomerId=c.Id),0)
         + COALESCE((SELECT SUM(COALESCE(t.RemainingAmount,0))
                     FROM Transactions t
                     WHERE t.CustomerId=c.Id
                       AND t.TransactionType IN ('SALE','COPY','SERVICE')
                       AND COALESCE(t.RemainingAmount,0)>0
                       AND NOT EXISTS (SELECT 1 FROM Debts_History d
                                       WHERE d.TransactionId=t.Id AND d.Type='DEBT')),0)
       ),
       (SELECT MAX(d.CreatedAt) FROM Debts_History d WHERE d.CustomerId=c.Id AND d.Type='PAYMENT'),
       (SELECT MIN(x.CreatedAt) FROM (
            SELECT d.CreatedAt AS CreatedAt FROM Debts_History d WHERE d.CustomerId=c.Id AND d.Type='DEBT'
            UNION ALL
            SELECT t.CreatedAt FROM Transactions t
            WHERE t.CustomerId=c.Id AND t.TransactionType IN ('SALE','COPY','SERVICE')
              AND COALESCE(t.RemainingAmount,0)>0
              AND NOT EXISTS (SELECT 1 FROM Debts_History d WHERE d.TransactionId=t.Id AND d.Type='DEBT')
       ) x)
FROM Customers c
WHERE ($q='' OR c.Name LIKE $like OR COALESCE(c.Phone,'') LIKE $like)
ORDER BY c.Name COLLATE NOCASE;";
                cmd.Parameters.AddWithValue("$q", q);
                cmd.Parameters.AddWithValue("$like", "%" + q + "%");

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    long id = r.GetInt64(0);
                    string name = r.IsDBNull(1) ? "" : r.GetString(1);
                    string phone = r.IsDBNull(2) ? "" : r.GetString(2);
                    decimal debt = r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3), CultureInfo.InvariantCulture);
                    DateTime? lastPay = ParseDate(r.IsDBNull(4) ? null : r.GetString(4));
                    DateTime? firstDebt = ParseDate(r.IsDBNull(5) ? null : r.GetString(5));
                    int? days = null;
                    if (debt > 0m)
                    {
                        DateTime? since = lastPay ?? firstDebt;
                        if (since.HasValue) days = Math.Max(0, (DateTime.Now.Date - since.Value.Date).Days);
                    }
                    list.Add(new CustomerRow(id, name, phone, debt, lastPay, days));
                }
            }

            decimal totalDebt = 0m;
            foreach (var row in list) totalDebt += row.Debt;
            int totalCustomers = list.Count;

            IEnumerable<CustomerRow> shown = list;
            if (debtorsOnly.Checked) shown = System.Linq.Enumerable.Where(shown, x => x.Debt > 0m);
            shown = sortBox.SelectedIndex switch
            {
                1 => System.Linq.Enumerable.ThenBy(
                        System.Linq.Enumerable.OrderByDescending(shown, x => x.Debt),
                        x => x.Name, StringComparer.CurrentCultureIgnoreCase),
                2 => System.Linq.Enumerable.ThenByDescending(
                        System.Linq.Enumerable.OrderByDescending(shown, x => x.Debt > 0m),
                        x => x.DaysWithoutPayment ?? -1),
                _ => shown
            };

            grid.Rows.Clear();
            int count = 0;
            foreach (var row in shown)
            {
                count++;
                int idx = grid.Rows.Add();
                grid.Rows[idx].Cells[0].Value = row.Id.ToString(CultureInfo.InvariantCulture);
                grid.Rows[idx].Cells[1].Value = string.IsNullOrWhiteSpace(row.Name) ? "بدون اسم" : row.Name;
                grid.Rows[idx].Cells[2].Value = string.IsNullOrWhiteSpace(row.Phone) ? "غير مسجل" : row.Phone;
                grid.Rows[idx].Cells[3].Value = row.Debt > 0m ? FormatMoney(row.Debt) : "لا يوجد دين";
                grid.Rows[idx].Cells[4].Value = row.LastPayment.HasValue
                    ? row.LastPayment.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "—";
                grid.Rows[idx].Cells[5].Value = row.DaysWithoutPayment.HasValue
                    ? DaysText(row.DaysWithoutPayment.Value) : "—";

                if (row.Debt > 0m)
                {
                    grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 238);
                    grid.Rows[idx].Cells[3].Style.ForeColor = Color.FromArgb(192, 57, 43);
                    if (row.DaysWithoutPayment >= 60)
                    {
                        grid.Rows[idx].Cells[5].Style.ForeColor = Color.FromArgb(192, 57, 43);
                        grid.Rows[idx].Cells[5].Style.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
                    }
                    else if (row.DaysWithoutPayment >= 30)
                    {
                        grid.Rows[idx].Cells[5].Style.ForeColor = Color.FromArgb(200, 120, 0);
                        grid.Rows[idx].Cells[5].Style.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
                    }
                }
                else
                {
                    grid.Rows[idx].Cells[3].Style.ForeColor = Color.FromArgb(39, 174, 96);
                }
            }

            countLabel.Text = count == totalCustomers
                ? $"عدد العملاء: {totalCustomers}"
                : $"المعروض: {count} من {totalCustomers} عميل";
            totalDebtLabel.Text = totalDebt > 0m
                ? $"إجمالي الديون الحالية: {FormatMoney(totalDebt)}"
                : "لا توجد ديون حالية على العملاء";
            totalDebtLabel.ForeColor = totalDebt > 0m ? Color.FromArgb(192, 57, 43) : Color.FromArgb(39, 120, 70);
            totalDebtLabel.BackColor = totalDebt > 0m ? Color.FromArgb(253, 236, 234) : Color.FromArgb(232, 245, 233);
            emptyState.Visible = count == 0;
            if (count == 0) emptyState.BringToFront();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static DateTime? ParseDate(string? text)
        => !string.IsNullOrWhiteSpace(text) &&
           DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d : null;

    private static string DaysText(int days) => days switch
    {
        0 => "النهارده",
        1 => "يوم",
        2 => "يومين",
        >= 3 and <= 10 => $"{days} أيام",
        _ => $"{days} يوم"
    };

    private bool TryGetSelected(out long id, out string name)
    {
        id = 0; name = "";
        if (grid.CurrentRow == null ||
            !long.TryParse(Convert.ToString(grid.CurrentRow.Cells[0].Value, CultureInfo.InvariantCulture), out id))
            return false;
        name = Convert.ToString(grid.CurrentRow.Cells[1].Value, CultureInfo.InvariantCulture) ?? "";
        return true;
    }

    private void EditSelectedCustomer()
    {
        if (!TryGetSelected(out long id, out _))
        {
            MessageBox.Show(this, "اختار العميل من الجدول الأول.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return;
        }
        try
        {
            string curName = "", curPhone = "";
            using (var c = AppDatabase.OpenConnection())
            using (var q = c.CreateCommand())
            {
                q.CommandText = "SELECT Name,COALESCE(Phone,'') FROM Customers WHERE Id=$id";
                q.Parameters.AddWithValue("$id", id);
                using var r = q.ExecuteReader();
                if (!r.Read()) return;
                curName = r.IsDBNull(0) ? "" : r.GetString(0);
                curPhone = r.GetString(1);
            }

            using var dlg = new CustomerEditorForm("تعديل بيانات العميل", curName, curPhone);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            CustomerService.Update(currentUser, id, dlg.CustomerName, dlg.Phone);
            LoadCustomers();
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل تعديل بيانات عميل", ex);
            MessageBox.Show(this, ex.Message, "خطأ في التعديل", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelectedCustomer()
    {
        if (!TryGetSelected(out long id, out string name))
        {
            MessageBox.Show(this, "اختار العميل من الجدول الأول.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return;
        }
        var ask = MessageBox.Show(this,
            $"متأكد إنك عايز تحذف العميل «{name}»؟\n\n(الحذف مسموح بس للعميل اللي معندوش أي حركات أو ديون)",
            "تأكيد الحذف", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        if (ask != DialogResult.Yes) return;
        try
        {
            CustomerService.Delete(currentUser, id);
            LoadCustomers();
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل حذف عميل", ex);
            MessageBox.Show(this, ex.Message, "تعذر الحذف", MessageBoxButtons.OK, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
    }

    private void AddCustomer()
    {
        using var dlg = new CustomerEditorForm();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        long customerId;
        try
        {
            using var c = AppDatabase.OpenConnection();
            customerId = AppDatabase.GetOrCreateCustomer(c, dlg.CustomerName, dlg.Phone);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ في إضافة العميل", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        LoadCustomers();

        // بعد إضافة العميل نسأل: هل عليه دين قديم من قبل البرنامج؟
        var ask = MessageBox.Show(this,
            $"تم حفظ العميل «{dlg.CustomerName}».\n\nهل على العميل دين قديم من قبل استخدام البرنامج؟",
            "دين قديم", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        if (ask == DialogResult.Yes)
            RegisterOldDebt(customerId, dlg.CustomerName);
    }

    // زر "إضافة دين قديم": يشتغل على العميل المحدد في الجدول
    private void AddOldDebtToSelected()
    {
        if (grid.CurrentRow == null ||
            !long.TryParse(Convert.ToString(grid.CurrentRow.Cells[0].Value, CultureInfo.InvariantCulture), out var id))
        {
            MessageBox.Show(this, "اختار العميل من الجدول الأول، أو اضغط «إضافة عميل» لو عميل جديد.",
                "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return;
        }
        string name = Convert.ToString(grid.CurrentRow.Cells[1].Value, CultureInfo.InvariantCulture) ?? "";
        RegisterOldDebt(id, name);
    }

    private void RegisterOldDebt(long customerId, string customerName)
    {
        try
        {
            decimal current;
            using (var c = AppDatabase.OpenConnection())
                current = AppDatabase.GetCustomerDebt(c, customerId);

            using var dlg = new OldDebtForm(customerName, current);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            SalesService.AddOpeningDebt(currentUser, customerId, dlg.Amount, dlg.Note);
            LoadCustomers();
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل تسجيل دين قديم على عميل", ex);
            MessageBox.Show(this, ex.Message, "خطأ في تسجيل الدين القديم", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (!long.TryParse(Convert.ToString(grid.Rows[e.RowIndex].Cells[0].Value, CultureInfo.InvariantCulture), out var id)) return;
        using var details = new CustomerDetailsForm(currentUser, id);
        details.ShowDialog(this);
        LoadCustomers();
    }

    internal static string FormatMoney(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture) + " ج.م";
}

internal sealed class CustomerEditorForm : Form
{
    private readonly TextBox nameBox = new();
    private readonly TextBox phoneBox = new();
    public string CustomerName => nameBox.Text.Trim();
    public string? Phone => string.IsNullOrWhiteSpace(phoneBox.Text) ? null : phoneBox.Text.Trim();

    public CustomerEditorForm(string title = "إضافة عميل", string initialName = "", string initialPhone = "")
    {
        UiKit.PrepareForm(this, title, 520, 320);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(500, 320);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 10.5f);
        UiKit.EnableEscape(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4,
            Padding = new Padding(24), BackColor = Color.White, RightToLeft = RightToLeft.Yes
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 21));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 21));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        Controls.Add(root);

        root.Controls.Add(FieldLabel("الاسم"), 0, 0);
        nameBox.Dock = DockStyle.Fill; nameBox.Margin = new Padding(3); nameBox.Font = new Font("Segoe UI", 11f); nameBox.TextAlign = HorizontalAlignment.Right;
        root.Controls.Add(nameBox, 1, 0);
        root.Controls.Add(FieldLabel("الرقم"), 0, 1);
        phoneBox.Dock = DockStyle.Fill; phoneBox.Margin = new Padding(3); phoneBox.Font = new Font("Segoe UI", 11f); phoneBox.TextAlign = HorizontalAlignment.Right;
        root.Controls.Add(phoneBox, 1, 1);

        var hint = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiKit.Muted,
            Text = "اضغط مرتين على العميل في الجدول لفتح كشف الحساب.", Padding = new Padding(4) };
        root.Controls.Add(hint, 0, 2); root.SetColumnSpan(hint, 2);

        var buttons = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, RightToLeft = RightToLeft.Yes, Margin = new Padding(0, 3, 0, 3) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        var save = UiKit.Button("✅ حفظ", UiKit.Success, 120, 42);
        var cancel = UiKit.Button("إلغاء", Color.FromArgb(110,118,126), 100, 42);
        save.Dock = DockStyle.Fill; cancel.Dock = DockStyle.Fill;
        save.Margin = new Padding(3); cancel.Margin = new Padding(3);
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(save, 0, 0); buttons.Controls.Add(cancel, 1, 0);
        root.Controls.Add(new Label(), 0, 3); root.Controls.Add(buttons, 1, 3);
        AcceptButton = save; CancelButton = cancel;
        nameBox.Text = initialName;
        phoneBox.Text = initialPhone;
        Shown += (_, _) => nameBox.Focus();
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            MessageBox.Show(this, "اسم العميل إجباري.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            nameBox.Focus(); return;
        }
        DialogResult = DialogResult.OK; Close();
    }

    private static Label FieldLabel(string text) => new()
    {
        Dock = DockStyle.Fill, Text = text, Font = new Font("Segoe UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight
    };
}

internal sealed class CustomerDetailsForm : Form
{
    private readonly string currentUser;
    private readonly long customerId;
    private readonly Label nameValue = new();
    private readonly Label phoneValue = new();
    private readonly Label debtValue = new();
    private readonly SafeDataGridView historyGrid = new();
    private Button payButton = null!;
    private bool isRegisteringPayment;
    private readonly List<StatementLine> statementLines = new();
    private string statementName = "";
    private string statementPhone = "";
    private decimal statementDebt;

    public CustomerDetailsForm(string userName, long id)
    {
        currentUser = userName;
        customerId = id;
        BuildUi();
        LoadData();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "كشف حساب العميل", 1100, 720);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 600);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 10.5f);
        UiKit.EnableEscape(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(245, 247, 250),
            RightToLeft = RightToLeft.Yes
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var titlePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 10, 18, 10) };
        var title = UiKit.Title("👤 كشف حساب العميل", 19);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleRight;
        titlePanel.Controls.Add(title);
        root.Controls.Add(titlePanel, 0, 0);

        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(12, 10, 12, 10),
            RightToLeft = RightToLeft.Yes
        };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13.75f));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13.75f));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13.75f));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13.75f));
        root.Controls.Add(info, 0, 1);

        info.Controls.Add(Card("الاسم", nameValue), 0, 0);
        info.Controls.Add(Card("الرقم", phoneValue), 1, 0);
        info.Controls.Add(Card("الدين الحالي", debtValue), 2, 0);

        payButton = UiKit.Button("💰 تسجيل دفعة", UiKit.Success, 150, 45);
        payButton.Dock = DockStyle.Fill;
        payButton.Margin = new Padding(6);
        payButton.Click += (_, _) => RegisterPayment();
        info.Controls.Add(payButton, 3, 0);

        // كشف الحساب: طباعة (معاينة) أو حفظ PDF
        var statementButton = UiKit.Button("🖨 كشف حساب", Color.FromArgb(52, 120, 190), 130, 45);
        statementButton.Dock = DockStyle.Fill;
        statementButton.Margin = new Padding(6);
        var statementMenu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, Font = new Font("Segoe UI", 11f) };
        statementMenu.Items.Add("🖨 معاينة وطباعة", null, (_, _) => PrintStatement());
        statementMenu.Items.Add("📄 حفظ PDF", null, (_, _) => SaveStatementPdf());
        statementButton.Click += (_, _) => statementMenu.Show(statementButton, new Point(0, statementButton.Height));
        info.Controls.Add(statementButton, 4, 0);

        // تصحيح دين قديم اتسجل بالغلط: اختار سطره في كشف الحساب ثم اضغط الزر
        var fixOldDebt = UiKit.Button("🛠 تصحيح دين قديم", Color.FromArgb(230, 126, 34), 130, 45);
        fixOldDebt.Dock = DockStyle.Fill;
        fixOldDebt.Margin = new Padding(6);
        fixOldDebt.Click += (_, _) => FixSelectedOldDebt();
        info.Controls.Add(fixOldDebt, 5, 0);

        var whatsAppButton = UiKit.Button("💬 تذكير واتساب", Color.FromArgb(37, 145, 83), 130, 45);
        whatsAppButton.Dock = DockStyle.Fill;
        whatsAppButton.Margin = new Padding(6);
        whatsAppButton.Click += (_, _) => OpenWhatsAppDebtReminder();
        info.Controls.Add(whatsAppButton, 6, 0);

        UiKit.StyleGrid(historyGrid);
        historyGrid.Dock = DockStyle.Fill;
        historyGrid.AutoGenerateColumns = false;
        // نضبط AutoSizeMode=Fill لكل عمود أسفل الجدول حتى يتوزع كشف الحساب
        // على عرض النافذة، مع حدود دنيا تتيح التمرير عند ضيق الشاشة.
        historyGrid.AllowUserToAddRows = false;
        historyGrid.AllowUserToDeleteRows = false;
        historyGrid.AllowUserToResizeRows = false;
        historyGrid.AllowUserToResizeColumns = true;
        historyGrid.ReadOnly = true;
        historyGrid.RowHeadersVisible = false;
        historyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        historyGrid.ColumnHeadersHeight = 46;
        historyGrid.RowTemplate.Height = 42;
        // اجعل كشف الحساب متجاوبًا كذلك: جميع الأعمدة تتقاسم العرض، ويكبر عمود
        // البيان نسبيًا. عند تصغير النافذة لأقل من الحدود الدنيا سيظل التمرير متاحًا.
        var dateCol = UiKit.GridText("Date", "التاريخ", 160, fill: true, minWidth: 125);
        var typeCol = UiKit.GridText("Type", "النوع", 140, fill: true, minWidth: 105);
        var amountCol = UiKit.GridText("Amount", "المبلغ", 140, fill: true, minWidth: 105);
        var balanceCol = UiKit.GridText("Balance", "الرصيد بعد الحركة", 165, fill: true, minWidth: 125);
        var userCol = UiKit.GridText("User", "بواسطة", 120, fill: true, minWidth: 90);
        var noteCol = UiKit.GridText("Note", "البيان", 320, fill: true, minWidth: 180);
        dateCol.FillWeight = 16;
        typeCol.FillWeight = 14;
        amountCol.FillWeight = 14;
        balanceCol.FillWeight = 16;
        userCol.FillWeight = 12;
        noteCol.FillWeight = 28;
        historyGrid.Columns.AddRange(new DataGridViewColumn[] { dateCol, typeCol, amountCol, balanceCol, userCol, noteCol });
        foreach (DataGridViewColumn col in historyGrid.Columns)
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        root.Controls.Add(historyGrid, 0, 2);
    }

    private static Control Card(string title, Label value)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(6), BackColor = Color.FromArgb(248, 249, 251), Padding = new Padding(10, 5, 10, 5) };
        var caption = new Label { Dock = DockStyle.Top, Height = 24, Text = title, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleRight;
        value.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        value.Text = "-";
        panel.Controls.Add(value);
        panel.Controls.Add(caption);
        return panel;
    }

    private void LoadData()
    {
        try
        {
            using var c = AppDatabase.OpenConnection();
            using (var q = c.CreateCommand())
            {
                q.CommandText = "SELECT Name,COALESCE(Phone,'') FROM Customers WHERE Id=$id";
                q.Parameters.AddWithValue("$id", customerId);
                using var r = q.ExecuteReader();
                if (!r.Read())
                {
                    MessageBox.Show(this, "العميل غير موجود.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Close();
                    return;
                }

                string name = r.IsDBNull(0) ? "" : r.GetString(0);
                string phone = r.IsDBNull(1) ? "" : r.GetString(1);
                Text = "كشف حساب: " + name;
                nameValue.Text = string.IsNullOrWhiteSpace(name) ? "بدون اسم" : name;
                phoneValue.Text = string.IsNullOrWhiteSpace(phone) ? "غير مسجل" : phone;
                statementName = name;
                statementPhone = phone;
            }

            decimal currentDebt = AppDatabase.GetCustomerDebt(c, customerId);
            statementDebt = currentDebt;
            statementLines.Clear();
            debtValue.Text = CustomersForm.FormatMoney(currentDebt);
            debtValue.ForeColor = currentDebt > 0m ? Color.FromArgb(192, 57, 43) : Color.FromArgb(39, 174, 96);

            historyGrid.Rows.Clear();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT CreatedAt,Type,Amount,BalanceAfter,UserName,Note,DebtId
FROM (
    SELECT d.CreatedAt,d.Type,d.Amount,d.BalanceAfter,
           COALESCE(u.UserName,'') AS UserName,COALESCE(d.Note,'') AS Note,
           d.Id AS DebtId
    FROM Debts_History d
    LEFT JOIN AppUsers u ON u.Id=d.UserId
    WHERE d.CustomerId=$id

    UNION ALL

    SELECT t.CreatedAt,'DEBT' AS Type,COALESCE(t.RemainingAmount,0) AS Amount,
           COALESCE((SELECT SUM(CASE WHEN dh.Type='DEBT' THEN dh.Amount ELSE -dh.Amount END)
                     FROM Debts_History dh
                     WHERE dh.CustomerId=t.CustomerId AND dh.CreatedAt<=t.CreatedAt),0)
             + COALESCE((SELECT SUM(COALESCE(old.RemainingAmount,0))
                         FROM Transactions old
                         WHERE old.CustomerId=t.CustomerId
                           AND old.TransactionType IN ('SALE','COPY','SERVICE')
                           AND COALESCE(old.RemainingAmount,0)>0
                           AND old.CreatedAt<=t.CreatedAt
                           AND NOT EXISTS (SELECT 1 FROM Debts_History oldDebt
                                           WHERE oldDebt.TransactionId=old.Id AND oldDebt.Type='DEBT')),0)
             AS BalanceAfter,
           COALESCE(u.UserName,'') AS UserName,
           'دين قديم مستعاد من سجل العملية: ' || COALESCE(t.Description,'') AS Note,
           NULL AS DebtId
    FROM Transactions t
    LEFT JOIN AppUsers u ON u.Id=t.UserId
    WHERE t.CustomerId=$id
      AND t.TransactionType IN ('SALE','COPY','SERVICE')
      AND COALESCE(t.RemainingAmount,0)>0
      AND NOT EXISTS (SELECT 1 FROM Debts_History dh
                      WHERE dh.TransactionId=t.Id AND dh.Type='DEBT')
)
ORDER BY CreatedAt DESC;";
            cmd.Parameters.AddWithValue("$id", customerId);
            using var rr = cmd.ExecuteReader();
            while (rr.Read())
            {
                DateTime dt = DateTime.TryParse(rr.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.Now;
                string type = rr.GetString(1) == "DEBT" ? "إضافة دين" : "دفعة / سداد";
                decimal amount = Convert.ToDecimal(rr.GetValue(2), CultureInfo.InvariantCulture);
                decimal balance = Convert.ToDecimal(rr.GetValue(3), CultureInfo.InvariantCulture);
                int row = historyGrid.Rows.Add(
                    dt.ToString("yyyy-MM-dd HH:mm"),
                    type,
                    CustomersForm.FormatMoney(amount),
                    CustomersForm.FormatMoney(balance),
                    rr.GetString(4),
                    rr.GetString(5));
                historyGrid.Rows[row].Tag = rr.IsDBNull(6) ? null : (long?)rr.GetInt64(6);
                statementLines.Add(new StatementLine(dt, type, amount, balance, rr.GetString(5)));
                historyGrid.Rows[row].DefaultCellStyle.BackColor = rr.GetString(1) == "PAYMENT"
                    ? Color.FromArgb(239, 251, 244)
                    : Color.FromArgb(255, 248, 238);
            }
            statementLines.Reverse(); // الكشف المطبوع: من الأقدم للأحدث
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenWhatsAppDebtReminder()
    {
        try
        {
            if (statementDebt <= 0)
            {
                MessageBox.Show(this, "لا يوجد رصيد مستحق على هذا العميل.", "تذكير واتساب", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string rawPhone = (statementPhone ?? string.Empty).Trim();
            // الأرقام التي تبدأ بـ + أو 00 تعتبر دولية صراحةً؛ غير ذلك يُعامل كرقم محلي مصري.
            // لا نضيف كود مصر إلى رقم دولي كتبه المستخدم بالفعل.
            bool explicitlyInternational = rawPhone.StartsWith("+", StringComparison.Ordinal)
                || rawPhone.StartsWith("00", StringComparison.Ordinal);
            string digits = "";
            foreach (char ch in rawPhone)
                if (char.IsDigit(ch)) digits += ch;
            if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits.Substring(2);
            if (!explicitlyInternational)
            {
                if (digits.StartsWith("0", StringComparison.Ordinal)) digits = "20" + digits.Substring(1);
                else if (!digits.StartsWith("20", StringComparison.Ordinal)) digits = "20" + digits;
            }
            if (digits.Length < 10 || digits.Length > 15)
            {
                MessageBox.Show(this, "رقم الهاتف غير مكتمل أو غير صالح. عدّل رقم العميل مع كتابة كود الدولة قبل فتح واتساب.", "رقم الهاتف", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string message = $"أهلًا {statementName}، نذكّرك بوجود رصيد مستحق قدره {statementDebt:N2} جنيه. نرجو التواصل معنا لتسوية الحساب. شكرًا لحضرتك.";
            string url = "https://wa.me/" + digits + "?text=" + Uri.EscapeDataString(message);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر فتح واتساب. تأكد من وجود متصفح أو تطبيق يدعم الروابط.\n\n" + ex.Message, "واتساب", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PrintStatement()
    {
        try
        {
            using var doc = StatementRenderer.CreateDocument(statementName, statementPhone, statementDebt, statementLines);
            using var preview = new PrintPreviewDialog
            {
                Document = doc,
                UseAntiAlias = true,
                WindowState = FormWindowState.Maximized,
                Text = "معاينة كشف الحساب"
            };
            preview.ShowDialog(this);
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل طباعة كشف حساب العميل", ex);
            MessageBox.Show(this, ex.Message, "خطأ في الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveStatementPdf()
    {
        try
        {
            string safeName = string.Concat((string.IsNullOrWhiteSpace(statementName) ? "عميل" : statementName.Trim())
                .Split(System.IO.Path.GetInvalidFileNameChars()));
            using var dlg = new SaveFileDialog
            {
                Title = "حفظ كشف الحساب PDF",
                Filter = "ملف PDF (*.pdf)|*.pdf",
                FileName = $"كشف حساب {safeName} {DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            StatementRenderer.SavePdf(statementName, statementPhone, statementDebt, statementLines, dlg.FileName);
            MessageBox.Show(this, "تم حفظ كشف الحساب.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل حفظ كشف الحساب PDF", ex);
            MessageBox.Show(this, ex.Message, "خطأ في حفظ PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void FixSelectedOldDebt()
    {
        var row = historyGrid.CurrentRow;
        string note = row == null ? "" : Convert.ToString(row.Cells[5].Value, CultureInfo.InvariantCulture) ?? "";
        if (row == null || row.Tag is not long debtId || !SalesService.IsOpeningDebtNote(note))
        {
            MessageBox.Show(this,
                "اختار من كشف الحساب سطر «دين قديم» الأول (الملاحظة بتاعته: دين قديم قبل استخدام البرنامج)، وبعدين اضغط الزر.",
                "تصحيح دين قديم", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return;
        }

        try
        {
            string amountText = Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture) ?? "0";
            decimal oldAmount = decimal.TryParse(
                amountText.Replace("ج.م", "").Replace(",", "").Trim(),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

            using var dlg = new OldDebtForm(statementName, statementDebt, oldAmount, SalesService.StripOpeningDebtPrefix(note));
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            if (dlg.DeleteRequested)
                SalesService.DeleteOpeningDebt(currentUser, debtId);
            else
                SalesService.UpdateOpeningDebt(currentUser, debtId, dlg.Amount, dlg.Note);
            LoadData();
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل تصحيح دين قديم", ex);
            MessageBox.Show(this, ex.Message, "تعذر التصحيح", MessageBoxButtons.OK, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
    }

    private void RegisterPayment()
    {
        if (isRegisteringPayment) return;
        isRegisteringPayment = true;
        payButton.Enabled = false;
        try
        {
            decimal current;
            using (var c = AppDatabase.OpenConnection())
                current = AppDatabase.GetCustomerDebt(c, customerId);

            if (current <= 0m)
            {
                MessageBox.Show(this, "لا يوجد دين حالي على العميل.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new DebtPaymentForm(current);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            SalesService.PayDebt(currentUser, customerId, dlg.Amount, dlg.Note);
            LoadData();
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل تسجيل دفعة دين من كشف حساب العميل", ex);
            MessageBox.Show(this, ex.Message, "خطأ في تسجيل الدفعة", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            isRegisteringPayment = false;
            if (!IsDisposed && !payButton.IsDisposed) payButton.Enabled = true;
        }
    }
}

internal sealed class DebtPaymentForm : Form
{
    private readonly decimal maximum;
    private readonly TextBox amountBox = new();
    private readonly TextBox noteBox = new();
    private readonly Label remainingLabel = new();

    public decimal Amount { get; private set; }
    public string Note => noteBox.Text.Trim();

    public DebtPaymentForm(decimal max)
    {
        maximum = max;
        BuildUi();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "تسجيل دفعة", 520, 360);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(500, 340);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 10.5f);
        UiKit.EnableEscape(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(30, 25, 30, 20),
            RightToLeft = RightToLeft.Yes
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 16));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 19));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 19));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
        Controls.Add(root);

        root.Controls.Add(FieldLabel("الدين الحالي"), 0, 0);
        root.Controls.Add(ValueLabel(CustomersForm.FormatMoney(maximum)), 1, 0);

        root.Controls.Add(FieldLabel("مبلغ الدفعة"), 0, 1);
        amountBox.Dock = DockStyle.Fill;
        amountBox.Margin = new Padding(3);
        amountBox.Font = new Font("Segoe UI", 12f);
        amountBox.TextAlign = HorizontalAlignment.Left;
        amountBox.Text = maximum.ToString("0.##", CultureInfo.InvariantCulture);
        amountBox.KeyPress += AmountBox_KeyPress;
        amountBox.TextChanged += (_, _) => UpdateRemainingPreview();
        root.Controls.Add(amountBox, 1, 1);

        root.Controls.Add(FieldLabel("البيان"), 0, 2);
        noteBox.Dock = DockStyle.Fill;
        noteBox.Margin = new Padding(3);
        noteBox.PlaceholderText = "مثال: دفعة نقدية";
        noteBox.TextAlign = HorizontalAlignment.Right;
        root.Controls.Add(noteBox, 1, 2);

        root.Controls.Add(FieldLabel("المتبقي بعد الدفعة"), 0, 3);
        remainingLabel.Dock = DockStyle.Fill;
        remainingLabel.TextAlign = ContentAlignment.MiddleRight;
        remainingLabel.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        root.Controls.Add(remainingLabel, 1, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        UiKit.EnableToolbarScroll(buttons);
        var ok = UiKit.Button("✅ حفظ الدفعة", UiKit.Success, 145, 42);
        var cancel = UiKit.Button("إلغاء", Color.FromArgb(110, 118, 126), 105, 42);
        ok.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(new Label(), 0, 4);
        root.Controls.Add(buttons, 1, 4);

        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => { amountBox.Focus(); amountBox.SelectAll(); UpdateRemainingPreview(); };
    }

    private static Label FieldLabel(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleRight
    };

    private static Label ValueLabel(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleRight
    };

    private void AmountBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsDigit(e.KeyChar) || e.KeyChar == '\b' || e.KeyChar == '.' || e.KeyChar == ',' || e.KeyChar >= 0x0660 && e.KeyChar <= 0x0669 || e.KeyChar >= 0x06F0 && e.KeyChar <= 0x06F9)
            return;
        e.Handled = true;
    }

    private void UpdateRemainingPreview()
    {
        if (!TryParseAmount(amountBox.Text, out var amount))
        {
            remainingLabel.Text = CustomersForm.FormatMoney(maximum);
            remainingLabel.ForeColor = Color.FromArgb(192, 57, 43);
            return;
        }

        decimal remaining = maximum - amount;
        remainingLabel.Text = CustomersForm.FormatMoney(Math.Max(0m, remaining));
        remainingLabel.ForeColor = remaining >= 0m ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 57, 43);
    }

    private void SaveAndClose()
    {
        if (!TryParseAmount(amountBox.Text, out var amount) || amount <= 0m)
        {
            MessageBox.Show(this, "اكتب مبلغ دفعة صحيح أكبر من صفر.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            amountBox.Focus();
            amountBox.SelectAll();
            return;
        }

        if (amount > maximum)
        {
            MessageBox.Show(this, $"أقصى مبلغ يمكن دفعه هو {CustomersForm.FormatMoney(maximum)}.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            amountBox.Focus();
            amountBox.SelectAll();
            return;
        }

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        DialogResult = DialogResult.OK;
        Close();
    }

    internal static bool TryParseAmount(string text, out decimal value)
    {
        var normalized = NormalizeDigits(text).Trim().Replace('٫', '.').Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    internal static string NormalizeDigits(string text)
    {
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '\u0660' and <= '\u0669' => (char)('0' + chars[i] - '\u0660'),
                >= '\u06F0' and <= '\u06F9' => (char)('0' + chars[i] - '\u06F0'),
                _ => chars[i]
            };
        }
        return new string(chars);
    }
}

internal sealed class OldDebtForm : Form
{
    private readonly string customerName;
    private readonly decimal currentDebt;
    private readonly TextBox amountBox = new();
    private readonly TextBox noteBox = new();
    private readonly Label afterLabel = new();
    private readonly bool editMode;
    private readonly decimal baselineDebt;
    private readonly decimal initialAmount;
    private readonly string initialNote;

    public decimal Amount { get; private set; }
    public string Note => noteBox.Text.Trim();
    public bool DeleteRequested { get; private set; }

    public OldDebtForm(string customerName, decimal currentDebt)
        : this(customerName, currentDebt, null, "") { }

    // وضع التصحيح: editAmount = مبلغ الدين القديم المسجل حاليًا
    public OldDebtForm(string customerName, decimal currentDebt, decimal? editAmount, string editNote)
    {
        this.customerName = customerName;
        this.currentDebt = currentDebt;
        editMode = editAmount.HasValue;
        initialAmount = editAmount ?? 0m;
        initialNote = editNote ?? "";
        baselineDebt = editMode ? currentDebt - initialAmount : currentDebt;
        BuildUi();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, editMode ? "تصحيح دين قديم" : "إضافة دين قديم", 640, 420);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(620, 400);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 10.5f);
        UiKit.EnableEscape(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6,
            Padding = new Padding(30, 22, 30, 18), RightToLeft = RightToLeft.Yes
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 14));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 14));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 17));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 17));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 14));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        Controls.Add(root);

        root.Controls.Add(FieldLabel("العميل"), 0, 0);
        root.Controls.Add(ValueLabel(string.IsNullOrWhiteSpace(customerName) ? "بدون اسم" : customerName), 1, 0);

        root.Controls.Add(FieldLabel("الدين الحالي"), 0, 1);
        root.Controls.Add(ValueLabel(CustomersForm.FormatMoney(currentDebt)), 1, 1);

        root.Controls.Add(FieldLabel("مبلغ الدين القديم"), 0, 2);
        amountBox.Dock = DockStyle.Fill; amountBox.Margin = new Padding(3);
        amountBox.Font = new Font("Segoe UI", 12f); amountBox.TextAlign = HorizontalAlignment.Left;
        amountBox.KeyPress += (_, e) =>
        {
            if (char.IsDigit(e.KeyChar) || e.KeyChar == '\b' || e.KeyChar == '.' || e.KeyChar == ',' ||
                (e.KeyChar >= 0x0660 && e.KeyChar <= 0x0669) || (e.KeyChar >= 0x06F0 && e.KeyChar <= 0x06F9))
                return;
            e.Handled = true;
        };
        amountBox.TextChanged += (_, _) => UpdatePreview();
        root.Controls.Add(amountBox, 1, 2);

        root.Controls.Add(FieldLabel("ملاحظة (اختياري)"), 0, 3);
        noteBox.Dock = DockStyle.Fill; noteBox.Margin = new Padding(3);
        noteBox.PlaceholderText = "مثال: حساب من الدفتر القديم";
        noteBox.TextAlign = HorizontalAlignment.Right;
        root.Controls.Add(noteBox, 1, 3);

        root.Controls.Add(FieldLabel(editMode ? "الدين بعد التعديل" : "الدين بعد الإضافة"), 0, 4);
        afterLabel.Dock = DockStyle.Fill; afterLabel.TextAlign = ContentAlignment.MiddleRight;
        afterLabel.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        root.Controls.Add(afterLabel, 1, 4);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        UiKit.EnableToolbarScroll(buttons);
        var ok = UiKit.Button(editMode ? "✅ حفظ التعديل" : "✅ حفظ الدين", UiKit.Success, 150, 42);
        var cancel = UiKit.Button("إلغاء", Color.FromArgb(110, 118, 126), 95, 42);
        ok.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        if (editMode)
        {
            var delete = UiKit.Button("🗑 إلغاء الدين", Color.FromArgb(192, 57, 43), 130, 42);
            delete.Click += (_, _) => ConfirmDelete();
            buttons.Controls.Add(delete);
            amountBox.Text = initialAmount.ToString("0.##", CultureInfo.InvariantCulture);
            noteBox.Text = initialNote;
        }
        root.Controls.Add(new Label(), 0, 5);
        root.Controls.Add(buttons, 1, 5);

        AcceptButton = ok; CancelButton = cancel;
        Shown += (_, _) => { amountBox.Focus(); amountBox.SelectAll(); UpdatePreview(); };
    }

    private void UpdatePreview()
    {
        if (!DebtPaymentForm.TryParseAmount(amountBox.Text, out var amount) || amount < 0m) amount = 0m;
        afterLabel.Text = CustomersForm.FormatMoney(Math.Max(0m, baselineDebt + amount));
        afterLabel.ForeColor = Color.FromArgb(192, 57, 43);
    }

    private void ConfirmDelete()
    {
        var ask = MessageBox.Show(this,
            "متأكد إنك عايز تلغي الدين القديم ده؟ هيتمسح من كشف الحساب (وبيفضل أثره في سجل المراجعة).",
            "إلغاء الدين القديم", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        if (ask != DialogResult.Yes) return;
        DeleteRequested = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SaveAndClose()
    {
        if (!DebtPaymentForm.TryParseAmount(amountBox.Text, out var amount) || amount <= 0m)
        {
            MessageBox.Show(this, "اكتب مبلغ دين صحيح أكبر من صفر.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            amountBox.Focus(); amountBox.SelectAll();
            return;
        }
        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Label FieldLabel(string text) => new()
    {
        Dock = DockStyle.Fill, Text = text, Font = new Font("Segoe UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight
    };

    private static Label ValueLabel(string text) => new()
    {
        Dock = DockStyle.Fill, Text = text, Font = new Font("Segoe UI", 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight
    };
}
