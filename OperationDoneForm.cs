using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// نافذة النجاح الحديثة بعد أي عملية طباعة أو تصوير.
/// - Enter أو زر «حسنًا»: إنهاء العملية كما هي.
/// - مسطرة (Space) أو زر «إضافة شيء آخر»: يظهر مكان إضافة سعر شيء خفيف
///   (ملف، قلم...) مع وصف اختياري والمبلغ المدفوع (فارغ = دفع الحساب كاملًا) والإجمالي.
/// - Backspace (لو مفيش رقم بتمسحه) = إغلاق النافذة زي Esc.
/// - في الطباعة: لو المدفوع أقل من حساب الطباعة، الفرق يتسجل دينًا من نفس اللوحة
///   (بدون نافذة إضافية غير اختيار العميل).
/// </summary>
internal sealed class OperationDoneForm : Form
{
    private readonly decimal baseTotal;
    private readonly decimal alreadyPaid;
    private readonly long? defaultCustomerId;
    private readonly bool allowBaseDebt;
    private readonly bool allowDiscount;
    private readonly decimal maxDiscountPct;
    private readonly TextBox txtDiscPct = new();
    private readonly TextBox txtDiscAmt = new();
    private bool syncingDiscount;
    private bool discountByAmount;
    private decimal discountAmount;
    private decimal discountPercent;

    private readonly Button okButton;
    private readonly Button extraButton;
    private readonly Button confirmButton;
    private readonly Panel extraPanel;
    private readonly TextBox txtExtra = new();
    private readonly TextBox txtDesc = new();
    private readonly TextBox txtPaid = new();
    private readonly TextBox txtGrand = new();
    private readonly Label lblInfo = new();

    private bool extraVisible;
    private int collapsedHeight;
    private readonly int ExtraPanelHeight;

    /// <summary> سعر الشيء الإضافي (0 = لا توجد إضافة). </summary>
    public decimal ExtraAmount { get; private set; }

    /// <summary> الجزء المدفوع من سعر الشيء الإضافي. </summary>
    public decimal ExtraPaid { get; private set; }

    /// <summary> وصف الشيء الإضافي (فارغ = الوصف الافتراضي). </summary>
    public string ExtraDescription { get; private set; } = "";

    /// <summary> خصم على حساب الطباعة نفسه (جنيه) اتكتب بعد ما العملية تمت. </summary>
    public decimal DiscountAmount { get; private set; }

    /// <summary> جزء من حساب الطباعة نفسه لم يُدفع ويُسجل دينًا. </summary>
    public decimal BaseDebt { get; private set; }

    /// <summary> العميل المسجل عليه الدين لو الإضافة غير مدفوعة بالكامل. </summary>
    public long? CustomerId { get; private set; }

