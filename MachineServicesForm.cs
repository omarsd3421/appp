using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LibraryPrintApp;

internal sealed class MachineServicesForm : Form
{
    private readonly string currentUser;
    private bool isSavingTransaction;
    private readonly string machineKey;
    private readonly string machineDisplayName;

    private ComboBox service = null!, tier = null!;
    private Label tierLabel = null!;
    private TableLayoutPanel side = null!;
    private bool loadingTiers;
    private const string ManualTierText = "مبلغ آخر (أكتبه بنفسي)";
    private TextBox otherService = null!, description = null!, customer = null!, phone = null!;
    private TextBox amount = null!, commission = null!, commissionPercent = null!, total = null!, paid = null!;
    private Label remaining = null!, change = null!, balanceLabel = null!;
    private Button execute = null!;
    private bool updatingCommission;
    private bool targetTotalMode;
    private long? selectedCustomerId;

    public MachineServicesForm(string userName, string machine)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        machineKey = machine == "BEE" || machine == "باب رزق" ? "BEE" : "OPay";
        machineDisplayName = machineKey == "BEE" ? "باب رزق" : "OPay";
        BuildUi();
        InputFocusHelper.Apply(this);
        UpdateLiveCalculation();
        UpdateAmounts();
        RefreshBalance();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"⚙ ماكينة {machineDisplayName} — المنفذ: {currentUser}", 1180, 820);
        UiKit.NeverMaximize(this);
        UiKit.EnableEscape(this);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 720);
        WindowState = FormWindowState.Normal;
        MaximizeBox = true;
        MinimizeBox = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Color.FromArgb(247, 249, 252);

        // رأس الشاشة: ثابت وواضح مع مساحة أمان أسفل العنوان.
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 145,
            BackColor = Color.White,
            Padding = new Padding(24, 10, 24, 10)
        };
        header.Controls.Add(UiKit.Title($"⚙ ماكينة {machineDisplayName} — الرصيد والعمليات", 18));

        balanceLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 132, 73),
            Text = "الرصيد الحالي: 0.00 ج.م"
        };
        header.Controls.Add(balanceLabel);

        // المحتوى الرئيسي: النموذج يسارًا، والبيانات الوصفية والخدمة في شريط جانبي يمينًا.
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(247, 249, 252),
            Padding = new Padding(22, 18, 22, 22),
            ColumnCount = 2,
            RowCount = 1,
            RightToLeft = RightToLeft.Yes,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        // في RTL: العمود 0 هو الجانب الأيمن، والعمود 1 هو الجانب الأيسر.
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // ===== الشريط الجانبي =====
        var sideCard = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "الخدمة والتفاصيل",
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            BackColor = Color.White,
            Padding = new Padding(14, 26, 14, 14),
            Margin = new Padding(0, 0, 12, 0)
        };

        side = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = Color.White,
            Padding = new Padding(2),
            RightToLeft = RightToLeft.Yes,
            AutoScroll = true
        };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // 0 عنوان الخدمة
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));   // 1 الخدمة
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // 2 عنوان الفئة (بيظهر لو للخدمة فئات)
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // 3 الفئة
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // 4 عنوان «أخرى»
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));   // 5 اسم «أخرى»
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // 6 عنوان الوصف
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // 7 الوصف

        side.Controls.Add(FieldLabel("الخدمة *"), 0, 0);

        service = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            TabIndex = 0,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Margin = new Padding(2, 4, 2, 8),
            MinimumSize = new Size(0, 40)
        };
        service.Items.AddRange(AppOptions.MachineServicesFor(machineKey).Cast<object>().ToArray());
        service.SelectedIndex = 0;
        ComboBoxStyler.ApplyModernStyle(service);
        side.Controls.Add(service, 0, 1);

        // خانة الفئة: بتظهر بس لما الخدمة المختارة يكون ليها فئات في الإعدادات.
        tierLabel = FieldLabel("الفئة");
        tierLabel.Visible = false;
        side.Controls.Add(tierLabel, 0, 2);

        tier = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            TabIndex = 1,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Margin = new Padding(2, 4, 2, 8),
            MinimumSize = new Size(0, 40),
            Visible = false
        };
        ComboBoxStyler.ApplyModernStyle(tier);
        tier.SelectedIndexChanged += (s, e) => TierChanged();
        side.Controls.Add(tier, 0, 3);

        side.Controls.Add(FieldLabel("اسم العملية عند اختيار (أخرى)"), 0, 4);

        otherService = new TextBox
        {
            Dock = DockStyle.Fill,
            TabIndex = 2,
            Enabled = false,
            Margin = new Padding(2, 4, 2, 8),
            MinimumSize = new Size(0, 40)
        };
        side.Controls.Add(otherService, 0, 5);

        side.Controls.Add(FieldLabel("الوصف (اختياري)"), 0, 6);

        description = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            TabIndex = 3,
            Margin = new Padding(2, 4, 2, 2),
            MinimumSize = new Size(0, 90)
        };
        side.Controls.Add(description, 0, 7);

        sideCard.Controls.Add(side);

        // ===== النموذج الرئيسي =====
        var mainCard = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "بيانات العملية",
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            BackColor = Color.White,
            Padding = new Padding(14, 26, 14, 14),
            Margin = new Padding(12, 0, 0, 0)
        };

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 9,
            BackColor = Color.White,
            RightToLeft = RightToLeft.Yes,
            Padding = new Padding(2)
        };
        // أعمدة مزدوجة تضع العمولة والنسبة بجوار بعضهما وتوفر صفًا لإجمالي المبلغ المستهدف.
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F)); // العميل
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F)); // السعر الأساسي
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F)); // العمولة والنسبة جنبًا إلى جنب
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F)); // إجمالي المبلغ بعد النسبة
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F)); // المدفوع
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F)); // الدين
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F)); // الباقي
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F)); // ملاحظة
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F)); // التنفيذ

        customer = new TextBox { Dock = DockStyle.Fill, TabIndex = 3, Margin = new Padding(2, 7, 2, 7), MinimumSize = new Size(0, 40), RightToLeft = RightToLeft.Yes };
        phone = new TextBox { Dock = DockStyle.Fill, TabIndex = 4, Visible = false };

        amount = NumberBox(5);
        commission = NumberBox(6);
        commissionPercent = NumberBox(7);
        total = NumberBox(8);
        paid = NumberBox(9);

        total.ReadOnly = false;
        total.Enabled = true;
        total.TabStop = true;

        var customerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            RightToLeft = RightToLeft.Yes
        };
        customerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        customerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72F));

        var choose = UiKit.Button("👤 اختيار / إضافة", Color.FromArgb(111, 66, 193), 130, 38);
        choose.Dock = DockStyle.Fill;
        choose.TabStop = false;
        choose.Click += (s, e) => PickCustomer();

        customerPanel.Controls.Add(choose, 0, 0);
        customerPanel.Controls.Add(customer, 1, 0);

        Add(body, "اسم العميل (اختياري)", customerPanel, 0);
        body.SetColumnSpan(customerPanel, 3);
        Add(body, "المبلغ المطلوب قبل الخصم *", amount, 1);
        body.SetColumnSpan(amount, 3);

        body.Controls.Add(FieldLabel("العمولة"), 0, 2);
        body.Controls.Add(commission, 1, 2);
        body.Controls.Add(FieldLabel("نسبة العمولة (%)"), 2, 2);
        body.Controls.Add(commissionPercent, 3, 2);

        Add(body, "إجمالي المبلغ بعد النسبة", total, 3);
        body.SetColumnSpan(total, 3);
        Add(body, "المبلغ المدفوع (اختياري)", paid, 4);
        body.SetColumnSpan(paid, 3);

        remaining = ResultLabel(Color.FromArgb(192, 57, 43));
        change = ResultLabel(UiKit.Success);

        Add(body, "المتبقي / الدين", remaining, 5);
        body.SetColumnSpan(remaining, 3);
        Add(body, "الباقي للعميل", change, 6);
        body.SetColumnSpan(change, 3);

        var hint = new Label
        {
            Text = "اكتب الإجمالي المستهدف هنا؛ العمولة والنسبة تتحدثان لحظيًا مع كل رقم.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(90, 98, 107),
            Margin = new Padding(8, 0, 8, 0)
        };
        body.Controls.Add(hint, 0, 7);
        body.SetColumnSpan(hint, 4);

        execute = UiKit.Button("✅ تنفيذ العملية", UiKit.Success, 220, 48);
        execute.Dock = DockStyle.Fill;
        execute.TabIndex = 10;
        execute.Click += Execute_Click;

        var executeLabel = new Label
        {
            Text = "حفظ العملية",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            Margin = new Padding(8)
        };
        body.Controls.Add(executeLabel, 0, 8);
        body.Controls.Add(execute, 1, 8);
        body.SetColumnSpan(execute, 3);

        mainCard.Controls.Add(body);

        content.Controls.Add(sideCard, 0, 0);
        content.Controls.Add(mainCard, 1, 0);

        Controls.Add(content);
        Controls.Add(header);

        WheelScrollFix.Apply(content);

        AcceptButton = execute;

        service.SelectedIndexChanged += (s, e) =>
        {
            bool isOther = service.SelectedItem?.ToString() == AppOptions.OtherService;
            otherService.Enabled = isOther;
            otherService.TabStop = isOther;
            if (!isOther) otherService.Clear();
            LoadTiers();
            if (isOther) otherService.Focus();
            else if (tier.Visible) tier.Focus();
        };
        LoadTiers();

        // الحساب يتغير مع كل رقم، لكننا لا نعيد كتابة النص داخل الحقل الذي يكتب فيه المستخدم.
        amount.TextChanged += (s, e) =>
        {
            if (updatingCommission) return;
            targetTotalMode = false;
            UpdateLiveCalculation();
        };
        commissionPercent.TextChanged += (s, e) =>
        {
            if (updatingCommission) return;
            targetTotalMode = false;
            UpdateLiveCalculation();
        };
        commission.TextChanged += (s, e) =>
        {
            if (updatingCommission) return;
            targetTotalMode = false;
            UpdateLiveFromCommissionText();
        };
        total.TextChanged += (s, e) =>
        {
            if (updatingCommission) return;
            targetTotalMode = true;
            UpdateFromTargetTotal();
        };
        paid.TextChanged += (s, e) => UpdateAmounts();

        foreach (var box in new[] { amount, commission, commissionPercent, total, paid })
        {
            box.KeyPress += NumericTextBox_KeyPress;
            box.Enter += (s, e) => box.SelectAll();
        }

    }

    private sealed class TierChoice
    {
        public PriceTier? Tier { get; init; }
        public string Text { get; init; } = string.Empty;
        public override string ToString() => Text;
    }

    /// <summary> يملا خانة الفئة بفئات الخدمة المختارة، ويخفيها لو مفيش فئات. </summary>
    private void LoadTiers()
    {
        string selected = service.SelectedItem?.ToString() ?? string.Empty;
        var tiers = selected == AppOptions.OtherService
            ? Array.Empty<PriceTier>()
            : AppOptions.TiersFor(machineKey, selected);

        loadingTiers = true;
        try
        {
            tier.BeginUpdate();
            tier.Items.Clear();
            foreach (var t in tiers)
            {
                string sell = AppOptions.FormatNumber(t.SellPrice);
                string text = t.Label == sell ? $"{t.Label} ج.م" : $"{t.Label} — {sell} ج.م";
                tier.Items.Add(new TierChoice { Tier = t, Text = text });
            }
            if (tiers.Count > 0) tier.Items.Add(new TierChoice { Tier = null, Text = ManualTierText });
            tier.SelectedIndex = -1;
            tier.EndUpdate();
        }
        finally { loadingTiers = false; }

        bool show = tiers.Count > 0;
        tierLabel.Visible = show;
        tier.Visible = show;
        side.RowStyles[2].Height = show ? 38 : 0;
        side.RowStyles[3].Height = show ? 58 : 0;
        side.PerformLayout();
        if (show) tier.BringToFront();
    }

    private void TierChanged()
    {
        if (loadingTiers || tier.SelectedItem is not TierChoice choice) return;

        if (choice.Tier is null)
        {
            // «مبلغ آخر»: نفضّي الأرقام ونسيب المستخدم يكتب السعر بنفسه.
            targetTotalMode = false;
            updatingCommission = true;
            try { amount.Clear(); commission.Clear(); commissionPercent.Clear(); }
            finally { updatingCommission = false; }
            UpdateAmounts();
            amount.Focus();
            return;
        }

        var t = choice.Tier;
        decimal fee = Math.Max(0m, t.SellPrice - t.Amount);
        decimal pct = t.Amount > 0m ? Math.Min(100m, fee * 100m / t.Amount) : 0m;

        // نكتب الأرقام التلاتة مع بعض من غير ما كل خانة تعيد حساب التانية.
        targetTotalMode = false;
        updatingCommission = true;
        try
        {
            amount.Text = AppOptions.FormatNumber(t.Amount);
            commission.Text = fee == 0m ? string.Empty : fee.ToString("0.00", CultureInfo.InvariantCulture);
            commissionPercent.Text = pct == 0m ? string.Empty : pct.ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally { updatingCommission = false; }
        UpdateAmounts();
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleRight,
        RightToLeft = RightToLeft.Yes,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Margin = new Padding(2, 0, 2, 0)
    };

    private static TextBox NumberBox(int tabIndex) => new()
    {
        Dock = DockStyle.Fill,
        Text = string.Empty,
        TextAlign = HorizontalAlignment.Center,
        RightToLeft = RightToLeft.No,
        TabIndex = tabIndex,
        Font = new Font("Segoe UI", 11F),
        Margin = new Padding(2, 7, 2, 7),
        MinimumSize = new Size(0, 40),
        BorderStyle = BorderStyle.FixedSingle
    };

    private static void NumericTextBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!ArithmeticInput.IsAllowedKey(e.KeyChar)) e.Handled = true;
    }

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
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            RightToLeft = RightToLeft.Yes,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            Margin = new Padding(4, 0, 6, 0),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.None
        }, 0, row);
        p.Controls.Add(c, 1, row);
    }

    private static bool TryParseTypedDecimal(string text, out decimal value) =>
        ArithmeticInput.TryEvaluateSum(text, out value);

    private decimal ReadDecimal(TextBox box, decimal fallback = 0m)
    {
        if (!TryParseTypedDecimal(box.Text, out var value))
            return fallback;
        return Math.Max(0m, value);
    }

    private decimal ReadPercent(TextBox box, decimal fallback = 0m)
    {
        return Math.Min(100m, ReadDecimal(box, fallback));
    }

    private void UpdateLiveCalculation()
    {
        if (updatingCommission) return;

        decimal typedAmount = Math.Min(100000000m, ReadDecimal(amount));
        decimal typedPercent = ReadPercent(commissionPercent);
        decimal fee = Math.Round(typedAmount * typedPercent / 100m, 2, MidpointRounding.AwayFromZero);

        updatingCommission = true;
        try { commission.Text = fee == 0m ? string.Empty : fee.ToString("0.00", CultureInfo.InvariantCulture); }
        finally { updatingCommission = false; }

        UpdateAmounts();
    }

    private void UpdateLiveFromCommissionText()
    {
        if (updatingCommission) return;

        decimal typedAmount = Math.Min(100000000m, ReadDecimal(amount));
        decimal typedCommission = Math.Min(100000000m, ReadDecimal(commission));

        updatingCommission = true;
        try
        {
            if (typedAmount > 0m)
            {
                decimal pct = Math.Min(100m, typedCommission * 100m / typedAmount);
                commissionPercent.Text = pct == 0m ? string.Empty : pct.ToString("0.##", CultureInfo.InvariantCulture);
            }
            else commissionPercent.Clear();
        }
        finally { updatingCommission = false; }

        UpdateAmounts();
    }

    /// <summary>Derives commission and percent from the user-entered target total.</summary>
    private void UpdateFromTargetTotal()
    {
        if (updatingCommission) return;
        decimal baseAmount = Math.Min(100000000m, ReadDecimal(amount));
        decimal requestedTotal = Math.Min(100000000m, ReadDecimal(total));
        decimal fee = Math.Max(0m, requestedTotal - baseAmount);
        decimal pct = baseAmount > 0m
            ? Math.Min(100m, fee * 100m / baseAmount)
            : 0m;

        updatingCommission = true;
        try
        {
            commission.Text = fee == 0m ? string.Empty : fee.ToString("0.00", CultureInfo.InvariantCulture);
            commissionPercent.Text = pct == 0m ? string.Empty : pct.ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally { updatingCommission = false; }

        UpdateAmounts();
    }

    /// <summary>
    /// ترك المدفوع فارغًا يعني دفع الإجمالي كاملًا، وكتابة صفر تعني أن الإجمالي دين.
    /// </summary>
    private decimal GetEffectivePaid(decimal totalAmount)
    {
        if (string.IsNullOrWhiteSpace(paid.Text))
            return Math.Max(0m, totalAmount);
        return Math.Min(100000000m, ReadDecimal(paid));
    }

    private void UpdateAmounts()
    {
        decimal currentAmount = Math.Min(100000000m, ReadDecimal(amount));
        decimal currentCommission = Math.Min(100000000m, ReadDecimal(commission));
        decimal t = targetTotalMode
            ? Math.Min(100000000m, ReadDecimal(total))
            : currentAmount + currentCommission;

        if (!targetTotalMode)
        {
            string formatted = t.ToString("0.00", CultureInfo.InvariantCulture);
            if (!string.Equals(total.Text, formatted, StringComparison.Ordinal))
            {
                updatingCommission = true;
                try { total.Text = formatted; }
                finally { updatingCommission = false; }
            }
        }

        decimal currentPaid = GetEffectivePaid(t);
        decimal diff = t - currentPaid;
        remaining.Text = diff > 0m ? $"{diff:F2} ج.م" : "0.00 ج.م";
        change.Text = currentPaid > t ? $"{currentPaid - t:F2} ج.م" : "0.00 ج.م";
    }

    private void RefreshBalance()
    {
        try
        {
            using var c = AppDatabase.OpenConnection();
            var a = AppDatabase.GetMachineAccount(c, machineKey);
            balanceLabel.Text = $"رصيد {a.displayName}: {a.current:F2} ج.م   |   الرصيد الافتتاحي: {a.opening:F2} ج.م";
            balanceLabel.ForeColor = a.current >= 0 ? Color.FromArgb(30, 132, 73) : Color.FromArgb(192, 57, 43);
        }
        catch
        {
            balanceLabel.Text = "تعذر قراءة رصيد الماكينة";
        }
    }

    private void PickCustomer()
    {
        using var picker = new CustomerPickerForm(customer.Text);
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        selectedCustomerId = picker.SelectedId;
        customer.Text = picker.SelectedName;
        phone.Text = picker.SelectedPhone;
    }

    private void Execute_Click(object? sender, EventArgs e)
    {
        if (isSavingTransaction) return;
        isSavingTransaction = true;
        if (sender is Control saveControl) saveControl.Enabled = false;
        try
        {
            string selected = service.SelectedItem?.ToString() ?? "";
            string operation = selected == AppOptions.OtherService ? otherService.Text.Trim() : selected;
            if (tier.Visible && tier.SelectedItem is TierChoice { Tier: not null } picked)
                operation += $" (فئة {picked.Tier.Label})";
            if (string.IsNullOrWhiteSpace(operation))
            {
                MessageBox.Show(this, "اكتب اسم العملية عند اختيار «أخرى».", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                otherService.Focus();
                return;
            }
            if (!ArithmeticInput.TryEvaluateCompleteSum(amount.Text, out _) ||
                (!string.IsNullOrWhiteSpace(commission.Text) && !ArithmeticInput.TryEvaluateCompleteSum(commission.Text, out _)) ||
                (!string.IsNullOrWhiteSpace(commissionPercent.Text) && !ArithmeticInput.TryEvaluateCompleteSum(commissionPercent.Text, out _)) ||
                (!string.IsNullOrWhiteSpace(paid.Text) && !ArithmeticInput.TryEvaluateCompleteSum(paid.Text, out _)) ||
                (targetTotalMode && !ArithmeticInput.TryEvaluateCompleteSum(total.Text, out _)))
            {
                MessageBox.Show(this, "أكمل عملية الجمع في الخانات قبل الحفظ.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (ReadDecimal(amount) <= 0)
            {
                MessageBox.Show(this, "اكتب السعر أولًا.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                amount.Focus();
                return;
            }

            decimal amountValue = Math.Min(100000000m, ReadDecimal(amount));
            decimal commissionValue = Math.Min(100000000m, ReadDecimal(commission));
            decimal commissionPercentValue = ReadPercent(commissionPercent);
            decimal totalAmount = targetTotalMode ? Math.Min(100000000m, ReadDecimal(total)) : amountValue + commissionValue;
            if (totalAmount < amountValue || (targetTotalMode && !ArithmeticInput.TryEvaluateCompleteSum(total.Text, out _)))
            {
                MessageBox.Show(this, "إجمالي المبلغ بعد النسبة يجب أن يكون رقمًا صحيحًا وألا يقل عن السعر الأساسي.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                total.Focus();
                return;
            }
            decimal paidAmount = GetEffectivePaid(totalAmount);
            decimal rem = Math.Max(0, totalAmount - paidAmount);

            if (rem > 0 && !selectedCustomerId.HasValue)
            {
                using var picker = new CustomerPickerForm(customer.Text);
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                selectedCustomerId = picker.SelectedId;
                customer.Text = picker.SelectedName;
                phone.Text = picker.SelectedPhone;
            }

            string serviceName = $"ماكينة {machineDisplayName} - {operation}";
            long id = ServiceTransactionManager.Complete(
                currentUser,
                serviceName,
                description.Text.Trim(),
                customer.Text.Trim(),
                phone.Text.Trim(),
                amountValue,
                commissionValue,
                commissionPercentValue,
                paidAmount,
                selectedCustomerId,
                out decimal remainingAmount,
                out decimal changeAmount);

            string paidText = $"{paidAmount:F2} ج.م";
            using (var success = new ServiceSuccessForm(
                machineDisplayName, operation, amountValue, commissionValue, commissionPercentValue,
                totalAmount, paidText, remainingAmount, changeAmount, id))
            {
                success.ShowDialog(this);
            }

            ResetForm();
            RefreshBalance();
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
        targetTotalMode = false;
        updatingCommission = true;
        service.SelectedIndex = 0;
        LoadTiers();
        otherService.Clear();
        description.Clear();
        customer.Clear();
        phone.Clear();
        amount.Clear();
        commission.Clear();
        commissionPercent.Clear();
        total.Clear();
        paid.Clear();
        updatingCommission = false;
        UpdateAmounts();
        service.Focus();
    }
}


internal sealed class ServiceSuccessForm : Form
{
    public ServiceSuccessForm(string machine, string service, decimal amount, decimal commission, decimal commissionPercent,
        decimal total, string paid, decimal remaining, decimal change, long id)
    {
        UiKit.PrepareForm(this, "تم حفظ العملية", 650, 580);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(600, 540);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.FromArgb(244,246,249);
        UiKit.EnableEscape(this);

        var header = new Panel { Dock=DockStyle.Top, Height=88, BackColor=Color.White, Padding=new Padding(25,12,25,12) };
        var title = new Label { Dock=DockStyle.Fill, Text="✅ تم تنفيذ العملية بنجاح", TextAlign=ContentAlignment.MiddleCenter, Font=new Font("Segoe UI",16F,FontStyle.Bold), ForeColor=Color.FromArgb(30,132,73) };
        header.Controls.Add(title);

        var body = new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(35,20,35,20), ColumnCount=2, RowCount=9, BackColor=Color.White, RightToLeft=RightToLeft.Yes };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        for(int i=0;i<9;i++) body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Add(body,"الماكينة",machine,0); Add(body,"الخدمة",service,1); Add(body,"السعر",$"{amount:F2} ج.م",2); Add(body,"العمولة",$"{commission:F2} ج.م ({commissionPercent:F2}%)",3);
        Add(body,"الإجمالي",$"{total:F2} ج.م",4); Add(body,"المدفوع",paid,5); Add(body,"المتبقي / الدين",$"{remaining:F2} ج.م",6); Add(body,"الباقي للعميل",$"{change:F2} ج.م",7); Add(body,"رقم العملية",id.ToString(),8);

        var footer = new Panel { Dock=DockStyle.Bottom, Height=70, BackColor=Color.White, Padding=new Padding(35,10,35,10) };
        var ok = UiKit.Button("حسنًا", UiKit.Primary, 140, 44); ok.Dock=DockStyle.Right; ok.Click+=(s,e)=>{DialogResult=DialogResult.OK;Close();}; footer.Controls.Add(ok);
        // ترتيب الإضافة مهم: Fill أولًا ثم الرأس والتذييل حتى لا يغطي الرأس أول صفين (الماكينة والخدمة)
        Controls.Add(body); Controls.Add(header); Controls.Add(footer);
        AcceptButton=ok;
    }
    private static void Add(TableLayoutPanel p,string label,string value,int row)
    {
        p.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Right,Font=new Font("Segoe UI",10.5F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleRight},0,row);
        p.Controls.Add(new Label{Text=value,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI",10.5F)},1,row);
    }
}
