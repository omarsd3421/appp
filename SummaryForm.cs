using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

/// <summary>
/// سجل وملخص جميع العمليات: الطباعة والتصوير والمبيعات وخدمات الدفع،
/// مع إظهار العمليات الآجلة حتى إن لم ينتج عنها أي حركة خزينة.
/// </summary>
public class SummaryForm : Form
{
    private readonly string currentUser;
    private ComboBox cmbUser = null!;
    private ComboBox cmbPeriod = null!;
    private ComboBox cmbSource = null!;
    private DateTimePicker dtDate = null!;
    private DataGridView grid = null!;
    private Label summary = null!;

    public SummaryForm(string userName = "غير معروف")
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        ShowResults();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "📊 سجل العمليات والملخصات", 1180, 700);
        UiKit.EnableEscape(this);

        var header = new Panel { Dock = DockStyle.Top, Height = 120, BackColor = Color.White, Padding = new Padding(18, 8, 18, 8) };
        header.Controls.Add(UiKit.Title("📊 سجل العمليات والملخصات", 18));

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 3)
        };
        UiKit.EnableToolbarScroll(filters);

        cmbSource = Combo("المصدر", new[] { "كل العمليات", "الطباعة والتصوير", "الطباعة فقط", "التصوير فقط", "المبيعات فقط", "خدمات الدفع فقط", "خدمات الكاش فقط" }, 190);
        cmbUser = Combo("المستخدم", new[] { "الكل", "عمر", "أحمد" }, 120);
        cmbPeriod = Combo("الفترة", new[] { "يومي", "شهري", "سنوي" }, 110);
        dtDate = new DateTimePicker { Width = 150, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Margin = new Padding(5) };
        var show = UiKit.Button("🔎 عرض", UiKit.Primary, 110, 40);
        show.Click += (s, e) => ShowResults();

        filters.Controls.Add(cmbSource);
        filters.Controls.Add(cmbUser);
        filters.Controls.Add(cmbPeriod);
        filters.Controls.Add(dtDate);
        filters.Controls.Add(show);
        header.Controls.Add(filters);

        grid = new DataGridView();
        UiKit.StyleGrid(grid);
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.AllowUserToResizeColumns = true;
        grid.Columns.Add(UiKit.GridText("Time", "التاريخ والوقت", 155));
        grid.Columns.Add(UiKit.GridText("Source", "المصدر", 125));
        grid.Columns.Add(UiKit.GridText("User", "بواسطة", 90));
        grid.Columns.Add(UiKit.GridText("Description", "البيان / التفاصيل", 520, true));
        grid.Columns.Add(UiKit.GridText("Amount", "الإجمالي", 130));
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            if (grid.Rows[e.RowIndex].Tag is not SummaryOperationReference reference) return;
            using var details = new OperationDetailsForm(reference.ReferenceType, reference.ReferenceId, 0, currentUser);
            details.ShowDialog(this);
            ShowResults();
        };

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15, 0, 15, 15), BackColor = UiKit.Background };
        host.Controls.Add(grid);

        summary = new Label
        {
            Dock = DockStyle.Bottom, Height = 58, BackColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            ForeColor = UiKit.Success, BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(host);
        Controls.Add(summary);
        Controls.Add(header);
    }

    private static ComboBox Combo(string caption, string[] items, int width)
    {
        var c = new ComboBox { Width = width, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(5) };
        c.Items.AddRange(items);
        c.SelectedIndex = 0;
        c.AccessibleName = caption;
        ComboBoxStyler.ApplyModernStyle(c);
        return c;
    }

    private void ShowResults()
    {
        try
        {
            var rows = new List<(DateTime Time, string Source, string User, string Description, decimal Amount, string? RefType, long RefId)>();
            string source = cmbSource.SelectedItem?.ToString() ?? "كل العمليات";
            string user = cmbUser.SelectedItem?.ToString() ?? "الكل";
            string period = cmbPeriod.SelectedItem?.ToString() ?? "يومي";
            DateTime d = dtDate.Value.Date;

            bool includeAll = source == "كل العمليات";
            bool includePrint = includeAll || source == "الطباعة والتصوير" || source == "الطباعة فقط";
            bool includeCopy = includeAll || source == "الطباعة والتصوير" || source == "التصوير فقط";
            bool includeSales = includeAll || source == "المبيعات فقط";
            bool includeServices = includeAll || source == "خدمات الدفع فقط";
            bool includeCashServices = includeAll || source == "خدمات الكاش فقط";

            if (includePrint)
            {
                foreach (var x in PrintLogger.LoadDatabaseEntries())
                {
                    if (!MatchesPeriod(x.Timestamp, period, d)) continue;
                    if (user != "الكل" && x.UserName != user) continue;
                    decimal amount = x.Price;
                    string fileLabel = OperationDetailsForm.FormatFileNameWithPages(x.FileName, x.Pages);
                    var parts = new List<string> { fileLabel };
                    if (!string.IsNullOrWhiteSpace(x.FileType)) parts.Add(x.FileType);
                    if (x.Copies > 0) parts.Add($"{x.Copies} نسخة");
                    if (!string.IsNullOrWhiteSpace(x.PricingCategory)) parts.Add($"الفئة: {x.PricingCategory}");
                    if (!string.IsNullOrWhiteSpace(x.ColorType)) parts.Add(x.ColorType);
                    if (!string.IsNullOrWhiteSpace(x.DuplexMode)) parts.Add(x.DuplexMode);
                    rows.Add((x.Timestamp, "الطباعة", x.UserName, string.Join(" | ", parts), amount, "PrintLog", x.Id));
                }
            }

            if (includeCopy)
            {
                using var c = AppDatabase.OpenConnection();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
SELECT t.CreatedAt,u.UserName,COALESCE(t.Description,''),
       COALESCE(t.RequiredAmount,0),COALESCE(t.PaidAmount,0),COALESCE(t.RemainingAmount,0),
       COALESCE(c.Name,''),t.Id
FROM Transactions t
JOIN AppUsers u ON u.Id=t.UserId
LEFT JOIN Customers c ON c.Id=t.CustomerId
WHERE t.TransactionType='COPY'
  AND ($u='الكل' OR u.UserName=$u)
ORDER BY t.CreatedAt DESC";
                cmd.Parameters.AddWithValue("$u", user);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    DateTime time = DateTime.Parse(r.GetString(0), CultureInfo.InvariantCulture);
                    if (!MatchesPeriod(time, period, d)) continue;
                    decimal required = r.GetDecimal(3);
                    decimal paid = r.GetDecimal(4);
                    decimal remaining = r.GetDecimal(5);
                    string customer = r.IsDBNull(6) ? "" : r.GetString(6);
                    string detail = r.GetString(2);
                    if (!string.IsNullOrWhiteSpace(customer)) detail += $" | العميل: {customer}";
                    detail += $" | الإجمالي: {required:F2} | المدفوع: {paid:F2} | الدين: {remaining:F2}";
                    string sourceName = remaining > 0 ? "التصوير - دين" : "التصوير";
                    rows.Add((time, sourceName, r.GetString(1), detail, required, "Copy", r.GetInt64(7)));
                }
            }

            if (includeCashServices)
            {
                using var c = AppDatabase.OpenConnection();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
SELECT cs.CreatedAt,cs.ServiceType,cs.Provider,COALESCE(cs.CustomerName,c.Name,''),
       COALESCE(cs.Phone,''),cs.Amount,cs.Commission,cs.TotalAmount,u.UserName,cs.Id
FROM Cash_Services cs
JOIN AppUsers u ON u.Id=cs.UserId
LEFT JOIN Customers c ON c.Id=cs.CustomerId
WHERE ($u='الكل' OR u.UserName=$u)
ORDER BY cs.CreatedAt DESC;";
                cmd.Parameters.AddWithValue("$u", user);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    DateTime time = DateTime.Parse(r.GetString(0), CultureInfo.InvariantCulture);
                    if (!MatchesPeriod(time, period, d)) continue;
                    string customer = r.IsDBNull(3) ? "" : r.GetString(3);
                    string phone = r.IsDBNull(4) ? "" : r.GetString(4);
                    decimal amount = Convert.ToDecimal(r.GetValue(5), CultureInfo.InvariantCulture);
                    decimal commission = Convert.ToDecimal(r.GetValue(6), CultureInfo.InvariantCulture);
                    decimal serviceTotalAmount = Convert.ToDecimal(r.GetValue(7), CultureInfo.InvariantCulture);
                    string detail = $"{r.GetString(1)} | {r.GetString(2)} | المبلغ: {amount:N2} ج.م | العمولة: {commission:N2} ج.م";
                    if (!string.IsNullOrWhiteSpace(customer)) detail += " | العميل: " + customer;
                    if (!string.IsNullOrWhiteSpace(phone)) detail += " | الهاتف: " + phone;
                    rows.Add((time, "خدمات الكاش", r.GetString(8), detail, serviceTotalAmount, "CashService", r.GetInt64(9)));
                }
            }

            // كانت المبيعات وخدمات الدفع تُسجل في Transactions لكنها غير مدرجة في
            // هذا السجل أصلًا؛ لذلك كانت عملية البيع الآجلة تختفي من سجل العمليات.
            // عرضها مباشرة من جدول العمليات، لا من حركات الخزينة، حتى يظهر الدين
            // كاملًا من غير اعتباره مبلغًا نقديًا تم تحصيله.
            if (includeSales || includeServices)
            {
                string types = includeSales && includeServices ? "'SALE','SERVICE'"
                    : includeSales ? "'SALE'" : "'SERVICE'";
                using var c = AppDatabase.OpenConnection();
                using var cmd = c.CreateCommand();
                cmd.CommandText = $@"
SELECT t.CreatedAt,t.TransactionType,COALESCE(t.ServiceName,''),
       COALESCE(t.Description,''),COALESCE(t.RequiredAmount,0),
       COALESCE(t.PaidAmount,0),COALESCE(t.RemainingAmount,0),
       COALESCE(c.Name,''),u.UserName,t.Id
FROM Transactions t
JOIN AppUsers u ON u.Id=t.UserId
LEFT JOIN Customers c ON c.Id=t.CustomerId
WHERE t.TransactionType IN ({types})
  AND ($u='الكل' OR u.UserName=$u)
ORDER BY t.CreatedAt DESC;";
                cmd.Parameters.AddWithValue("$u", user);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    DateTime time = DateTime.Parse(r.GetString(0), CultureInfo.InvariantCulture);
                    if (!MatchesPeriod(time, period, d)) continue;
                    string type = r.GetString(1);
                    string serviceName = r.IsDBNull(2) ? "" : r.GetString(2);
                    string description = r.IsDBNull(3) ? "" : r.GetString(3);
                    decimal required = r.GetDecimal(4);
                    decimal paid = r.GetDecimal(5);
                    decimal remaining = r.GetDecimal(6);
                    string customer = r.IsDBNull(7) ? "" : r.GetString(7);
                    string userName = r.GetString(8);
                    string sourceName = type == "SALE" ? "المبيعات" :
                        (string.IsNullOrWhiteSpace(serviceName) ? "خدمات الدفع" : serviceName);
                    if (remaining > 0) sourceName += " - دين";
                    if (!string.IsNullOrWhiteSpace(customer)) description += $" | العميل: {customer}";
                    description += $" | الإجمالي: {required:F2} | المدفوع: {paid:F2} | الدين: {remaining:F2}";
                    rows.Add((time, sourceName, userName, description, required,
                        type == "SALE" ? "Transaction" : "ServiceTransaction", r.GetInt64(9)));
                }
            }

            grid.Rows.Clear();
            decimal total = 0;
            foreach (var x in rows.OrderByDescending(x => x.Time))
            {
                int rowIndex = grid.Rows.Add(x.Time.ToString("yyyy-MM-dd HH:mm"), x.Source, x.User, x.Description, $"{x.Amount:F2} ج.م");
                if (!string.IsNullOrWhiteSpace(x.RefType) && x.RefId > 0)
                    grid.Rows[rowIndex].Tag = new SummaryOperationReference(x.RefType, x.RefId);
                total += x.Amount;
            }

            summary.Text = $"عدد العمليات: {rows.Count}    |    إجمالي قيمة العمليات (يشمل الديون): {total:F2} ج.م";
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

    private sealed record SummaryOperationReference(string ReferenceType, long ReferenceId);

    private static bool MatchesPeriod(DateTime value, string period, DateTime d)
    {
        return period switch
        {
            "سنوي" => value.Year == d.Year,
            "شهري" => value.Year == d.Year && value.Month == d.Month,
            _ => value.Date == d.Date
        };
    }
}