    public OperationDoneForm(
        string windowTitle,
        string headline,
        IReadOnlyList<(string Label, string Value)> rows,
        decimal baseTotal,
        decimal alreadyPaid,
        long? defaultCustomerId = null,
        bool allowBaseDebt = false,
        bool allowDiscount = false)
    {
        this.allowBaseDebt = allowBaseDebt;
        this.allowDiscount = allowDiscount;
        maxDiscountPct = Math.Min(100m, Math.Max(0m, PricingStore.Load().MaxPrintDiscountPercent));
        ExtraPanelHeight = 280 + (allowDiscount ? 46 : 0);
        this.baseTotal = baseTotal;
        this.alreadyPaid = alreadyPaid;
        this.defaultCustomerId = defaultCustomerId;

        UiKit.PrepareForm(this, windowTitle, 640, 480);
        MinimumSize = new Size(560, 300);
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.White;
        AutoScroll = false;
        UiKit.EnableEscape(this);

        // ---------- الرأس ----------
        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = headline,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 132, 73),
            BackColor = Color.White
        };

        // ---------- التفاصيل ----------
        var details = BuildDetailsTable(rows);

        // ---------- لوحة الإضافة (مخفية حتى الضغط على مسطرة) ----------
        extraPanel = BuildExtraPanel();
        extraPanel.Visible = false;

        // ---------- الأزرار ----------
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            RightToLeft = RightToLeft.No, // أول زر (حسنًا) يظهر أقصى اليمين
            WrapContents = false,
            Padding = new Padding(30, 12, 30, 8),
            BackColor = Color.White
        };

        okButton = UiKit.Button("حسنًا", UiKit.Primary, 140, 46);
        okButton.Click += (_, _) => FinishWithoutExtra();

        extraButton = UiKit.Button(
            allowDiscount ? "➕ خصم / إضافة شيء / دين  [مسطرة]"
            : allowBaseDebt ? "➕ إضافة شيء آخر / دين  [مسطرة]"
            : "➕ إضافة شيء آخر  [مسطرة]",
            Color.FromArgb(52, 73, 94), 340, 46);
        extraButton.Click += (_, _) => ShowExtra();

        confirmButton = UiKit.Button("✔ تأكيد الإضافة", UiKit.Success, 190, 46);
        confirmButton.Click += (_, _) => ConfirmExtra();
        confirmButton.Visible = false;

        okButton.RightToLeft = extraButton.RightToLeft = confirmButton.RightToLeft = RightToLeft.Yes;
        footer.Controls.Add(okButton);
        footer.Controls.Add(extraButton);
        footer.Controls.Add(confirmButton);

        // ---------- التجميع: جدول واحد بدل Dock متعدد لتفادي تداخل العناصر ----------
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(details, 0, 1);
        root.Controls.Add(extraPanel, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);

        collapsedHeight = 78 + rows.Count * 36 + 12 + 74 + 40;
        ClientSize = new Size(640, collapsedHeight);

        AcceptButton = okButton;
        Shown += (_, _) => okButton.Focus();
    }

    /// <summary> جدول التفاصيل (التسمية يمين والقيمة شمال) يُستخدم أيضًا في نافذة تأكيد الطباعة. </summary>
    internal static TableLayoutPanel BuildDetailsTable(IReadOnlyList<(string Label, string Value)> rows)
    {
        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows.Count,
            Padding = new Padding(35, 6, 35, 6),
            BackColor = Color.White,
            RightToLeft = RightToLeft.Yes
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        for (int i = 0; i < rows.Count; i++)
        {
            details.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            details.Controls.Add(new Label
            {
                Text = rows[i].Label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
            }, 0, i);
            details.Controls.Add(new Label
            {
                Text = rows[i].Value,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10.5F)
            }, 1, i);
        }
        return details;
    }

    private Panel BuildExtraPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = ExtraPanelHeight,
            BackColor = Color.FromArgb(244, 246, 249),
            Padding = new Padding(35, 12, 35, 8),
            Margin = new Padding(20, 0, 20, 0)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = allowDiscount ? 6 : 5,
            BackColor = Color.Transparent,
            RightToLeft = RightToLeft.Yes
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        if (allowDiscount)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        void AddRow(int row, string label, Control input)
        {
            grid.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
            }, 0, row);
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            grid.Controls.Add(input, 1, row);
        }

        StyleInput(txtExtra);
        txtExtra.PlaceholderText = allowBaseDebt ? "اتركه فارغًا لو دين فقط" : "مثال: 5";
        txtExtra.TextChanged += (_, _) => RefreshTotals();

        StyleInput(txtPaid);
        txtPaid.PlaceholderText = "فارغ = دفع الحساب كاملًا";
        txtPaid.TextChanged += (_, _) => RefreshTotals();

        txtGrand.ReadOnly = true;
        txtGrand.TabStop = false;
        txtGrand.RightToLeft = RightToLeft.No;
        txtGrand.TextAlign = HorizontalAlignment.Center;
        txtGrand.BorderStyle = BorderStyle.FixedSingle;
        txtGrand.BackColor = Color.White;
        txtGrand.ForeColor = UiKit.Primary;
        txtGrand.Font = new Font("Segoe UI", 16F, FontStyle.Bold);

        txtDesc.RightToLeft = RightToLeft.Yes;
        txtDesc.Font = new Font("Segoe UI", 11F);
        txtDesc.BorderStyle = BorderStyle.FixedSingle;
        txtDesc.PlaceholderText = "اختياري — لو فاضي: إضافة سريعة مع طباعة";

        int o = 0;
        if (allowDiscount)
        {
            StyleInput(txtDiscPct);
            StyleInput(txtDiscAmt);
            txtDiscPct.PlaceholderText = $"% (حد {maxDiscountPct:0.##})";
            txtDiscAmt.PlaceholderText = "ج.م";
            txtDiscPct.TextChanged += (_, _) => { if (!syncingDiscount) { discountByAmount = false; RefreshTotals(); } };
            txtDiscAmt.TextChanged += (_, _) => { if (!syncingDiscount) { discountByAmount = true; RefreshTotals(); } };

            var discRow = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Height = 38,
                BackColor = Color.Transparent,
                RightToLeft = RightToLeft.No
            };
            discRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            discRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            txtDiscPct.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtDiscAmt.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            discRow.Controls.Add(txtDiscPct, 0, 0);
            discRow.Controls.Add(txtDiscAmt, 1, 0);
            AddRow(0, "الخصم (% / ج.م)", discRow);
            o = 1;
        }

        AddRow(0 + o, "سعر الشيء الإضافي", txtExtra);
        AddRow(1 + o, "وصف الشيء (اختياري)", txtDesc);
        AddRow(2 + o, "المبلغ المدفوع", txtPaid);
        AddRow(3 + o, "الحساب الإجمالي", txtGrand);

        lblInfo.Dock = DockStyle.Fill;
        lblInfo.TextAlign = ContentAlignment.MiddleCenter;
        lblInfo.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblInfo.ForeColor = UiKit.Muted;
        grid.Controls.Add(lblInfo, 0, 4 + o);
        grid.SetColumnSpan(lblInfo, 2);

        panel.Controls.Add(grid);
        return panel;
    }

    private static void StyleInput(TextBox box)
    {
        box.RightToLeft = RightToLeft.No;
        box.TextAlign = HorizontalAlignment.Center;
        box.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    // ============================================================
    // مسطرة = إضافة شيء آخر (إلا لو كتابة داخل خانة، فتُكتب مسافة عادية)
    // ============================================================
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Shortcuts.Matches("op.extra", keyData) && !extraVisible)
        {
            ShowExtra();
            return true;
        }

        // Backspace: لو مش بتمسح رقم (اللوحة مقفولة أو الخانة الرقمية فاضية) يقفل النافذة زي Esc
        if (Shortcuts.Matches("global.back", keyData))
        {
            bool erasingNumber =
                (txtExtra.Focused && txtExtra.TextLength > 0) ||
                (txtPaid.Focused && txtPaid.TextLength > 0) ||
                (txtDiscPct.Focused && txtDiscPct.TextLength > 0) ||
                (txtDiscAmt.Focused && txtDiscAmt.TextLength > 0) ||
                txtDesc.Focused;

            if (!erasingNumber)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>ينهي النافذة بدون بيع إضافي، مع الاحتفاظ بأي خصم/تسوية للطباعة أُدخلت.</summary>
    private void FinishWithoutExtra()
    {
        if (!extraVisible)
        {
            ExtraAmount = 0;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        ResolveDiscount();
        bool hasDiscount = discountAmount > 0m;
        bool hasPaidValue = !string.IsNullOrWhiteSpace(txtPaid.Text);

        // هذا الزر يعني «من غير إضافة شيء»: تجاهل سعر/وصف الإضافة إن كانا مكتوبين،
        // لكن لا تُسقط الخصم المكتوب ولا مبلغ التسوية/الدين على عملية الطباعة نفسها.
        if (!hasDiscount && !hasPaidValue)
        {
            ExtraAmount = 0;
            ExtraPaid = 0;
            DiscountAmount = 0;
            BaseDebt = 0;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (!string.IsNullOrEmpty(txtExtra.Text))
            txtExtra.Text = string.Empty;
        ConfirmExtra();
    }

    private void ShowExtra()
    {
        if (extraVisible) return;
        extraVisible = true;

        extraPanel.Visible = true;
        extraButton.Visible = false;
        okButton.Text = "إنهاء بدون إضافة";
        okButton.Width = 190;
        okButton.MinimumSize = new Size(190, 36);
        confirmButton.Visible = true;
        AcceptButton = confirmButton;

        ClientSize = new Size(ClientSize.Width, collapsedHeight + ExtraPanelHeight);
        // نعيد التمركز بحيث تظل النافذة كاملة على الشاشة
        Rectangle work = Screen.FromControl(this).WorkingArea;
        if (Bottom > work.Bottom) Top = Math.Max(work.Top, work.Bottom - Height);

        RefreshTotals();
        if (allowDiscount) { txtDiscPct.Focus(); txtDiscPct.SelectAll(); }
        else { txtExtra.Focus(); txtExtra.SelectAll(); }
    }

    // ============================================================
    // الحسابات
    // ============================================================
    private static string NormalizeDigits(string text)
    {
        var chars = text.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c >= '٠' && c <= '٩') chars[i] = (char)('0' + (c - '٠'));
            else if (c >= '۰' && c <= '۹') chars[i] = (char)('0' + (c - '۰'));
            else if (c == '٫' || c == '،') chars[i] = '.';
        }
        return new string(chars).Replace("٬", "").Replace(",", ".");
    }

    private static bool TryParseMoney(string text, out decimal value)
    {
        return decimal.TryParse(NormalizeDigits(text), NumberStyles.Number,
            CultureInfo.InvariantCulture, out value);
    }

    private decimal ReadExtra()
        => TryParseMoney(txtExtra.Text, out var v) && v > 0 ? v : 0m;

    /// <summary> اللي اتدفع فعلًا للطباعة بعد الخصم (الخصم بيقلل اللي في الخزينة). </summary>
    private decimal EffectivePaid => alreadyPaid - discountAmount;

    private static string ShortNumber(decimal v) => v <= 0 ? "" : Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static void SetBoxText(TextBox box, string text)
    {
        if (box.Text == text) return;
        box.Text = text;
        box.SelectionStart = box.TextLength;
    }

    /// <summary> الخصم نسبة ⇄ جنيه لحظيًا، ومحكوم بالحد الأقصى من الإدارة. </summary>
    private void ResolveDiscount()
    {
        if (!allowDiscount) return;

        decimal typedPct = TryParseMoney(txtDiscPct.Text, out var p) && p > 0 ? p : 0m;
        decimal typedAmt = TryParseMoney(txtDiscAmt.Text, out var a) && a > 0 ? a : 0m;
        var d = DiscountMath.Resolve(baseTotal, discountByAmount, typedPct, typedAmt, maxDiscountPct);

        syncingDiscount = true;
        try
        {
            if (discountByAmount)
            {
                if (d.Clamped) SetBoxText(txtDiscAmt, ShortNumber(d.Amount));
                SetBoxText(txtDiscPct, ShortNumber(d.Percent));
            }
            else
            {
                if (d.Clamped) SetBoxText(txtDiscPct, ShortNumber(d.Percent));
                SetBoxText(txtDiscAmt, ShortNumber(d.Amount));
            }
        }
        finally { syncingDiscount = false; }

        discountAmount = d.Amount;
        discountPercent = d.Percent;
    }

    private void RefreshTotals()
    {
        ResolveDiscount();
        decimal extra = ReadExtra();
        decimal grand = baseTotal - discountAmount + extra;
        txtGrand.Text = $"{grand:F2} ج.م";

        if (string.IsNullOrWhiteSpace(txtPaid.Text))
        {
            lblInfo.ForeColor = UiKit.Muted;
            lblInfo.Text = "المدفوع فارغ: الزبون دفع الحساب كاملًا";
            return;
        }

        if (!TryParseMoney(txtPaid.Text, out decimal paid) || paid < 0)
        {
            lblInfo.ForeColor = UiKit.Danger;
            lblInfo.Text = "المبلغ المدفوع غير صحيح";
            return;
        }

        decimal diff = paid - (EffectivePaid + extra);
        if (diff < 0)
        {
            lblInfo.ForeColor = UiKit.Danger;
            lblInfo.Text = $"المتبقي / الدين: {-diff:F2} ج.م";
        }
        else if (diff > 0)
        {
            lblInfo.ForeColor = UiKit.SuccessText;
            lblInfo.Text = $"الباقي للعميل: {diff:F2} ج.م";
        }
        else
        {
            lblInfo.ForeColor = UiKit.SuccessText;
            lblInfo.Text = "المبلغ مطابق للحساب";
        }
    }

    private void ConfirmExtra()
    {
        if (!extraVisible) return;

        ResolveDiscount();

        decimal extra = 0;
        if (!string.IsNullOrWhiteSpace(txtExtra.Text) &&
            (!TryParseMoney(txtExtra.Text, out extra) || extra < 0))
        {
            MessageBox.Show(this, "سعر الشيء الإضافي غير صحيح.", "تنبيه",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtExtra.Focus();
            return;
        }

        bool paidBlank = string.IsNullOrWhiteSpace(txtPaid.Text);
        if (extra == 0 && discountAmount == 0 && (!allowBaseDebt || paidBlank))
        {
            MessageBox.Show(this,
                allowBaseDebt
                    ? "اكتب الخصم، أو سعر الشيء الإضافي، أو المبلغ المدفوع لو في دين على الطباعة."
                    : "اكتب سعر الشيء الإضافي (رقم أكبر من صفر).",
                "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtExtra.Focus();
            return;
        }

        decimal paidTotal;
        if (paidBlank)
        {
            paidTotal = EffectivePaid + extra; // فارغ = دفع كامل
        }
        else if (!TryParseMoney(txtPaid.Text, out paidTotal) || paidTotal < 0)
        {
            MessageBox.Show(this, "المبلغ المدفوع غير صحيح.", "تنبيه",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtPaid.Focus();
            return;
        }

        decimal basePaid = EffectivePaid;
        if (paidTotal < EffectivePaid)
        {
            if (!allowBaseDebt)
            {
                MessageBox.Show(this,
                    $"المبلغ المدفوع أقل من المبلغ المسجل مدفوعًا للعملية ({EffectivePaid:F2} ج.م).",
                    "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPaid.Focus();
                return;
            }
            basePaid = paidTotal; // الدفعة تغطي الطباعة أولًا
        }

        decimal baseDebt = EffectivePaid - basePaid;
        decimal extraPaid = Math.Min(extra, Math.Max(0m, paidTotal - basePaid));
        decimal extraDebt = extra - extraPaid;

        if (extra == 0 && baseDebt == 0 && discountAmount == 0)
        {
            MessageBox.Show(this, "المبلغ المدفوع يغطي الحساب كاملًا، لا يوجد دين ولا إضافة.", "تنبيه",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        long? customer = defaultCustomerId;

        // أي جزء غير مدفوع (من الطباعة أو الإضافة) يُسجل دينًا على عميل
        if (baseDebt + extraDebt > 0 && customer == null)
        {
            using var picker = new CustomerPickerForm();
            if (picker.ShowDialog(this) != DialogResult.OK || picker.CustomerId == null)
                return;
            customer = picker.CustomerId;
        }

        DiscountAmount = discountAmount;
        ExtraAmount = extra;
        ExtraPaid = extraPaid;
        BaseDebt = baseDebt;
        ExtraDescription = txtDesc.Text.Trim();
        CustomerId = customer;
        DialogResult = DialogResult.OK;
        Close();
    }

    // ============================================================
    // تسجيل نتيجة النافذة: دين الطباعة (لو وُجد) ثم الشيء الإضافي كبيع عادي
    // ============================================================
    public static void ApplyResult(
        string userName,
        string context,
        OperationDoneForm dialog,
        IReadOnlyList<long>? printLogIds = null)
    {
        // كل ما ينتج عن إنهاء الطباعة (الخصم والدين والإضافة) يُحفظ في معاملة واحدة.
        // لو أي خطوة فشلت، يتم التراجع عن الخطوات المالية السابقة بدل ترك عملية ناقصة.
        long[] ids = (printLogIds ?? Array.Empty<long>()).Distinct().ToArray();
        if (dialog.BaseDebt > 0 && dialog.CustomerId == null)
            throw new InvalidOperationException("يجب اختيار عميل لتسجيل الدين.");

        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        var csvPriceUpdates = new List<(PrintLogger.LogEntry Entry, decimal OldPrice, decimal NewPrice)>();

        if (dialog.DiscountAmount > 0)
            PrintDiscountService.ApplyWithinTransaction(
                connection, tx, userName, ids, dialog.DiscountAmount, csvPriceUpdates);

        if (dialog.BaseDebt > 0)
            PrintDebtService.ApplyWithinTransaction(
                connection, tx, userName, ids, dialog.BaseDebt, dialog.CustomerId!.Value,
                $"دين {context}");

        if (dialog.ExtraAmount > 0)
        {
            string description = string.IsNullOrWhiteSpace(dialog.ExtraDescription)
                ? $"إضافة سريعة مع {context}"
                : dialog.ExtraDescription;

            SalesService.CompleteSaleWithinTransaction(
                connection, tx, userName, description,
                dialog.ExtraAmount, 0m, dialog.ExtraAmount,
                dialog.ExtraPaid, dialog.CustomerId, null,
                out _, out _);
        }

        tx.Commit();
        // ملف CSV نسخة عرض/توافق لبعض الشاشات؛ قاعدة البيانات أصبحت ملتزمة بالفعل.
        // فشل تحديث CSV لا ينبغي أن يعيد أو يكرر أي حركة مالية.
        PrintDiscountService.UpdateCsvPrices(csvPriceUpdates);
    }

}

/// <summary>
/// خصم بعد الطباعة: بيوزّع مبلغ الخصم على ملفات الطباعة (بنسبة سعر كل ملف)،
/// ويقلل سعرها في سجل الطباعة وحركات الخزينة، فالتقارير والدرج يبقوا على السعر بعد الخصم.
/// </summary>
internal static class PrintDiscountService
{
    public static void Apply(string userName, IReadOnlyList<long> printLogIds, decimal discount)
    {
        if (discount <= 0) return;
        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        var csvPriceUpdates = new List<(PrintLogger.LogEntry Entry, decimal OldPrice, decimal NewPrice)>();
        ApplyWithinTransaction(connection, tx, userName, printLogIds, discount, csvPriceUpdates);
        tx.Commit();
        UpdateCsvPrices(csvPriceUpdates);
    }

    internal static void ApplyWithinTransaction(
        SqliteConnection connection,
        SqliteTransaction tx,
        string userName,
        IReadOnlyList<long> printLogIds,
        decimal discount,
        List<(PrintLogger.LogEntry Entry, decimal OldPrice, decimal NewPrice)> csvPriceUpdates)
    {
        if (discount <= 0) return;
        if (printLogIds == null || printLogIds.Count == 0)
            throw new InvalidOperationException("مفيش سجل طباعة لتطبيق الخصم عليه.");

        var logs = new List<(long Id, decimal Price, PrintLogger.LogEntry Entry)>();
        foreach (long id in printLogIds.Distinct())
        {
            using var read = connection.CreateCommand();
            read.Transaction = tx;
            read.CommandText = @"
SELECT p.Timestamp,COALESCE(p.FileName,''),COALESCE(p.FileType,''),p.Pages,p.Copies,
       COALESCE(p.ColorType,''),COALESCE(p.DuplexMode,''),p.PhysicalSheets,p.Price,u.UserName
FROM PrintLog p JOIN AppUsers u ON u.Id=p.UserId WHERE p.Id=$id LIMIT 1";
            read.Parameters.AddWithValue("$id", id);
            using var reader = read.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException($"سجل الطباعة رقم {id} غير موجود، لم يتم تطبيق الخصم.");

            DateTime timestamp = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            timestamp = new DateTime(timestamp.Year, timestamp.Month, timestamp.Day,
                timestamp.Hour, timestamp.Minute, timestamp.Second);
            decimal price = Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture);
            var entry = new PrintLogger.LogEntry
            {
                Timestamp = timestamp,
                FileName = reader.GetString(1),
                FileType = reader.GetString(2),
                Pages = reader.GetInt32(3),
                Copies = reader.GetInt32(4),
                ColorType = reader.GetString(5),
                DuplexMode = reader.GetString(6),
                PhysicalSheets = reader.GetInt32(7),
                Price = price,
                UserName = reader.GetString(9)
            };
            logs.Add((id, price, entry));
        }

        decimal total = logs.Sum(l => l.Price);
        if (total <= 0)
            throw new InvalidOperationException("تعذر تطبيق الخصم: إجمالي الطباعة صفر.");

        decimal maxPct = PricingStore.Load().MaxPrintDiscountPercent;
        if (!DiscountMath.IsWithinMax(total, discount, maxPct))
            throw new InvalidOperationException($"أقصى خصم مسموح من الإدارة للطباعة والتصوير هو {maxPct:0.##}%.");
        if (discount > total)
            throw new InvalidOperationException("الخصم أكبر من إجمالي الطباعة.");

        decimal allocated = 0m;
        for (int i = 0; i < logs.Count; i++)
        {
            var (id, price, csvEntry) = logs[i];
            decimal share = i == logs.Count - 1
                ? discount - allocated
                : Math.Round(discount * price / total, 2, MidpointRounding.AwayFromZero);
            share = Math.Min(price, Math.Max(0m, share));
            allocated += share;
            if (share <= 0m) continue;

            using (var upd = connection.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE PrintLog SET Price=$p WHERE Id=$id";
                upd.Parameters.AddWithValue("$p", price - share);
                upd.Parameters.AddWithValue("$id", id);
                if (upd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("تعذر تحديث سعر سجل الطباعة، لم يتم تطبيق الخصم.");
            }
            csvPriceUpdates.Add((csvEntry, price, price - share));

            var movements = new List<(long Id, decimal Amount)>();
            using (var m = connection.CreateCommand())
            {
                m.Transaction = tx;
                m.CommandText = "SELECT Id, Amount FROM TreasuryMovements WHERE ReferenceType='PrintLog' AND ReferenceId=$id AND MovementType='IN'";
                m.Parameters.AddWithValue("$id", id);
                using var reader = m.ExecuteReader();
                while (reader.Read())
                    movements.Add((reader.GetInt64(0), Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture)));
            }

            decimal left = share;
            foreach (var (mid, amount) in movements)
            {
                if (left <= 0.0049m) break;
                decimal cut = Math.Min(left, amount);
                using var write = connection.CreateCommand();
                write.Transaction = tx;
                if (amount - cut <= 0.0049m)
                    write.CommandText = "DELETE FROM TreasuryMovements WHERE Id=$mid";
                else
                {
                    write.CommandText = "UPDATE TreasuryMovements SET Amount=$amount WHERE Id=$mid";
                    write.Parameters.AddWithValue("$amount", amount - cut);
                }
                write.Parameters.AddWithValue("$mid", mid);
                write.ExecuteNonQuery();
                left -= cut;
            }

            if (left > 0.0049m)
                throw new InvalidOperationException("تعذر تعديل حركة الخزينة الخاصة بالطباعة، لم يتم تطبيق الخصم.");
        }

        AppDatabase.AddAuditLog(connection, tx, userName, "خصم", "طباعة/تصوير", null,
            $"قيمة الخصم: {discount:0.00} | أرقام سجلات الطباعة: {string.Join(",", printLogIds)}");
    }

    internal static void UpdateCsvPrices(
        IEnumerable<(PrintLogger.LogEntry Entry, decimal OldPrice, decimal NewPrice)> csvPriceUpdates)
    {
        foreach (var update in csvPriceUpdates)
        {
            try
            {
                if (!PrintLogger.UpdateCsvPrice(update.Entry, update.OldPrice, update.NewPrice))
                    AppLog.Info($"تعذر مطابقة سطر سجل الطباعة لتحديث CSV بعد الخصم: {update.Entry.FileName}");
            }
            catch (Exception ex)
            {
                AppLog.Error("تم تطبيق خصم الطباعة بقاعدة البيانات لكن تعذر تحديث CSV الخاص بسجل العمليات", ex);
            }
        }
    }
}

/// <summary>
/// تحويل جزء من مبلغ الطباعة (المسجل في الخزينة كمدفوع كامل وقت الطباعة) إلى دين على عميل:
/// يخصم الجزء غير المدفوع من حركات الخزينة الخاصة بالطباعة ويسجل قيد دين في سجل الديون.
/// </summary>
internal static class PrintDebtService
{
    public static void Apply(string userName, IReadOnlyList<long> printLogIds, decimal debt, long customerId, string note)
    {
        if (debt <= 0) return;
        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        ApplyWithinTransaction(connection, tx, userName, printLogIds, debt, customerId, note);
        tx.Commit();
    }

    internal static void ApplyWithinTransaction(
        SqliteConnection connection,
        SqliteTransaction tx,
        string userName,
        IReadOnlyList<long> printLogIds,
        decimal debt,
        long customerId,
        string note)
    {
        if (debt <= 0) return;
        if (printLogIds == null || printLogIds.Count == 0)
            throw new InvalidOperationException("مفيش سجل طباعة يمكن تحويل جزء منه إلى دين.");

        long userId = AppDatabase.GetUserId(connection, userName, tx);
        decimal left = debt;
        var allocations = new List<(long PrintLogId, decimal Amount)>();
        for (int i = printLogIds.Count - 1; i >= 0 && left > 0.0049m; i--)
        {
            var movements = new List<(long Id, decimal Amount)>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT Id, Amount FROM TreasuryMovements WHERE ReferenceType='PrintLog' AND ReferenceId=$id AND MovementType='IN'";
                read.Parameters.AddWithValue("$id", printLogIds[i]);
                using var reader = read.ExecuteReader();
                while (reader.Read())
                    movements.Add((reader.GetInt64(0), Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture)));
            }

            foreach (var (id, amount) in movements)
            {
                if (left <= 0.0049m) break;
                decimal cut = Math.Min(left, amount);
                using var write = connection.CreateCommand();
                write.Transaction = tx;
                if (amount - cut <= 0.0049m)
                    write.CommandText = "DELETE FROM TreasuryMovements WHERE Id=$id";
                else
                {
                    write.CommandText = "UPDATE TreasuryMovements SET Amount=$amount WHERE Id=$id";
                    write.Parameters.AddWithValue("$amount", amount - cut);
                }
                write.Parameters.AddWithValue("$id", id);
                write.ExecuteNonQuery();
                left -= cut;
                allocations.Add((printLogIds[i], cut));
            }
        }

        if (left > 0.0049m)
            throw new InvalidOperationException("قيمة الدين أكبر من المدفوع المسجل في حركات الطباعة، لم يتم حفظ العملية.");

        decimal balance = AppDatabase.GetCustomerDebt(connection, customerId, tx) + debt;
        using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"
INSERT INTO Debts_History(CustomerId,TransactionId,UserId,Type,Amount,BalanceAfter,Note,CreatedAt)
VALUES($customer,NULL,$user,'DEBT',$amount,$balance,$note,$created);
SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$customer", customerId);
        insert.Parameters.AddWithValue("$user", userId);
        insert.Parameters.AddWithValue("$amount", debt);
        insert.Parameters.AddWithValue("$balance", balance);
        insert.Parameters.AddWithValue("$note", note);
        insert.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        long debtId = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);

        foreach (var (printLogId, amount) in allocations)
        {
            using var link = connection.CreateCommand();
            link.Transaction = tx;
            link.CommandText = "INSERT INTO PrintDebtLinks(DebtId,PrintLogId,Amount) VALUES($d,$p,$a)";
            link.Parameters.AddWithValue("$d", debtId);
            link.Parameters.AddWithValue("$p", printLogId);
            link.Parameters.AddWithValue("$a", amount);
            link.ExecuteNonQuery();
        }

        AppDatabase.AddAuditLog(connection, tx, userName, "تحويل إلى دين", "طباعة/تصوير", customerId,
            $"قيمة الدين: {debt:0.00} | العميل: {customerId} | الملاحظة: {note} | أرقام سجلات الطباعة: {string.Join(",", printLogIds)}");
    }
}
