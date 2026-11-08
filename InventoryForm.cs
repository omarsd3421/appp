using System;
using System.Drawing;
using System.Globalization;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

/// <summary>
/// شاشة إدارة المخزون:
/// - بحث فوري بالاسم/الباركود/الوصف/التصنيف.
/// - إضافة وتعديل وحذف.
/// - أعمدة قابلة للتكبير والتصغير بالماوس.
/// - عناوين الأعمدة ثابتة وواضحة.
/// </summary>
internal sealed class InventoryForm : Form
{
    private DataGridView grid = null!;
    private TextBox search = null!;
    private Label resultLabel = null!;
    private readonly string user;

    public InventoryForm(string userName)
    {
        user = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        LoadData();
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"📦 إدارة المخزون | المنفذ: {user}", 1250, 760);
        MinimumSize = new Size(1050, 650);

        // ============================================================
        // Layout ثابت من صفين: الرأس ثم الجدول.
        // هذا يمنع الجدول من تغطية عناوين الرأس كما كان يحدث سابقًا.
        // ============================================================
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 165));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ============================================================
        // Header
        // ============================================================
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiKit.Surface,
            Padding = new Padding(28, 12, 28, 10)
        };

        var title = new Label
        {
            Text = "📦 إدارة المخزون",
            Dock = DockStyle.Top,
            Height = 46,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = UiKit.Text,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var userLabel = new Label
        {
            Text = $"المنفذ الحالي: {user}",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = UiKit.Primary,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            ColumnCount = 6,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 4)
        };

        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340)); // بحث
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115)); // بحث
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); // إضافة
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115)); // تعديل
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125)); // حذف
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115)); // تحديث

        search = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11f),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(4, 4, 8, 4),
            PlaceholderText = "بحث بالاسم أو الباركود أو الوصف أو التصنيف..."
        };
        search.TextChanged += (s, e) => LoadData();
        search.KeyDown += Search_KeyDown;

        var find = UiKit.Button("🔎 بحث", UiKit.Primary, 100, 38);
        find.Dock = DockStyle.Fill;
        find.Margin = new Padding(4);
        find.Click += (s, e) => LoadData();

        var add = UiKit.Button("➕ إضافة صنف", UiKit.Success, 130, 38);
        add.Dock = DockStyle.Fill;
        add.Margin = new Padding(4);
        add.Click += (s, e) => EditProduct(null);

        var edit = UiKit.Button("✏️ تعديل", Color.FromArgb(108, 117, 125), 105, 38);
        edit.Dock = DockStyle.Fill;
        edit.Margin = new Padding(4);
        edit.Click += (s, e) => EditSelectedProduct();

        var delete = UiKit.Button("🗑️ حذف صنف", UiKit.Danger, 115, 38);
        delete.Dock = DockStyle.Fill;
        delete.Margin = new Padding(4);
        delete.Click += (s, e) => DeleteSelectedProduct();

        var refresh = UiKit.Button("↻ تحديث", Color.FromArgb(52, 73, 94), 105, 38);
        refresh.Dock = DockStyle.Fill;
        refresh.Margin = new Padding(4);
        refresh.Click += (s, e) => LoadData();

        toolbar.Controls.Add(search, 0, 0);
        toolbar.Controls.Add(find, 1, 0);
        toolbar.Controls.Add(add, 2, 0);
        toolbar.Controls.Add(edit, 3, 0);
        toolbar.Controls.Add(delete, 4, 0);
        toolbar.Controls.Add(refresh, 5, 0);

        resultLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = UiKit.Muted,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 2, 4, 0)
        };

        header.Controls.Add(resultLabel);
        header.Controls.Add(toolbar);
        header.Controls.Add(userLabel);
        header.Controls.Add(title);

        // ============================================================
        // جدول المخزون
        // ============================================================
        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            RightToLeft = RightToLeft.Yes,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeColumns = true,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            ScrollBars = ScrollBars.Both,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = UiKit.Background,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = UiKit.Border,
            ColumnHeadersVisible = true,
            ColumnHeadersHeight = 46,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing,
            RowTemplate = { Height = 44 },
            EnableHeadersVisualStyles = false
        };

        UiKit.ApplyRightToLeftGridLayout(grid);
        UiKit.EnableColumnSorting(grid);
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(52, 73, 94),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            Padding = new Padding(4)
        };

        grid.RowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = UiKit.Text,
            SelectionBackColor = Color.FromArgb(214, 234, 248),
            SelectionForeColor = Color.Black,
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            Padding = new Padding(4)
        };

        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = UiKit.Text,
            SelectionBackColor = Color.FromArgb(214, 234, 248),
            SelectionForeColor = Color.Black
        };

        // ID نحتاجه داخليًا فقط، ولا نعرضه للمستخدم.
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Id",
            HeaderText = "ID",
            Width = 60,
            Visible = false
        });
        grid.Columns.Add(GridColumn("Barcode", "الباركود", 150));
        grid.Columns.Add(GridColumn("Name", "اسم الصنف", 240));
        grid.Columns.Add(GridColumn("Description", "الوصف", 280));
        grid.Columns.Add(GridColumn("Category", "التصنيف", 150));
        grid.Columns.Add(GridColumn("Purchase", "سعر الشراء", 120));
        grid.Columns.Add(GridColumn("Sale", "سعر البيع", 120));
        grid.Columns.Add(GridColumn("Qty", "الكمية", 95));
        grid.Columns.Add(GridColumn("MinQty", "حد التنبيه", 95));

        // فتح التعديل مرة واحدة فقط عند الضغط مرتين على صف.
        // لا نربط DoubleClick و CellDoubleClick معًا حتى لا تفتح نافذة التعديل مرتين.
        grid.CellDoubleClick -= Grid_CellDoubleClick;
        grid.CellDoubleClick += Grid_CellDoubleClick;
        grid.KeyDown += Grid_KeyDown;
        UiKit.EnableKeyboardDelete(grid, DeleteSelectedProduct);

        // تركيب الجدول أولاً ثم الرأس فوقه يمنع تغطية الـHeader.
        root.Controls.Add(grid, 0, 1);
        root.Controls.Add(header, 0, 0);
        Controls.Add(root);

        UiKit.SelectAll(this);

        // اختصارات المخزن (قابلة للتغيير من إعدادات الإدارة ← الاختصارات).
        // F1 بيعرض قائمة الاختصارات (UiKit.EnableHelpKey من PrepareForm).
        KeyPreview = true;
        KeyDown += Inventory_KeyDown;
    }

    private void Inventory_KeyDown(object? sender, KeyEventArgs e)
    {
        // أثناء الكتابة في خانة البحث نسمح بس بالاختصارات اللي فيها Ctrl/Alt أو مفاتيح F.
        if (ActiveControl is TextBoxBase && !Shortcuts.SafeWhileTyping(e.KeyData))
            return;

        Action? run = null;

        if (Shortcuts.Is("inv.add", e)) run = () => EditProduct(null);
        else if (Shortcuts.Is("inv.edit", e)) run = EditSelectedProduct;
        else if (Shortcuts.Is("inv.search", e)) run = () => { search.Focus(); search.SelectAll(); };
        else if (Shortcuts.Is("inv.refresh", e)) run = LoadData;

        if (run == null)
            return;

        e.SuppressKeyPress = true;
        e.Handled = true;
        run();
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;

        EditSelectedProduct();
    }

    private static DataGridViewTextBoxColumn GridColumn(string name, string header, int width)
        => new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            MinimumWidth = 60,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Padding = new Padding(6)
            }
        };

    private void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!Shortcuts.Is("inv.doSearch", e))
            return;

        e.SuppressKeyPress = true;
        LoadData();
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Shortcuts.Is("inv.rowEdit", e))
        {
            e.SuppressKeyPress = true;
            EditSelectedProduct();
        }
    }

    private long? GetSelectedProductId()
    {
        if (grid.CurrentRow == null)
            return null;

        object? value = grid.CurrentRow.Cells["Id"].Value;
        if (value == null || value == DBNull.Value)
            return null;

        return Convert.ToInt64(value);
    }

    private void EditSelectedProduct()
    {
        long? id = GetSelectedProductId();
        if (!id.HasValue)
        {
            MessageBox.Show(this, "اختر صنفًا أولًا.", "تعديل الصنف", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        EditProduct(id);
    }

    private void DeleteSelectedProduct()
    {
        long? id = GetSelectedProductId();
        if (!id.HasValue)
        {
            MessageBox.Show(this, "اختر الصنف المراد حذفه أولًا.", "حذف الصنف", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string itemName = grid.CurrentRow?.Cells["Name"].Value?.ToString() ?? "الصنف";

        DialogResult answer = MessageBox.Show(
            this,
            $"هل أنت متأكد من حذف الصنف:\n\n{itemName}\n\nلا يمكن التراجع عن الحذف.",
            "تأكيد حذف الصنف",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
            return;

        try
        {
            AppDatabase.Initialize();
            using var c = AppDatabase.OpenConnection();
            using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM Products WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id.Value);

            int affected = cmd.ExecuteNonQuery();
            if (affected == 0)
            {
                MessageBox.Show(this, "الصنف غير موجود أو تم حذفه بالفعل.", "الحذف", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppDatabase.AddAuditLog(c, tx, user, "حذف", "صنف", id.Value, $"تم حذف الصنف: {itemName}");
            tx.Commit();
            LoadData();
            MessageBox.Show(this, "تم حذف الصنف بنجاح.", "تم الحذف", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            MessageBox.Show(
                this,
                "لا يمكن حذف هذا الصنف لأنه مرتبط بسجلات أخرى في قاعدة البيانات.",
                "تعذر الحذف",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر حذف الصنف:\n\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadData()
    {
        if (grid == null)
            return;

        try
        {
            AppDatabase.Initialize();
            using var c = AppDatabase.OpenConnection();

            string q = search?.Text.Trim() ?? "";
            string pattern = "%" + q + "%";

            grid.Rows.Clear();

            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
SELECT
    Id,
    COALESCE(Barcode, ''),
    Name,
    COALESCE(Description, ''),
    COALESCE(Category, ''),
    PurchasePrice,
    SalePrice,
    Quantity,
    MinQuantity
FROM Products
WHERE
    $q = '' OR
    Name LIKE $pattern OR
    COALESCE(Barcode, '') LIKE $pattern OR
    COALESCE(Description, '') LIKE $pattern OR
    COALESCE(Category, '') LIKE $pattern
ORDER BY Name COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$q", q);
            cmd.Parameters.AddWithValue("$pattern", pattern);

            using var r = cmd.ExecuteReader();
            int count = 0;
            int lowStock = 0;
            while (r.Read())
            {
                int stockQty = Convert.ToInt32(r.GetValue(7));
                int minimumQty = Convert.ToInt32(r.GetValue(8));
                int rowIndex = grid.Rows.Add(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.GetString(3),
                    r.GetString(4),
                    FormatDecimal(r.GetValue(5)),
                    FormatDecimal(r.GetValue(6)),
                    stockQty,
                    minimumQty);
                if (stockQty <= minimumQty)
                {
                    lowStock++;
                    grid.Rows[rowIndex].Cells["Qty"].Style.BackColor = stockQty <= 0
                        ? Color.MistyRose : Color.LemonChiffon;
                    grid.Rows[rowIndex].Cells["Qty"].Style.ForeColor = stockQty <= 0
                        ? Color.DarkRed : Color.DarkGoldenrod;
                    grid.Rows[rowIndex].Cells["Qty"].Style.Font = new Font(grid.Font, FontStyle.Bold);
                }
                count++;
            }

            string prefix = q.Length == 0
                ? $"عدد الأصناف: {count}"
                : $"نتيجة البحث: {count} صنف — البحث يشمل الاسم والباركود والوصف والتصنيف";
            resultLabel.Text = lowStock > 0
                ? $"{prefix}   |   ⚠ أصناف وصلت لحد التنبيه أو أقل: {lowStock}"
                : prefix;
            resultLabel.ForeColor = lowStock > 0 ? Color.FromArgb(170, 95, 0) : UiKit.Text;

            if (grid.Rows.Count > 0)
                grid.ClearSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "تعذر تحميل المخزون:\n\n" + ex.Message,
                "خطأ",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string FormatDecimal(object value)
    {
        decimal amount = Convert.ToDecimal(value);
        return $"{amount:F2}";
    }

    private void EditProduct(long? id)
    {
        using var f = new ProductEditorForm(id);
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            LoadData();
        }
    }
}

internal sealed class ProductEditorForm : Form
{
    private readonly long? id;

    private readonly TextBox barcode = new();
    private readonly TextBox name = new();
    private readonly TextBox description = new();
    private readonly TextBox category = new();
    private readonly NumericUpDown purchase = new();
    private readonly NumericUpDown sale = new();
    private readonly NumericUpDown quantity = new();
    private readonly NumericUpDown minQuantity = new();

    public ProductEditorForm(long? productId)
    {
        id = productId;

        AppDatabase.Initialize();
        UiKit.PrepareForm(
            this,
            id.HasValue ? "✏️ تعديل صنف" : "➕ إضافة صنف",
            620,
            680);

        MinimumSize = new Size(580, 640);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(28),
            BackColor = UiKit.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var title = new Label
        {
            Text = id.HasValue ? "✏️ تعديل بيانات الصنف" : "➕ إضافة صنف جديد",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = UiKit.Text,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            BackColor = UiKit.Surface,
            Padding = new Padding(20)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        AddRow(fields, "الباركود (اختياري)", barcode, 0);
        AddRow(fields, "اسم الصنف *", name, 1);

        description.Multiline = true;
        description.ScrollBars = ScrollBars.Vertical;
        description.TextAlign = HorizontalAlignment.Right;
        AddRow(fields, "الوصف", description, 2);

        AddRow(fields, "التصنيف", category, 3);
        AddRow(fields, "سعر الشراء", purchase, 4);
        AddRow(fields, "سعر البيع", sale, 5);
        AddRow(fields, "الكمية الحالية", quantity, 6);
        AddRow(fields, "التنبيه عند كمية", minQuantity, 7);

        ConfigureNumber(purchase, 0.25m, 100000000m, 2);
        ConfigureNumber(sale, 0.25m, 100000000m, 2);
        ConfigureNumber(quantity, 1m, 100000000m, 0);
        ConfigureNumber(minQuantity, 1m, 100000000m, 0);
        minQuantity.Value = 3;

        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        UiKit.EnableToolbarScroll(buttonBar);

        var save = UiKit.Button("💾 حفظ الصنف", UiKit.Success, 170, 42);
        save.Click += (s, e) => Save();

        var cancel = UiKit.Button("إلغاء", Color.FromArgb(108, 117, 125), 130, 42);
        cancel.Click += (s, e) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        buttonBar.Controls.Add(save);
        buttonBar.Controls.Add(cancel);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(buttonBar, 0, 2);
        Controls.Add(root);

        AcceptButton = save;
        CancelButton = cancel;

        if (id.HasValue)
            LoadExisting();

        UiKit.SelectAll(this);
        name.Focus();
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control control, int row)
    {
        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = UiKit.Text,
            Padding = new Padding(0, 0, 8, 0)
        };

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(4);
        if (control is TextBox tb)
        {
            tb.Font = new Font("Segoe UI", 10.5f);
        }

        panel.Controls.Add(labelControl, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private static void ConfigureNumber(NumericUpDown number, decimal increment, decimal maximum, int decimalPlaces)
    {
        number.Minimum = 0;
        number.Maximum = maximum;
        number.DecimalPlaces = decimalPlaces;
        number.Increment = increment;
        number.ThousandsSeparator = false;
        number.TextAlign = HorizontalAlignment.Center;
        number.Font = new Font("Segoe UI", 10.5f);
    }

    private void LoadExisting()
    {
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(Barcode, ''),
    Name,
    COALESCE(Description, ''),
    COALESCE(Category, ''),
    PurchasePrice,
    SalePrice,
    Quantity,
    MinQuantity
FROM Products
WHERE Id = $id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id!.Value);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return;

        barcode.Text = r.GetString(0);
        name.Text = r.GetString(1);
        description.Text = r.GetString(2);
        category.Text = r.GetString(3);
        purchase.Value = Convert.ToDecimal(r.GetValue(4));
        sale.Value = Convert.ToDecimal(r.GetValue(5));
        quantity.Value = Convert.ToDecimal(r.GetValue(6));
        minQuantity.Value = Convert.ToDecimal(r.GetValue(7));
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            MessageBox.Show(
                this,
                "اسم الصنف إجباري.",
                "تنبيه",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            name.Focus();
            return;
        }

        try
        {
            AppDatabase.Initialize();
            using var c = AppDatabase.OpenConnection();
            using var tx = c.BeginTransaction();

            int oldQuantity = 0;
            if (id.HasValue)
            {
                using var oldStock = c.CreateCommand();
                oldStock.Transaction = tx;
                oldStock.CommandText = "SELECT Quantity FROM Products WHERE Id=$id";
                oldStock.Parameters.AddWithValue("$id", id.Value);
                object? oldValue = oldStock.ExecuteScalar();
                if (oldValue == null || oldValue == DBNull.Value) throw new InvalidOperationException("الصنف غير موجود.");
                oldQuantity = Convert.ToInt32(oldValue, CultureInfo.InvariantCulture);
            }

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = id.HasValue
                ? @"
UPDATE Products
SET
    Barcode = $barcode,
    Name = $name,
    Description = $description,
    Category = $category,
    PurchasePrice = $purchase,
    SalePrice = $sale,
    Quantity = $quantity,
    MinQuantity = $minQuantity,
    UpdatedAt = $now,
    StockUpdatedAt = $now
WHERE Id = $id;"
                : @"
INSERT INTO Products
    (Barcode, Name, Description, Category, PurchasePrice, SalePrice, Quantity, MinQuantity, CreatedAt, UpdatedAt, StockUpdatedAt)
VALUES
    ($barcode, $name, $description, $category, $purchase, $sale, $quantity, $minQuantity, $now, $now, $now);";

            cmd.Parameters.AddWithValue("$barcode", NullableText(barcode.Text));
            cmd.Parameters.AddWithValue("$name", name.Text.Trim());
            cmd.Parameters.AddWithValue("$description", NullableText(description.Text));
            cmd.Parameters.AddWithValue("$category", NullableText(category.Text));
            cmd.Parameters.AddWithValue("$purchase", purchase.Value);
            cmd.Parameters.AddWithValue("$sale", sale.Value);
            cmd.Parameters.AddWithValue("$quantity", Convert.ToInt32(quantity.Value));
            cmd.Parameters.AddWithValue("$minQuantity", Convert.ToInt32(minQuantity.Value));
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));

            if (id.HasValue)
                cmd.Parameters.AddWithValue("$id", id.Value);

            int affected = cmd.ExecuteNonQuery();
            if (id.HasValue && affected == 0)
                throw new InvalidOperationException("الصنف غير موجود.");

            long savedProductId = id ?? 0;
            if (!id.HasValue)
            {
                using var idCommand = c.CreateCommand();
                idCommand.Transaction = tx;
                idCommand.CommandText = "SELECT last_insert_rowid();";
                savedProductId = Convert.ToInt64(idCommand.ExecuteScalar());
            }
            int newQuantity = Convert.ToInt32(quantity.Value, CultureInfo.InvariantCulture);
            int stockDelta = newQuantity - oldQuantity;
            if (stockDelta != 0)
                BusinessManagementStore.RecordStockMovement(c, tx, AppLog.CurrentUser ?? "غير معروف", savedProductId,
                    "MANUAL_ADJUSTMENT", stockDelta, purchase.Value, "ProductEditor", savedProductId,
                    $"تعديل يدوي للرصيد من {oldQuantity} إلى {newQuantity}");
            AppDatabase.AddAuditLog(c, tx, AppLog.CurrentUser ?? "غير معروف", id.HasValue ? "تعديل" : "إضافة",
                "صنف", savedProductId,
                $"الاسم: {name.Text.Trim()} | الكمية: {oldQuantity} → {newQuantity} | حد التنبيه: {Convert.ToInt32(minQuantity.Value)} | سعر البيع: {sale.Value:F2}");
            tx.Commit();

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            MessageBox.Show(
                this,
                "الباركود مستخدم بالفعل لصنف آخر. اكتب باركودًا مختلفًا أو اتركه فارغًا.",
                "باركود مكرر",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            barcode.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "تعذر حفظ الصنف:\n\n" + ex.Message,
                "خطأ",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static object NullableText(string text)
        => string.IsNullOrWhiteSpace(text) ? DBNull.Value : text.Trim();
}
