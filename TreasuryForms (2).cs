using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp;

internal sealed class TreasuryMovementForm : Form
{
    private readonly string currentUser;
    private string movementType;
    private Button insideButton = null!, outsideButton = null!;
    private ComboBox cmbCategory = null!;
    private ComboBox cmbSourceAccount = null!;
    private NumericUpDown numAmount = null!;
    private TextBox txtOtherName = null!, txtNote = null!;
    private Label directionHint = null!, titleLabel = null!, otherNameLabel = null!;
    private TableLayoutPanel contentGrid = null!, otherRow = null!, sourceRow = null!;
    private Button saveButton = null!;
    private bool isSavingMovement;

    public TreasuryMovementForm(string userName, string initialMovementType = "IN", string? initialCategory = null)
    {
        currentUser = userName;
        movementType = initialMovementType == "OUT" ? "OUT" : "IN";
        BuildUi(initialCategory);
        InputFocusHelper.Apply(this);
    }

    private void BuildUi(string? initialCategory)
    {
        UiKit.PrepareForm(this, GetWindowTitle(), 860, 760);
        UiKit.EnableEscape(this);
        MinimumSize = new Size(780, 680);
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(12);
        AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            BackColor = UiKit.Background,
            RightToLeft = RightToLeft.Yes
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var titlePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16, 6, 16, 6), Margin = new Padding(0,0,0,8) };
        titleLabel = UiKit.Title(GetWindowTitle(), 18);
        titleLabel.Dock = DockStyle.Fill;
        titlePanel.Controls.Add(titleLabel);
        root.Controls.Add(titlePanel, 0, 0);

        var directionCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 0, 8),
            RightToLeft = RightToLeft.Yes
        };
        directionCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        directionCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        directionCard.Controls.Add(new Label
        {
            Text = "نوع الحركة",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = UiKit.Text
        }, 0, 0);

        var directionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White,
            RightToLeft = RightToLeft.Yes,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        directionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        directionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        insideButton = CreateDirectionButton("⬆  داخل\nإضافة أموال", UiKit.Success);
        outsideButton = CreateDirectionButton("⬇  خارج\nسحب أموال", Color.FromArgb(232,236,240));
        insideButton.Click += (_, _) => SetMovementType("IN");
        outsideButton.Click += (_, _) => SetMovementType("OUT");
        directionPanel.Controls.Add(outsideButton, 0, 0);
        directionPanel.Controls.Add(insideButton, 1, 0);
        directionCard.Controls.Add(directionPanel, 0, 1);
        root.Controls.Add(directionCard, 0, 1);

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiKit.Background,
            Padding = new Padding(0),
            AutoScroll = true
        };
        root.Controls.Add(host, 0, 2);

        // ثابت أسفل الشاشة: زر الحفظ لا يدخل داخل المحتوى القابل للتمرير، وبالتالي لا يختفي.
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 78,
            BackColor = UiKit.Background,
            Padding = new Padding(0, 8, 0, 0)
        };
        saveButton = UiKit.Button("💾 تسجيل الحركة", UiKit.Success, 300, 58);
        saveButton.Dock = DockStyle.Fill;
        saveButton.Margin = new Padding(0);
        saveButton.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        saveButton.Click += Save_Click;
        footer.Controls.Add(saveButton);
        host.Controls.Add(footer);

        contentGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Width = 800,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = UiKit.Background,
            RightToLeft = RightToLeft.Yes,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 8)
        };
        contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));  // التصنيف
        contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));  // المبلغ
        contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));  // حساب الإضافة أو السحب
        contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));  // الملاحظات
        contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));   // اسم حركة أخرى
        contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));  // تنبيه
        host.Controls.Add(contentGrid);

        cmbCategory = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 11f),
            Margin = new Padding(0, 5, 0, 5)
        };
        cmbCategory.Items.AddRange(AppOptions.TreasuryCategories.Cast<object>().ToArray());
        ComboBoxStyler.ApplyModernStyle(cmbCategory);
        SetInitialCategory(initialCategory);

        numAmount = new NumericUpDown
        {
            Dock = DockStyle.Fill, DecimalPlaces = 2, Minimum = 0m, Maximum = 100000000, Value = 0m,
            Increment = .01m, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Center,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(0, 5, 0, 5)
        };

        cmbSourceAccount = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 11f),
            Margin = new Padding(0, 5, 0, 5)
        };
        cmbSourceAccount.Items.AddRange(new object[]
        {
            "النقدية في الدرج",
            "فودافون كاش",
            "إنستا باي"
        });
        cmbSourceAccount.SelectedIndex = 0;
        ComboBoxStyler.ApplyModernStyle(cmbSourceAccount);
        cmbSourceAccount.SelectedIndexChanged += (_, _) => UpdateDirectionHint();

        txtNote = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 10.5f), Margin = new Padding(0, 5, 0, 5)
        };

        AddFieldCard(contentGrid, 0, "التصنيف", cmbCategory);
        AddFieldCard(contentGrid, 1, "المبلغ (ج.م)", numAmount);
        sourceRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1,
            BackColor = UiKit.Background, Margin = new Padding(0), Padding = new Padding(0), Visible = true
        };
        AddFieldCard(sourceRow, 0, "الحساب", cmbSourceAccount);
        contentGrid.Controls.Add(sourceRow, 0, 2);
        AddFieldCard(contentGrid, 3, "ملاحظات", txtNote);

        otherNameLabel = new Label
        {
            Text = "اسم الحركة", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Padding = new Padding(0,0,6,0), ForeColor = UiKit.Text
        };
        txtOtherName = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f), Margin = new Padding(0,5,0,5) };
        otherRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, RightToLeft = RightToLeft.Yes,
            BackColor = Color.White, Padding = new Padding(10,2,10,2), Margin = new Padding(0,0,0,4), Visible = false
        };
        otherRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        otherRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        otherRow.Controls.Add(otherNameLabel, 0, 0); otherRow.Controls.Add(txtOtherName, 1, 0);
        contentGrid.Controls.Add(otherRow, 0, 4);

        var hint = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12,0,12,0), Margin = new Padding(0,5,0,4) };
        directionHint = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        hint.Controls.Add(directionHint);
        contentGrid.Controls.Add(hint, 0, 5);

        cmbCategory.SelectedIndexChanged += (_, _) => UpdateOtherUi();
        SetMovementType(movementType);
        UpdateOtherUi();
        AcceptButton = saveButton;

        host.Resize += (_, _) =>
        {
            contentGrid.Width = Math.Max(0, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        };
        Shown += (_, _) =>
        {
            contentGrid.Width = Math.Max(0, host.ClientSize.Width - 6);
            numAmount.Focus();
        };
    }

    private string GetWindowTitle() => movementType == "OUT"
        ? $"💰 سحب أموال من الخزينة — المنفذ: {currentUser}"
        : $"💰 إضافة سيولة إلى الخزينة — المنفذ: {currentUser}";

    private static Button CreateDirectionButton(string text, Color backColor) => new()
    {
        Text = text, Dock = DockStyle.Fill, Margin = new Padding(4), FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter,
        BackColor = backColor, ForeColor = backColor == UiKit.Success ? Color.White : Color.FromArgb(60,68,78),
        Cursor = Cursors.Hand, UseCompatibleTextRendering = false, AutoEllipsis = false, Padding = new Padding(4)
    };

    private void SetInitialCategory(string? initialCategory)
    {
        int initialIndex = 0;
        if (!string.IsNullOrWhiteSpace(initialCategory))
            for (int i = 0; i < cmbCategory.Items.Count; i++)
                if (string.Equals(cmbCategory.Items[i]?.ToString(), initialCategory, StringComparison.Ordinal)) { initialIndex = i; break; }
        cmbCategory.SelectedIndex = initialIndex;
    }

    private void SetMovementType(string type)
    {
        movementType = type == "OUT" ? "OUT" : "IN";
        bool inside = movementType == "IN";
        insideButton.BackColor = inside ? UiKit.Success : Color.FromArgb(232,236,240);
        insideButton.ForeColor = inside ? Color.White : Color.FromArgb(60,68,78);
        outsideButton.BackColor = inside ? Color.FromArgb(232,236,240) : UiKit.Danger;
        outsideButton.ForeColor = inside ? Color.FromArgb(60,68,78) : Color.White;
        Text = GetWindowTitle();
        titleLabel.Text = GetWindowTitle();
        sourceRow.Visible = true;
        contentGrid.RowStyles[2].Height = 72;
        directionHint.ForeColor = inside ? UiKit.SuccessText : UiKit.Danger;
        UpdateDirectionHint();
        contentGrid.PerformLayout();
    }

    private string GetSelectedSourceAccountKey()
    {
        string source = cmbSourceAccount?.SelectedItem?.ToString() ?? "النقدية في الدرج";
        return source switch
        {
            "فودافون كاش" => "CASH",
            "إنستا باي" => "INSTAPAY",
            _ => "DRAWER"
        };
    }

    private void UpdateDirectionHint()
    {
        if (directionHint == null) return;
        string source = cmbSourceAccount?.SelectedItem?.ToString() ?? "النقدية في الدرج";
        try
        {
            using var connection = AppDatabase.OpenConnection();
            decimal balance = AppDatabase.GetManualWithdrawalAvailableBalance(connection, GetSelectedSourceAccountKey());
            if (movementType == "IN")
            {
                directionHint.ForeColor = UiKit.SuccessText;
                directionHint.Text = $"الإضافة إلى: {source} — الرصيد الحالي: {balance:N2} ج.م.";
            }
            else
            {
                directionHint.ForeColor = balance > 0 ? UiKit.Danger : Color.FromArgb(180, 60, 60);
                directionHint.Text = $"مصدر السحب: {source} — الرصيد المتاح: {balance:N2} ج.م. لن يُسمح بسحب مبلغ أكبر من الرصيد.";
            }
        }
        catch
        {
            directionHint.ForeColor = movementType == "IN" ? UiKit.SuccessText : UiKit.Danger;
            directionHint.Text = movementType == "IN"
                ? $"سيتم إضافة المبلغ إلى {source}."
                : $"مصدر السحب: {source}. سيتحقق البرنامج من الرصيد قبل الحفظ.";
        }
    }

    private void UpdateOtherUi()
    {
        bool isOther = string.Equals(cmbCategory.SelectedItem?.ToString(), "أخرى", StringComparison.Ordinal);
        otherRow.Visible = isOther;
        otherNameLabel.Visible = isOther;
        txtOtherName.Visible = isOther;
        txtOtherName.TabStop = isOther;
        contentGrid.RowStyles[4].Height = isOther ? 66 : 0;
        if (!isOther) txtOtherName.Clear();
        contentGrid.PerformLayout();
    }

    private static void AddFieldCard(TableLayoutPanel p, int row, string label, Control c)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.White,
            RightToLeft = RightToLeft.Yes, Margin = new Padding(0,2,0,4), Padding = new Padding(10,2,10,2)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label
        {
            Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = UiKit.Text, Padding = new Padding(0,0,6,0)
        }, 0, 0);
        card.Controls.Add(c, 1, 0);
        p.Controls.Add(card, 0, row);
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (isSavingMovement) return;
        isSavingMovement = true;
        saveButton.Enabled = false;
        try
        {
            string category = cmbCategory.SelectedItem?.ToString() ?? "إضافة سيولة";
            string? otherName = null;
            if (category == "أخرى")
            {
                otherName = txtOtherName.Text.Trim();
                if (otherName.Length == 0) { MessageBox.Show(this, "اكتب اسم الحركة عند اختيار «أخرى».", "بيانات ناقصة", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtOtherName.Focus(); return; }
            }
            if (numAmount.Value <= 0) { MessageBox.Show(this, "أدخل مبلغًا أكبر من صفر.", "بيانات ناقصة", MessageBoxButtons.OK, MessageBoxIcon.Warning); numAmount.Focus(); return; }

            string note = txtNote.Text.Trim();
            string? description = string.IsNullOrWhiteSpace(otherName) ? (note.Length == 0 ? null : note) : (note.Length == 0 ? otherName : $"{otherName} — {note}");
            using var c = AppDatabase.OpenConnection();
            if (movementType == "IN")
            {
                string accountKey = GetSelectedSourceAccountKey();
                AppDatabase.RecordManualTreasuryDeposit(c, currentUser, category, numAmount.Value, description, accountKey);
                MessageBox.Show(this,
                    $"تمت إضافة {numAmount.Value:N2} ج.م إلى {cmbSourceAccount.SelectedItem}.",
                    "تم الحفظ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                string accountKey = GetSelectedSourceAccountKey();
                decimal available = AppDatabase.GetManualWithdrawalAvailableBalance(c, accountKey);
                if (numAmount.Value > available)
                {
                    string source = cmbSourceAccount.SelectedItem?.ToString() ?? "المصدر المحدد";
                    MessageBox.Show(this,
                        $"الرصيد المتاح في {source} هو {available:N2} ج.م فقط.\nقلّل مبلغ السحب أو اختر مصدرًا آخر.",
                        "الرصيد غير كافٍ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    numAmount.Focus();
                    return;
                }
                AppDatabase.RecordManualTreasuryWithdrawal(c, currentUser, category, numAmount.Value, description, accountKey);
                MessageBox.Show(this,
                    $"تم سحب {numAmount.Value:N2} ج.م من {cmbSourceAccount.SelectedItem}.",
                    "تم الحفظ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { AppLog.Error("فشلت حركة يدوية في الخزينة", ex); MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            isSavingMovement = false;
            if (!IsDisposed && !saveButton.IsDisposed) saveButton.Enabled = true;
        }
    }
}

internal sealed class TreasuryForm : Form
{
    private readonly string currentUser;
    private DataGridView grid = null!;
    private TableLayoutPanel balancePanel = null!;
    private Label balance = null!;
    private Label drawerBalanceLabel = null!;
    private Label periodSummary = null!;
    private Button drawerEyeButton = null!;
    private Button treasuryEyeButton = null!;
    private decimal drawerBalanceValue;
    private decimal treasuryBalanceValue;
    private decimal periodIncomingValue;
    private decimal periodOutgoingValue;
    private bool drawerBalanceVisible;
    private bool treasuryBalanceVisible;
    private ComboBox user = null!;
    private ComboBox period = null!;
    private DateTimePicker date = null!;
    private Label cashSummary = null!, salesSummary = null!, printSummary = null!, paymentSummary = null!, otherSummary = null!;
    private Label emptyState = null!;
    private readonly ToolTip treasuryHelp = new();

    public TreasuryForm(string userName)
    {
        currentUser = userName;
        BuildUi();
        LoadData();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "💰 الخزينة", 1250, 800);
        UiKit.EnableEscape(this);

        var header = new Panel { Dock = DockStyle.Top, Height = 168, BackColor = Color.White, Padding = new Padding(25, 12, 25, 8) };
        header.Controls.Add(UiKit.Title("💰 الخزينة وحركة الأموال", 18));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoScroll = true, Padding = new Padding(4) };
        UiKit.EnableToolbarScroll(toolbar);
        user = new ComboBox { Width = 115, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(5) };
        user.Items.AddRange(new object[] { "الكل", "عمر", "أحمد" });
        user.SelectedIndex = 0;
        ComboBoxStyler.ApplyModernStyle(user);
        period = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(5) };
        period.Items.AddRange(new object[] { "يومي", "شهري", "سنوي" });
        period.SelectedIndex = 0;
        ComboBoxStyler.ApplyModernStyle(period);
        date = new DateTimePicker { Width = 150, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Margin = new Padding(5) };
        var show = UiKit.Button("🔎 عرض", UiKit.Primary, 105, 40);
        var add = UiKit.Button("➕ إضافة سيولة", UiKit.Success, 150, 40);
        var withdraw = UiKit.Button("➖ سحب من الخزينة", UiKit.Danger, 155, 40);
        var reports = UiKit.Button("📊 التقارير", Color.FromArgb(52,73,94), 150, 40);
        var journal = UiKit.Button("📚 سجل اليوميات", Color.FromArgb(111,66,193), 145, 40);
        var delete = UiKit.Button("🗑️ حذف نهائي للبيع/الطباعة", UiKit.Danger, 205, 40);
        show.Click += (s,e)=>LoadData();
        delete.Click += (s,e)=>DeleteSelectedOperation();
        add.Click += (s,e)=>{ using var f=new TreasuryMovementForm(currentUser, "IN", "إضافة سيولة"); if(f.ShowDialog(this)==DialogResult.OK) LoadData(); };
        withdraw.Click += (s,e)=>{ using var f=new TreasuryMovementForm(currentUser, "OUT", "سحب شخصي"); if(f.ShowDialog(this)==DialogResult.OK) LoadData(); };
        reports.Click += (s,e)=>{ using var f=new ReportsForm(currentUser); f.ShowDialog(this); LoadData(); };
        journal.Click += (s,e)=>{ using var f=new DailyJournalForm(currentUser); f.ShowDialog(this); LoadData(); };
        toolbar.Controls.Add(user); toolbar.Controls.Add(period); toolbar.Controls.Add(date); toolbar.Controls.Add(show);
        toolbar.Controls.Add(add); toolbar.Controls.Add(withdraw); toolbar.Controls.Add(delete); toolbar.Controls.Add(reports); toolbar.Controls.Add(journal);
        header.Controls.Add(toolbar);

        var buckets = new TableLayoutPanel { Dock = DockStyle.Top, Height = 105, ColumnCount = 5, Padding = new Padding(15,10,15,10), BackColor = Color.FromArgb(244,246,249) };
        for(int i=0;i<5;i++) buckets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));
        cashSummary = BucketCard("💸 خدمات الكاش", Color.FromArgb(40,167,69));
        salesSummary = BucketCard("📚 المبيعات", Color.FromArgb(255,140,0));
        printSummary = BucketCard("🖨️ الطباعة", Color.FromArgb(0,122,204));
        paymentSummary = BucketCard("💳 خدمات الدفع", Color.FromArgb(0,102,102));
        otherSummary = BucketCard("📦 أخرى", Color.FromArgb(111,66,193));
        buckets.Controls.Add(cashSummary,0,0); buckets.Controls.Add(salesSummary,1,0); buckets.Controls.Add(printSummary,2,0); buckets.Controls.Add(paymentSummary,3,0); buckets.Controls.Add(otherSummary,4,0);

        balancePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
            Padding = new Padding(8, 2, 8, 2)
        };
        balancePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        balancePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        balancePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var drawerStrip = CreateBalanceStrip("💵 رصيد الدرج الفعلي: •••••• ج.م", Color.FromArgb(30, 132, 73), out drawerBalanceLabel, out drawerEyeButton);
        var treasuryStrip = CreateBalanceStrip("💰 رصيد الخزينة الكلي: •••••• ج.م", Color.FromArgb(0, 102, 102), out balance, out treasuryEyeButton);
        periodSummary = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 132, 73),
            BackColor = Color.White
        };
        balancePanel.Controls.Add(drawerStrip, 0, 0);
        balancePanel.Controls.Add(treasuryStrip, 0, 1);
        balancePanel.Controls.Add(periodSummary, 0, 2);
        drawerEyeButton.Click += (s, e) => { drawerBalanceVisible = !drawerBalanceVisible; RefreshBalanceSummary(); };
        treasuryEyeButton.Click += (s, e) => { treasuryBalanceVisible = !treasuryBalanceVisible; RefreshBalanceSummary(); };
        treasuryHelp.SetToolTip(drawerBalanceLabel, "رصيد الدرج المحسوب من المبيعات والطباعة/التصوير والحركات النقدية والتحصيلات المسجلة. هو رصيد النظام، ويحتاج مطابقة مع النقد الموجود فعليًا عند الجرد.");
        treasuryHelp.SetToolTip(balance, "إجمالي الأموال المسجلة = النقدية في الدرج + الأرصدة الحالية للمحافظ الإلكترونية + الأرصدة الحالية لكل الماكينات.");
        treasuryHelp.SetToolTip(drawerEyeButton, "إظهار/إخفاء رصيد الدرج الفعلي");
        treasuryHelp.SetToolTip(treasuryEyeButton, "إظهار/إخفاء إجمالي رصيد الخزينة");
        RefreshBalanceSummary();

        grid = new SafeDataGridView();
        UiKit.StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        // لا نضبط AutoSizeColumnsMode=Fill هنا (يتعارض مع AutoSizeMode=None الصريح لكل عمود
        // ويسبب عدم ظهور الجدول في RTL) — نعتمد على العرض الثابت لكل عمود + التمرير الأفقي.
        grid.ColumnHeadersVisible = true;
        grid.AllowUserToResizeColumns = true;
        grid.AllowUserToResizeRows = false;
        // عمود "التفاصيل" كان آخر عمود بعرض كبير (430) بعد 12 عمودًا أخرى،
        // فمجموع عرض الأعمدة يتجاوز عرض الشاشة بكثير ويحتاج تمرير أفقي طويل
        // ليظهر — وهو سبب شكوى "التفاصيل مش ظاهرة". نقلناه مباشرة بعد التاريخ
        // وقلّلنا عرضه قليلاً + قلّلنا عرض الأعمدة الرقمية الأقل أهمية حتى يظهر
        // ضمن أول ما يراه المستخدم بدون تمرير.
        // سبب "لسه صغيرة" بعد نقل العمود: كان مضبوطًا fill=true (وضع "املأ
        // المساحة المتبقية")، لكن مجموع عرض الأعمدة الثابتة الأخرى وحدها
        // (بدون التفاصيل) كان أصلاً أكبر من عرض الشاشة، فما تبقى من مساحة
        // لعمود "املأ" كان صفرًا تقريبًا فانكمش لأقل عرض ممكن. الحل: نعطيه
        // عرضًا ثابتًا كبيرًا صراحة (fill=false) بدل الاعتماد على مساحة
        // متبقية قد لا توجد.
        grid.Columns.Add(UiKit.GridText("Time","التاريخ والوقت",145));
        grid.Columns.Add(UiKit.GridText("Description","التفاصيل",560, fill: true, minWidth: 320));
        grid.Columns.Add(UiKit.GridText("Service","الخدمة",140));
        grid.Columns.Add(UiKit.GridText("Customer","العميل",140));
        grid.Columns.Add(UiKit.GridText("Price","السعر",90));
        grid.Columns.Add(UiKit.GridText("Commission","العمولة",90));
        grid.Columns.Add(UiKit.GridText("Total","الإجمالي",95));
        grid.Columns.Add(UiKit.GridText("Paid","المدفوع",95));
        grid.Columns.Add(UiKit.GridText("Remaining","الباقي/الدين",100));
        grid.Columns.Add(UiKit.GridText("In","داخل",90));
        grid.Columns.Add(UiKit.GridText("Out","خارج",90));
        var machineBalanceColumn = UiKit.GridText("MachineBalance","رصيد الخزينة",115);
        machineBalanceColumn.DefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        machineBalanceColumn.DefaultCellStyle.ForeColor = Color.FromArgb(30, 132, 73);
        machineBalanceColumn.ToolTipText = "الرصيد الكلي بعد الحركة، مع احتساب رصيد بداية الحسابات وأرصدة المحافظ والماكينات والدرج.";
        grid.Columns.Add(machineBalanceColumn);
        grid.Columns.Add(UiKit.GridText("User","بواسطة",80));
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            if (grid.Rows[e.RowIndex].Tag is not TreasuryLedgerRow selected) return;
            using var details = new OperationDetailsForm(selected.RefType, selected.RefId, selected.MovementId, currentUser);
            details.ShowDialog(this);
            LoadData();
        };

        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15, 0, 15, 15), BackColor = Color.FromArgb(244,246,249) };
        gridHost.Controls.Add(grid);
        emptyState = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            Text = "لا توجد حركات خزينة أو عمليات آجلة في الفترة المحددة.",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(120, 128, 136),
            BackColor = Color.FromArgb(244,246,249),
            Visible = false
        };
        gridHost.Controls.Add(emptyState);
        emptyState.BringToFront();

        // نضع عناصر الخزينة داخل حاوية مستقلة حتى لا يحدث تداخل بين الـDock
        // ويظل رأس الجدول والعمليات ظاهرين دائمًا.
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(244,246,249),
            Padding = new Padding(0)
        };
        content.Controls.Add(gridHost);
        content.Controls.Add(balancePanel);
        content.Controls.Add(buckets);

        Controls.Add(content);
        Controls.Add(header);
        grid.BringToFront();
    }

    private static Panel CreateBalanceStrip(string initialText, Color accent, out Label amountLabel, out Button eyeButton)
    {
        var strip = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(2, 0, 2, 0)
        };
        amountLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = initialText,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            ForeColor = accent,
            BackColor = Color.White,
            Padding = new Padding(2, 0, 2, 0),
            AutoEllipsis = true
        };
        eyeButton = new Button
        {
            Text = "👁",
            Dock = DockStyle.Left,
            Width = 48,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(52, 73, 94),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Emoji", 10.5F, FontStyle.Regular),
            Cursor = Cursors.Hand,
            TabStop = true
        };
        eyeButton.FlatAppearance.BorderSize = 0;
        strip.Controls.Add(amountLabel);
        strip.Controls.Add(eyeButton);
        return strip;
    }

    private void RefreshBalanceSummary()
    {
        if (drawerBalanceLabel == null || balance == null || periodSummary == null) return;
        drawerBalanceLabel.Text = $"💵 رصيد الدرج الفعلي: {(drawerBalanceVisible ? drawerBalanceValue.ToString("N2") + " ج.م" : "•••••• ج.م")}";
        balance.Text = $"💰 رصيد الخزينة الكلي: {(treasuryBalanceVisible ? treasuryBalanceValue.ToString("N2") + " ج.م" : "•••••• ج.م")}";
        drawerEyeButton.Text = drawerBalanceVisible ? "🙈" : "👁";
        treasuryEyeButton.Text = treasuryBalanceVisible ? "🙈" : "👁";
        drawerEyeButton.AccessibleName = drawerBalanceVisible ? "إخفاء رصيد الدرج" : "إظهار رصيد الدرج";
        treasuryEyeButton.AccessibleName = treasuryBalanceVisible ? "إخفاء رصيد الخزينة" : "إظهار رصيد الخزينة";
        periodSummary.Text = $"الداخل للفترة: {periodIncomingValue:N2} ج.م    |    الخارج للفترة: {periodOutgoingValue:N2} ج.م";
    }

    private static Label BucketCard(string title, Color color) => new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(5),
        Padding = new Padding(8),
        BackColor = Color.White,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
        ForeColor = color,
        Text = title
    };

    private void LoadData()
    {
        try
        {
            using var c = AppDatabase.OpenConnection();
            grid.Rows.Clear();
            drawerBalanceValue = AppDatabase.GetActualDrawerBalance(c);
            treasuryBalanceValue = AppDatabase.GetTreasuryBalance(c);
            string p = period.SelectedItem?.ToString() ?? "يومي";
            string u = user.SelectedItem?.ToString() ?? "الكل";
            string dateWhere = p == "سنوي"
                ? "substr(t.CreatedAt,1,4)=substr($date,1,4)"
                : p == "شهري"
                    ? "substr(t.CreatedAt,1,7)=substr($date,1,7)"
                    : "substr(t.CreatedAt,1,10)=substr($date,1,10)";

            using var cmd = c.CreateCommand();
            cmd.CommandText = $@"
SELECT t.Id,
       t.CreatedAt,
       t.ReferenceType,
       COALESCE(t.ReferenceId,0),
       CASE WHEN tr.TransactionType='COPY' AND COALESCE(tr.RemainingAmount,0)>0 THEN 'التصوير - دين'
            WHEN tr.TransactionType='COPY' THEN 'التصوير'
            WHEN tr.TransactionType='SALE' AND COALESCE(tr.RemainingAmount,0)>0 THEN 'المبيعات - دين'
            WHEN tr.TransactionType='SALE' THEN 'المبيعات'
            WHEN tr.TransactionType='SERVICE' AND COALESCE(tr.RemainingAmount,0)>0
                 THEN COALESCE(tr.ServiceName, cs.ServiceType, t.Category, 'خدمات الدفع') || ' - دين'
            ELSE COALESCE(tr.ServiceName, cs.ServiceType, t.Category) END,
       COALESCE(c.Name, cs.CustomerName, ''),
       COALESCE(tr.OriginalAmount, cs.Amount, 0),
       COALESCE(tr.Commission, cs.Commission, 0),
       COALESCE(tr.RequiredAmount, cs.TotalAmount, 0),
       COALESCE(tr.PaidAmount, 0),
       COALESCE(tr.RemainingAmount, 0),
       t.Amount,t.MovementType,t.Description,u.UserName,
       COALESCE(pl.FileName,''),COALESCE(pl.Pages,0),COALESCE(pl.Copies,0),COALESCE(pl.PricingCategory,tr.PricingCategory,'')
FROM TreasuryMovements t
JOIN AppUsers u ON u.Id=t.UserId
LEFT JOIN PrintLog pl ON t.ReferenceType='PrintLog' AND t.ReferenceId=pl.Id
LEFT JOIN Transactions tr ON t.ReferenceId=tr.Id AND t.ReferenceType IN ('ServiceTransaction','Transaction','Copy')
LEFT JOIN Cash_Services cs ON t.ReferenceType='CashService' AND t.ReferenceId=cs.Id
LEFT JOIN Customers c ON tr.CustomerId=c.Id
WHERE {dateWhere} AND ($user='الكل' OR u.UserName=$user)
  AND COALESCE(t.AccountKey,'')<>'PERCENTAGE'
ORDER BY t.CreatedAt DESC";
            cmd.Parameters.AddWithValue("$date", date.Value.Date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$user", u);

            // رصيد الخزينة الصافي بعد كل حركة على حدة، محسوب من تاريخ
            // الحركات بالكامل حتى يكون صحيحًا مهما كان فلتر الفترة/المستخدم المعروض.
            var runningBalances = AppDatabase.GetTreasuryRunningBalances(c);

            var items = new List<TreasuryLedgerRow>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    long movementId = r.GetInt64(0);
                    items.Add(new TreasuryLedgerRow
                    {
                        MovementId = movementId,
                        Time = DateTime.Parse(r.GetString(1)),
                        RefType = r.IsDBNull(2) ? "" : r.GetString(2),
                        RefId = r.GetInt64(3),
                        Service = r.GetString(4),
                        Customer = r.GetString(5),
                        Price = r.GetDecimal(6),
                        Commission = r.GetDecimal(7),
                        Total = r.GetDecimal(8),
                        Paid = r.GetDecimal(9),
                        Remaining = r.GetDecimal(10),
                        MovementAmount = r.GetDecimal(11),
                        MovementType = r.GetString(12),
                        Description = r.IsDBNull(13) ? "" : r.GetString(13),
                        User = r.GetString(14),
                        FileName = r.IsDBNull(15) ? "" : r.GetString(15),
                        PageCount = r.IsDBNull(16) ? 0 : r.GetInt32(16),
                        Copies = r.IsDBNull(17) ? 0 : r.GetInt32(17),
                        PricingCategory = r.IsDBNull(18) ? "" : r.GetString(18),
                        BalanceAfter = runningBalances.TryGetValue(movementId, out var bal) ? bal : (decimal?)null
                    });
                }
            }

            decimal incoming = 0m, outgoing = 0m;
            foreach (var row in items)
            {
                incoming += row.MovementType == "IN" ? row.MovementAmount : 0m;
                outgoing += row.MovementType == "OUT" ? row.MovementAmount : 0m;
            }

            // تجميع حركات نفس العملية في صف واحد: الدخل والخارج يظهران معًا بدل تكرار العملية مرتين.
            var grouped = new List<TreasuryLedgerRow>();
            var linked = items.Where(x => !string.IsNullOrWhiteSpace(x.RefType) && x.RefId > 0)
                              .GroupBy(x => string.Equals(x.RefType, "DebtPayment", StringComparison.OrdinalIgnoreCase)
                                  ? x.RefType + ":movement:" + x.MovementId
                                  : x.RefType + ":" + x.RefId);
            foreach (var g in linked)
            {
                var first = g.OrderBy(x => x.Time).First();
                // رصيد الخزينة بعد العملية المجمَّعة = الرصيد بعد آخر حركة خزينة
                // فعلية ضمنها زمنيًا (وليس أول حركة)، لأنه الرصيد الصحيح لحظة
                // اكتمال العملية بالكامل.
                var last = g.OrderBy(x => x.Time).ThenBy(x => x.MovementId).Last();
                var merged = new TreasuryLedgerRow
                {
                    Time = g.Min(x => x.Time), RefType = first.RefType, RefId = first.RefId,
                    MovementId = last.MovementId, FileName = first.FileName, PageCount = first.PageCount,
                    Copies = first.Copies, PricingCategory = first.PricingCategory,
                    Service = first.Service, Customer = first.Customer, Price = first.Price,
                    Commission = first.Commission, Total = first.Total, Paid = first.Paid,
                    Remaining = first.Remaining, User = first.User,
                    Incoming = g.Where(x=>x.MovementType=="IN").Sum(x=>x.MovementAmount),
                    Outgoing = g.Where(x=>x.MovementType=="OUT").Sum(x=>x.MovementAmount),
                    Description = BuildLedgerDescription(first, g.Select(x => x.Description)),
                    BalanceAfter = last.BalanceAfter
                };
                grouped.Add(merged);
            }
            foreach (var row in items.Where(x => string.IsNullOrWhiteSpace(x.RefType) || x.RefId <= 0))
            {
                row.Incoming = row.MovementType == "IN" ? row.MovementAmount : 0m;
                row.Outgoing = row.MovementType == "OUT" ? row.MovementAmount : 0m;
                grouped.Add(row);
            }

            // أظهر عمليات البيع/التصوير/الخدمات الآجلة التي لم ينتج عنها أي حركة خزينة
            // (مثل عملية بدين كامل). هذه صفوف معلوماتية ولا تدخل في الداخل أو الخارج.
            string debtDateWhere = p == "سنوي"
                ? "substr(tr.CreatedAt,1,4)=substr($date,1,4)"
                : p == "شهري"
                    ? "substr(tr.CreatedAt,1,7)=substr($date,1,7)"
                    : "substr(tr.CreatedAt,1,10)=substr($date,1,10)";
            using (var debtCmd = c.CreateCommand())
            {
                debtCmd.CommandText = $@"
SELECT tr.Id,tr.CreatedAt,tr.TransactionType,COALESCE(tr.Description,''),
       COALESCE(tr.ServiceName,''),COALESCE(c.Name,''),
       COALESCE(tr.OriginalAmount,0),COALESCE(tr.Commission,0),
       COALESCE(tr.RequiredAmount,0),COALESCE(tr.PaidAmount,0),COALESCE(tr.RemainingAmount,0),u.UserName
FROM Transactions tr
JOIN AppUsers u ON u.Id=tr.UserId
LEFT JOIN Customers c ON c.Id=tr.CustomerId
WHERE {debtDateWhere}
  AND ($user='الكل' OR u.UserName=$user)
  AND tr.RemainingAmount>0
  AND tr.TransactionType IN ('SALE','COPY','SERVICE')
  AND NOT EXISTS (
      SELECT 1 FROM TreasuryMovements tm
      WHERE tm.ReferenceId=tr.Id
        AND ((tr.TransactionType='SALE' AND tm.ReferenceType='Transaction')
          OR (tr.TransactionType='COPY' AND tm.ReferenceType IN ('Copy','Transaction'))
          OR (tr.TransactionType='SERVICE' AND tm.ReferenceType='ServiceTransaction'))
  )
ORDER BY tr.CreatedAt DESC;";
                debtCmd.Parameters.AddWithValue("$date", date.Value.Date.ToString("yyyy-MM-dd"));
                debtCmd.Parameters.AddWithValue("$user", u);
                using var debtReader = debtCmd.ExecuteReader();
                while (debtReader.Read())
                {
                    string type = debtReader.GetString(2);
                    string description = debtReader.IsDBNull(3) ? "" : debtReader.GetString(3);
                    string serviceName = type == "COPY" ? "التصوير"
                        : type == "SALE" ? "المبيعات"
                        : (debtReader.IsDBNull(4) || string.IsNullOrWhiteSpace(debtReader.GetString(4)) ? "خدمات الدفع" : debtReader.GetString(4));
                    grouped.Add(new TreasuryLedgerRow
                    {
                        Time = DateTime.Parse(debtReader.GetString(1)),
                        RefType = type == "COPY" ? "Copy" : type == "SALE" ? "Transaction" : "ServiceTransaction",
                        RefId = debtReader.GetInt64(0),
                        Service = serviceName,
                        Customer = debtReader.IsDBNull(5) ? "" : debtReader.GetString(5),
                        Price = debtReader.GetDecimal(6),
                        Commission = debtReader.GetDecimal(7),
                        Total = debtReader.GetDecimal(8),
                        Paid = debtReader.GetDecimal(9),
                        Remaining = debtReader.GetDecimal(10),
                        Description = "دين مستحق (غير نقدي) — " + description,
                        User = debtReader.GetString(11),
                        DebtOnly = true
                    });
                }
            }

            foreach (var row in grouped.OrderByDescending(x=>x.Time))
            {
                // ترتيب القيم هنا لازم يطابق ترتيب إضافة الأعمدة بالظبط (Time, Description,
                // Service, Customer, Price, Commission, Total, Paid, Remaining, In, Out,
                // Balance, User) بعد نقل عمود التفاصيل ليصبح ثاني عمود.
                int gridRowIndex = grid.Rows.Add(
                    row.Time.ToString("yyyy-MM-dd HH:mm"), row.Description, row.Service, row.Customer,
                    $"{row.Price:F2} ج.م", $"{row.Commission:F2} ج.م", $"{row.Total:F2} ج.م",
                    $"{row.Paid:F2} ج.م", $"{row.Remaining:F2} ج.م",
                    row.Incoming > 0 ? $"{row.Incoming:F2} ج.م" : "",
                    row.Outgoing > 0 ? $"{row.Outgoing:F2} ج.م" : "",
                    row.BalanceAfter.HasValue ? $"{row.BalanceAfter.Value:F2} ج.م" : "—",
                    row.User);
                // نحتفظ ببيانات العملية الحقيقية مع الصف حتى يعمل زر الحذف
                // على العملية المجمعة كلها وليس على أول حركة منها فقط.
                grid.Rows[gridRowIndex].Tag = row;
            }

            if (grid.Rows.Count > 0)
                grid.FirstDisplayedScrollingColumnIndex = 0;
            emptyState.Visible = grid.Rows.Count == 0;
            periodIncomingValue = incoming;
            periodOutgoingValue = outgoing;
            RefreshBalanceSummary();
            var b = AppDatabase.GetTreasuryBuckets(c);
            cashSummary.Text = BucketText("💸 خدمات الكاش", b["خدمات الكاش"]);
            salesSummary.Text = BucketText("📚 المبيعات", b["مبيعات"]);
            printSummary.Text = BucketText("🖨️ الطباعة", b["الطباعة"]);
            paymentSummary.Text = BucketText("💳 خدمات الدفع", b["خدمات الدفع"]);
            otherSummary.Text = BucketText("📦 أخرى", b["أخرى"]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر تحميل الخزينة:\n\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelectedOperation()
    {
        if (grid.SelectedRows.Count == 0)
        {
            MessageBox.Show(this, "برجاء تحديد عملية بيع أو طباعة/تصوير أولًا.", "حذف نهائي", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (grid.SelectedRows[0].Tag is not TreasuryLedgerRow row)
        {
            MessageBox.Show(this, "تعذر تحديد بيانات العملية المحددة.", "حذف نهائي", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool isPrintLog = string.Equals(row.RefType, "PrintLog", StringComparison.OrdinalIgnoreCase) && row.RefId > 0;
        bool isSaleOrCopy = false;
        string transactionType = string.Empty;
        if ((string.Equals(row.RefType, "Transaction", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(row.RefType, "Copy", StringComparison.OrdinalIgnoreCase)) && row.RefId > 0)
        {
            using var checkConnection = AppDatabase.OpenConnection();
            isSaleOrCopy = AppDatabase.TryGetSaleOrCopyTransactionType(checkConnection, row.RefId, out transactionType);
        }

        // حماية متعمدة: لا نحذف السحب/الإيداع أو عمليات الكاش أو الماكينات أو خدمات الدفع من هذه الشاشة.
        if (!isPrintLog && !isSaleOrCopy)
        {
            MessageBox.Show(this,
                "الحذف النهائي من هنا متاح للمبيعات والطباعة/التصوير فقط.\n\n" +
                "السحب والإيداع وحركات النقدية المستقلة وخدمات الدفع والماكينات محمية ولن تُحذف من هذا الزر.",
                "العملية محمية", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string operationName = string.IsNullOrWhiteSpace(row.Description) ? row.Service : row.Description;
        string typeLabel = isPrintLog ? "عملية طباعة" : transactionType == "SALE" ? "عملية بيع" : "عملية تصوير";
        string detail = isPrintLog
            ? "سيُحذف سجل الطباعة والحركة المالية المرتبطة به من الخزينة وسجل الطباعة، وأي دين اتسجل على عميل من هذه الطباعة هيتشال من حسابه."
            : "سيُحذف سجل البيع/التصوير، وحركة الخزينة المرتبطة، وقيد الدين المرتبط بهذه العملية إن وجد.\n" +
              "إذا كان بيعًا جديدًا مرتبطًا بصنف مخزني فستُعاد الكمية للمخزون؛ السجلات القديمة غير المرتبطة بصنف لن تغيّر المخزون تلقائيًا.";

        string amountSummary = isPrintLog
            ? $"قيمة الطباعة المسجلة: {row.Incoming:F2} ج.م"
            : $"الإجمالي: {row.Total:F2} ج.م\nالمدفوع: {row.Paid:F2} ج.م\nالمتبقي: {row.Remaining:F2} ج.م";
        var confirm = MessageBox.Show(this,
            $"هل أنت متأكد من الحذف النهائي؟\n\nالنوع: {typeLabel}\nالتاريخ: {row.Time:yyyy-MM-dd HH:mm}\nالتفاصيل: {operationName}\n{amountSummary}\n\n{detail}\n\nالحذف نهائي ولا يمكن التراجع عنه.",
            "تأكيد الحذف النهائي", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            if (isPrintLog)
            {
                int deleted = PrintLogger.DeleteDatabaseEntry(row.RefId, currentUser);
                if (deleted == 0)
                    MessageBox.Show(this, "سجل الطباعة غير موجود أو تم حذفه بالفعل.", "حذف نهائي", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(this, "تم حذف عملية الطباعة نهائيًا من سجل الطباعة والخزينة، وأي دين مرتبط بيها اتشال من حساب العميل.", "تم الحذف", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                using var connection = AppDatabase.OpenConnection();
                AppDatabase.DeleteSaleOrCopyOperation(connection, row.RefId, currentUser, out string deletedType, out int restoredStock);
                string stockMessage = deletedType == "SALE" && restoredStock > 0
                    ? "\nتمت إعادة الكمية إلى المخزون."
                    : deletedType == "SALE"
                        ? "\nلم تُعدّل كمية المخزون لأن العملية القديمة لا تحتوي على ارتباط محفوظ بصنف مخزني."
                        : string.Empty;
                MessageBox.Show(this,
                    $"تم حذف العملية نهائيًا من السجل المالي وسجل المبيعات/التصوير، وقيد الدين المرتبط بها (إن وجد) اتشال من حساب العميل.{stockMessage}",
                    "تم الحذف", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر حذف العملية:\n\n" + ex.Message, "خطأ في الحذف", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string BuildLedgerDescription(TreasuryLedgerRow row, IEnumerable<string> descriptions)
    {
        string raw = string.Join(" | ", descriptions.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.FileName))
            parts.Add(OperationDetailsForm.FormatFileNameWithPages(row.FileName, row.PageCount));

        // أظهر فئة الطباعة/التصوير الحالية في السطر نفسه حتى يتأكد المستخدم من تعديلها.
        // أوصاف التصوير القديمة قد تحتوي بالفعل على "فئة:"، فلا نكررها.
        if (!string.IsNullOrWhiteSpace(row.PricingCategory) &&
            raw.IndexOf("فئة:", StringComparison.OrdinalIgnoreCase) < 0)
            parts.Add("الفئة: " + row.PricingCategory);

        if (!string.IsNullOrWhiteSpace(raw) &&
            (string.IsNullOrWhiteSpace(row.FileName) ||
             !string.Equals(raw.Trim(), row.FileName.Trim(), StringComparison.OrdinalIgnoreCase)))
            parts.Add(raw);

        return string.Join(" | ", parts);
    }

    private sealed class TreasuryLedgerRow
    {
        public long MovementId; public DateTime Time; public string RefType=""; public long RefId; public string Service=""; public string Customer="";
        public string FileName=""; public int PageCount; public int Copies; public string PricingCategory="";
        public decimal Price; public decimal Commission; public decimal Total; public decimal Paid; public decimal Remaining;
        public decimal MovementAmount; public string MovementType=""; public decimal Incoming; public decimal Outgoing; public string Description=""; public string User="";
        public decimal? BalanceAfter;
        public bool DebtOnly;
    }

    private static string BucketText(string title, AppDatabase.TreasuryBucketSummary s) =>
        $"{title}\nداخل: {s.Incoming:F2} ج.م\nخارج: {s.Outgoing:F2} ج.م\nالصافي: {s.Net:F2} ج.م";
}
