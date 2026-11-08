using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal sealed class OperationDetailsForm : Form
{
    internal static string FormatFileNameWithPages(string? fileName, int pages)
    {
        string name = fileName?.Trim() ?? string.Empty;
        if (name.Length == 0) return string.Empty;
        return pages > 0 ? $"{name} [{pages}]" : name;
    }

    private readonly string referenceType;
    private readonly long referenceId;
    private readonly long movementId;
    private readonly string actor;
    private TableLayoutPanel rootLayout = null!;
    private Control categoryPanel = null!;
    private DataGridView detailsGrid = null!;
    private ComboBox categoryCombo = null!;
    private Button saveCategoryButton = null!;
    private Label categoryHint = null!;
    private bool categoryEditable;
    private bool categoryRelevant;
    private DateTime? operationTime;
    private string currentCategory = "";

    public OperationDetailsForm(string? referenceType, long referenceId, long movementId, string actor)
    {
        this.referenceType = referenceType ?? "";
        this.referenceId = referenceId;
        this.movementId = movementId;
        this.actor = string.IsNullOrWhiteSpace(actor) ? "غير معروف" : actor;
        BuildUi();
        LoadDetails();
        InputFocusHelper.Apply(this);
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "📋 تفاصيل العملية", 820, 700);
        UiKit.EnableEscape(this);
        MinimumSize = new Size(700, 560);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            BackColor = UiKit.Background
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        Controls.Add(rootLayout);

        var title = UiKit.Title("📋 تفاصيل العملية بالكامل", 18);
        title.Dock = DockStyle.Fill;
        rootLayout.Controls.Add(title, 0, 0);

        detailsGrid = new SafeDataGridView();
        UiKit.StyleGrid(detailsGrid);
        detailsGrid.Dock = DockStyle.Fill;
        detailsGrid.AllowUserToAddRows = false;
        detailsGrid.AllowUserToDeleteRows = false;
        detailsGrid.ReadOnly = true;
        detailsGrid.RowHeadersVisible = false;
        detailsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        detailsGrid.Columns.Add(UiKit.GridText("Field", "البيان", 220));
        detailsGrid.Columns.Add(UiKit.GridText("Value", "التفاصيل", 520, true));
        rootLayout.Controls.Add(detailsGrid, 0, 1);

        var editPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(12),
            Margin = new Padding(0, 10, 0, 0),
            RightToLeft = RightToLeft.Yes
        };
        editPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        editPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        editPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editPanel.Controls.Add(new Label
        {
            Text = "تعديل الفئة",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = UiKit.Text
        }, 0, 0);
        categoryCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10.5f),
            Margin = new Padding(4)
        };
        categoryCombo.Items.AddRange(new object[] { "الطلبة", "عادي", "مخصص" });
        ComboBoxStyler.ApplyModernStyle(categoryCombo);
        editPanel.Controls.Add(categoryCombo, 1, 0);
        saveCategoryButton = UiKit.Button("💾 حفظ وإعادة التسعير", UiKit.Success, 170, 36);
        saveCategoryButton.Dock = DockStyle.Fill;
        saveCategoryButton.Click += SaveCategory_Click;
        editPanel.Controls.Add(saveCategoryButton, 2, 0);
        categoryHint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = UiKit.Muted,
            Font = new Font("Segoe UI", 9f),
            Text = "تغيير الفئة سيعيد حساب السعر ويحدّث الخزينة والدين المرتبط بالعملية."
        };
        editPanel.Controls.Add(categoryHint, 0, 1);
        editPanel.SetColumnSpan(categoryHint, 3);
        categoryPanel = editPanel;
        rootLayout.Controls.Add(editPanel, 0, 2);
    }

    private void LoadDetails()
    {
        detailsGrid.Rows.Clear();
        categoryEditable = false;
        categoryRelevant = false;
        operationTime = null;
        currentCategory = "";
        categoryCombo.SelectedIndex = -1;

        using var connection = AppDatabase.OpenConnection();
        AddMovementDetails(connection);

        if (referenceId > 0 && string.Equals(referenceType, "PrintLog", StringComparison.OrdinalIgnoreCase))
            LoadPrintDetails(connection);
        else if (referenceId > 0 && (IsTransactionReference(referenceType)))
            LoadTransactionDetails(connection);
        else if (referenceId > 0 && (string.Equals(referenceType, "CashService", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(referenceType, "CashCommission", StringComparison.OrdinalIgnoreCase)))
            LoadCashServiceDetails(connection);
        else if (referenceId > 0 && string.Equals(referenceType, "DebtPayment", StringComparison.OrdinalIgnoreCase))
            LoadDebtPaymentDetails(connection);


        bool sameDay = operationTime.HasValue && operationTime.Value.Date == DateTime.Today;
        categoryEditable = categoryEditable && sameDay;
        categoryPanel.Visible = categoryRelevant;
        rootLayout.RowStyles[2].Height = categoryRelevant ? 116 : 0;
        categoryCombo.Enabled = categoryEditable;
        saveCategoryButton.Enabled = categoryEditable;
        categoryHint.Text = categoryEditable
            ? "التعديل متاح اليوم: سيتم إعادة حساب السعر وتحديث المبلغ الداخل للخزينة والدين المرتبط تلقائيًا."
            : categoryRelevant && operationTime.HasValue
                ? "تعديل الفئة متاح في نفس يوم عملية الطباعة أو التصوير فقط."
                : "هذه العملية لا تحتوي على فئة طباعة/تصوير قابلة للتعديل.";
        if (!categoryEditable)
        {
            categoryCombo.SelectedIndex = -1;
        }
        else if (!string.IsNullOrWhiteSpace(currentCategory))
        {
            int index = categoryCombo.Items.IndexOf(currentCategory);
            categoryCombo.SelectedIndex = index;
        }
    }

    private void AddMovementDetails(SqliteConnection connection)
    {
        if (movementId <= 0 && (referenceId <= 0 || string.IsNullOrWhiteSpace(referenceType))) return;

        using var command = connection.CreateCommand();
        if (referenceId > 0 && !string.IsNullOrWhiteSpace(referenceType) &&
            !string.Equals(referenceType, "DebtPayment", StringComparison.OrdinalIgnoreCase))
        {
            // بعض العمليات (مثل خدمات الكاش) تنتج أكثر من حركة خزينة واحدة؛
            // نعرض كل الحركات المرتبطة بالعملية بدل الاكتفاء بآخر حركة فقط.
            command.CommandText = @"
SELECT t.CreatedAt,t.MovementType,t.Category,t.AccountKey,t.Amount,COALESCE(t.Description,''),COALESCE(u.UserName,'')
FROM TreasuryMovements t LEFT JOIN AppUsers u ON u.Id=t.UserId
WHERE t.ReferenceType=$type AND t.ReferenceId=$reference
ORDER BY t.CreatedAt,t.Id;";
            command.Parameters.AddWithValue("$type", referenceType);
            command.Parameters.AddWithValue("$reference", referenceId);
        }
        else
        {
            command.CommandText = @"
SELECT t.CreatedAt,t.MovementType,t.Category,t.AccountKey,t.Amount,COALESCE(t.Description,''),COALESCE(u.UserName,'')
FROM TreasuryMovements t LEFT JOIN AppUsers u ON u.Id=t.UserId
WHERE t.Id=$id LIMIT 1;";
            command.Parameters.AddWithValue("$id", movementId);
        }

        var movements = new List<(DateTime Time, string Direction, string Category, string Account, decimal Amount, string Description, string User)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                movements.Add((
                    ParseTime(reader.GetString(0)),
                    reader.GetString(1) == "IN" ? "داخل" : "خارج",
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    AccountLabel(reader.IsDBNull(3) ? "" : reader.GetString(3)),
                    Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                    reader.IsDBNull(5) ? "" : reader.GetString(5),
                    reader.IsDBNull(6) ? "" : reader.GetString(6)));
            }
        }

        for (int i = 0; i < movements.Count; i++)
        {
            var movement = movements[i];
            operationTime ??= movement.Time;
            string label = movements.Count == 1 ? "حركة الخزينة" : $"حركة الخزينة {i + 1}";
            var parts = new List<string>
            {
                movement.Direction,
                movement.Category,
                "الحساب: " + movement.Account,
                "المبلغ: " + movement.Amount.ToString("N2", CultureInfo.InvariantCulture) + " ج.م",
                "الوقت: " + movement.Time.ToString("yyyy-MM-dd HH:mm:ss")
            };
            if (!string.IsNullOrWhiteSpace(movement.Description)) parts.Add("البيان: " + movement.Description);
            if (!string.IsNullOrWhiteSpace(movement.User)) parts.Add("المستخدم: " + movement.User);
            Add(label, string.Join(" | ", parts.Where(x => !string.IsNullOrWhiteSpace(x))));
        }
    }

    private void LoadPrintDetails(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT p.Timestamp,p.FileName,COALESCE(p.FileType,''),p.Pages,p.Copies,
       COALESCE(p.ColorType,''),COALESCE(p.DuplexMode,''),p.PhysicalSheets,p.Price,
       COALESCE(p.PricingCategory,''),COALESCE(p.PrintOptionsJson,''),u.UserName
FROM PrintLog p LEFT JOIN AppUsers u ON u.Id=p.UserId WHERE p.Id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", referenceId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return;

        DateTime time = ParseTime(reader.GetString(0));
        operationTime = time;
        string fileName = reader.IsDBNull(1) ? "" : reader.GetString(1);
        string fileType = reader.IsDBNull(2) ? "" : reader.GetString(2);
        int pages = reader.GetInt32(3);
        int copies = reader.GetInt32(4);
        string color = reader.IsDBNull(5) ? "" : reader.GetString(5);
        string duplex = reader.IsDBNull(6) ? "" : reader.GetString(6);
        int sheets = reader.GetInt32(7);
        decimal price = Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture);
        currentCategory = reader.IsDBNull(9) ? "" : reader.GetString(9);
        string optionsJson = reader.IsDBNull(10) ? "" : reader.GetString(10);

        Add("نوع العملية", "طباعة");
        Add("اسم الملف", fileName);
        Add("نوع الملف", fileType);
        if (pages > 0) Add("عدد الصفحات", pages.ToString(CultureInfo.InvariantCulture));
        if (copies > 0) Add("عدد النسخ", copies.ToString(CultureInfo.InvariantCulture));
        Add("الفئة السعرية", string.IsNullOrWhiteSpace(currentCategory) ? "غير مسجلة في هذه العملية القديمة" : currentCategory);
        Add("الألوان", color);
        Add("الطباعة على الوجهين", duplex);
        if (sheets > 0) Add("عدد أوراق الطابعة", sheets.ToString(CultureInfo.InvariantCulture));
        AddPositive("القيمة المسجلة", price, " ج.م");
        Add("تاريخ ووقت الطباعة", time.ToString("yyyy-MM-dd HH:mm:ss"));
        Add("المستخدم", reader.IsDBNull(11) ? "" : reader.GetString(11));
        AddPrintOptions(optionsJson, duplex);
        categoryRelevant = true;
        categoryEditable = true;
    }

    private void AddPrintOptions(string json, string duplexMode)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var options = JsonSerializer.Deserialize<PrintOptions>(json);
            if (options == null) return;
            Add("الصفحات لكل ورقة", options.PagesPerSheet > 0 ? options.PagesPerSheet.ToString(CultureInfo.InvariantCulture) : "");
            if (options.PageRangeMode == PrintPageRangeMode.Range && !string.IsNullOrWhiteSpace(options.PageRangesText))
                Add("نطاق الصفحات", options.PageRangesText);
            else
                Add("نطاق الصفحات", "كل الصفحات");
            Add("اتجاه الورق", options.Orientation switch
            {
                PrintPageOrientation.Portrait => "عمودي",
                PrintPageOrientation.Landscape => "أفقي",
                _ => "تلقائي"
            });
            Add("مقاس الورق", options.PaperName);
            Add("التحجيم", options.Scaling switch
            {
                PrintScalingMode.ActualSize => "الحجم الفعلي",
                PrintScalingMode.Custom => $"مخصص ({options.CustomScalePercent:0.##}%)",
                _ => "ملاءمة المساحة القابلة للطباعة"
            });
            if (string.Equals(duplexMode, "وش وظهر", StringComparison.OrdinalIgnoreCase))
                Add("التقليب", options.DuplexBinding == DuplexBindingEdge.ShortEdge ? "الحافة القصيرة" : "الحافة الطويلة");
            if (options.PagesPerSheet > 1 && options.NUpGapInches > 0)
                Add("المسافة بين الصفحات", $"{options.NUpGapInches:0.###} بوصة");
            if (options.MarginLeftMm > 0 || options.MarginTopMm > 0 || options.MarginRightMm > 0 || options.MarginBottomMm > 0)
                Add("الهوامش (مم)", $"يمين {options.MarginRightMm:0.##} | يسار {options.MarginLeftMm:0.##} | أعلى {options.MarginTopMm:0.##} | أسفل {options.MarginBottomMm:0.##}");
            Add("التوسيط الأفقي", options.CenterHorizontal ? "نعم" : "لا");
            Add("التوسيط الرأسي", options.CenterVertical ? "نعم" : "لا");
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر قراءة إعدادات الطباعة المحفوظة لعرض التفاصيل", ex);
            Add("إعدادات الطباعة", "تعذر قراءة الإعدادات المحفوظة لهذا السجل القديم.");
        }
    }

    private void LoadTransactionDetails(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT t.CreatedAt,t.TransactionType,t.Description,COALESCE(t.RequiredAmount,0),
       COALESCE(t.PaidAmount,0),COALESCE(t.RemainingAmount,0),COALESCE(t.ChangeAmount,0),
       COALESCE(t.OriginalAmount,0),COALESCE(t.DiscountPercent,0),COALESCE(t.ServiceName,''),
       COALESCE(t.Commission,0),COALESCE(t.CommissionPercent,0),COALESCE(t.Quantity,1),
       COALESCE(t.PricingCategory,''),COALESCE(c.Name,''),COALESCE(p.Name,''),COALESCE(u.UserName,'')
FROM Transactions t
LEFT JOIN Customers c ON c.Id=t.CustomerId
LEFT JOIN Products p ON p.Id=t.ProductId
LEFT JOIN AppUsers u ON u.Id=t.UserId
WHERE t.Id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", referenceId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return;
        DateTime time = ParseTime(reader.GetString(0));
        operationTime = time;
        string type = reader.IsDBNull(1) ? "" : reader.GetString(1);
        string description = reader.IsDBNull(2) ? "" : reader.GetString(2);
        string category = reader.IsDBNull(13) ? "" : reader.GetString(13);
        if (string.IsNullOrWhiteSpace(category) && type == "COPY")
        {
            var match = Regex.Match(description, @"فئة:\s*([^|]+)");
            if (match.Success) category = match.Groups[1].Value.Trim();
        }
        currentCategory = category;

        Add("نوع العملية", type switch { "COPY" => "تصوير", "SALE" => "بيع", "SERVICE" => "خدمة", _ => type });
        Add("البيان", description);
        Add("تاريخ ووقت العملية", time.ToString("yyyy-MM-dd HH:mm:ss"));
        Add("العميل", reader.IsDBNull(14) ? "" : reader.GetString(14));
        Add("الصنف", reader.IsDBNull(15) ? "" : reader.GetString(15));
        if (type == "COPY")
        {
            Add("الفئة السعرية", string.IsNullOrWhiteSpace(category) ? "غير مسجلة في هذه العملية القديمة" : category);
            AddPositive("الإجمالي قبل الخصم", Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture), " ج.م");
            AddPositive("نسبة الخصم", Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture), "%");
            categoryRelevant = true;
            categoryEditable = true;
        }
        else if (type == "SALE")
        {
            if (reader.GetInt32(12) > 0) Add("الكمية", reader.GetInt32(12).ToString(CultureInfo.InvariantCulture));
            AddPositive("السعر قبل الخصم", Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture), " ج.م");
            AddPositive("نسبة الخصم", Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture), "%");
        }
        else if (type == "SERVICE")
        {
            Add("اسم الخدمة", reader.IsDBNull(9) ? "" : reader.GetString(9));
            AddPositive("العمولة", Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture), " ج.م");
            AddPositive("نسبة العمولة", Convert.ToDecimal(reader.GetValue(11), CultureInfo.InvariantCulture), "%");
        }
        Add("الإجمالي", Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture).ToString("N2", CultureInfo.InvariantCulture) + " ج.م");
        Add("المدفوع", Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture).ToString("N2", CultureInfo.InvariantCulture) + " ج.م");
        Add("المتبقي / الدين", Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture).ToString("N2", CultureInfo.InvariantCulture) + " ج.م");
        decimal change = Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture);
        if (change > 0m) Add("الباقي للعميل", change.ToString("N2", CultureInfo.InvariantCulture) + " ج.م");
        Add("المستخدم", reader.IsDBNull(16) ? "" : reader.GetString(16));
    }

    private void LoadCashServiceDetails(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT cs.CreatedAt,COALESCE(cs.ServiceType,''),COALESCE(cs.Provider,''),COALESCE(cs.CustomerName,c.Name,''),
       COALESCE(cs.Phone,''),cs.Amount,cs.Commission,cs.CommissionPercent,cs.TotalAmount,u.UserName
FROM Cash_Services cs LEFT JOIN Customers c ON c.Id=cs.CustomerId
LEFT JOIN AppUsers u ON u.Id=cs.UserId WHERE cs.Id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", referenceId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return;
        DateTime time = ParseTime(reader.GetString(0));
        operationTime = time;
        Add("نوع العملية", reader.GetString(1));
        Add("الشركة / المحفظة", reader.GetString(2));
        Add("العميل", reader.IsDBNull(3) ? "" : reader.GetString(3));
        Add("رقم الهاتف", reader.IsDBNull(4) ? "" : reader.GetString(4));
        AddPositive("المبلغ", Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture), " ج.م");
        AddPositive("العمولة", Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture), " ج.م");
        AddPositive("نسبة العمولة", Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture), "%");
        AddPositive("الإجمالي", Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture), " ج.م");
        Add("التاريخ والوقت", time.ToString("yyyy-MM-dd HH:mm:ss"));
        Add("المستخدم", reader.IsDBNull(9) ? "" : reader.GetString(9));
    }

    private void SaveCategory_Click(object? sender, EventArgs e)
    {
        string nextCategory = categoryCombo.SelectedItem?.ToString() ?? "";
        if (!categoryEditable || string.IsNullOrWhiteSpace(nextCategory) || referenceId <= 0) return;
        if (!operationTime.HasValue || operationTime.Value.Date != DateTime.Today)
        {
            MessageBox.Show(this, "يمكن إعادة تسعير العملية في نفس يوم تنفيذها فقط.", "انتهت مهلة التعديل", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.Equals(currentCategory, nextCategory, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "الفئة المختارة هي المسجلة بالفعل.", "لا يوجد تغيير", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            OperationRepricingResult result;
            using (var connection = AppDatabase.OpenConnection())
            using (var tx = connection.BeginTransaction())
            {
                if (string.Equals(referenceType, "PrintLog", StringComparison.OrdinalIgnoreCase))
                    result = OperationRepricingService.RepricePrint(connection, tx, referenceId, nextCategory, actor);
                else
                    result = OperationRepricingService.RepriceCopy(connection, tx, referenceId, nextCategory, actor);

                DialogResult confirmation = MessageBox.Show(this, result.ConfirmationText,
                    "تأكيد إعادة التسعير", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes)
                {
                    tx.Rollback();
                    return;
                }
                tx.Commit();
            }

            if (result.CsvEntry != null)
            {
                bool optionsUpdated = PrintLogger.UpdateCsvPrintOptions(result.CsvEntry, result.CsvEntry.PrintOptionsJson);
                bool categoryUpdated = PrintLogger.UpdateCsvCategory(result.CsvEntry, nextCategory);
                bool priceUpdated = PrintLogger.UpdateCsvPrice(result.CsvEntry, result.OldPrice, result.NewPrice);
                if (!optionsUpdated || !categoryUpdated || !priceUpdated)
                    AppLog.Info($"تمت إعادة تسعير سجل الطباعة رقم {referenceId} في SQLite، لكن تعذرت مزامنة كل حقوله في PrintLog.csv.");
            }

            MessageBox.Show(this,
                $"تمت إعادة التسعير بنجاح.\n\nالسعر الجديد: {result.NewPrice:N2} ج.م\n" +
                $"المبلغ الداخل للخزينة: {result.NewCash:N2} ج.م\n" +
                $"الدين المرتبط: {result.NewDebt:N2} ج.م",
                "تم التحديث المالي", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadDetails();
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر إعادة تسعير عملية الطباعة/التصوير", ex);
            MessageBox.Show(this, "تعذر إعادة التسعير، ولم يتم اعتماد أي تغيير مالي:\n\n" + ex.Message,
                "تعذر التحديث", MessageBoxButtons.OK, MessageBoxIcon.Error);
            LoadDetails();
        }
    }

    private void LoadDebtPaymentDetails(SqliteConnection connection)
    {
        string customerName;
        string phone;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT COALESCE(Name,''),COALESCE(Phone,'')
FROM Customers WHERE Id=$id LIMIT 1;";
            command.Parameters.AddWithValue("$id", referenceId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return;
            customerName = reader.IsDBNull(0) ? "" : reader.GetString(0);
            phone = reader.IsDBNull(1) ? "" : reader.GetString(1);
        }
        Add("نوع العملية", "سداد دين عميل");
        Add("العميل", customerName);
        Add("رقم الهاتف", phone);
        decimal currentDebt = AppDatabase.GetCustomerDebt(connection, referenceId);
        Add("رصيد الدين الحالي", currentDebt.ToString("N2", CultureInfo.InvariantCulture) + " ج.م");
    }

    private static bool IsTransactionReference(string type) =>
        type.Equals("Copy", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("Transaction", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("ServiceTransaction", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("MachineCommission", StringComparison.OrdinalIgnoreCase);

    private void Add(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        detailsGrid.Rows.Add(label, value);
    }

    private void AddPositive(string label, decimal value, string suffix = "")
    {
        if (value <= 0m) return;
        Add(label, value.ToString("N2", CultureInfo.InvariantCulture) + suffix);
    }

    private static DateTime TruncateToSeconds(DateTime value) => new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Kind);

    private static DateTime ParseTime(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)) return result;
        return DateTime.Parse(value, CultureInfo.CurrentCulture);
    }

    private static string AccountLabel(string account) => account switch
    {
        "DRAWER" => "النقدية في الدرج",
        "CASH" => "فودافون كاش / محافظ أخرى",
        "INSTAPAY" => "إنستا باي",
        "OPAY" => "OPay",
        "BEE" => "باب رزق",
        "PRINTING" => "الطباعة والتصوير",
        "SALES" => "المبيعات",
        "PERCENTAGE" => "النسبة",
        _ => account
    };
}
