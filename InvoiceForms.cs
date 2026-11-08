using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp;

// ============================================================
// تشغيل مسار الفاتورة كامل: اختيار العمليات ← معاينة / طباعة / PDF
// ============================================================
internal static class InvoiceFlow
{
    /// <returns>true لو الفاتورة اتطبعت أو اتحفظت PDF.</returns>
    public static bool Run(IWin32Window owner, string userName, DateTime? since)
    {
        using var picker = new InvoicePickerForm(userName, since);
        if (picker.ShowDialog(owner) != DialogResult.OK || picker.SelectedOperations.Count == 0)
            return false;

        var data = InvoiceStore.Build(picker.SelectedOperations, InvoiceStore.NextNumber());
        using var form = new InvoiceForm(userName, data, picker.SelectedOperations);
        form.ShowDialog(owner);
        return form.Issued;
    }
}

// ============================================================
// قائمة عمليات اليوم لاختيار اللي يتكتب في الفاتورة
// ============================================================
internal sealed class InvoicePickerForm : Form
{
    private readonly string currentUser;
    private readonly DateTime? since;
    private readonly DateTimePicker day = new();
    private readonly DataGridView grid = new();
    private readonly Label summary = new();
    private List<OperationRow> rows = new();
    private bool loading;

    public List<OperationRow> SelectedOperations { get; private set; } = new();

    public InvoicePickerForm(string userName, DateTime? sinceTime)
    {
        currentUser = userName;
        since = sinceTime;
        BuildUi();
        InputFocusHelper.Apply(this);
        LoadRows();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "🧾 إصدار فاتورة", 1000, 640);
        UiKit.EnableEscape(this);

