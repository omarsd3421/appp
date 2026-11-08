using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal sealed class SalesForm : Form
{
    private readonly string currentUser;
    private bool isSavingTransaction;
    private TextBox product = null!;
    private TextBox barcode = null!;
    private NumericUpDown originalPrice = null!;
    private NumericUpDown qty = null!;
    private Button btnInvoice = null!;
    private Button btnCancelInvoice = null!;
    private bool sanitizingQty;
    private NumericUpDown discount = null!;
    private NumericUpDown required = null!;
    private NumericUpDown paid = null!;
    private Label remaining = null!;
    private Label change = null!;
    private long? selectedCustomer;
    private long? selectedProductId;
    private NumericUpDown discountAmt = null!;
    private Label discountLabel = null!;
    private bool syncingDiscount;
    private bool discountByAmount;
    private decimal maxSalesPct;
    private decimal currentDiscountPercent;
    private bool loadingProduct;
    private bool paidEdited;
    private bool resetting;
    // آخر باركود تم البحث عنه — عشان خروج المؤشر من الخانة (Leave) مايعيدش البحث
    // ويصفّر الخصم/المبلغ كل مرة، وعشان نعرف إن كان في باركود لسه ماتحملش.
    private string? loadedBarcode;

    public SalesForm(string userName)
    {
        currentUser = userName;
        BuildUi();
        InputFocusHelper.Apply(this);
        RecalculateRequired();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"📚 المبيعات — المنفذ: {currentUser}", 900, 750);
        UiKit.EnableEscape(this);

        var header = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Color.White };
        header.Controls.Add(UiKit.Title($"📚 نقطة البيع — المنفذ: {currentUser}", 18));

        // زر «ابدأ فاتورة» فوق شمال الشاشة. الإحداثيات هنا داخل الـPanel (مش مقلوبة).
        btnInvoice = UiKit.Button("🧾 ابدأ فاتورة", Color.FromArgb(0, 121, 107), 160, 40);
        btnInvoice.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        btnInvoice.Location = new Point(14, 12);
        btnInvoice.Click += (s, e) => InvoiceButton_Click();
        btnCancelInvoice = UiKit.Button("✖", Color.FromArgb(150, 60, 60), 40, 40);
        btnCancelInvoice.MinimumSize = new Size(40, 36);
        btnCancelInvoice.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        btnCancelInvoice.Location = new Point(14 + 160 + 6, 12);
        btnCancelInvoice.Click += (s, e) => CancelInvoice_Click();
        header.Controls.Add(btnInvoice);
        header.Controls.Add(btnCancelInvoice);
        btnInvoice.BringToFront();
        btnCancelInvoice.BringToFront();
        RefreshInvoiceButtons();
        Controls.Add(header);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(55, 16, 55, 10),
            ColumnCount = 2,
            RowCount = 10
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        for (int i = 0; i < 10; i++)
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 10f));

        product = new TextBox { Dock = DockStyle.Fill, TabIndex = 0 };
        barcode = new TextBox { Dock = DockStyle.Fill, TabIndex = 1 };
        originalPrice = NumberBox(3);
        qty = NumberBox(4);
        discount = NumberBox(5);
        discountAmt = NumberBox(6);
        required = NumberBox(7);
        paid = NumberBox(8);

        // العدد: رقم صحيح فقط (مفيش 1.5)، والافتراضي 1.
        qty.DecimalPlaces = 0;
        qty.Increment = 1;
        qty.Minimum = 1;
        qty.Maximum = 100000;
        qty.Value = 1;

        // السعر قبل الخصم يملأ تلقائيًا من الصنف/الباركود، لكنه يظل قابلًا للتحرير في البيع اليدوي.
        originalPrice.ReadOnly = false;
        originalPrice.Enabled = true;

        required.Minimum = 0;
        required.Value = 0;

        // «السعر المطلوب بعد الخصم» ناتج حسابي فقط: مقفول ومينفعش يتعدل يدويًا.
        required.ReadOnly = true;
        required.TabStop = false;
        required.Increment = 0;
        required.Controls[0].Visible = false;      // أزرار الزيادة/النقصان
        required.BackColor = Color.FromArgb(232, 235, 240);
        required.Cursor = Cursors.Default;

        discount.Increment = 0.5m;
        discountAmt.Increment = 1m;

        remaining = ResultLabel(Color.FromArgb(192, 57, 43));
        change = ResultLabel(Color.FromArgb(30, 132, 73));

        Add(body, "اسم الصنف / الكتاب", product, 0);
        Add(body, "الباركود (اختياري)", barcode, 1);
        Add(body, "السعر قبل الخصم", originalPrice, 2);
        Add(body, "العدد", qty, 3);
        discountLabel = Add(body, "الخصم (%)", discount, 4);
        Add(body, "الخصم (ج.م)", discountAmt, 5);
        Add(body, "السعر المطلوب بعد الخصم", required, 6);
        Add(body, "المبلغ المدفوع", paid, 7);
        Add(body, "المتبقي على العميل", remaining, 8);
        Add(body, "الباقي للعميل", change, 9);
        RefreshMaxDiscount();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.White, Padding = new Padding(35, 12, 35, 12) };
        var save = UiKit.Button("✅ إتمام البيع", UiKit.Success, 220, 46);
        save.Dock = DockStyle.Right;
        save.Click += Save_Click;
        footer.Controls.Add(save);

        // ترتيب الـDock: الـFill أولًا، ثم الشريط السفلي، ثم الرأس (الأعلى) آخرًا،
        // وبكده الصف الأول (اسم الصنف) مبقاش مخفي تحت الرأس.
        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        AcceptButton = save;

        barcode.KeyDown += Barcode_KeyDown;
        barcode.Leave += (s, e) => LoadByBarcode(false);
        // تحديث فوري أثناء الكتابة نفسها، بدون لمس Value أثناء وجود المؤشر داخل الخانة
        // حتى لا يتحرك الـ caret أو تتعكس الأرقام.
        originalPrice.TextChanged += (s, e) => Recalc();
        qty.KeyPress += Qty_KeyPress;
        qty.TextChanged += (s, e) => { SanitizeQty(); Recalc(); };
        qty.Leave += (s, e) => NormalizeQty();
        qty.ValueChanged += (s, e) => Recalc();
        discount.TextChanged += (s, e) => OnDiscountPercentEdited();
        discountAmt.TextChanged += (s, e) => OnDiscountAmountEdited();
        required.TextChanged += (s, e) => UpdateAmountsFromEditors();
        paid.TextChanged += (s, e) =>
        {
            if (!resetting)
                paidEdited = true;
            UpdateAmountsFromEditors();
        };

        // يبقى ValueChanged للتغييرات القادمة من أزرار الزيادة/النقصان أو الاختيار البرمجي.
        originalPrice.ValueChanged += (s, e) => Recalc();
        discount.ValueChanged += (s, e) => OnDiscountPercentEdited();
        discountAmt.ValueChanged += (s, e) => OnDiscountAmountEdited();
        required.ValueChanged += (s, e) => UpdateAmountsFromControls();
        paid.ValueChanged += (s, e) =>
        {
            if (!resetting) paidEdited = true;
            UpdateAmountsFromControls();
        };
        product.Leave += (s, e) => UpdateDescriptionFromProduct();
    }

    private static NumericUpDown NumberBox(int tabIndex) => new()
    {
        Dock = DockStyle.Fill,
        DecimalPlaces = 2,
        Minimum = 0,
        Maximum = 100000000,
        TabIndex = tabIndex,
        TextAlign = HorizontalAlignment.Center
    };

    private static Label ResultLabel(Color color) => new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 12F, FontStyle.Bold),
        ForeColor = color
    };

    private static Label Add(TableLayoutPanel p, string label, Control c, int row)
    {
        var lbl = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
        };
        p.Controls.Add(lbl, 0, row);
        p.Controls.Add(c, 1, row);
        return lbl;
    }

    private void Barcode_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            LoadByBarcode(true, true);
        }
    }

    /// <summary>
    /// تحميل الصنف من الباركود.
    /// showErrors: يُظهر رسالة «لم يتم العثور على صنف».
    /// warnStock : يُظهر تنبيه المخزون الصفري. مش بنظهره من حدث Leave أبدًا، لأن
    ///             رسالة modal وقت ضغط زر «إتمام» كانت بتبلع الضغطة الأولى
    ///             ولازم المستخدم يدوس مرتين. تنبيه المخزون بيظهر مرة واحدة وقت الإتمام.
    /// </summary>
    private void LoadByBarcode(bool showErrors, bool warnStock = false)
    {
        string code = barcode.Text.Trim();
        if (code.Length == 0)
        {
            loadedBarcode = null;
            return;
        }

        // نفس الباركود اتحمّل قبل كده (خروج عادي من الخانة) — مفيش داعي نعيد ونصفّر الخصم.
        if (!showErrors && string.Equals(code, loadedBarcode, StringComparison.Ordinal))
            return;

        try
        {
            AppDatabase.Initialize();
            using var c = AppDatabase.OpenConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Name, SalePrice, Quantity
FROM Products
WHERE Barcode = $b
LIMIT 1";
            cmd.Parameters.AddWithValue("$b", code);

            using var r = cmd.ExecuteReader();
            loadedBarcode = code;
            if (!r.Read())
            {
                // لو كان في صنف متحمّل قبل كده نصفّر سعره، لكن لو المستخدم كاتب سعر يدوي
                // وباركود مش موجود ما نمسحش السعر اللي كتبه.
                bool hadProduct = selectedProductId.HasValue;
                selectedProductId = null;
                if (hadProduct)
                    originalPrice.Value = 0;
                if (showErrors)
                    MessageBox.Show(this, "لم يتم العثور على صنف بهذا الباركود.", "البحث", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            loadingProduct = true;
            selectedProductId = r.GetInt64(0);
            product.Text = r.GetString(1);
            originalPrice.Value = Convert.ToDecimal(r.GetValue(2), CultureInfo.InvariantCulture);
            required.Value = Math.Min(required.Maximum, originalPrice.Value);
            discount.Value = 0;
            discountAmt.Value = 0;
            discountByAmount = false;
            loadingProduct = false;
            RecalculateRequired();

            if (warnStock && Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture) <= 0)
            {
                MessageBox.Show(this, ZeroStockMessage, "المخزون", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            loadingProduct = false;
            if (showErrors)
                MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RecalculateRequired() => Recalc();

    /// <summary>يقرأ الحد الأقصى للخصم في المبيعات من إعدادات الإدارة ويعرضه على الشاشة.</summary>
    private void RefreshMaxDiscount()
    {
        maxSalesPct = Math.Min(100m, Math.Max(0m, PricingStore.Load().MaxDiscountPercent));
        discount.Maximum = maxSalesPct;
        discountLabel.Text = $"الخصم (%) — الحد الأقصى {maxSalesPct:0.##}%";
    }

    private void OnDiscountPercentEdited()
    {
        if (syncingDiscount) return;
        discountByAmount = false;
        Recalc();
    }

    private void OnDiscountAmountEdited()
    {
        if (syncingDiscount) return;
        discountByAmount = true;
        Recalc();
    }

    private static decimal ReadAmount(NumericUpDown c)
        => TryParseAmount(c.Text, out var v)
            ? Math.Max(0m, v)
            : (string.IsNullOrWhiteSpace(c.Text) ? 0m : c.Value);

    private static void SetNumeric(NumericUpDown c, decimal value)
    {
        value = Math.Min(c.Maximum, Math.Max(c.Minimum, Math.Round(value, 2)));
        if (c.Value == value && TryParseAmount(c.Text, out var shown) && shown == value) return;
        c.Value = value;
        if (c.Focused) c.Select(c.Text.Length, 0);
    }

    /// <summary>
    /// الحساب الوحيد لكل الأرقام: السعر × العدد = الإجمالي قبل الخصم، والخصم بالنسبة أو بالجنيه
    /// (آخر خانة اتكتب فيها هي الأصل والتانية بتتحسب منها لحظيًا)، والباقي = السعر بعد الخصم.
    /// </summary>
    private void Recalc()
    {
        if (loadingProduct || syncingDiscount) return;

        int quantity = GetQty();
        decimal gross = ReadAmount(originalPrice) * quantity;
        var r = DiscountMath.Resolve(gross, discountByAmount, ReadAmount(discount), ReadAmount(discountAmt), maxSalesPct);
        currentDiscountPercent = r.Percent;

        syncingDiscount = true;
        try
        {
            if (discountByAmount)
            {
                if (r.Clamped) SetNumeric(discountAmt, r.Amount);
                SetNumeric(discount, r.Percent);
            }
            else
            {
                if (r.Clamped) SetNumeric(discount, r.Percent);
                SetNumeric(discountAmt, r.Amount);
            }
        }
        finally { syncingDiscount = false; }

        SetRequiredSafely(gross - r.Amount);
        UpdateAmountsFromEditors();
    }

    private void SetRequiredSafely(decimal amount)
    {
        amount = Math.Min(required.Maximum, Math.Max(0m, Math.Round(amount, 2)));
        if (required.Value == amount) return;
        required.Value = amount;
    }

    private decimal GetEffectivePaid(decimal requiredAmount, decimal paidAmount, bool paidWasEdited)
    {
        // ترك خانة المدفوع فارغة = دفع كامل القيمة.
        return paidWasEdited ? paidAmount : requiredAmount;
    }

    private void UpdateAmountsFromControls()
    {
        UpdateAmountsCore(required.Value, paid.Text, paidEdited);
    }

    private void UpdateAmountsFromEditors()
    {
        decimal requiredAmount = TryParseAmount(required.Text, out var req) ? Math.Max(0m, req) : required.Value;
        UpdateAmountsCore(requiredAmount, paid.Text, paidEdited);
    }

    private void UpdateAmountsCore(decimal requiredAmount, string paidText, bool paidWasEdited)
    {
        decimal paidAmount = TryParseAmount(paidText, out var parsedPaid) ? Math.Max(0m, parsedPaid) : 0m;
        decimal effectivePaid = GetEffectivePaid(requiredAmount, paidAmount, paidWasEdited);
        decimal difference = requiredAmount - effectivePaid;
        remaining.Text = $"{Math.Max(0, difference):F2} ج.م";
        change.Text = $"{Math.Max(0, -difference):F2} ج.م";
    }

    private static bool TryParseAmount(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string normalized = text.Trim()
            .Replace('٠','0').Replace('١','1').Replace('٢','2').Replace('٣','3').Replace('٤','4')
            .Replace('٥','5').Replace('٦','6').Replace('٧','7').Replace('٨','8').Replace('٩','9')
            .Replace('٬', ',').Replace('٫', '.');

        // حذف فواصل الآلاف في حالة وجودها، مع الإبقاء على النقطة كفاصل عشري.
        normalized = normalized.Replace(",", "");
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }

    private const string ZeroStockMessage =
        "تنبيه: الكمية الحالية للصنف صفر، ولا يمكن إتمام البيع حتى تتوفر الكمية.";

    private static int GetProductStock(long productId)
    {
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Quantity FROM Products WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", productId);
        object? value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value
            ? 0
            : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private void UpdateDescriptionFromProduct()
    {
        if (string.IsNullOrWhiteSpace(product.Text))
            product.Text = "بيع";
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (isSavingTransaction) return;
        isSavingTransaction = true;
        if (sender is Control saveControl) saveControl.Enabled = false;
        try
        {
            // لو الباركود مكتوب ولسه ما اتحملش (مثلًا الإتمام بالـEnter من غير ما المؤشر يخرج
            // من الخانة) نحمّله صامتًا الأول — من غير أي رسالة تبلع الضغطة.
            LoadByBarcode(false);

            // اسم الصنف مش إجباري: لو فاضي نكتب «بيع» تلقائيًا ونكمل من أول ضغطة.
            if (string.IsNullOrWhiteSpace(product.Text))
                product.Text = "بيع";

            // الحد الأقصى ممكن الإدارة تكون غيّرته: نقراه من جديد ونعيد الحساب قبل الحفظ.
            RefreshMaxDiscount();
            Recalc();
            if (currentDiscountPercent > maxSalesPct + 0.0001m)
            {
                MessageBox.Show(this, $"أقصى خصم مسموح من الإدارة هو {maxSalesPct:0.##}%.", "الخصم", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                discount.Focus();
                discount.Select(0, discount.Text.Length);
                return;
            }

            decimal effectivePaid = GetEffectivePaid(required.Value, TryParseAmount(paid.Text, out var parsedPaid) ? Math.Max(0m, parsedPaid) : 0m, paidEdited);
            decimal rem = Math.Max(0, required.Value - effectivePaid);
            decimal ch = Math.Max(0, effectivePaid - required.Value);

            if (rem > 0 && !selectedCustomer.HasValue)
            {
                using var picker = new CustomerPickerForm();
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;
                selectedCustomer = picker.CustomerId;
            }

            NormalizeQty(false);
            int quantity = GetQty();

            // تنبيه المخزون بيظهر هنا مرة واحدة فقط (بدل ما كان بيظهر عند الخروج من خانة
            // الباركود ويبلع ضغطة الإتمام الأولى).
            if (selectedProductId.HasValue)
            {
                int stockQty = GetProductStock(selectedProductId.Value);
                if (stockQty <= 0)
                {
                    MessageBox.Show(this, ZeroStockMessage, "المخزون", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (stockQty < quantity)
                {
                    MessageBox.Show(this,
                        $"الكمية المطلوبة ({quantity}) أكبر من المتاح في المخزن ({stockQty}).",
                        "المخزون", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            decimal baseAmount = ReadAmount(originalPrice) * quantity;
            SalesService.CompleteSale(
                currentUser,
                product.Text.Trim(),
                baseAmount,
                currentDiscountPercent,
                required.Value,
                effectivePaid,
                selectedCustomer,
                selectedProductId,
                out rem,
                out ch,
                quantity);

            string message = ch > 0
                ? $"تم البيع بنجاح.\nالباقي للعميل: {ch:F2} ج.م"
                : rem > 0
                    ? $"تم البيع وتسجيل المتبقي كدين: {rem:F2} ج.م"
                    : "تم البيع نقدًا بالكامل.";

            MessageBox.Show(this, message, "تمت العملية", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    // ============================================================
    // العدد: رقم صحيح فقط
    // ============================================================
    private void Qty_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar)) return;
        if (!char.IsDigit(e.KeyChar)) e.Handled = true;   // يمنع النقطة والفاصلة والسالب والحروف
    }

    private static string LeadingDigits(string? text)
    {
        string normalized = OperationText.NormalizeDigits(text).Trim();
        var sb = new System.Text.StringBuilder();
        foreach (char c in normalized)
        {
            if (c >= '0' && c <= '9') sb.Append(c);
            else break;                                   // أي كسر (1.5) يتقص عند العلامة
        }
        return sb.ToString();
    }

    private void SanitizeQty()
    {
        if (sanitizingQty) return;
        string raw = qty.Text ?? "";
        string digits = LeadingDigits(raw);
        if (digits == raw) return;

        sanitizingQty = true;
        try
        {
            qty.Text = digits;
            qty.Select(qty.Text.Length, 0);
        }
        finally { sanitizingQty = false; }
    }

    private int GetQty()
    {
        string digits = LeadingDigits(qty.Text);
        if (digits.Length == 0 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            return 1;
        return Math.Max(1, Math.Min((int)qty.Maximum, value));
    }

    private void NormalizeQty(bool recalculate = true)
    {
        int value = GetQty();
        sanitizingQty = true;
        try
        {
            qty.Value = value;
            qty.Text = value.ToString(CultureInfo.InvariantCulture);
        }
        finally { sanitizingQty = false; }
        if (recalculate) Recalc();
    }

    // ============================================================
    // زر «ابدأ فاتورة» / «إنهاء الفاتورة»
    // ============================================================
    private void RefreshInvoiceButtons()
    {
        bool active = InvoiceSession.Active;
        btnInvoice.Text = active ? "🧾 إنهاء الفاتورة" : "🧾 ابدأ فاتورة";
        btnInvoice.BackColor = active ? Color.FromArgb(211, 84, 0) : Color.FromArgb(0, 121, 107);
        btnCancelInvoice.Visible = active;
    }

    private void InvoiceButton_Click()
    {
        if (!InvoiceSession.Active)
        {
            InvoiceSession.Start(currentUser);
            RefreshInvoiceButtons();
            return;
        }

        if (InvoiceFlow.Run(this, currentUser, InvoiceSession.Since))
            InvoiceSession.End();
        RefreshInvoiceButtons();
    }

    private void CancelInvoice_Click()
    {
        if (MessageBox.Show(this, "إلغاء الفاتورة الحالية؟ (العمليات نفسها متسجلة ومش هتتمسح)", "فاتورة",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        InvoiceSession.End();
        RefreshInvoiceButtons();
    }

    private void ResetForm()
    {
        product.Clear();
        barcode.Clear();
        originalPrice.Value = 0;
        qty.Value = 1;
        discount.Value = 0;
        discountAmt.Value = 0;
        discountByAmount = false;
        required.Value = 0;
        resetting = true;
        paid.Value = 0;
        paidEdited = false;
        paid.Text = string.Empty;
        resetting = false;
        selectedCustomer = null;
        selectedProductId = null;
        loadedBarcode = null;
        RefreshMaxDiscount();
        Recalc();
        product.Focus();
    }
}

internal sealed class NewCustomerForm : Form
{
    public long CustomerId { get; private set; }
    private readonly TextBox name = new();
    private readonly TextBox phone = new();

    public NewCustomerForm()
    {
        UiKit.PrepareForm(this, "➕ إضافة عميل", 460, 300);
        UiKit.EnableEscape(this);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(35), ColumnCount = 2, RowCount = 3 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        for (int i = 0; i < 3; i++) body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Add(body, "اسم العميل *", name, 0);
        Add(body, "رقم الهاتف (اختياري)", phone, 1);
        var ok = UiKit.Button("💾 حفظ", UiKit.Success, 140, 42);
        ok.Dock = DockStyle.Fill; ok.Click += (s,e) => Save();
        body.Controls.Add(new Label(),0,2); body.Controls.Add(ok,1,2);
        Controls.Add(body);
        AcceptButton = ok;
        InputFocusHelper.Apply(this);
    }

    private static void Add(TableLayoutPanel p,string label,Control c,int row)
    {
        p.Controls.Add(new Label { Text=label, AutoSize=true, Anchor=AnchorStyles.Right, Font=new Font("Segoe UI",10F,FontStyle.Bold)},0,row);
        c.Dock=DockStyle.Fill; p.Controls.Add(c,1,row);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            MessageBox.Show(this,"اسم العميل إجباري.","العميل",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            name.Focus(); return;
        }
        using var c=AppDatabase.OpenConnection();
        CustomerId=AppDatabase.GetOrCreateCustomer(c,name.Text,phone.Text);
        DialogResult=DialogResult.OK; Close();
    }
}
