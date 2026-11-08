using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LibraryPrintApp;

internal sealed class CashServicesForm : Form
{
    private readonly string currentUser;
    private bool isSavingTransaction;
    private ComboBox provider = null!;
    private Button btnDeposit = null!, btnWithdraw = null!;
    private Label modeLabel = null!, providerBalanceLabel = null!;
    private NumericUpDown amount = null!, commissionPercent = null!, commissionValue = null!, total = null!, drawerCash = null!;
    private string serviceType = "إيداع";   // القيمة الداخلية في قاعدة البيانات (إيداع = تحويل، سحب = سحب)

    // مصدر العمولة: نسبة (المبلغ × النسبة) أو مبلغ ثابت مكتوب يدويًا.
    private enum FeeSource { Percent, Value }
    private FeeSource feeSource = FeeSource.Percent;
    private bool updating;

    public CashServicesForm(string userName)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        BuildUi();
        InputFocusHelper.Apply(this);
        UpdateTotal();
        SetMode("إيداع");
        RefreshProviderBalance();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"💸 السحب والإيداع — المنفذ: {currentUser}", 980, 720);
        UiKit.EnableEscape(this);

        // Root layout with explicit rows prevents the selector and first fields
        // from being covered by Dock=Fill controls on smaller screens.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = Color.FromArgb(244, 246, 249)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        Controls.Add(root);

        var modePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(65, 10, 65, 10),
            ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(244, 246, 249)
        };
        modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        btnDeposit = ModeButton("💰 تحويل", UiKit.Success);
        btnWithdraw = ModeButton("💸 سحب", Color.FromArgb(220, 53, 69));
        btnDeposit.Click += (s, e) => SetMode("إيداع");
        btnWithdraw.Click += (s, e) => SetMode("سحب");
        modePanel.Controls.Add(btnDeposit, 0, 0);
        modePanel.Controls.Add(btnWithdraw, 1, 0);
        root.Controls.Add(modePanel, 0, 0);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Color.White, Padding = new Padding(12, 5, 12, 5)
        };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        header.Controls.Add(UiKit.Title($"💸 خدمات الكاش — المنفذ: {currentUser}", 18), 0, 0);
        modeLabel = new Label
        {
            Text = "نوع العملية: تحويل", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            ForeColor = UiKit.Success
        };
        header.Controls.Add(modeLabel, 0, 1);
        providerBalanceLabel = new Label
        {
            Text = "رصيد الحساب: 0.00 ج.م", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(52, 73, 94)
        };
        header.Controls.Add(providerBalanceLabel, 0, 2);
        root.Controls.Add(header, 0, 1);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(65, 10, 65, 10),
            ColumnCount = 2, RowCount = 6, BackColor = Color.FromArgb(244, 246, 249),
            AutoScroll = true
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 23));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 17));

        provider = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 12F) };
        provider.Items.AddRange(new object[] { "Vodafone Cash", "InstaPay" });
        provider.SelectedIndex = 0; // Vodafone Cash is the default
        ComboBoxStyler.ApplyModernStyle(provider);
        amount = NumberBox(0, 100000000, 0.01m);
        commissionPercent = NumberBox(0, 100, 0.5m);
        drawerCash = NumberBox(-100000000, 100000000, 0.01m);
        commissionValue = NumberBox(0, 100000000, 0.01m);
        total = NumberBox(0, 100000000, 0.01m);
        drawerCash.ReadOnly = true; drawerCash.Enabled = false; drawerCash.TabStop = false;
        drawerCash.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        drawerCash.AutoSize = false;
        drawerCash.Height = 48;
        total.ReadOnly = true; total.Enabled = false; total.TabStop = false;

        Add(body, "طريقة التحويل", provider, 0);
        Add(body, "المبلغ", amount, 1);
        Add(body, "نسبة العمولة (%)", commissionPercent, 2);
        Add(body, "العمولة (مبلغ)", commissionValue, 3);
        Add(body, "النقدية في الدرج", drawerCash, 4);
        Add(body, "الإجمالي", total, 5);
        root.Controls.Add(body, 0, 2);

        // التحديث اللحظي: بنقرأ الرقم من النص أثناء الكتابة (قيمة NumericUpDown نفسها
        // مبتتحدثش إلا لما تخرج من الخانة).
        foreach (var box in new[] { amount, commissionPercent, commissionValue })
        {
            box.TextChanged += (s, e) => OnFieldEdited(s);
            box.KeyUp += (s, e) => OnFieldEdited(s);
            box.ValueChanged += (s, e) => OnFieldEdited(s);
        }
        provider.SelectedIndexChanged += (s, e) => RefreshProviderBalance();

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(60, 10, 60, 10) };
        var save = UiKit.Button("✅ تنفيذ العملية", UiKit.Success, 230, 46);
        save.Dock = DockStyle.Right;
        save.Click += Save_Click;
        footer.Controls.Add(save);
        root.Controls.Add(footer, 0, 3);
        AcceptButton = save;
    }

    private static Button ModeButton(string text, Color color)
    {
        var b = UiKit.Button(text, color, 11, 44);
        b.Dock = DockStyle.Fill;
        b.Margin = new Padding(6);
        b.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        return b;
    }

    private static NumericUpDown NumberBox(decimal min, decimal max, decimal increment) => new()
    {
        Dock = DockStyle.Fill,
        DecimalPlaces = 2,
        Minimum = min,
        Maximum = max,
        Increment = increment,
        TextAlign = HorizontalAlignment.Center
    };

    private static void Add(TableLayoutPanel p, string label, Control c, int row)
    {
        p.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
        }, 0, row);
        p.Controls.Add(c, 1, row);
    }

    private void SetMode(string type)
    {
        serviceType = type;
        bool deposit = type == "إيداع";
        modeLabel.Text = deposit ? "نوع العملية: تحويل" : "نوع العملية: سحب";
        modeLabel.ForeColor = deposit ? UiKit.Success : Color.FromArgb(192, 57, 43);
        btnDeposit.BackColor = deposit ? Color.FromArgb(30, 132, 73) : Color.FromArgb(170, 205, 184);
        btnWithdraw.BackColor = deposit ? Color.FromArgb(225, 205, 205) : Color.FromArgb(192, 57, 43);
        UpdateTotal();
    }

    private void RefreshProviderBalance()
    {
        try
        {
            string p = provider.SelectedItem?.ToString() ?? "Vodafone Cash";
            using var c = AppDatabase.OpenConnection();
            var a = AppDatabase.GetWalletAccount(c, p);
            providerBalanceLabel.Text = $"رصيد {p}: {a.current:F2} ج.م   |   افتتاحي: {a.opening:F2} ج.م";
            providerBalanceLabel.ForeColor = a.current >= 0 ? Color.FromArgb(30,132,73) : Color.FromArgb(192,57,43);
        }
        catch { providerBalanceLabel.Text = "تعذر قراءة رصيد الحساب"; }
    }

    /// <summary> أي خانة اتغيّرت: نحدد مصدر العمولة (نسبة ولا مبلغ) ونعيد الحساب. </summary>
    private void OnFieldEdited(object? sender)
    {
        if (updating) return;
        if (ReferenceEquals(sender, commissionPercent)) feeSource = FeeSource.Percent;
        else if (ReferenceEquals(sender, commissionValue)) feeSource = FeeSource.Value;
        UpdateTotal();
    }

    /// <summary> الرقم المكتوب حاليًا في الخانة (حتى لو لسه ماخرجتش منها). </summary>
    private static decimal ReadNumber(NumericUpDown box)
    {
        string text = (box.Text ?? "").Trim();
        // أرقام عربية/فاصلة عربية → إنجليزي
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
            else if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch == '\u066B' || ch == '\u060C' || ch == ',') sb.Append('.');
            else sb.Append(ch);
        }

        // مهم: ممنوع نقرا box.Value هنا — قراءتها بتعمل Validate للنص وتعيد تنسيقه
        // وتحرك المؤشر (فكان بيقبل رقم واحد بس أثناء الكتابة).
        decimal value = 0m;
        if (sb.Length > 0 &&
            decimal.TryParse(sb.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            value = parsed;
        return Clamp(box, value);
    }

    private static decimal Clamp(NumericUpDown box, decimal value) =>
        Math.Max(box.Minimum, Math.Min(box.Maximum, value));

    private static void SetValue(NumericUpDown box, decimal value)
    {
        value = Clamp(box, value);
        // بنقارن بالنص (مش box.Value) عشان قراءة Value بتعيد تنسيق الخانة.
        string shown = value.ToString("F" + box.DecimalPlaces, CultureInfo.CurrentCulture);
        if (box.Text != shown) box.Value = value;
    }

    /// <summary> الحسابات الحالية (من النصوص المكتوبة): المبلغ، النسبة، العمولة. </summary>
    private (decimal Amount, decimal Percent, decimal Fee) CurrentFigures()
    {
        decimal a = ReadNumber(amount);
        decimal fee, pct;
        if (feeSource == FeeSource.Percent)
        {
            pct = ReadNumber(commissionPercent);
            fee = Math.Round(a * pct / 100m, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            fee = ReadNumber(commissionValue);
            pct = a > 0m ? Math.Min(100m, Math.Round(fee / a * 100m, 2, MidpointRounding.AwayFromZero)) : 0m;
        }
        return (a, pct, fee);
    }

    private void UpdateTotal()
    {
        if (updating) return;
        updating = true;
        try
        {
            var (a, pct, fee) = CurrentFigures();

            // نكتب في الخانة التانية بس (مش اللي المستخدم بيكتب فيها).
            if (feeSource == FeeSource.Percent) SetValue(commissionValue, fee);
            else SetValue(commissionPercent, pct);

            SetValue(total, a + fee);
            // النقدية اللي داخلة/خارجة من الدرج في العملية دي.
            decimal drawerImpact = serviceType == "إيداع" ? a + fee : fee - a;
            SetValue(drawerCash, drawerImpact);
        }
        finally
        {
            updating = false;
        }
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (isSavingTransaction) return;
        isSavingTransaction = true;
        if (sender is Control saveControl) saveControl.Enabled = false;
        try
        {
            var (amt, pct, fee) = CurrentFigures();
            CashServiceManager.AddCashService(
                currentUser,
                serviceType,
                provider.SelectedItem?.ToString() ?? "Vodafone Cash",
                null,
                null,
                amt,
                pct,
                fee);

            string opName = serviceType == "إيداع" ? "التحويل" : "السحب";
            MessageBox.Show(
                this,
                $"تم تنفيذ {opName} بنجاح.\nقيمة العمولة: {fee:F2} ج.م\nالإجمالي: {amt + fee:F2} ج.م",
                "تمت العملية",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            feeSource = FeeSource.Percent;
            amount.Value = 0;
            commissionPercent.Value = 0;
            commissionValue.Value = 0;
            provider.SelectedIndex = 0;
            SetMode("إيداع");
            RefreshProviderBalance();
            provider.Focus();
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
}
