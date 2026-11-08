using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal sealed class ReportsForm : Form
{
    private readonly string currentUser;
    private ComboBox service = null!, user = null!, period = null!;
    private DateTimePicker date = null!;
    private DataGridView grid = null!;
    private Label summary = null!;
    private Label emptyState = null!;

    public ReportsForm(string userName = "غير معروف")
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        LoadReport();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "📊 التقارير العامة", 1250, 760);
        UiKit.EnableEscape(this);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 155,
            BackColor = Color.White,
            Padding = new Padding(22, 10, 22, 10)
        };
        header.Controls.Add(UiKit.Title("📊 التقارير العامة للخدمات", 19));

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 4)
        };
        UiKit.EnableToolbarScroll(filters);

        service = new ComboBox { Width = 175, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(5) };
        service.Items.AddRange(new object[] { "الكل", "الطباعة", "المبيعات", "النقدية في الدرج", "خدمات الكاش", "خدمات الدفع", "التصوير", "تحصيل ديون", "النسبة", "الخارج" });
        service.SelectedIndex = 0;
        ComboBoxStyler.ApplyModernStyle(service);

        user = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(5) };
        user.Items.AddRange(new object[] { "الكل", "عمر", "أحمد" });
        user.SelectedIndex = 0;
        ComboBoxStyler.ApplyModernStyle(user);

        period = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(5) };
        period.Items.AddRange(new object[] { "يومي", "شهري", "سنوي" });
        period.SelectedIndex = 0;
        ComboBoxStyler.ApplyModernStyle(period);

        date = new DateTimePicker
        {
            Width = 150,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Margin = new Padding(5)
        };

        var show = UiKit.Button("🔎 عرض التقرير", UiKit.Primary, 135, 42);
        show.Click += (s, e) => LoadReport();
        var export = UiKit.Button("📤 تصدير CSV", UiKit.Success, 125, 42);
        export.Click += (s, e) => ExportReport();

        filters.Controls.Add(service);
        filters.Controls.Add(user);
        filters.Controls.Add(period);
        filters.Controls.Add(date);
        filters.Controls.Add(show);
        filters.Controls.Add(export);
        header.Controls.Add(filters);

        grid = new DataGridView();
        UiKit.StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        // لا نضبط AutoSizeColumnsMode=Fill هنا (يتعارض مع AutoSizeMode=None الصريح لكل عمود
        // ويسبب عدم ظهور الجدول في RTL) — نعتمد على العرض الثابت لكل عمود + التمرير الأفقي.
        grid.AllowUserToResizeColumns = true;
        grid.ScrollBars = ScrollBars.Both;
        grid.ColumnHeadersVisible = true;
        grid.Columns.Add(UiKit.GridText("Time", "التاريخ والوقت", 145));
        grid.Columns.Add(UiKit.GridText("Service", "الخدمة", 150));
        grid.Columns.Add(UiKit.GridText("Customer", "العميل", 150));
        grid.Columns.Add(UiKit.GridText("Price", "السعر", 105));
        grid.Columns.Add(UiKit.GridText("Commission", "العمولة", 105));
        grid.Columns.Add(UiKit.GridText("Total", "الإجمالي", 110));
        grid.Columns.Add(UiKit.GridText("Paid", "المدفوع", 110));
        grid.Columns.Add(UiKit.GridText("Remaining", "الباقي/الدين", 110));
        grid.Columns.Add(UiKit.GridText("In", "داخل", 105));
        grid.Columns.Add(UiKit.GridText("Out", "خارج", 105));
        grid.Columns.Add(UiKit.GridText("User", "بواسطة", 90));
        grid.Columns.Add(UiKit.GridText("Description", "التفاصيل", 430, true));
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            if (grid.Rows[e.RowIndex].Tag is not ReportOperationReference reference) return;
            using var details = new OperationDetailsForm(reference.ReferenceType, reference.ReferenceId, reference.MovementId, currentUser);
            details.ShowDialog(this);
            LoadReport();
        };

        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15, 0, 15, 15), BackColor = Color.FromArgb(244,246,249) };
        gridHost.Controls.Add(grid);
        emptyState = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            Text = "لا توجد عمليات في الفترة المحددة.",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(120, 128, 136),
            BackColor = Color.FromArgb(244, 246, 249),
            Visible = false
        };
        gridHost.Controls.Add(emptyState);
        emptyState.BringToFront();

        summary = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            BackColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 132, 73),
            BorderStyle = BorderStyle.FixedSingle
        };

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(244, 246, 249),
            Padding = new Padding(15, 0, 15, 15)
        };
        // الجدول داخل حاوية مستقلة حتى يظهر Header والصفوف بشكل ثابت.
        gridHost.Dock = DockStyle.Fill;
        gridHost.Padding = new Padding(0);
        content.Controls.Add(gridHost);
        content.Controls.Add(summary);

        Controls.Add(content);
        Controls.Add(header);
        grid.BringToFront();
    }

    private void ExportReport()
    {
        if (grid == null || grid.Columns.Count == 0)
        {
            MessageBox.Show(this, "لا توجد بيانات لتصديرها.", "تصدير التقرير", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "تصدير التقرير الحالي لفتحه في Excel",
            Filter = "ملف CSV متوافق مع Excel (*.csv)|*.csv",
            FileName = $"تقرير_المكتبة_{DateTime.Now:yyyy-MM-dd_HHmm}.csv",
            AddExtension = true,
            DefaultExt = "csv",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", grid.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible).Select(c => CsvCell(c.HeaderText))));
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                csv.AppendLine(string.Join(",", grid.Columns.Cast<DataGridViewColumn>()
                    .Where(c => c.Visible)
                    .Select(c => CsvCell(row.Cells[c.Index].Value?.ToString() ?? string.Empty))));
            }
            csv.AppendLine();
            csv.AppendLine(CsvCell(summary.Text));
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show(this, "تم تصدير التقرير. تقدر تفتحه مباشرة في Excel.", "تم التصدير",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل تصدير التقرير", ex);
            MessageBox.Show(this, "تعذر تصدير التقرير:\n" + ex.Message, "خطأ في التصدير",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string CsvCell(string value)
    {
        string safe = (value ?? string.Empty).Replace("\"", "\"\"");
        return "\"" + safe + "\"";
    }

    private sealed record ReportOperationReference(string ReferenceType, long ReferenceId, long MovementId);

    private void LoadReport()
    {
        try
        {
            using var c = AppDatabase.OpenConnection();
            grid.Rows.Clear();

            string p = period.SelectedItem?.ToString() ?? "يومي";
            string u = user.SelectedItem?.ToString() ?? "الكل";
            string selectedService = service.SelectedItem?.ToString() ?? "الكل";
            DateTime d = date.Value.Date;

            using var cmd = c.CreateCommand();

            string dateWhere;
            if (p == "سنوي")
                dateWhere = "strftime('%Y',t.CreatedAt)=strftime('%Y',$d)";
            else if (p == "شهري")
                dateWhere = "strftime('%Y-%m',t.CreatedAt)=strftime('%Y-%m',$d)";
            else
                dateWhere = "date(t.CreatedAt)=date($d)";

            // استبعاد بند «النسبة» من تقارير الأموال العامة لأنه تحليل لعمولات موجودة أصلًا في الدرج.
            // يمكن اختياره منفصلًا عند الرغبة في مراجعة مجموع العمولات.
            string serviceWhere = selectedService == "النسبة" ? "t.AccountKey='PERCENTAGE'" : "COALESCE(t.AccountKey,'')<>'PERCENTAGE'";
            if (selectedService == "النسبة")
            { /* Already filtered to the informational commission account. */ }
            else if (selectedService == "الطباعة")
                serviceWhere = "t.Category='الطباعة'";
            else if (selectedService == "المبيعات")
                serviceWhere = "t.AccountKey='SALES'";
            else if (selectedService == "النقدية في الدرج")
                serviceWhere = "t.AccountKey='DRAWER'";
            else if (selectedService == "خدمات الكاش")
                serviceWhere = "t.Category='خدمات الكاش'";
            else if (selectedService == "خدمات الدفع")
                serviceWhere = "t.Category='خدمات الدفع'";
            else if (selectedService == "التصوير")
                serviceWhere = "(t.ReferenceType='Copy' OR t.Category='التصوير')";
            else if (selectedService == "تحصيل ديون")
                serviceWhere = "t.Category='تحصيل ديون'";
            else if (selectedService == "الخارج")
                serviceWhere = "t.MovementType='OUT'";

            cmd.CommandText = $@"
SELECT t.CreatedAt,
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
       t.Amount,
       t.MovementType,
       u.UserName,
       COALESCE(t.Description,''),t.ReferenceType,COALESCE(t.ReferenceId,0),t.Id,
       COALESCE(pl.FileName,''),COALESCE(pl.Pages,0),COALESCE(pl.PricingCategory,tr.PricingCategory,'')
FROM TreasuryMovements t
JOIN AppUsers u ON u.Id=t.UserId
LEFT JOIN PrintLog pl ON t.ReferenceType='PrintLog' AND t.ReferenceId=pl.Id
LEFT JOIN Transactions tr
  ON t.ReferenceId=tr.Id AND t.ReferenceType IN ('ServiceTransaction','Transaction','Copy')
LEFT JOIN Cash_Services cs
  ON t.ReferenceType='CashService' AND t.ReferenceId=cs.Id
LEFT JOIN Customers c
  ON tr.CustomerId=c.Id
WHERE {dateWhere}
  AND {serviceWhere}
  AND ($u='الكل' OR u.UserName=$u)
ORDER BY t.CreatedAt DESC";

            cmd.Parameters.AddWithValue("$d", d.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$u", u);

            decimal incoming = 0;
            decimal outgoing = 0;

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    bool isIn = r.GetString(9) == "IN";
                    decimal amount = r.GetDecimal(8);
                    if (isIn) incoming += amount; else outgoing += amount;

                    string fileName = r.IsDBNull(15) ? "" : r.GetString(15);
                    int pageCount = r.IsDBNull(16) ? 0 : r.GetInt32(16);
                    string priceCategory = r.IsDBNull(17) ? "" : r.GetString(17);
                    string description = string.IsNullOrWhiteSpace(fileName) ? r.GetString(11) : OperationDetailsForm.FormatFileNameWithPages(fileName, pageCount);
                    if (!string.IsNullOrWhiteSpace(priceCategory) && description.IndexOf("فئة:", StringComparison.OrdinalIgnoreCase) < 0)
                        description += " | الفئة: " + priceCategory;
                    int rowIndex = grid.Rows.Add(
                        DateTime.Parse(r.GetString(0)).ToString("yyyy-MM-dd HH:mm"),
                        r.GetString(1),
                        r.GetString(2),
                        $"{r.GetDecimal(3):F2} ج.م",
                        $"{r.GetDecimal(4):F2} ج.م",
                        $"{r.GetDecimal(5):F2} ج.م",
                        $"{r.GetDecimal(6):F2} ج.م",
                        $"{r.GetDecimal(7):F2} ج.م",
                        isIn ? $"{amount:F2} ج.م" : "",
                        !isIn ? $"{amount:F2} ج.م" : "",
                        r.GetString(10),
                        description);
                    grid.Rows[rowIndex].Tag = new ReportOperationReference(r.IsDBNull(12) ? "" : r.GetString(12), r.GetInt64(13), r.GetInt64(14));
                }
            }

            // الدين عملية مسجلة لكنه ليس نقدًا دخل الخزينة؛ أظهر العمليات الآجلة
            // التي لا يوجد لها أي قيد خزينة حتى لا تختفي من سجل العمليات.
            string debtTypeFilter = selectedService switch
            {
                "المبيعات" => "tr.TransactionType='SALE'",
                "التصوير" => "tr.TransactionType='COPY'",
                "خدمات الدفع" => "tr.TransactionType='SERVICE'",
                "الكل" => "tr.TransactionType IN ('SALE','COPY','SERVICE')",
                _ => "1=0"
            };
            if (debtTypeFilter != "1=0")
            {
                string debtDateFilter = p == "سنوي"
                    ? "strftime('%Y',tr.CreatedAt)=strftime('%Y',$d)"
                    : p == "شهري"
                        ? "strftime('%Y-%m',tr.CreatedAt)=strftime('%Y-%m',$d)"
                        : "date(tr.CreatedAt)=date($d)";
                using var debtCmd = c.CreateCommand();
                debtCmd.CommandText = $@"
SELECT tr.CreatedAt,
       CASE WHEN tr.TransactionType='COPY' THEN 'التصوير - دين'
            WHEN tr.TransactionType='SALE' THEN 'المبيعات - دين'
            ELSE COALESCE(tr.ServiceName,'خدمة') || ' - دين' END,
       COALESCE(c.Name,''),COALESCE(tr.OriginalAmount,0),COALESCE(tr.Commission,0),
       tr.RequiredAmount,tr.PaidAmount,tr.RemainingAmount,u.UserName,COALESCE(tr.Description,''),tr.Id
FROM Transactions tr
JOIN AppUsers u ON u.Id=tr.UserId
LEFT JOIN Customers c ON c.Id=tr.CustomerId
WHERE {debtDateFilter}
  AND ($u='الكل' OR u.UserName=$u)
  AND tr.RemainingAmount>0
  AND {debtTypeFilter}
  AND NOT EXISTS (
      SELECT 1 FROM TreasuryMovements tm
      WHERE tm.ReferenceId=tr.Id
        AND ((tr.TransactionType='SALE' AND tm.ReferenceType='Transaction')
          OR (tr.TransactionType='COPY' AND tm.ReferenceType IN ('Copy','Transaction'))
          OR (tr.TransactionType='SERVICE' AND tm.ReferenceType='ServiceTransaction'))
  )
ORDER BY tr.CreatedAt DESC;";
                debtCmd.Parameters.AddWithValue("$d", d.ToString("yyyy-MM-dd"));
                debtCmd.Parameters.AddWithValue("$u", u);
                using var debtReader = debtCmd.ExecuteReader();
                while (debtReader.Read())
                {
                    int rowIndex = grid.Rows.Add(
                        DateTime.Parse(debtReader.GetString(0)).ToString("yyyy-MM-dd HH:mm"),
                        debtReader.GetString(1),
                        debtReader.GetString(2),
                        $"{debtReader.GetDecimal(3):F2} ج.م",
                        $"{debtReader.GetDecimal(4):F2} ج.م",
                        $"{debtReader.GetDecimal(5):F2} ج.م",
                        $"{debtReader.GetDecimal(6):F2} ج.م",
                        $"{debtReader.GetDecimal(7):F2} ج.م",
                        "", "",
                        debtReader.GetString(8),
                        debtReader.GetString(9));
                    string transactionType = debtReader.GetString(1).Contains("التصوير", StringComparison.Ordinal) ? "Copy"
                        : debtReader.GetString(1).Contains("المبيعات", StringComparison.Ordinal) ? "Transaction" : "ServiceTransaction";
                    grid.Rows[rowIndex].Tag = new ReportOperationReference(transactionType, debtReader.GetInt64(10), 0);
                }
            }

            emptyState.Visible = grid.Rows.Count == 0;
            summary.Text =
                $"إجمالي الداخل: {incoming:F2} ج.م    |    " +
                $"إجمالي الخارج: {outgoing:F2} ج.م    |    " +
                $"الصافي: {(incoming - outgoing):F2} ج.م";
        }
        catch (SqliteException ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ قاعدة البيانات", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