        // ترتيب الإضافة مهم للـDock: العنصر الـFill أولًا ثم السفلي ثم العلوي (الأعلى آخرًا).
        var header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.White };
        header.Controls.Add(UiKit.Title("🧾 اختر العمليات اللي هتتكتب في الفاتورة", 16));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(20, 8, 20, 4),
            BackColor = UiKit.Background,
            WrapContents = false
        };

        day.Format = DateTimePickerFormat.Short;
        day.Width = 150;
        day.Value = DateTime.Today;
        day.MaxDate = DateTime.Today;
        day.ValueChanged += (s, e) => LoadRows();

        var all = UiKit.Button("تحديد الكل", Color.FromArgb(52, 73, 94), 120, 36);
        all.Click += (s, e) => SetAllChecked(true);
        var none = UiKit.Button("إلغاء التحديد", Color.FromArgb(120, 130, 140), 130, 36);
        none.Click += (s, e) => SetAllChecked(false);

        bar.Controls.Add(new Label { Text = "اليوم:", AutoSize = true, Margin = new Padding(6, 10, 6, 0), Font = new Font("Segoe UI", 10.5F, FontStyle.Bold) });
        bar.Controls.Add(day);
        bar.Controls.Add(all);
        bar.Controls.Add(none);

        grid.Dock = DockStyle.Fill;
        UiKit.StyleGrid(grid);
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Pick", HeaderText = "✔", FillWeight = 6 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "الوقت", FillWeight = 9, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "النوع", FillWeight = 10, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Text", HeaderText = "البيان", FillWeight = 40, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "المبلغ", FillWeight = 12, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "User", HeaderText = "بواسطة", FillWeight = 10, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Invoice", HeaderText = "فاتورة سابقة", FillWeight = 13, ReadOnly = true });

        grid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (s, e) => { if (!loading) UpdateSummary(); };
        grid.CellClick += (s, e) =>
        {
            // الضغط على أي مكان في الصف يعلّم / يشيل العلامة.
            if (e.RowIndex < 0 || e.ColumnIndex == 0) return;
            var cell = grid.Rows[e.RowIndex].Cells["Pick"];
            cell.Value = !(cell.Value is bool b && b);
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.White, Padding = new Padding(25, 12, 25, 12) };
        var next = UiKit.Button("إنشاء الفاتورة", UiKit.Success, 220, 46);
        next.Dock = DockStyle.Right;
        next.Click += (s, e) => Confirm();
        var cancel = UiKit.Button("إلغاء", Color.FromArgb(120, 130, 140), 120, 46);
        cancel.Dock = DockStyle.Right;
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        summary.Dock = DockStyle.Fill;
        summary.TextAlign = ContentAlignment.MiddleCenter;
        summary.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        footer.Controls.Add(summary);
        footer.Controls.Add(cancel);
        footer.Controls.Add(next);

        Controls.Add(grid);
        Controls.Add(footer);
        Controls.Add(bar);
        Controls.Add(header);
        AcceptButton = next;
    }

    private void LoadRows()
    {
        loading = true;
        try
        {
            rows = InvoiceStore.LoadOperations(day.Value.Date);
            grid.Rows.Clear();
            foreach (var row in rows)
            {
                bool preselect = since.HasValue &&
                                 day.Value.Date == DateTime.Today &&
                                 row.Time >= since.Value &&
                                 string.Equals(row.UserName, currentUser, StringComparison.Ordinal) &&
                                 !row.InvoiceNumber.HasValue;

                int index = grid.Rows.Add(
                    preselect,
                    row.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
                    row.Kind,
                    row.Text,
                    OperationText.Money(row.Amount) + " ج.م",
                    row.UserName,
                    row.InvoiceNumber.HasValue ? "رقم " + row.InvoiceNumber.Value.ToString("D4", CultureInfo.InvariantCulture) : "—");
                grid.Rows[index].Tag = row;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر تحميل عمليات اليوم للفاتورة", ex);
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            loading = false;
        }
        UpdateSummary();
    }

    private IEnumerable<OperationRow> Checked()
    {
        foreach (DataGridViewRow r in grid.Rows)
            if (r.Cells["Pick"].Value is bool b && b && r.Tag is OperationRow op)
                yield return op;
    }

    private void SetAllChecked(bool value)
    {
        loading = true;
        foreach (DataGridViewRow r in grid.Rows)
            r.Cells["Pick"].Value = value;
        loading = false;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var list = Checked().ToList();
        decimal total = list.Sum(x => x.Amount);
        summary.Text = list.Count == 0
            ? (grid.Rows.Count == 0 ? "لا توجد عمليات في هذا اليوم" : "لم يتم اختيار أي عملية")
            : $"المختار: {list.Count} عملية — الإجمالي {OperationText.Money(total)} ج.م";
    }

    private void Confirm()
    {
        SelectedOperations = Checked().ToList();
        if (SelectedOperations.Count == 0)
        {
            MessageBox.Show(this, "اختر عملية واحدة على الأقل.", "الفاتورة", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var already = SelectedOperations.Where(x => x.InvoiceNumber.HasValue).ToList();
        if (already.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"{already.Count} من العمليات المختارة اتكتبت في فاتورة قبل كده.\nتكمل وتعمل فاتورة جديدة بيها؟",
                "تأكيد", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

// ============================================================
// معاينة الفاتورة + اسم العميل (كتابة) + طباعة + حفظ PDF
// ============================================================
internal sealed class InvoiceForm : Form
{
    private const string PrinterSetting = "InvoicePrinter";

    private readonly string currentUser;
    private readonly InvoiceData data;
    private readonly List<OperationRow> items;
    private readonly TextBox customer = new();
    private readonly NumericUpDown number = new();
    private readonly ComboBox printers = new();
    private readonly PictureBox preview = new();
    private readonly Label pageLabel = new();
    private int previewPage;
    private long savedInvoiceId;

    public bool Issued { get; private set; }

    public InvoiceForm(string userName, InvoiceData invoice, List<OperationRow> operations, long existingInvoiceId = 0)
    {
        currentUser = userName;
        data = invoice;
        items = operations;
        savedInvoiceId = existingInvoiceId;
        BuildUi();
        InputFocusHelper.Apply(this);
        RefreshPreview();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"🧾 فاتورة — المنفذ: {currentUser}", 1000, 720);
        UiKit.EnableEscape(this);

        var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Color.White };
        header.Controls.Add(UiKit.Title("🧾 معاينة الفاتورة", 16));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ---------- أدوات التحكم ----------
        var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11, Padding = new Padding(6) };
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        customer.Dock = DockStyle.Fill;
        customer.Text = data.CustomerName;
        customer.PlaceholderText = "اكتب اسم العميل (اختياري)";
        customer.TextChanged += (s, e) =>
        {
            data.CustomerName = customer.Text.Trim();
            RefreshPreview();
        };

        number.Dock = DockStyle.Fill;
        number.DecimalPlaces = 0;
        number.Minimum = 1;
        number.Maximum = 99999999;
        number.Value = Math.Max(1, data.Number);
        number.TextAlign = HorizontalAlignment.Center;
        number.ValueChanged += (s, e) =>
        {
            data.Number = (int)number.Value;
            RefreshPreview();
        };

        printers.Dock = DockStyle.Fill;
        printers.DropDownStyle = ComboBoxStyle.DropDownList;
        FillPrinters();
        ComboBoxStyler.ApplyModernStyle(printers, Color.FromArgb(52, 73, 94));

        side.Controls.Add(FieldLabel("اسم العميل"), 0, 0);
        side.Controls.Add(customer, 0, 1);
        side.Controls.Add(FieldLabel("رقم الفاتورة"), 0, 2);
        side.Controls.Add(number, 0, 3);
        side.Controls.Add(FieldLabel("الطابعة"), 0, 4);
        side.Controls.Add(printers, 0, 5);

        var print = UiKit.Button("🖨 طباعة (A4 — نسختين)", UiKit.Success, 260, 44);
        print.Dock = DockStyle.Fill;
        print.Click += (s, e) => PrintInvoice();
        var pdf = UiKit.Button("💾 حفظ PDF (A5)", UiKit.Primary, 260, 44);
        pdf.Dock = DockStyle.Fill;
        pdf.Click += (s, e) => SavePdf();
        var close = UiKit.Button("إغلاق", Color.FromArgb(120, 130, 140), 260, 44);
        close.Dock = DockStyle.Fill;
        close.Click += (s, e) => Close();
        side.Controls.Add(print, 0, 7);
        side.Controls.Add(pdf, 0, 8);
        side.Controls.Add(close, 0, 9);

        var note = new Label
        {
            Dock = DockStyle.Fill,
            Text = "طباعة الفاتورة نفسها لا تُحسب في فلوس الطباعة ولا تدخل الخزينة.",
            ForeColor = UiKit.Muted,
            Font = new Font("Segoe UI", 9F),
            TextAlign = ContentAlignment.TopRight
        };
        side.Controls.Add(note, 0, 10);

        // ---------- المعاينة ----------
        var previewHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        previewHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        preview.Dock = DockStyle.Fill;
        preview.SizeMode = PictureBoxSizeMode.Zoom;
        preview.BackColor = Color.FromArgb(225, 228, 232);
        previewHost.Controls.Add(preview, 0, 0);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        var prev = UiKit.Button("◀", Color.FromArgb(52, 73, 94), 44, 32);
        prev.Click += (s, e) => MovePage(-1);
        var nextPage = UiKit.Button("▶", Color.FromArgb(52, 73, 94), 44, 32);
        nextPage.Click += (s, e) => MovePage(1);
        pageLabel.AutoSize = true;
        pageLabel.Margin = new Padding(10, 8, 10, 0);
        pageLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        nav.Controls.Add(prev);
        nav.Controls.Add(pageLabel);
        nav.Controls.Add(nextPage);
        previewHost.Controls.Add(nav, 0, 1);

        root.Controls.Add(side, 0, 0);
        root.Controls.Add(previewHost, 1, 0);
        Controls.Add(root);
        Controls.Add(header);
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomRight,
        Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
    };

    private void FillPrinters()
    {
        foreach (string name in PrinterSettings.InstalledPrinters)
            printers.Items.Add(name);

        string? saved = InvoiceStore.GetSetting(PrinterSetting);
        string defaultPrinter = new PrinterSettings().PrinterName;
        if (!string.IsNullOrWhiteSpace(saved) && printers.Items.Contains(saved))
            printers.SelectedItem = saved;
        else if (printers.Items.Contains(defaultPrinter))
            printers.SelectedItem = defaultPrinter;
        else if (printers.Items.Count > 0)
            printers.SelectedIndex = 0;
    }

    private void MovePage(int delta)
    {
        int count = InvoiceRenderer.PageCount(data);
        previewPage = Math.Max(0, Math.Min(count - 1, previewPage + delta));
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        int count = InvoiceRenderer.PageCount(data);
        previewPage = Math.Max(0, Math.Min(count - 1, previewPage));
        var old = preview.Image;
        preview.Image = InvoiceRenderer.RenderPageBitmap(data, previewPage, 110);
        old?.Dispose();
        pageLabel.Text = $"صفحة {previewPage + 1} من {count}";
    }

    private void SaveRecord()
    {
        data.CustomerName = customer.Text.Trim();
        data.Number = (int)number.Value;
        savedInvoiceId = InvoiceStore.Save(savedInvoiceId, data, currentUser, items);
    }

    private void PrintInvoice()
    {
        try
        {
            string? printer = printers.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(printer))
            {
                MessageBox.Show(this, "اختر الطابعة الأول.", "الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveRecord();
            InvoiceStore.SetSetting(PrinterSetting, printer);

            // الطباعة هنا مباشرة بدون PrintLogger عشان فلوس الفاتورة ما تتحسبش في دخل الطباعة.
            using var doc = InvoiceRenderer.CreateTwoUpDocument(data, printer);
            if (!doc.PrinterSettings.IsValid)
            {
                MessageBox.Show(this, "الطابعة المختارة غير متاحة.", "الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            doc.Print();
            Issued = true;
            MessageBox.Show(this, "تم إرسال الفاتورة للطابعة (نسختين على ورقة A4).", "الفاتورة", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر طباعة الفاتورة", ex);
            MessageBox.Show(this, "تعذر طباعة الفاتورة:\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SavePdf()
    {
        try
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"فاتورة-{data.Number.ToString("D4", CultureInfo.InvariantCulture)}.pdf",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            SaveRecord();
            InvoiceRenderer.SavePdf(data, dialog.FileName);
            Issued = true;
            MessageBox.Show(this, "تم حفظ الفاتورة PDF:\n" + dialog.FileName, "الفاتورة", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر حفظ الفاتورة PDF", ex);
            MessageBox.Show(this, "تعذر حفظ الـPDF:\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        preview.Image?.Dispose();
        preview.Image = null;
        base.OnFormClosed(e);
    }
}
