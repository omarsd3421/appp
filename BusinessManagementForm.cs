using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal sealed class BusinessManagementForm : Form
{
    private readonly string currentUser;
    private readonly TabControl tabs = new();

    // الموردون
    private readonly DataGridView supplierGrid = new();
    private readonly DataGridView supplierPurchaseGrid = new();
    private readonly DataGridView stockHistoryGrid = new();
    private readonly TextBox stockHistorySearch = new();
    private readonly TextBox supplierName = new();
    private readonly TextBox supplierPhone = new();
    private readonly TextBox supplierAddress = new();

    // المشتريات
    private readonly ComboBox purchaseSupplier = new();
    private readonly TextBox purchaseNumber = new();
    private readonly TextBox purchaseProductSearch = new();
    private readonly ComboBox purchaseProduct = new();
    private readonly NumericUpDown purchaseQty = new();
    private readonly NumericUpDown purchaseCost = new();
    private readonly NumericUpDown purchasePaid = new();
    private readonly TextBox purchaseNotes = new();
    private readonly DataGridView purchaseGrid = new();
    private readonly List<PurchaseDraftLine> purchaseLines = new();

    // المرتجعات
    private readonly TextBox returnSearch = new();
    private readonly DataGridView returnGrid = new();
    private readonly NumericUpDown returnQty = new();
    private readonly TextBox returnReason = new();

    // الأرباح
    private readonly DateTimePicker profitFrom = new();
    private readonly DateTimePicker profitTo = new();
    private readonly Label profitSummary = new();
    private readonly DataGridView profitProductGrid = new();
    private readonly DataGridView profitEmployeeGrid = new();
    private ProfitSnapshot? lastProfit;

    // البحث العام
    private readonly TextBox globalSearch = new();
    private readonly DataGridView globalGrid = new();

    // أرشيف الفواتير
    private readonly TextBox invoiceSearch = new();
    private readonly DataGridView invoiceGrid = new();

    public BusinessManagementForm(string userName)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        LoadSuppliers();
        LoadPurchaseProducts("");
        LoadReturnRows();
        LoadInvoiceRows();
        LoadStockHistory();
        InputFocusHelper.Apply(this);
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "🧾 الإدارة التجارية المتقدمة", 1320, 820);
        MinimumSize = new Size(1100, 700);
        var header = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = Color.White, Padding = new Padding(18, 8, 18, 8) };
        var title = UiKit.Title($"🧾 الإدارة التجارية المتقدمة — {currentUser}", 18); title.Dock = DockStyle.Fill; header.Controls.Add(title);
        tabs.Dock = DockStyle.Fill;
        tabs.RightToLeft = RightToLeft.Yes;
        tabs.RightToLeftLayout = true;
        tabs.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        tabs.TabPages.Add(BuildSuppliersTab());
        tabs.TabPages.Add(BuildPurchasesTab());
        tabs.TabPages.Add(BuildReturnsTab());
        tabs.TabPages.Add(BuildProfitTab());
        tabs.TabPages.Add(BuildSearchTab());
        tabs.TabPages.Add(BuildInvoicesTab());
        tabs.TabPages.Add(BuildInventoryIoTab());
        tabs.TabPages.Add(BuildStockHistoryTab());
        Controls.Add(tabs);
        Controls.Add(header);
    }

    private TabPage NewTab(string text)
    {
        var page = new TabPage(text) { BackColor = UiKit.Background, Padding = new Padding(10) };
        return page;
    }

    private TabPage BuildSuppliersTab()
    {
        var page = NewTab("👤 الموردون");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(4) };
        ConfigureText(supplierName, "اسم المورد *", 210); ConfigureText(supplierPhone, "رقم الهاتف", 150); ConfigureText(supplierAddress, "العنوان", 190);
        bar.Controls.Add(supplierName); bar.Controls.Add(supplierPhone); bar.Controls.Add(supplierAddress);
        var add = UiKit.Button("➕ إضافة مورد", UiKit.Success, 135, 40); add.Click += (_, _) => AddSupplier(); bar.Controls.Add(add);
        var pay = UiKit.Button("💵 سداد حساب المورد المحدد", Color.FromArgb(0, 121, 107), 200, 40); pay.Click += (_, _) => PaySelectedSupplier(); bar.Controls.Add(pay);
        layout.Controls.Add(bar, 0, 0);
        StyleBusinessGrid(supplierGrid);
        supplierGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        supplierGrid.Columns.Add(UiKit.GridText("Name", "اسم المورد", 280));
        supplierGrid.Columns.Add(UiKit.GridText("Phone", "الهاتف", 180));
        supplierGrid.Columns.Add(UiKit.GridText("Balance", "المستحق للمورد", 180));
        layout.Controls.Add(supplierGrid, 0, 1);
        StyleBusinessGrid(supplierPurchaseGrid);
        supplierPurchaseGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Date", "تاريخ الفاتورة", 170));
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Invoice", "رقم فاتورة المورد", 170));
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Total", "الإجمالي", 130));
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Paid", "المدفوع", 130));
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Remaining", "المتبقي", 130));
        supplierGrid.SelectionChanged += (_, _) => LoadSupplierPurchaseHistory();
        layout.Controls.Add(supplierPurchaseGrid, 0, 2);
        var note = MakeHint("رصيد المورد محسوب تلقائيًا من فواتير الشراء غير المسددة. السداد يوزّع المبلغ على أقدم الفواتير أولًا.");
        layout.Controls.Add(note, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPurchasesTab()
    {
        var page = NewTab("📦 فواتير الشراء");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        var invoiceBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(3) };
        purchaseSupplier.Width = 250; purchaseSupplier.DropDownStyle = ComboBoxStyle.DropDownList; ComboBoxStyler.ApplyModernStyle(purchaseSupplier);
        purchaseNumber.Width = 145; purchaseNumber.PlaceholderText = "رقم فاتورة المورد";
        invoiceBar.Controls.Add(LabeledControl("المورد", purchaseSupplier, 290));
        invoiceBar.Controls.Add(LabeledControl("رقم الفاتورة", purchaseNumber, 180));
        purchaseNotes.Width = 260; purchaseNotes.PlaceholderText = "ملاحظات اختيارية";
        invoiceBar.Controls.Add(LabeledControl("ملاحظات", purchaseNotes, 285));
        root.Controls.Add(invoiceBar, 0, 0);

        var lineBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(3) };
        purchaseProductSearch.Width = 190; purchaseProductSearch.PlaceholderText = "بحث عن صنف";
        var search = UiKit.Button("🔎 بحث", UiKit.Primary, 85, 38); search.Click += (_, _) => LoadPurchaseProducts(purchaseProductSearch.Text);
        purchaseProduct.Width = 240; purchaseProduct.DropDownStyle = ComboBoxStyle.DropDownList; ComboBoxStyler.ApplyModernStyle(purchaseProduct);
        purchaseQty.Minimum = 1; purchaseQty.Maximum = 100000; purchaseQty.Value = 1; purchaseQty.DecimalPlaces = 0; purchaseQty.Width = 85;
        purchaseCost.Minimum = 0; purchaseCost.Maximum = 100000000; purchaseCost.DecimalPlaces = 2; purchaseCost.Width = 115;
        purchaseProduct.SelectedIndexChanged += (_, _) => { if (purchaseProduct.SelectedItem is ProductLookupRow p) purchaseCost.Value = Math.Min(purchaseCost.Maximum, p.PurchasePrice); };
        var addLine = UiKit.Button("＋ إضافة الصنف", UiKit.Success, 130, 40); addLine.Click += (_, _) => AddPurchaseLine();
        lineBar.Controls.Add(LabeledControl("بحث", purchaseProductSearch, 195)); lineBar.Controls.Add(search);
        lineBar.Controls.Add(LabeledControl("الصنف", purchaseProduct, 250)); lineBar.Controls.Add(LabeledControl("الكمية", purchaseQty, 90)); lineBar.Controls.Add(LabeledControl("تكلفة الوحدة", purchaseCost, 120)); lineBar.Controls.Add(addLine);
        root.Controls.Add(lineBar, 0, 1);

        StyleBusinessGrid(purchaseGrid);
        purchaseGrid.Columns.Add(UiKit.GridText("Product", "الصنف", 360));
        purchaseGrid.Columns.Add(UiKit.GridText("Qty", "الكمية", 100));
        purchaseGrid.Columns.Add(UiKit.GridText("Cost", "تكلفة الوحدة", 140));
        purchaseGrid.Columns.Add(UiKit.GridText("Total", "الإجمالي", 150));
        root.Controls.Add(purchaseGrid, 0, 2);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(3) };
        purchasePaid.Minimum = 0; purchasePaid.Maximum = 100000000; purchasePaid.DecimalPlaces = 2; purchasePaid.Width = 150;
        footer.Controls.Add(LabeledControl("المدفوع الآن", purchasePaid, 165));
        var total = UiKit.Button("حساب الإجمالي", Color.FromArgb(58, 84, 112), 120, 40); total.Click += (_, _) => ShowPurchaseTotal(); footer.Controls.Add(total);
        var del = UiKit.Button("حذف السطر المحدد", UiKit.Danger, 145, 40); del.Click += (_, _) => RemovePurchaseLine(); footer.Controls.Add(del);
        var save = UiKit.Button("💾 حفظ فاتورة الشراء وتحديث المخزن", Color.FromArgb(0, 121, 107), 260, 42); save.Click += (_, _) => SavePurchase(); footer.Controls.Add(save);
        var clear = UiKit.Button("تفريغ", Color.FromArgb(108, 117, 125), 90, 40); clear.Click += (_, _) => ClearPurchase(); footer.Controls.Add(clear);
        root.Controls.Add(footer, 0, 3);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildReturnsTab()
    {
        var page = NewTab("↩️ المرتجعات");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        returnSearch.Width = 270; returnSearch.PlaceholderText = "بحث باسم الصنف";
        var find = UiKit.Button("🔎 تحديث النتائج", UiKit.Primary, 140, 40); find.Click += (_, _) => LoadReturnRows();
        bar.Controls.Add(returnSearch); bar.Controls.Add(find); bar.Controls.Add(MakeHint("تظهر المبيعات النقدية أو المبيعات التي أصبح رصيد حساب عميلها صفرًا؛ لا يمكن رد بيع آجل مع وجود دين قائم على الحساب.")); root.Controls.Add(bar, 0, 0);
        StyleBusinessGrid(returnGrid);
        returnGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        returnGrid.Columns.Add(UiKit.GridText("Date", "تاريخ البيع", 150));
        returnGrid.Columns.Add(UiKit.GridText("Product", "الصنف", 320));
        returnGrid.Columns.Add(UiKit.GridText("Sold", "المباع", 90));
        returnGrid.Columns.Add(UiKit.GridText("Returned", "المرتجع", 95));
        returnGrid.Columns.Add(UiKit.GridText("Available", "المتاح للإرجاع", 120));
        returnGrid.Columns.Add(UiKit.GridText("Amount", "قيمة الفاتورة", 135));
        returnGrid.Columns.Add(UiKit.GridText("Tx", "رقم العملية", 100));
        returnGrid.SelectionChanged += (_, _) => UpdateReturnQuantityLimit();
        root.Controls.Add(returnGrid, 0, 1);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(3) };
        returnQty.Minimum = 1; returnQty.Maximum = 100000; returnQty.Value = 1; returnQty.DecimalPlaces = 0; returnQty.Width = 90;
        returnReason.Width = 260; returnReason.PlaceholderText = "سبب المرتجع (إجباري)";
        var save = UiKit.Button("تأكيد المرتجع ورد المبلغ", UiKit.Danger, 210, 42); save.Click += (_, _) => ProcessReturn();
        footer.Controls.Add(LabeledControl("الكمية المرتجعة", returnQty, 115)); footer.Controls.Add(LabeledControl("السبب", returnReason, 270)); footer.Controls.Add(save);
        root.Controls.Add(footer, 0, 2); page.Controls.Add(root); return page;
    }

    private TabPage BuildProfitTab()
    {
        var page = NewTab("📈 الأرباح");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 218));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        profitFrom.Format = DateTimePickerFormat.Custom; profitFrom.CustomFormat = "yyyy-MM-dd"; profitFrom.Value = DateTime.Today;
        profitTo.Format = DateTimePickerFormat.Custom; profitTo.CustomFormat = "yyyy-MM-dd"; profitTo.Value = DateTime.Today;
        var show = UiKit.Button("📊 حساب النتائج", UiKit.Primary, 150, 42); show.Click += (_, _) => ShowProfit();
        bar.Controls.Add(LabeledControl("من", profitFrom, 165)); bar.Controls.Add(LabeledControl("إلى", profitTo, 165)); bar.Controls.Add(show);
        root.Controls.Add(bar, 0, 0);
        profitSummary.Dock = DockStyle.Fill; profitSummary.BackColor = Color.White; profitSummary.ForeColor = UiKit.Text;
        profitSummary.Padding = new Padding(18); profitSummary.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        profitSummary.TextAlign = ContentAlignment.MiddleRight;
        profitSummary.Text = "اختر الفترة واضغط حساب النتائج.\n\nسيتم احتساب هامش مبيعات المنتجات بتكلفة الشراء المحفوظة وقت البيع، وعمولات الخدمات، والمصروفات التشغيلية المسجلة.";
        root.Controls.Add(profitSummary, 0, 1);

        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 0, 0) };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56)); detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        var productsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
        productsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); productsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        productsPanel.Controls.Add(MakeHint("ربحية الأصناف — بعد خصم المرتجعات"), 0, 0);
        StyleBusinessGrid(profitProductGrid);
        profitProductGrid.Columns.Add(UiKit.GridText("Product", "الصنف", 230));
        profitProductGrid.Columns.Add(UiKit.GridText("Revenue", "المبيعات", 100));
        profitProductGrid.Columns.Add(UiKit.GridText("Cost", "التكلفة", 100));
        profitProductGrid.Columns.Add(UiKit.GridText("Returns", "المرتجعات", 100));
        profitProductGrid.Columns.Add(UiKit.GridText("Profit", "الربح", 100));
        productsPanel.Controls.Add(profitProductGrid, 0, 1);
        var employeePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
        employeePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); employeePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        employeePanel.Controls.Add(MakeHint("مبيعات الأصناف حسب الموظف — الربح للعمليات ذات التكلفة المعروفة"), 0, 0);
        StyleBusinessGrid(profitEmployeeGrid);
        profitEmployeeGrid.Columns.Add(UiKit.GridText("User", "الموظف", 130));
        profitEmployeeGrid.Columns.Add(UiKit.GridText("Sales", "المبيعات", 100));
        profitEmployeeGrid.Columns.Add(UiKit.GridText("Profit", "هامش معروف", 110));
        profitEmployeeGrid.Columns.Add(UiKit.GridText("Count", "العمليات", 80));
        profitEmployeeGrid.Columns.Add(UiKit.GridText("Unknown", "بلا تكلفة", 85));
        employeePanel.Controls.Add(profitEmployeeGrid, 0, 1);
        detail.Controls.Add(productsPanel, 0, 0); detail.Controls.Add(employeePanel, 1, 0);
        root.Controls.Add(detail, 0, 2);
        page.Controls.Add(root); return page;
    }

    private TabPage BuildSearchTab()
    {
        var page = NewTab("🔎 البحث الشامل");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        globalSearch.Width = 430; globalSearch.PlaceholderText = "ابحث عن عميل أو صنف أو مورد أو عملية أو فاتورة...";
        var search = UiKit.Button("بحث شامل", UiKit.Primary, 130, 40); search.Click += (_, _) => RunGlobalSearch();
        globalSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunGlobalSearch(); } };
        bar.Controls.Add(globalSearch); bar.Controls.Add(search); root.Controls.Add(bar, 0, 0);
        StyleBusinessGrid(globalGrid);
        globalGrid.Columns.Add(UiKit.GridText("Kind", "النوع", 130)); globalGrid.Columns.Add(UiKit.GridText("Title", "الاسم / البيان", 320));
        globalGrid.Columns.Add(UiKit.GridText("Info", "تفاصيل", 240)); globalGrid.Columns.Add(UiKit.GridText("Value", "قيمة / كمية", 150));
        globalGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "رقم", Width = 90 });
        root.Controls.Add(globalGrid, 0, 1); page.Controls.Add(root); return page;
    }

    private TabPage BuildInvoicesTab()
    {
        var page = NewTab("🧾 أرشيف الفواتير");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        invoiceSearch.Width = 350; invoiceSearch.PlaceholderText = "رقم الفاتورة أو اسم العميل";
        var find = UiKit.Button("بحث", UiKit.Primary, 100, 40); find.Click += (_, _) => LoadInvoiceRows();
        var open = UiKit.Button("فتح ومعاينة الفاتورة", UiKit.Success, 180, 40); open.Click += (_, _) => OpenSelectedInvoice();
        invoiceSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadInvoiceRows(); } };
        bar.Controls.Add(invoiceSearch); bar.Controls.Add(find); bar.Controls.Add(open); root.Controls.Add(bar, 0, 0);
        StyleBusinessGrid(invoiceGrid);
        invoiceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        invoiceGrid.Columns.Add(UiKit.GridText("Number", "رقم الفاتورة", 130));
        invoiceGrid.Columns.Add(UiKit.GridText("Customer", "العميل", 280));
        invoiceGrid.Columns.Add(UiKit.GridText("Total", "الإجمالي", 150));
        invoiceGrid.Columns.Add(UiKit.GridText("Date", "التاريخ", 210));
        invoiceGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelectedInvoice(); };
        root.Controls.Add(invoiceGrid, 0, 1); page.Controls.Add(root); return page;
    }

    private TabPage BuildInventoryIoTab()
    {
        var page = NewTab("📤 استيراد / تصدير المخزن");
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 4, Padding = new Padding(26) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        var title = new Label { Text = "نقل بيانات الأصناف من وإلى Excel باستخدام CSV", Font = new Font("Segoe UI", 16F, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        panel.Controls.Add(title, 0, 0);
        var export = UiKit.Button("📤 تصدير جميع الأصناف إلى CSV", UiKit.Success, 270, 46); export.Click += (_, _) => ExportInventoryCsv();
        var import = UiKit.Button("📥 استيراد / تحديث الأصناف من CSV", UiKit.Primary, 290, 46); import.Click += (_, _) => ImportInventoryCsv();
        var template = UiKit.Button("📄 إنشاء ملف قالب فارغ", Color.FromArgb(58, 84, 112), 210, 46); template.Click += (_, _) => ExportInventoryTemplate();
        panel.Controls.Add(Center(export), 0, 1); panel.Controls.Add(Center(import), 0, 2); panel.Controls.Add(Center(template), 0, 3);
        var hint = MakeHint("الأعمدة: Barcode, Name, Description, Category, PurchasePrice, SalePrice, Quantity, MinQuantity. الاستيراد يحدّث الصنف المطابق بالباركود أو يضيف صنفًا جديدًا، ويسجل فرق الكمية في سجل حركة المخزون.");
        hint.Dock = DockStyle.Bottom; hint.Height = 90;
        page.Controls.Add(panel); page.Controls.Add(hint); return page;
    }

    private static Control Center(Control control)
    {
        var p = new Panel { Dock = DockStyle.Fill };
        control.Dock = DockStyle.None; control.Anchor = AnchorStyles.None;
        p.Controls.Add(control);
        p.Resize += (_, _) => control.Location = new Point((p.Width - control.Width) / 2, (p.Height - control.Height) / 2);
        return p;
    }

    private TabPage BuildStockHistoryTab()
    {
        var page = NewTab("📚 حركة المخزون");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        stockHistorySearch.Width = 330; stockHistorySearch.PlaceholderText = "ابحث باسم الصنف أو الباركود";
        var find = UiKit.Button("عرض سجل الحركة", UiKit.Primary, 145, 40); find.Click += (_, _) => LoadStockHistory();
        stockHistorySearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadStockHistory(); } };
        bar.Controls.Add(stockHistorySearch); bar.Controls.Add(find); root.Controls.Add(bar, 0, 0);
        StyleBusinessGrid(stockHistoryGrid);
        stockHistoryGrid.Columns.Add(UiKit.GridText("Date", "التاريخ", 170));
        stockHistoryGrid.Columns.Add(UiKit.GridText("Product", "الصنف", 240));
        stockHistoryGrid.Columns.Add(UiKit.GridText("Type", "نوع الحركة", 150));
        stockHistoryGrid.Columns.Add(UiKit.GridText("Delta", "فرق الكمية", 100));
        stockHistoryGrid.Columns.Add(UiKit.GridText("Cost", "تكلفة الوحدة", 120));
        stockHistoryGrid.Columns.Add(UiKit.GridText("Reference", "المرجع", 130));
        stockHistoryGrid.Columns.Add(UiKit.GridText("User", "بواسطة", 100));
        stockHistoryGrid.Columns.Add(UiKit.GridText("Note", "ملاحظات", 300, true));
        root.Controls.Add(stockHistoryGrid, 0, 1); page.Controls.Add(root); return page;
    }

    private static void ConfigureText(TextBox box, string placeholder, int width)
    {
        box.Width = width; box.PlaceholderText = placeholder; box.TextAlign = HorizontalAlignment.Right; box.Font = new Font("Segoe UI", 10.5F);
    }

    private static Control LabeledControl(string label, Control control, int width)
    {
        var panel = new Panel { Width = width, Height = 62, Margin = new Padding(4), BackColor = Color.Transparent };
        var caption = MakeHint(label); caption.Dock = DockStyle.Top; caption.Height = 22; caption.TextAlign = ContentAlignment.MiddleCenter;
        control.Dock = DockStyle.Bottom; control.Height = 34;
        panel.Controls.Add(control); panel.Controls.Add(caption); return panel;
    }

    private static Label MakeHint(string text) => new() { Text = text, AutoSize = false, ForeColor = Color.FromArgb(90, 100, 110), Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

    private static void StyleBusinessGrid(DataGridView grid)
    {
        UiKit.StyleGrid(grid);
        grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        // GridText ينشئ أعمدة بعرض ابتدائي ثابت؛ حوّلها إلى Fill مع الاحتفاظ
        // بنسب عروضها الأصلية حتى تتمدد الجداول بعرض الصفحة بدل أن تتكدس في نصفها.
        void FillColumn(DataGridViewColumn column)
        {
            if (!column.Visible) return;
            if (column.AutoSizeMode == DataGridViewAutoSizeColumnMode.None)
            {
                column.FillWeight = Math.Max(50, column.Width);
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
        }
        grid.ColumnAdded += (_, e) => FillColumn(e.Column);
        foreach (DataGridViewColumn column in grid.Columns) FillColumn(column);
        grid.ColumnHeadersHeight = 46;
        grid.RowTemplate.Height = 42;
        grid.ScrollBars = ScrollBars.Both;
    }

    private void LoadSuppliers()
    {
        try
        {
            var suppliers = BusinessManagementStore.LoadSuppliers();
            supplierGrid.Rows.Clear();
            foreach (var s in suppliers) supplierGrid.Rows.Add(s.Id, s.Name, s.Phone, s.Balance.ToString("N2") + " ج.م");
            long? selected = purchaseSupplier.SelectedItem is SupplierLookupRow old ? old.Id : null;
            purchaseSupplier.Items.Clear();
            foreach (var s in suppliers) purchaseSupplier.Items.Add(s);
            if (selected.HasValue)
                for (int i = 0; i < purchaseSupplier.Items.Count; i++)
                    if (purchaseSupplier.Items[i] is SupplierLookupRow row && row.Id == selected.Value)
                    {
                        purchaseSupplier.SelectedIndex = i;
                        break;
                    }
            if (purchaseSupplier.SelectedIndex < 0 && purchaseSupplier.Items.Count > 0) purchaseSupplier.SelectedIndex = 0;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "تحميل الموردين", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadSupplierPurchaseHistory()
    {
        if (supplierPurchaseGrid.Columns.Count == 0) return;
        supplierPurchaseGrid.Rows.Clear();
        if (supplierGrid.CurrentRow == null) return;
        try
        {
            long supplierId = Convert.ToInt64(supplierGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
            using var c = AppDatabase.OpenConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT Id,CreatedAt,COALESCE(InvoiceNumber,''),TotalAmount,PaidAmount,RemainingAmount
FROM Purchases WHERE SupplierId=$s ORDER BY CreatedAt DESC,Id DESC LIMIT 300";
            cmd.Parameters.AddWithValue("$s", supplierId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                DateTime date = DateTime.TryParse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
                supplierPurchaseGrid.Rows.Add(r.GetInt64(0), date.ToString("yyyy-MM-dd HH:mm"), r.GetString(2),
                    Dec(r.GetValue(3)).ToString("N2"), Dec(r.GetValue(4)).ToString("N2"), Dec(r.GetValue(5)).ToString("N2"));
            }
        }
        catch (Exception ex) { AppLog.Error("تعذر تحميل فواتير المورد", ex); }
    }

    private void AddSupplier()
    {
        try
        {
            BusinessManagementStore.AddSupplier(currentUser, supplierName.Text, supplierPhone.Text, supplierAddress.Text, null);
            supplierName.Clear(); supplierPhone.Clear(); supplierAddress.Clear(); LoadSuppliers();
            MessageBox.Show(this, "تمت إضافة المورد.", "الموردون", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "إضافة مورد", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void PaySelectedSupplier()
    {
        if (supplierGrid.CurrentRow == null) { MessageBox.Show(this, "اختر موردًا أولًا.", "سداد مورد", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        long id = Convert.ToInt64(supplierGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
        string name = supplierGrid.CurrentRow.Cells["Name"].Value?.ToString() ?? "المورد";
        decimal balance = decimal.TryParse((supplierGrid.CurrentRow.Cells["Balance"].Value?.ToString() ?? "").Replace(" ج.م", ""), NumberStyles.Any, CultureInfo.CurrentCulture, out var b) ? b : 0m;
        if (balance <= 0) { MessageBox.Show(this, "لا يوجد رصيد مستحق لهذا المورد.", "سداد مورد", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dialog = new AmountNoteDialog($"سداد حساب {name}", balance);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { BusinessManagementStore.PaySupplier(currentUser, id, dialog.Amount, dialog.Note); LoadSuppliers(); MessageBox.Show(this, "تم تسجيل السداد وتحديث رصيد المورد.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "سداد مورد", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadPurchaseProducts(string query)
    {
        try
        {
            var products = BusinessManagementStore.LoadProducts(query);
            purchaseProduct.Items.Clear();
            foreach (var p in products) purchaseProduct.Items.Add(p);
            if (purchaseProduct.Items.Count > 0) purchaseProduct.SelectedIndex = 0;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "الأصناف", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void AddPurchaseLine()
    {
        if (purchaseProduct.SelectedItem is not ProductLookupRow p) { MessageBox.Show(this, "اختر صنفًا من قائمة البحث.", "فاتورة شراء", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        int qty = Convert.ToInt32(purchaseQty.Value, CultureInfo.InvariantCulture);
        var existing = purchaseLines.FirstOrDefault(x => x.ProductId == p.Id && x.UnitCost == purchaseCost.Value);
        if (existing != null) existing.Quantity += qty;
        else purchaseLines.Add(new PurchaseDraftLine { ProductId = p.Id, Name = p.Name, Quantity = qty, UnitCost = purchaseCost.Value });
        RefreshPurchaseGrid();
    }

    private void RefreshPurchaseGrid()
    {
        purchaseGrid.Rows.Clear();
        foreach (var l in purchaseLines) purchaseGrid.Rows.Add(l.Name, l.Quantity, l.UnitCost.ToString("N2"), (l.Quantity * l.UnitCost).ToString("N2"));
        decimal total = purchaseLines.Sum(x => x.Quantity * x.UnitCost);
        purchaseGrid.Tag = total;
    }

    private void ShowPurchaseTotal()
    {
        decimal total = purchaseLines.Sum(x => x.Quantity * x.UnitCost);
        MessageBox.Show(this, $"إجمالي فاتورة الشراء: {total:N2} ج.م\nالمتبقي على المورد: {Math.Max(0, total - purchasePaid.Value):N2} ج.م", "حساب الفاتورة", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RemovePurchaseLine()
    {
        if (purchaseGrid.CurrentRow == null || purchaseGrid.CurrentRow.Index < 0 || purchaseGrid.CurrentRow.Index >= purchaseLines.Count) return;
        purchaseLines.RemoveAt(purchaseGrid.CurrentRow.Index); RefreshPurchaseGrid();
    }

    private void SavePurchase()
    {
        if (purchaseSupplier.SelectedItem is not SupplierLookupRow supplier) { MessageBox.Show(this, "أضف موردًا واختره أولًا.", "فاتورة شراء", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            decimal total = purchaseLines.Sum(x => x.Quantity * x.UnitCost);
            if (purchasePaid.Value > total) throw new InvalidOperationException("المدفوع أكبر من إجمالي فاتورة الشراء.");
            long id = BusinessManagementStore.CreatePurchase(currentUser, supplier.Id, purchaseNumber.Text, purchaseNotes.Text, purchasePaid.Value,
                purchaseLines.Select(x => new PurchaseLineInput { ProductId = x.ProductId, Quantity = x.Quantity, UnitCost = x.UnitCost }).ToList());
            MessageBox.Show(this, $"تم حفظ فاتورة الشراء رقم {id}.\nتم تحديث كميات المخزن ومتوسط تكلفة الشراء تلقائيًا.", "تم حفظ المشتريات", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ClearPurchase(); LoadSuppliers(); LoadPurchaseProducts(purchaseProductSearch.Text);
        }
        catch (Exception ex) { AppLog.Error("تعذر حفظ فاتورة شراء", ex); MessageBox.Show(this, ex.Message, "فاتورة شراء", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ClearPurchase()
    {
        purchaseLines.Clear(); purchaseNumber.Clear(); purchaseNotes.Clear(); purchasePaid.Value = 0; RefreshPurchaseGrid();
    }

    private void LoadReturnRows()
    {
        try
        {
            returnGrid.Rows.Clear();
            foreach (var sale in BusinessManagementStore.LoadReturnableSales(returnSearch.Text))
            {
                int row = returnGrid.Rows.Add(sale.TransactionId, sale.CreatedAt.ToString("yyyy-MM-dd HH:mm"), sale.ProductName,
                    sale.SoldQuantity, sale.ReturnedQuantity, sale.AvailableQuantity, sale.NetAmount.ToString("N2"), sale.TransactionId);
                returnGrid.Rows[row].Tag = sale;
            }
            if (returnGrid.Rows.Count > 0) { returnGrid.ClearSelection(); returnGrid.Rows[0].Selected = true; UpdateReturnQuantityLimit(); }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "تحميل المرتجعات", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void UpdateReturnQuantityLimit()
    {
        if (returnGrid.CurrentRow?.Tag is not ReturnableSaleRow row) return;
        decimal limit = Math.Max(1, row.AvailableQuantity);
        if (returnQty.Value > limit) returnQty.Value = limit;
        returnQty.Maximum = limit;
    }

    private void ProcessReturn()
    {
        if (returnGrid.CurrentRow?.Tag is not ReturnableSaleRow sale) { MessageBox.Show(this, "اختر عملية بيع من الجدول.", "مرتجع", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        int qty = Convert.ToInt32(returnQty.Value, CultureInfo.InvariantCulture);
        if (qty > sale.AvailableQuantity) { MessageBox.Show(this, $"المتاح للإرجاع {sale.AvailableQuantity} فقط.", "الكمية", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            var answer = MessageBox.Show(this, $"تأكيد إرجاع {qty} من ({sale.ProductName})؟\nسيتم رد المبلغ وإضافة الكمية للمخزن.", "تأكيد المرتجع", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            var result = BusinessManagementStore.RecordSaleReturn(currentUser, sale.TransactionId, qty, returnReason.Text);
            returnReason.Clear(); LoadReturnRows();
            MessageBox.Show(this, $"تم تسجيل المرتجع رقم {result.ReturnId}.\nالمبلغ المردود: {result.Refund:N2} ج.م", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { AppLog.Error("تعذر تسجيل مرتجع", ex); MessageBox.Show(this, ex.Message, "مرتجع", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ShowProfit()
    {
        if (profitTo.Value.Date < profitFrom.Value.Date) { MessageBox.Show(this, "تاريخ النهاية يجب ألا يسبق تاريخ البداية.", "الفترة", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            lastProfit = BusinessManagementStore.CalculateProfit(profitFrom.Value.Date, profitTo.Value.Date);
            var p = lastProfit;
            profitSummary.Text =
                $"تقرير الفترة: {p.From:yyyy-MM-dd} إلى {p.To:yyyy-MM-dd}\n\n" +
                $"مبيعات بتكلفة تاريخية معروفة: {p.KnownSalesRevenue:N2} ج.م\n" +
                $"تكلفة البضاعة المباعة: {p.KnownCostOfGoods:N2} ج.م\n" +
                $"هامش ربح المبيعات: {p.SalesGrossProfit:N2} ج.م\n" +
                $"مرتجعات المبيعات: {p.ReturnedSales:N2} ج.م  |  تكلفة المرتجع: {p.ReturnCost:N2} ج.م\n" +
                $"عمولات خدمات الكاش: {p.CashServiceCommissions:N2} ج.م\n" +
                $"عمولات الخدمات الأخرى: {p.OtherServiceCommissions:N2} ج.م\n" +
                $"المصروفات التشغيلية المسجلة: {p.OperatingExpenses:N2} ج.م\n\n" +
                $"صافي الربح المعروف: {p.KnownNetProfit:N2} ج.م\n" +
                $"إيرادات الطباعة المسجلة (تكلفتها غير محسوبة): {p.PrintingRevenue:N2} ج.م\n\n" +
                $"مبيعات قديمة بلا تكلفة شراء محفوظة: {p.UncostedLegacySales:N2} ج.م ({p.UncostedLegacyCount} عملية).\n" +
                $"مرتجعات مرتبطة بمبيعات بلا تكلفة تاريخية: {p.UncostedReturns:N2} ج.م (معروضة منفصلة ولا تخصم من صافي الربح المعروف).\n" +
                "تنبيه: صافي الربح هنا تقدير للجزء الذي تتوفر له تكلفة تاريخية؛ لا يشمل تكلفة مستهلكات الطباعة أو أي مصروف لم يتم تسجيله.";
            profitProductGrid.Rows.Clear();
            foreach (var item in p.ProductBreakdown)
                profitProductGrid.Rows.Add(item.ProductName, item.Revenue.ToString("N2"), item.Cost.ToString("N2"), item.Refunds.ToString("N2"), item.NetProfit.ToString("N2"));
            profitEmployeeGrid.Rows.Clear();
            foreach (var item in p.EmployeeBreakdown)
                profitEmployeeGrid.Rows.Add(item.UserName, item.SalesRevenue.ToString("N2"), item.KnownGrossProfit.ToString("N2"), item.SaleCount, item.UncostedCount);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "تقرير الأرباح", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RunGlobalSearch()
    {
        try
        {
            globalGrid.Rows.Clear();
            foreach (var row in BusinessManagementStore.GlobalSearch(globalSearch.Text)) globalGrid.Rows.Add(row);
            if (globalGrid.Rows.Count == 0) MessageBox.Show(this, "لا توجد نتائج مطابقة. أدخل حرفين على الأقل.", "البحث الشامل", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "البحث الشامل", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadInvoiceRows()
    {
        try
        {
            invoiceGrid.Rows.Clear();
            foreach (var row in InvoiceStore.LoadSavedInvoices(invoiceSearch.Text))
                invoiceGrid.Rows.Add(row.Id, row.Number.ToString("D4", CultureInfo.InvariantCulture), row.Customer,
                    row.Total.ToString("N2") + " ج.م", row.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "أرشيف الفواتير", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OpenSelectedInvoice()
    {
        if (invoiceGrid.CurrentRow == null) { MessageBox.Show(this, "اختر فاتورة من الأرشيف.", "الفواتير", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        long id = Convert.ToInt64(invoiceGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
        try
        {
            if (!InvoiceStore.TryLoadSavedInvoice(id, out var data, out var operations))
                throw new InvalidOperationException("تعذر إعادة تكوين الفاتورة؛ قد تكون بعض العمليات الأصلية قد حُذفت.");
            using var form = new InvoiceForm(currentUser, data, operations, id);
            form.ShowDialog(this);
            LoadInvoiceRows();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "فتح الفاتورة", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadStockHistory()
    {
        try
        {
            stockHistoryGrid.Rows.Clear();
            string q = stockHistorySearch.Text.Trim();
            using var c = AppDatabase.OpenConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT im.CreatedAt,p.Name,im.MovementType,im.QuantityDelta,im.UnitCost,
COALESCE(im.ReferenceType,'') || CASE WHEN im.ReferenceId IS NOT NULL THEN ' #' || im.ReferenceId ELSE '' END,
u.UserName,COALESCE(im.Note,'')
FROM InventoryMovements im JOIN Products p ON p.Id=im.ProductId JOIN AppUsers u ON u.Id=im.UserId
WHERE $q='' OR p.Name LIKE $p OR COALESCE(p.Barcode,'') LIKE $p
ORDER BY im.CreatedAt DESC,im.Id DESC LIMIT 1500";
            cmd.Parameters.AddWithValue("$q", q); cmd.Parameters.AddWithValue("$p", "%" + q + "%");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                DateTime date = DateTime.TryParse(r.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
                string type = r.GetString(2) switch
                {
                    "PURCHASE_IN" => "شراء / إضافة",
                    "SALE_OUT" => "بيع / خصم",
                    "SALE_RETURN" => "مرتجع بيع",
                    "MANUAL_ADJUSTMENT" => "تعديل يدوي",
                    "CSV_ADJUSTMENT" => "استيراد CSV",
                    _ => r.GetString(2)
                };
                stockHistoryGrid.Rows.Add(date.ToString("yyyy-MM-dd HH:mm"), r.GetString(1), type,
                    Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture), r.IsDBNull(4) ? "—" : Dec(r.GetValue(4)).ToString("N2"),
                    r.GetString(5), r.GetString(6), r.GetString(7));
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "حركة المخزون", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExportInventoryTemplate() => SaveInventoryCsv(true);
    private void ExportInventoryCsv() => SaveInventoryCsv(false);

    private void SaveInventoryCsv(bool template)
    {
        using var dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = template ? "قالب_المخزن.csv" : $"المخزن_{DateTime.Now:yyyy-MM-dd}.csv", AddExtension = true, DefaultExt = "csv", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Barcode,Name,Description,Category,PurchasePrice,SalePrice,Quantity,MinQuantity");
            if (!template)
            {
                using var c = AppDatabase.OpenConnection(); using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT COALESCE(Barcode,''),Name,COALESCE(Description,''),COALESCE(Category,''),PurchasePrice,SalePrice,Quantity,MinQuantity FROM Products ORDER BY Name";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var fields = new[] { r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), Dec(r.GetValue(4)).ToString(CultureInfo.InvariantCulture), Dec(r.GetValue(5)).ToString(CultureInfo.InvariantCulture), r.GetValue(6).ToString() ?? "0", r.GetValue(7).ToString() ?? "3" };
                    sb.AppendLine(string.Join(",", fields.Select(CsvEscape)));
                }
            }
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            MessageBox.Show(this, "تم حفظ ملف CSV بنجاح.", "المخزن", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "تصدير المخزن", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ImportInventoryCsv()
    {
        using var dialog = new OpenFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv|كل الملفات (*.*)|*.*", Title = "اختيار ملف أصناف CSV" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var csvRows = ParseCsvRows(File.ReadAllText(dialog.FileName, Encoding.UTF8));
            if (csvRows.Count < 2) throw new InvalidOperationException("الملف فارغ أو لا يحتوي على صفوف أصناف.");
            var header = csvRows[0].Select(x => x.Trim()).ToList();
            if (header.Count > 0) header[0] = header[0].TrimStart('\uFEFF');
            string[] requiredHeaders = { "Barcode", "Name", "Description", "Category", "PurchasePrice", "SalePrice", "Quantity", "MinQuantity" };
            foreach (string h in requiredHeaders) if (!header.Contains(h, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("أعمدة الملف غير مطابقة. استخدم قالب المخزن الذي يوفره البرنامج.");
            int Index(string name) => header.FindIndex(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            int added = 0, updated = 0;
            var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var c = AppDatabase.OpenConnection(); using var tx = c.BeginTransaction();
            long userId = AppDatabase.GetUserId(c, currentUser, tx);
            for (int i = 1; i < csvRows.Count; i++)
            {
                var values = csvRows[i];
                if (values.All(string.IsNullOrWhiteSpace)) continue;
                if (values.Count != header.Count)
                    throw new InvalidOperationException($"عدد الأعمدة لا يطابق صف العناوين في سجل CSV رقم {i + 1}.");
                string Val(string name) { int ix = Index(name); return ix >= 0 && ix < values.Count ? values[ix].Trim() : ""; }
                string name = Val("Name"); if (name.Length == 0) throw new InvalidOperationException($"اسم الصنف فارغ في السطر {i + 1}.");
                string barcode = Val("Barcode");
                if (barcode.Length > 0 && !seenBarcodes.Add(barcode))
                    throw new InvalidOperationException($"الباركود {barcode} مكرر داخل ملف الاستيراد (السطر {i + 1}).");
                decimal purchase = ParseDec(Val("PurchasePrice"), "PurchasePrice", i + 1);
                decimal sale = ParseDec(Val("SalePrice"), "SalePrice", i + 1);
                int qty = ParseInt(Val("Quantity"), "Quantity", i + 1);
                int min = string.IsNullOrWhiteSpace(Val("MinQuantity")) ? 3 : ParseInt(Val("MinQuantity"), "MinQuantity", i + 1);
                if (purchase < 0 || sale < 0 || qty < 0 || min < 0)
                    throw new InvalidOperationException($"لا يُسمح بقيم سالبة في السطر {i + 1}.");
                long? existingId = null; int oldQty = 0;
                if (barcode.Length > 0)
                {
                    using var find = c.CreateCommand(); find.Transaction = tx; find.CommandText = "SELECT Id,Quantity FROM Products WHERE Barcode=$b LIMIT 1"; find.Parameters.AddWithValue("$b", barcode);
                    using var r = find.ExecuteReader(); if (r.Read()) { existingId = r.GetInt64(0); oldQty = Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture); }
                }
                long id;
                if (existingId.HasValue)
                {
                    id = existingId.Value;
                    using var upd = c.CreateCommand(); upd.Transaction = tx;
                    upd.CommandText = @"UPDATE Products SET Name=$n,Description=$d,Category=$cat,PurchasePrice=$p,SalePrice=$s,Quantity=$q,MinQuantity=$m,UpdatedAt=$now,StockUpdatedAt=$now WHERE Id=$id";
                    upd.Parameters.AddWithValue("$n", name); upd.Parameters.AddWithValue("$d", DbText(Val("Description"))); upd.Parameters.AddWithValue("$cat", DbText(Val("Category")));
                    upd.Parameters.AddWithValue("$p", purchase); upd.Parameters.AddWithValue("$s", sale); upd.Parameters.AddWithValue("$q", qty); upd.Parameters.AddWithValue("$m", min);
                    upd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O", CultureInfo.InvariantCulture)); upd.Parameters.AddWithValue("$id", id); upd.ExecuteNonQuery(); updated++;
                }
                else
                {
                    using var ins = c.CreateCommand(); ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO Products(Barcode,Name,Description,Category,PurchasePrice,SalePrice,Quantity,MinQuantity,CreatedAt,UpdatedAt,StockUpdatedAt)
VALUES($b,$n,$d,$cat,$p,$s,$q,$m,$now,$now,$now); SELECT last_insert_rowid();";
                    ins.Parameters.AddWithValue("$b", DbText(barcode)); ins.Parameters.AddWithValue("$n", name); ins.Parameters.AddWithValue("$d", DbText(Val("Description"))); ins.Parameters.AddWithValue("$cat", DbText(Val("Category")));
                    ins.Parameters.AddWithValue("$p", purchase); ins.Parameters.AddWithValue("$s", sale); ins.Parameters.AddWithValue("$q", qty); ins.Parameters.AddWithValue("$m", min); ins.Parameters.AddWithValue("$now", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
                    id = Convert.ToInt64(ins.ExecuteScalar(), CultureInfo.InvariantCulture); oldQty = 0; added++;
                }
                int delta = qty - oldQty;
                if (delta != 0) BusinessManagementStore.RecordStockMovement(c, tx, currentUser, id, "CSV_ADJUSTMENT", delta, purchase, "CSVImport", null, $"تحديث رصيد الصنف من الملف: {Path.GetFileName(dialog.FileName)}");
                AppDatabase.AddAuditLog(c, tx, currentUser, "استيراد", "صنف", id, $"{name} | الكمية: {oldQty} → {qty}");
            }
            tx.Commit();
            MessageBox.Show(this, $"اكتمل الاستيراد.\nأصناف جديدة: {added}\nأصناف تم تحديثها: {updated}", "استيراد المخزن", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { AppLog.Error("فشل استيراد المخزون من CSV", ex); MessageBox.Show(this, ex.Message, "استيراد المخزن", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static List<List<string>> ParseCsvRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                row.Add(field.ToString()); field.Clear();
            }
            else if ((ch == '\r' || ch == '\n') && !quoted)
            {
                row.Add(field.ToString()); field.Clear();
                if (row.Any(x => x.Length > 0)) rows.Add(row);
                row = new List<string>();
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else field.Append(ch);
        }
        if (quoted) throw new InvalidOperationException("علامات الاقتباس في ملف CSV غير مكتملة.");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Any(x => x.Length > 0)) rows.Add(row);
        }
        return rows;
    }

    private static string CsvEscape(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    private static decimal ParseDec(string value, string field, int line)
    {
        string normalized = OperationText.NormalizeDigits(value.Trim());
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidOperationException($"القيمة في عمود {field} غير رقمية في السطر {line}.");
        return parsed;
    }
    private static int ParseInt(string value, string field, int line)
    {
        string normalized = OperationText.NormalizeDigits(value.Trim());
        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidOperationException($"القيمة في عمود {field} يجب أن تكون عددًا صحيحًا في السطر {line}.");
        return parsed;
    }
    private static decimal Dec(object value) => value == null || value == DBNull.Value ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    private static object DbText(string value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private sealed class PurchaseDraftLine
    {
        public long ProductId { get; init; }
        public string Name { get; init; } = "";
        public int Quantity { get; set; }
        public decimal UnitCost { get; init; }
    }
}

internal sealed class AmountNoteDialog : Form
{
    private readonly NumericUpDown amount = new();
    private readonly TextBox note = new();
    public decimal Amount => amount.Value;
    public string Note => note.Text;

    public AmountNoteDialog(string title, decimal maximum)
    {
        UiKit.PrepareForm(this, title, 480, 300);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        amount.Minimum = 0.01m; amount.Maximum = Math.Min(100000000m, Math.Max(0.01m, maximum)); amount.DecimalPlaces = 2; amount.Dock = DockStyle.Fill; amount.Value = amount.Maximum; amount.TextAlign = HorizontalAlignment.Center;
        note.Dock = DockStyle.Fill; note.PlaceholderText = "ملاحظة اختيارية";
        root.Controls.Add(MakeFieldLabel("المبلغ"), 0, 0); root.Controls.Add(amount, 1, 0);
        root.Controls.Add(MakeFieldLabel("ملاحظات"), 0, 1); root.Controls.Add(note, 1, 1);
        var ok = UiKit.Button("تأكيد السداد", UiKit.Success, 140, 42); ok.Dock = DockStyle.Fill; ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        root.Controls.Add(new Label(), 0, 2); root.Controls.Add(ok, 1, 2); Controls.Add(root); AcceptButton = ok;
    }
    private static Label MakeFieldLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
}
