using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// تسجيل التصوير اليدوي خارج البرنامج.
/// المستخدم يسجل عدد مرات كل نوع من التصوير، ويظهر السعر والإجمالي لحظيًا.
/// </summary>
internal sealed class CopyForm : Form
{
    private readonly string currentUser;
    private PricingSettings pricing = null!;

    private TextBox txtDescription = null!;
    private ComboBox cmbPreset = null!;

    private TextBox blackSimpleQty = null!, blackSimplePrice = null!;
    private TextBox blackDuplexQty = null!, blackDuplexPrice = null!;
    private TextBox colorSimpleQty = null!, colorSimplePrice = null!;
    private TextBox colorDuplexQty = null!, colorDuplexPrice = null!;
    private TextBox paid = null!;

    private Label lblTotal = null!, lblRemaining = null!, lblChange = null!;
    private Label lblSubtotal = null!, lblDiscountPctCaption = null!;
    private NumericUpDown discountPct = null!, discountAmt = null!;
    private bool syncingDiscount;
    private bool discountByAmount;   // آخر خانة اتكتب فيها: الجنيه (true) ولا النسبة (false)
    private Label lblPresetHint = null!;

    public CopyForm(string userName)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        pricing = PricingStore.Load();

        BuildUi();
        InputFocusHelper.Apply(this);
        UpdatePriceMode();
        Recalculate();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"📄 التصوير — المنفذ: {currentUser}", 1120, 820);
        UiKit.EnableEscape(this);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 86,
            BackColor = Color.White
        };
        header.Controls.Add(UiKit.Title($"📄 تسجيل عملية التصوير — المنفذ: {currentUser}", 18));
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(30, 18, 30, 16),
            ColumnCount = 3,
            RowCount = 14,
            AutoScroll = true,
            BackColor = UiKit.Background
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        for (int i = 0; i < 14; i++)
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 2 ? 38 : 44));

        txtDescription = new TextBox
        {
            Dock = DockStyle.Fill,
            TabIndex = 0,
            PlaceholderText = "مثال: كتاب رياضيات / أوراق مدرسية"
        };
        body.Controls.Add(LabelCell("ما الذي تم تصويره؟"), 0, 0);
        body.Controls.Add(txtDescription, 1, 0);
        body.SetColumnSpan(txtDescription, 2);

        cmbPreset = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            TabIndex = 1
        };
        cmbPreset.Items.AddRange(new object[] { "الطلبة", "عادي", "مخصص" });
        cmbPreset.SelectedItem = "عادي";
        ComboBoxStyler.ApplyModernStyle(cmbPreset, Color.FromArgb(52, 73, 94));
        cmbPreset.SelectedIndexChanged += (s, e) =>
        {
            UpdatePriceMode();
            Recalculate();
        };
        body.Controls.Add(LabelCell("الفئة السعرية"), 0, 1);
        body.Controls.Add(cmbPreset, 1, 1);
        body.SetColumnSpan(cmbPreset, 2);

        body.Controls.Add(HeaderCell("نوع التصوير"), 0, 2);
        body.Controls.Add(HeaderCell("العدد"), 1, 2);
        body.Controls.Add(HeaderCell("السعر"), 2, 2);

        blackSimpleQty = QtyBox(2);
        blackSimplePrice = PriceBox(3);
        blackDuplexQty = QtyBox(4);
        blackDuplexPrice = PriceBox(5);
        colorSimpleQty = QtyBox(6);
        colorSimplePrice = PriceBox(7);
        colorDuplexQty = QtyBox(8);
        colorDuplexPrice = PriceBox(9);
        paid = new TextBox
        {
            Dock = DockStyle.Fill,
            TabIndex = 12,
            TextAlign = HorizontalAlignment.Center,
            PlaceholderText = "اتركه فارغًا = دفع كامل المبلغ"
        };

        discountPct = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            DecimalPlaces = 2,
            Minimum = 0,
            Maximum = 100,
            Increment = 0.5m,
            TextAlign = HorizontalAlignment.Center,
            TabIndex = 10
        };
        discountAmt = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            DecimalPlaces = 2,
            Minimum = 0,
            Maximum = 100000000,
            Increment = 1m,
            TextAlign = HorizontalAlignment.Center,
            TabIndex = 11
        };

        AddCopyRow(body, 3, "أسود — وش فقط", blackSimpleQty, blackSimplePrice);
        AddCopyRow(body, 4, "أسود — وش وظهر", blackDuplexQty, blackDuplexPrice);
        AddCopyRow(body, 5, "ألوان — وش فقط", colorSimpleQty, colorSimplePrice);
        AddCopyRow(body, 6, "ألوان — وش وظهر", colorDuplexQty, colorDuplexPrice);

        lblPresetHint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = UiKit.Muted,
            AutoSize = false
        };
        body.Controls.Add(LabelCell("ملاحظة"), 0, 7);
        body.Controls.Add(lblPresetHint, 1, 7);
        body.SetColumnSpan(lblPresetHint, 2);

        lblSubtotal = ResultLabel(UiKit.Muted, 11);
        body.Controls.Add(LabelCell("الإجمالي قبل الخصم"), 0, 8);
        body.Controls.Add(lblSubtotal, 1, 8);
        body.SetColumnSpan(lblSubtotal, 2);

        lblDiscountPctCaption = LabelCell("الخصم (%)");
        body.Controls.Add(lblDiscountPctCaption, 0, 9);
        body.Controls.Add(discountPct, 1, 9);
        body.SetColumnSpan(discountPct, 2);

        body.Controls.Add(LabelCell("الخصم (ج.م)"), 0, 10);
        body.Controls.Add(discountAmt, 1, 10);
        body.SetColumnSpan(discountAmt, 2);

        lblTotal = ResultLabel(UiKit.Success, 14);
        body.Controls.Add(LabelCell("الإجمالي بعد الخصم"), 0, 11);
        body.Controls.Add(lblTotal, 1, 11);
        body.SetColumnSpan(lblTotal, 2);

        body.Controls.Add(LabelCell("المبلغ المدفوع فعليًا"), 0, 12);
        body.Controls.Add(paid, 1, 12);
        body.SetColumnSpan(paid, 2);

        var resultPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        resultPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        resultPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lblRemaining = ResultLabel(UiKit.Danger, 11);
        lblChange = ResultLabel(UiKit.Success, 11);
        resultPanel.Controls.Add(lblRemaining, 0, 0);
        resultPanel.Controls.Add(lblChange, 1, 0);
        body.Controls.Add(new Label { Text = "الحساب", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold) }, 0, 13);
        body.Controls.Add(resultPanel, 1, 13);
        body.SetColumnSpan(resultPanel, 2);

        WheelScrollFix.Apply(body);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 68,
            BackColor = Color.White,
            Padding = new Padding(30, 10, 30, 10)
        };
        var save = UiKit.Button("✅ تسجيل عملية التصوير", UiKit.Success, 280, 48);
        save.Dock = DockStyle.Right;
        save.Click += Save_Click;
        footer.Controls.Add(save);
        // Add docked controls in bottom/fill/top order so the header never covers
        // the first input row (especially the category selector).
        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        AcceptButton = save;

        foreach (TextBox box in new[]
        {
            blackSimpleQty, blackSimplePrice,
            blackDuplexQty, blackDuplexPrice,
            colorSimpleQty, colorSimplePrice,
            colorDuplexQty, colorDuplexPrice
        })
        {
            box.TextChanged += (s, e) => Recalculate();
            box.KeyPress += ArithmeticTextBox_KeyPress;
        }
        paid.TextChanged += (s, e) => Recalculate();
        paid.KeyUp += (s, e) => Recalculate();
        paid.MouseUp += (s, e) => Recalculate();
        paid.KeyPress += ArithmeticTextBox_KeyPress;

        discountPct.TextChanged += (s, e) => { if (!syncingDiscount) { discountByAmount = false; Recalculate(); } };
        discountPct.ValueChanged += (s, e) => { if (!syncingDiscount) { discountByAmount = false; Recalculate(); } };
        discountAmt.TextChanged += (s, e) => { if (!syncingDiscount) { discountByAmount = true; Recalculate(); } };
        discountAmt.ValueChanged += (s, e) => { if (!syncingDiscount) { discountByAmount = true; Recalculate(); } };
        ApplyMaxDiscountUi();
    }

    /// <summary>الحد الأقصى للخصم (طباعة وتصوير) من إعدادات الإدارة.</summary>
    private decimal MaxDiscountPct => Math.Min(100m, Math.Max(0m, pricing.MaxPrintDiscountPercent));

    private void ApplyMaxDiscountUi()
    {
        discountPct.Maximum = MaxDiscountPct;
        lblDiscountPctCaption.Text = $"الخصم (%) — الحد الأقصى {MaxDiscountPct:0.##}%";
    }

    private static void SetNumeric(NumericUpDown c, decimal value)
    {
        value = Math.Min(c.Maximum, Math.Max(c.Minimum, Math.Round(value, 2)));
        if (c.Value == value && LiveValue(c) == value) return;
        c.Value = value;
        if (c.Focused) c.Select(c.Text.Length, 0);
    }

    private decimal ComputeSubtotal() =>
        ReadExpression(blackSimpleQty) * ReadExpression(blackSimplePrice) +
        ReadExpression(blackDuplexQty) * ReadExpression(blackDuplexPrice) +
        ReadExpression(colorSimpleQty) * ReadExpression(colorSimplePrice) +
        ReadExpression(colorDuplexQty) * ReadExpression(colorDuplexPrice);

    private void AddCopyRow(TableLayoutPanel body, int row, string title, TextBox quantity, TextBox price)
    {
        body.Controls.Add(LabelCell(title), 0, row);
        body.Controls.Add(quantity, 1, row);
        body.Controls.Add(price, 2, row);
    }

    private void UpdatePriceMode()
    {
        bool custom = cmbPreset.SelectedItem?.ToString() == "مخصص";
        if (custom)
        {
            pricing = PricingStore.Load();
            blackSimplePrice.Text = FormatInput(pricing.CopyCustomBlack);
            blackDuplexPrice.Text = FormatInput(pricing.CopyCustomDuplexBlack);
            colorSimplePrice.Text = FormatInput(pricing.CopyCustomColor);
            colorDuplexPrice.Text = FormatInput(pricing.CopyCustomDuplexColor);
            EnablePriceEditors(true);
            lblPresetHint.Text = "💡 مخصص: اكتب سعر كل نوع بنفسك، والإجمالي يتغير فورًا أثناء الكتابة.";
            ApplyMaxDiscountUi();
            return;
        }

        pricing = PricingStore.Load();
        bool student = cmbPreset.SelectedItem?.ToString() == "الطلبة";

        blackSimplePrice.Text = FormatInput(student ? pricing.CopyStudentBlack : pricing.CopyNormalBlack);
        blackDuplexPrice.Text = FormatInput(student ? pricing.CopyStudentDuplexBlack : pricing.CopyNormalDuplexBlack);
        colorSimplePrice.Text = FormatInput(student ? pricing.CopyStudentColor : pricing.CopyNormalColor);
        colorDuplexPrice.Text = FormatInput(student ? pricing.CopyStudentDuplexColor : pricing.CopyNormalDuplexColor);

        EnablePriceEditors(false);
        lblPresetHint.Text = "";
        ApplyMaxDiscountUi();
    }

    private void EnablePriceEditors(bool enabled)
    {
        foreach (TextBox box in new[] { blackSimplePrice, blackDuplexPrice, colorSimplePrice, colorDuplexPrice })
        {
            box.ReadOnly = !enabled;
            box.TabStop = enabled;
        }
    }

    private void Recalculate()
    {
        if (syncingDiscount) return;

        decimal subtotal = ComputeSubtotal();
        var d = DiscountMath.Resolve(subtotal, discountByAmount, LiveValue(discountPct), LiveValue(discountAmt), MaxDiscountPct);

        syncingDiscount = true;
        try
        {
            if (discountByAmount)
            {
                if (d.Clamped) SetNumeric(discountAmt, d.Amount);
                SetNumeric(discountPct, d.Percent);
            }
            else
            {
                if (d.Clamped) SetNumeric(discountPct, d.Percent);
                SetNumeric(discountAmt, d.Amount);
            }
        }
        finally { syncingDiscount = false; }

        decimal total = subtotal - d.Amount;
        lblSubtotal.Text = $"{subtotal:F2} ج.م";

        decimal paidAmount = ParsePaidAmount(total, throwOnInvalid: false);
        decimal remaining = Math.Max(0, total - paidAmount);
        decimal change = Math.Max(0, paidAmount - total);

        lblTotal.Text = $"{total:F2} ج.م";
        lblRemaining.Text = $"المتبقي / الدين: {remaining:F2} ج.م";
        lblChange.Text = $"الباقي للعميل: {change:F2} ج.م";
    }

    private static decimal LiveValue(NumericUpDown control)
    {
        string text = control.Text?.Trim() ?? "";
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal value))
            return value;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return value;
        return control.Value;
    }

    private static decimal ReadExpression(TextBox box)
    {
        if (!ArithmeticInput.TryEvaluateSum(box.Text, out decimal value)) return 0m;
        return Math.Min(100000000m, Math.Max(0m, value));
    }

    private static string FormatInput(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static int ReadCount(TextBox box, string caption)
    {
        if (!ArithmeticInput.TryEvaluateCompleteSum(box.Text, out decimal value))
            throw new ArgumentException($"اكتمل إدخال الجمع في خانة «{caption}» أولًا.");
        if (value < 0m || value > 1000000m || value != decimal.Truncate(value))
            throw new ArgumentException($"خانة «{caption}» لازم تكون عددًا صحيحًا. يمكنك كتابة جمع مثل 60+50+35.");
        return decimal.ToInt32(value);
    }

    private static decimal ReadPrice(TextBox box, string caption)
    {
        if (!ArithmeticInput.TryEvaluateCompleteSum(box.Text, out decimal value) || value < 0m || value > 100000m)
            throw new ArgumentException($"قيمة «{caption}» غير صحيحة. اكتب رقمًا أو جمعًا مثل 1+2.5+3.");
        return value;
    }

    private static void ArithmeticTextBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!ArithmeticInput.IsAllowedKey(e.KeyChar)) e.Handled = true;
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        try
        {
            // Evaluate sums without rewriting the text being typed, preserving live updates and caret.
            int qBlackSimple = ReadCount(blackSimpleQty, "أسود — وش فقط");
            int qBlackDuplex = ReadCount(blackDuplexQty, "أسود — وش وظهر");
            int qColorSimple = ReadCount(colorSimpleQty, "ألوان — وش فقط");
            int qColorDuplex = ReadCount(colorDuplexQty, "ألوان — وش وظهر");
            decimal pBlackSimple = ReadPrice(blackSimplePrice, "سعر الأسود — وش فقط");
            decimal pBlackDuplex = ReadPrice(blackDuplexPrice, "سعر الأسود — وش وظهر");
            decimal pColorSimple = ReadPrice(colorSimplePrice, "سعر الألوان — وش فقط");
            decimal pColorDuplex = ReadPrice(colorDuplexPrice, "سعر الألوان — وش وظهر");
            string preset = cmbPreset.SelectedItem?.ToString() ?? "عادي";
            decimal subtotal = ComputeSubtotal();
            var disc = DiscountMath.Resolve(subtotal, discountByAmount, LiveValue(discountPct), LiveValue(discountAmt), MaxDiscountPct);
            decimal previewTotal = subtotal - disc.Amount;

            decimal paidAmount = ParsePaidAmount(previewTotal, throwOnInvalid: true);

            long? customerId = null;
            if (previewTotal - paidAmount > 0)
            {
                using var picker = new CustomerPickerForm();
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;
                customerId = picker.CustomerId;
            }

            long copyId = CopyService.RecordCopy(
                currentUser,
                string.IsNullOrWhiteSpace(txtDescription.Text) ? "تصوير مستند" : txtDescription.Text.Trim(),
                preset,
                qBlackSimple, pBlackSimple,
                qBlackDuplex, pBlackDuplex,
                qColorSimple, pColorSimple,
                qColorDuplex, pColorDuplex,
                disc.Amount,
                paidAmount, customerId,
                out decimal total, out decimal remaining, out decimal change);

            int totalSheets = qBlackSimple + qBlackDuplex + qColorSimple + qColorDuplex;

            var doneRows = new List<(string Label, string Value)>
            {
                ("العملية", string.IsNullOrWhiteSpace(txtDescription.Text) ? "تصوير مستند" : txtDescription.Text.Trim()),
                ("عدد الأوراق", totalSheets.ToString()),
                ("السعر قبل الخصم", $"{subtotal:F2} ج.م"),
                ("الخصم", disc.Amount > 0 ? $"{disc.Amount:F2} ج.م ({disc.Percent:0.##}%)" : "بدون خصم"),
                ("السعر بعد الخصم", $"{total:F2} ج.م"),
                ("المدفوع", $"{paidAmount:F2} ج.م"),
                ("المتبقي / الدين", $"{remaining:F2} ج.م"),
                ("الباقي للعميل", $"{change:F2} ج.م"),
                ("رقم العملية", copyId.ToString())
            };

            using (var done = new OperationDoneForm(
                "تم الحفظ",
                "✅ تم تسجيل التصوير بنجاح",
                doneRows,
                total,
                Math.Min(total, paidAmount),
                customerId))
            {
                done.ShowDialog(this);
                try
                {
                    OperationDoneForm.ApplyResult(currentUser, "تصوير", done);
                }
                catch (Exception extraEx)
                {
                    MessageBox.Show(this, "تم حفظ التصوير، لكن تعذر تسجيل الشيء الإضافي:\n\n" + extraEx.Message,
                        "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static TextBox QtyBox(int tabIndex) => new()
    {
        Dock = DockStyle.Fill,
        Text = "0",
        TextAlign = HorizontalAlignment.Center,
        RightToLeft = RightToLeft.No,
        TabIndex = tabIndex
    };

    private static TextBox PriceBox(int tabIndex) => new()
    {
        Dock = DockStyle.Fill,
        Text = "0",
        TextAlign = HorizontalAlignment.Center,
        RightToLeft = RightToLeft.No,
        TabIndex = tabIndex
    };

    private decimal ParsePaidAmount(decimal total, bool throwOnInvalid)
    {
        string text = paid.Text?.Trim() ?? string.Empty;
        // الحقل اختياري: تركه فارغًا يعني أن العميل دفع كامل الإجمالي.
        if (string.IsNullOrWhiteSpace(text))
            return total;

        if (ArithmeticInput.TryEvaluateCompleteSum(text, out var value))
        {
            if (value < 0 || value > 100000000m)
            {
                if (throwOnInvalid) throw new ArgumentException("المبلغ المدفوع غير صالح.");
                return 0m;
            }
            return value;
        }

        if (throwOnInvalid)
            throw new ArgumentException("اكتب المبلغ المدفوع كرقم أو جمع مكتمل مثل 60+50، أو اترك الخانة فارغة لدفع المبلغ كاملًا.");
        return 0m;
    }

    private static decimal Clamp(decimal value) => Math.Min(100000m, Math.Max(0m, value));

    private static Label LabelCell(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
    };

    private static Label HeaderCell(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        ForeColor = Color.White,
        BackColor = Color.FromArgb(52, 73, 94)
    };

    private static Label ResultLabel(Color color, float size) => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", size, FontStyle.Bold),
        ForeColor = color,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle
    };
}
