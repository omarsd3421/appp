using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LibraryPrintApp;

internal sealed class PaymentServicesForm : Form
{
    private readonly string currentUser;
    private bool isSavingTransaction;
    private ComboBox service = null!;
    private TextBox otherService = null!, description = null!, customer = null!, phone = null!;
    private NumericUpDown amount = null!, commission = null!, commissionPercent = null!, total = null!;
    private TextBox paid = null!;
    private Label remaining = null!, change = null!;
    private Button execute = null!;
    private bool updatingCommission;
    private long? selectedCustomerId;

    public PaymentServicesForm(string userName)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        BuildUi();
        InputFocusHelper.Apply(this);
        UpdateCommissionFromPercent();
        UpdateAmounts();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"💳 خدمات الدفع — المنفذ: {currentUser}", 980, 760);
        UiKit.EnableEscape(this);

        var header = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Color.White };
        header.Controls.Add(UiKit.Title($"💳 خدمات الدفع والماكينات — المنفذ: {currentUser}", 18));
        Controls.Add(header);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(55, 18, 55, 18),
            ColumnCount = 2,
            RowCount = 12,
            BackColor = UiKit.Background
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        for (int i = 0; i < 12; i++)
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));

        service = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 0 };
        service.Items.AddRange(AppOptions.PaymentServices.Cast<object>().ToArray());
        service.SelectedIndex = 0;
        ComboBoxStyler.ApplyModernStyle(service);

        otherService = new TextBox { Dock = DockStyle.Fill, TabIndex = 1, Visible = false };
        description = new TextBox { Dock = DockStyle.Fill, TabIndex = 2 };
        customer = new TextBox { Dock = DockStyle.Fill, TabIndex = 3 };
        phone = new TextBox { Dock = DockStyle.Fill, TabIndex = 4, Visible = false };

        amount = NumberBox(5);
        commission = NumberBox(6);
        commissionPercent = NumberBox(7);
        total = NumberBox(8);

        // المدفوع نص عادي وليس NumericUpDown عمدًا: NumericUpDown لا يمكنه أبدًا
        // أن يكون "فارغًا" (قيمته الافتراضية دائمًا 0)، فيستحيل التفريق بين
        // "المستخدم لم يكتب شيئًا" و"المستخدم كتب صفر صراحةً". بالنص العادي:
        // خانة فارغة = دفع الإجمالي كاملًا، وكتابة 0 صراحة = المبلغ بالكامل دين.
        paid = new TextBox
        {
            Dock = DockStyle.Fill,
            TabIndex = 9,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font("Segoe UI", 10.5f),
            Text = string.Empty
        };
        paid.KeyPress += (s, e) =>
        {
            if (char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar)) return;
            if (e.KeyChar == '.' || e.KeyChar == ',') return;
            e.Handled = true;
        };

        commission.Minimum = 0; commission.Maximum = 100000000;
        commissionPercent.Minimum = 0; commissionPercent.Maximum = 100;
        total.ReadOnly = true; total.Enabled = false; total.TabStop = false;

        remaining = ResultLabel(Color.FromArgb(192, 57, 43));
        change = ResultLabel(UiKit.Success);

        Add(body, "الخدمة", service, 0);
        Add(body, "اسم العملية (عند اختيار أخرى)", otherService, 1);
        Add(body, "الوصف (اختياري)", description, 2);

        var customerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        customerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        customerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        var choose = UiKit.Button("👤 اختيار / إضافة", Color.FromArgb(111, 66, 193), 130, 38);
        choose.Dock = DockStyle.Fill;
        choose.TabStop = false;
        choose.Click += (s, e) => PickCustomer();
        customerPanel.Controls.Add(customer, 0, 0);
        customerPanel.Controls.Add(choose, 1, 0);
        Add(body, "اسم العميل (اختياري)", customerPanel, 3);

        Add(body, "السعر", amount, 4);
        Add(body, "العمولة", commission, 5);
        Add(body, "نسبة العمولة (%)", commissionPercent, 6);
        Add(body, "الإجمالي", total, 7);
        Add(body, "المبلغ المدفوع (اختياري)", paid, 8);
        Add(body, "المتبقي / الدين", remaining, 9);
        Add(body, "الباقي للعميل", change, 10);

        execute = UiKit.Button("✅ تنفيذ العملية", UiKit.Success, 220, 44);
        execute.Dock = DockStyle.Fill;
        execute.TabIndex = 10;
        body.Controls.Add(new Label { Text = "حفظ العملية", AutoSize = true, Anchor = AnchorStyles.Right, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold) }, 0, 11);
        body.Controls.Add(execute, 1, 11);

        Controls.Add(body);
        AcceptButton = execute;

        service.SelectedIndexChanged += (s, e) =>
        {
            otherService.Visible = service.SelectedItem?.ToString() == "أخرى";
            otherService.TabStop = otherService.Visible;
            if (otherService.Visible) otherService.Focus();
            UpdateAmounts();
        };

        amount.ValueChanged += (s, e) =>
        {
            if (!updatingCommission) UpdateCommissionFromPercent();
            UpdateAmounts();
        };
        commissionPercent.ValueChanged += (s, e) =>
        {
            if (!updatingCommission) UpdateCommissionFromPercent();
            UpdateAmounts();
        };
        commission.ValueChanged += (s, e) =>
        {
            if (updatingCommission) return;
            updatingCommission = true;
            if (amount.Value > 0)
                commissionPercent.Value = Math.Min(100, commission.Value * 100m / amount.Value);
            updatingCommission = false;
            UpdateAmounts();
        };
        paid.TextChanged += (s, e) => UpdateAmounts();
        execute.Click += Execute_Click;

        // Tab: الخدمة → الوصف → العميل → السعر → العمولة → النسبة → الإجمالي → المدفوع → تنفيذ.
        description.TabIndex = 2;
        customer.TabIndex = 3;
        amount.TabIndex = 4;
        commission.TabIndex = 5;
        commissionPercent.TabIndex = 6;
        paid.TabIndex = 7;
    }

    private static NumericUpDown NumberBox(int tabIndex) => new()
    {
        Dock = DockStyle.Fill,
        DecimalPlaces = 2,
        Minimum = 0,
        Maximum = 100000000,
        Increment = .01m,
        TextAlign = HorizontalAlignment.Center,
        TabIndex = tabIndex
    };

    private static Label ResultLabel(Color color) => new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 11F, FontStyle.Bold),
        ForeColor = color,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static void Add(TableLayoutPanel p, string label, Control c, int row)
    {
        p.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
        }, 0, row);
        p.Controls.Add(c, 1, row);
    }

    private void UpdateCommissionFromPercent()
    {
        if (updatingCommission) return;
        updatingCommission = true;
        decimal fee = Math.Round(amount.Value * commissionPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
        commission.Value = Math.Min(commission.Maximum, fee);
        updatingCommission = false;
    }

    /// <summary>
    /// تركها فارغة يعني أن العميل دفع كامل الإجمالي، وكتابة 0 صراحةً تعني أن
    /// كل المبلغ دين على العميل. أي رقم آخر يُستخدم كما هو (دفع جزئي).
    /// </summary>
    private decimal GetEffectivePaid(decimal total)
    {
        string text = paid.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return Math.Max(0m, total);

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ||
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return Math.Max(0m, Math.Min(100000000m, value));

        return 0m;
    }

    private void UpdateAmounts()
    {
        decimal t = amount.Value + commission.Value;
        total.Value = Math.Min(total.Maximum, t);
        decimal currentPaid = GetEffectivePaid(t);
        decimal diff = t - currentPaid;
        remaining.Text = diff > 0 ? $"{diff:F2} ج.م" : "0.00 ج.م";
        change.Text = currentPaid > t ? $"{currentPaid - t:F2} ج.م" : "0.00 ج.م";
    }

    private void PickCustomer()
    {
        using var picker = new CustomerPickerForm(customer.Text);
        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            selectedCustomerId = picker.SelectedId;
            customer.Text = picker.SelectedName;
            customer.Tag = picker.SelectedPhone;
            phone.Text = picker.SelectedPhone;
        }
    }

    private void Execute_Click(object? sender, EventArgs e)
    {
        if (isSavingTransaction) return;
        isSavingTransaction = true;
        if (sender is Control saveControl) saveControl.Enabled = false;
        try
        {
            string selected = service.SelectedItem?.ToString() ?? "";
            string serviceName = selected == "أخرى" ? otherService.Text.Trim() : selected;
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                MessageBox.Show(this, "اكتب اسم العملية عند اختيار «أخرى».", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                otherService.Focus();
                return;
            }

            decimal totalAmount = amount.Value + commission.Value;
            decimal paidAmount = GetEffectivePaid(totalAmount);
            decimal rem = Math.Max(0, totalAmount - paidAmount);

            if (rem > 0 && !selectedCustomerId.HasValue)
            {
                using var picker = new CustomerPickerForm(customer.Text);
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;
                selectedCustomerId = picker.SelectedId;
                customer.Text = picker.SelectedName;
                phone.Text = picker.SelectedPhone;
            }

            long id = ServiceTransactionManager.Complete(
                currentUser, serviceName, description.Text.Trim(),
                customer.Text.Trim(), phone.Text.Trim(),
                amount.Value, commission.Value, commissionPercent.Value,
                paidAmount, selectedCustomerId,
                out decimal remainingAmount, out decimal changeAmount);

            MessageBox.Show(this,
                $"تم تنفيذ العملية بنجاح.\n\nالخدمة: {serviceName}\n" +
                $"السعر: {amount.Value:F2} ج.م\nالعمولة: {commission.Value:F2} ج.م ({commissionPercent.Value:F2}%)\n" +
                $"الإجمالي: {totalAmount:F2} ج.م\nالمدفوع: {paidAmount:F2} ج.م\n" +
                $"المتبقي: {remainingAmount:F2} ج.م\nالباقي للعميل: {changeAmount:F2} ج.م\n\nرقم العملية: {id}",
                "تم الحفظ", MessageBoxButtons.OK, MessageBoxIcon.Information);

            ResetForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            isSavingTransaction = false;
            if (sender is Control saveControlToRestore && !saveControlToRestore.IsDisposed) saveControlToRestore.Enabled = true;
        }
    }

    private void ResetForm()
    {
        selectedCustomerId = null;
        service.SelectedIndex = 0;
        otherService.Clear();
        description.Clear();
        customer.Clear();
        phone.Clear();
        amount.Value = 0;
        commission.Value = 0;
        commissionPercent.Value = 0;
        paid.Text = string.Empty;
        service.Focus();
    }
}

internal sealed class CustomerPickerForm : Form
{
    public long? SelectedId { get; private set; }
    public long? CustomerId => SelectedId;
    public string SelectedName { get; private set; } = "";
    public string SelectedPhone { get; private set; } = "";

    private readonly DataGridView grid = new();
    private readonly TextBox search = new();
    private readonly Label countLabel = new();

    public CustomerPickerForm(string initialSearch = "")
    {
        BuildUi();
        search.Text = initialSearch;
        LoadCustomers();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "اختيار العميل", 980, 620);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(850, 540);
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
            Padding = new Padding(16),
            RightToLeft = RightToLeft.Yes
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var titlePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16, 8, 16, 8) };
        var title = UiKit.Title("👤 اختيار العميل", 18);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleRight;
        titlePanel.Controls.Add(title);
        root.Controls.Add(titlePanel, 0, 0);

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(10, 8, 10, 8),
            RightToLeft = RightToLeft.Yes
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        root.Controls.Add(toolbar, 0, 1);

        var add = UiKit.Button("➕ إضافة عميل", Color.FromArgb(111, 66, 193), 120, 40);
        add.Dock = DockStyle.Fill;
        add.Margin = new Padding(4);
        add.Click += (_, _) => AddCustomer();
        toolbar.Controls.Add(add, 0, 0);

        var find = UiKit.Button("🔍 بحث", UiKit.Primary, 100, 40);
        find.Dock = DockStyle.Fill;
        find.Margin = new Padding(4);
        find.Click += (_, _) => LoadCustomers();
        toolbar.Controls.Add(find, 1, 0);

        search.Dock = DockStyle.Fill;
        search.Margin = new Padding(4);
        search.Font = new Font("Segoe UI", 11f);
        search.TextAlign = HorizontalAlignment.Right;
        search.PlaceholderText = "ابحث بالاسم أو الرقم";
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) LoadCustomers(); };
        toolbar.Controls.Add(search, 2, 0);

        countLabel.Dock = DockStyle.Fill;
        countLabel.TextAlign = ContentAlignment.MiddleCenter;
        countLabel.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        countLabel.ForeColor = Color.FromArgb(90, 98, 107);
        toolbar.Controls.Add(countLabel, 3, 0);

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1) };
        root.Controls.Add(host, 0, 2);

        UiKit.StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        // لا نضبط AutoSizeColumnsMode=Fill هنا (يتعارض مع AutoSizeMode=None الصريح لكل عمود
        // ويسبب عدم ظهور الجدول في RTL) — نعتمد على العرض الثابت لكل عمود + التمرير الأفقي.
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.ColumnHeadersVisible = true;
        grid.ColumnHeadersHeight = 46;
        grid.RowTemplate.Height = 44;
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.Columns.Clear();
        var id = UiKit.GridText("Id", "#", 70); id.FillWeight = 10;
        var name = UiKit.GridText("Name", "الاسم", 360); name.FillWeight = 45;
        var phone = UiKit.GridText("Phone", "الرقم", 280); phone.FillWeight = 45;
        grid.Columns.Add(id);
        grid.Columns.Add(name);
        grid.Columns.Add(phone);
        grid.CellDoubleClick += (_, e) => SelectRow(e.RowIndex);
        grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { SelectRow(grid.CurrentRow?.Index ?? -1); e.Handled = true; } };
        host.Controls.Add(grid);
        AcceptButton = null;
    }

    private void LoadCustomers()
    {
        try
        {
            using var c = AppDatabase.OpenConnection();
            using var cmd = c.CreateCommand();
            var q = search.Text.Trim();
            cmd.CommandText = @"SELECT Id,Name,COALESCE(Phone,'') FROM Customers
WHERE ($q='' OR Name LIKE $like OR COALESCE(Phone,'') LIKE $like)
ORDER BY Name COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$q", q);
            cmd.Parameters.AddWithValue("$like", "%" + q + "%");
            grid.Rows.Clear();
            int count = 0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                count++;
                int row = grid.Rows.Add(
                    r.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                    string.IsNullOrWhiteSpace(r.GetString(1)) ? "بدون اسم" : r.GetString(1),
                    string.IsNullOrWhiteSpace(r.GetString(2)) ? "غير مسجل" : r.GetString(2));
                grid.Rows[row].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            countLabel.Text = $"عدد النتائج: {count}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SelectRow(int row)
    {
        if (row < 0 || row >= grid.Rows.Count) return;
        if (!long.TryParse(Convert.ToString(grid.Rows[row].Cells[0].Value, CultureInfo.InvariantCulture), out var id)) return;
        SelectedId = id;
        SelectedName = Convert.ToString(grid.Rows[row].Cells[1].Value) ?? "";
        SelectedPhone = Convert.ToString(grid.Rows[row].Cells[2].Value) ?? "";
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AddCustomer()
    {
        using var f = new NewCustomerMiniForm();
        if (f.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var c = AppDatabase.OpenConnection();
            SelectedId = AppDatabase.GetOrCreateCustomer(c, f.CustomerName, f.Phone);
            SelectedName = f.CustomerName;
            SelectedPhone = f.Phone;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class NewCustomerMiniForm : Form
{
    public string CustomerName => name.Text.Trim();
    public string Phone => phone.Text.Trim();
    private readonly TextBox name = new(), phone = new();

    public NewCustomerMiniForm()
    {
        UiKit.PrepareForm(this, "➕ إضافة عميل", 430, 300);
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(40), ColumnCount = 2, RowCount = 3 };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        name.Dock = DockStyle.Fill; phone.Dock = DockStyle.Fill;
        p.Controls.Add(new Label{Text="اسم العميل",Anchor=AnchorStyles.Right,AutoSize=true},0,0);
        p.Controls.Add(name,1,0);
        p.Controls.Add(new Label{Text="الهاتف (اختياري)",Anchor=AnchorStyles.Right,AutoSize=true},0,1);
        p.Controls.Add(phone,1,1);
        var ok=UiKit.Button("حفظ",UiKit.Success,120,40); ok.Dock=DockStyle.Fill; ok.Click+=(s,e)=>{
            if(string.IsNullOrWhiteSpace(CustomerName)){MessageBox.Show(this,"اكتب اسم العميل.");return;}
            DialogResult=DialogResult.OK; Close();
        };
        p.Controls.Add(new Label(),0,2); p.Controls.Add(ok,1,2);
        Controls.Add(p); AcceptButton=ok;
    }
}
