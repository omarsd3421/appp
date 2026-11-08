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
    private readonly DataGridView profitPrintingGrid = new();
    private ProfitSnapshot? lastProfit;

    // البحث العام
    private readonly TextBox globalSearch = new();
    private readonly DataGridView globalGrid = new();

    // أرشيف الفواتير
    private readonly TextBox invoiceSearch = new();
    private readonly DataGridView invoiceGrid = new();
    private readonly TextBox purchaseArchiveSearch = new();
    private readonly DataGridView purchaseArchiveGrid = new();
    private readonly TextBox returnArchiveSearch = new();
    private readonly DataGridView returnArchiveGrid = new();
    private readonly DataGridView supplierPaymentGrid = new();

    public BusinessManagementForm(string userName)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        LoadSuppliers();
        LoadPurchaseProducts("");
        LoadReturnRows();
        LoadInvoiceRows();
        LoadPurchaseArchive();
        LoadReturnArchive();
        LoadSupplierPayments();
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
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(4) };
        ConfigureText(supplierName, "اسم المورد *", 210); ConfigureText(supplierPhone, "رقم الهاتف", 150); ConfigureText(supplierAddress, "العنوان", 190);
        bar.Controls.Add(supplierName); bar.Controls.Add(supplierPhone); bar.Controls.Add(supplierAddress);
        var add = UiKit.Button("➕ إضافة مورد", UiKit.Success, 135, 40); add.Click += (_, _) => AddSupplier(); bar.Controls.Add(add);
        var pay = UiKit.Button("💵 سداد حساب المورد المحدد", Color.FromArgb(0, 121, 107), 200, 40); pay.Click += (_, _) => PaySelectedSupplier(); bar.Controls.Add(pay);
        var details = UiKit.Button("📋 تفاصيل المورد", Color.FromArgb(75, 85, 99), 145, 40); details.Click += (_, _) => ShowSelectedSupplierDetails(); bar.Controls.Add(details);
        layout.Controls.Add(bar, 0, 0);

        StyleBusinessGrid(supplierGrid);
        supplierGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        supplierGrid.Columns.Add(UiKit.GridText("Name", "اسم المورد", 280));
        supplierGrid.Columns.Add(UiKit.GridText("Phone", "الهاتف", 180));
        supplierGrid.Columns.Add(UiKit.GridText("Balance", "المستحق للمورد", 180));
        supplierGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowSelectedSupplierDetails(); };
        layout.Controls.Add(supplierGrid, 0, 1);

        StyleBusinessGrid(supplierPurchaseGrid);
        supplierPurchaseGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Date", "تاريخ الفاتورة", 170));
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Invoice", "رقم فاتورة المورد", 170));
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Total", "الإجمالي", 130));
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Paid", "المدفوع", 130));
        supplierPurchaseGrid.Columns.Add(UiKit.GridText("Remaining", "المتبقي", 130));
        supplierPurchaseGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowPurchaseInvoiceDetailsFromGrid(supplierPurchaseGrid); };
        layout.Controls.Add(supplierPurchaseGrid, 0, 2);

        StyleBusinessGrid(supplierPaymentGrid);
        supplierPaymentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        supplierPaymentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PurchaseId", Visible = false });
        supplierPaymentGrid.Columns.Add(UiKit.GridText("Date", "تاريخ الدفعة", 165));
        supplierPaymentGrid.Columns.Add(UiKit.GridText("Invoice", "الفاتورة", 140));
        supplierPaymentGrid.Columns.Add(UiKit.GridText("Amount", "المبلغ", 120));
        supplierPaymentGrid.Columns.Add(UiKit.GridText("Note", "البيان", 220));
        supplierPaymentGrid.Columns.Add(UiKit.GridText("User", "المستخدم", 130));
        supplierPaymentGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowPurchaseInvoiceDetailsFromGrid(supplierPaymentGrid); };
        layout.Controls.Add(supplierPaymentGrid, 0, 3);

        supplierGrid.SelectionChanged += (_, _) => { LoadSupplierPurchaseHistory(); LoadSupplierPayments(); };
        layout.Controls.Add(MakeHint("دبل كليك على المورد لعرض بياناته وسجل الدفعات. تعديل الاسم أو الهاتف أو العنوان يتطلب كلمة مرور الإدارة. دبل كليك على فاتورة أو دفعة يعرض تفاصيلها."), 0, 4);
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
        var supplierReturn = UiKit.Button("↩ مرتجع للمورد", UiKit.Danger, 165, 42); supplierReturn.Click += (_, _) => ShowPurchaseReturnDialog(); footer.Controls.Add(supplierReturn);
        var supplierReturnArchive = UiKit.Button("أرشيف مرتجعات الموردين", Color.FromArgb(75,85,99), 190, 42); supplierReturnArchive.Click += (_, _) => ShowPurchaseReturnArchive(); footer.Controls.Add(supplierReturnArchive);
        root.Controls.Add(footer, 0, 3);
        page.Controls.Add(root);
        return page;
    }


    private void ShowPurchaseReturnDialog()
    {
        using var dialog = new Form
        {
            Text = "مرتجع مشتريات إلى المورد",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(1120, 700), MinimumSize = new Size(900, 580),
            RightToLeft = RightToLeft.Yes, RightToLeftLayout = true,
            Font = new Font("Segoe UI", 10F), BackColor = UiKit.Background
        };
        EnableEscapeClose(dialog);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var supplier = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10F) };
        foreach (var item in BusinessManagementStore.LoadSuppliers()) supplier.Items.Add(item);
        if (supplier.Items.Count > 0) supplier.SelectedIndex = 0;
        var invoice = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10F) };
        var reason = new TextBox { Width = 250, PlaceholderText = "سبب/ملاحظة المرتجع" };
        top.Controls.Add(LabeledControl("المورد", supplier, 290));
        top.Controls.Add(LabeledControl("الفاتورة", invoice, 310));
        top.Controls.Add(LabeledControl("ملاحظات", reason, 270));
        root.Controls.Add(top, 0, 0);
        var hint = MakeHint("اختر «كل فواتير المورد» لإرجاع المتبقي من عدة فواتير مرة واحدة، أو اختر فاتورة بعينها. الكميات المتاحة محسوبة بعد المرتجعات السابقة ومحدودة برصيد المخزون الحالي حتى لا نرجع كتبًا تم بيعها.");
        root.Controls.Add(hint, 0, 1);
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.CellSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        StyleBusinessGrid(grid);
        grid.ReadOnly = false;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="PurchaseItemId", Visible=false, ReadOnly=true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="PurchaseId", Visible=false });
        grid.Columns.Add(UiKit.GridText("Invoice","فاتورة الشراء",120));
        grid.Columns.Add(UiKit.GridText("Date","تاريخ الشراء",130));
        grid.Columns.Add(UiKit.GridText("Product","الصنف",250));
        grid.Columns.Add(UiKit.GridText("Purchased","المشتَرى",80));
        grid.Columns.Add(UiKit.GridText("Available","المتاح للرد",90));
        grid.Columns.Add(UiKit.GridText("Cost","تكلفة الوحدة",100));
        var qtyCol = new DataGridViewTextBoxColumn { Name="ReturnQty", HeaderText="كمية المرتجع", FillWeight=90, ReadOnly=false };
        grid.Columns.Add(qtyCol);
        foreach (DataGridViewColumn column in grid.Columns) column.ReadOnly = true;
        qtyCol.ReadOnly = false;
        root.Controls.Add(grid, 0, 2);
        var footer = new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.RightToLeft, WrapContents=false, Padding=new Padding(3) };
        var totalLabel = new Label { AutoSize=true, Text="إجمالي المرتجع: 0.00 ج.م", Font=new Font("Segoe UI",11F,FontStyle.Bold), ForeColor=UiKit.Text, Padding=new Padding(8,10,8,0) };
        var refresh = UiKit.Button("تحديث الأصناف", UiKit.Primary, 130, 40);
        var save = UiKit.Button("تأكيد مرتجع المورد", UiKit.Danger, 190, 42);
        var close = UiKit.Button("إغلاق", Color.FromArgb(108,117,125), 100, 40);
        footer.Controls.Add(totalLabel); footer.Controls.Add(refresh); footer.Controls.Add(save); footer.Controls.Add(close);
        root.Controls.Add(footer,0,3);
        dialog.Controls.Add(root);
        void LoadInvoiceChoices()
        {
            invoice.Items.Clear();
            invoice.Items.Add(new PurchaseInvoiceChoice(null,"كل فواتير المورد"));
            if(supplier.SelectedItem is SupplierLookupRow selected)
                foreach(var p in BusinessManagementStore.LoadPurchaseArchiveRows("",selected.Id))
                    invoice.Items.Add(new PurchaseInvoiceChoice(p.Id,$"#{p.Id} — {p.InvoiceNumber} — {p.CreatedAt:yyyy-MM-dd}"));
            invoice.SelectedIndex=0;
        }
        void LoadRows()
        {
            grid.Rows.Clear(); totalLabel.Text="إجمالي المرتجع: 0.00 ج.م";
            if(supplier.SelectedItem is not SupplierLookupRow selected) return;
            long? purchaseId=(invoice.SelectedItem as PurchaseInvoiceChoice)?.PurchaseId;
            foreach(var item in BusinessManagementStore.LoadReturnablePurchaseItems(selected.Id,purchaseId))
                grid.Rows.Add(item.PurchaseItemId,item.PurchaseId,
                    string.IsNullOrWhiteSpace(item.InvoiceNumber)?"#"+item.PurchaseId:item.InvoiceNumber,
                    item.PurchaseDate.ToString("yyyy-MM-dd"),item.ProductName,item.PurchasedQuantity,
                    item.AvailableQuantity,item.UnitCost.ToString("N2"),0);
        }
        void Recalc()
        {
            decimal total=0m;
            foreach(DataGridViewRow row in grid.Rows)
            {
                int qty=0; int.TryParse(Convert.ToString(row.Cells["ReturnQty"].Value),out qty);
                decimal cost=0m; decimal.TryParse(Convert.ToString(row.Cells["Cost"].Value),NumberStyles.Any,CultureInfo.CurrentCulture,out cost);
                total+=Math.Max(0,qty)*cost;
            }
            totalLabel.Text=$"إجمالي المرتجع: {total:N2} ج.م";
        }
        supplier.SelectedIndexChanged += (_,_)=>{LoadInvoiceChoices();LoadRows();};
        invoice.SelectedIndexChanged += (_,_)=>LoadRows();
        grid.CellValueChanged += (_,_)=>Recalc();
        grid.CurrentCellDirtyStateChanged += (_,_)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        refresh.Click += (_,_)=>LoadRows();
        close.Click += (_,_)=>dialog.Close();
        save.Click += (_,_) =>
        {
            try
            {
                grid.EndEdit();
                if(supplier.SelectedItem is not SupplierLookupRow selected) throw new InvalidOperationException("اختر المورد أولًا.");
                var lines=new List<PurchaseReturnLineInput>();
                foreach(DataGridViewRow row in grid.Rows)
                {
                    int qty=0; int.TryParse(Convert.ToString(row.Cells["ReturnQty"].Value),out qty);
                    if(qty<=0) continue;
                    int available=Convert.ToInt32(row.Cells["Available"].Value,CultureInfo.InvariantCulture);
                    if(qty>available) throw new InvalidOperationException($"كمية مرتجع ({row.Cells["Product"].Value}) أكبر من المتاح ({available}).");
                    lines.Add(new PurchaseReturnLineInput { PurchaseItemId=Convert.ToInt64(row.Cells["PurchaseItemId"].Value,CultureInfo.InvariantCulture), Quantity=qty });
                }
                if(lines.Count==0) throw new InvalidOperationException("اكتب كمية مرتجع لصنف واحد على الأقل.");
                var confirm=MessageBox.Show(dialog,"سيتم خصم الكميات من المخزون وتسوية قيمة المرتجع مع رصيد المورد، وأي فائض مستحق من المورد سيُسجل كإيراد نقدي. هل تريد المتابعة؟","تأكيد مرتجع المشتريات",MessageBoxButtons.YesNo,MessageBoxIcon.Warning);
                if(confirm!=DialogResult.Yes)return;
                var result=BusinessManagementStore.RecordPurchaseReturn(currentUser,selected.Id,lines,reason.Text);
                LoadSuppliers(); LoadPurchaseArchive(); LoadStockHistory();
                MessageBox.Show(dialog,$"تم تسجيل مرتجع المورد رقم {result.ReturnId}.\nإجمالي المرتجع: {result.Total:N2} ج.م\nالمخصوم من حساب المورد: {result.SupplierCredit:N2} ج.م\nالمستلم نقدًا من المورد: {result.CashRefund:N2} ج.م","تم",MessageBoxButtons.OK,MessageBoxIcon.Information);
                dialog.Close();
            }
            catch(Exception ex){AppLog.Error("تعذر تسجيل مرتجع مشتريات للمورد",ex);MessageBox.Show(dialog,ex.Message,"مرتجع المورد",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        };
        LoadInvoiceChoices(); LoadRows();
        dialog.ShowDialog(this);
    }


    private void ShowPurchaseReturnArchive()
    {
        using var form=new Form { Text="أرشيف مرتجعات المشتريات للموردين",StartPosition=FormStartPosition.CenterParent,
            Size=new Size(1100,650),MinimumSize=new Size(900,520),RightToLeft=RightToLeft.Yes,RightToLeftLayout=true,
            Font=new Font("Segoe UI",10F),BackColor=Color.White };
        EnableEscapeClose(form);
        var grid=new DataGridView { Dock=DockStyle.Fill,AllowUserToAddRows=false,ReadOnly=true,SelectionMode=DataGridViewSelectionMode.FullRowSelect,
            MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill };
        StyleBusinessGrid(grid);
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Id",Visible=false});
        grid.Columns.Add(UiKit.GridText("Return","رقم المرتجع",90));
        grid.Columns.Add(UiKit.GridText("Date","التاريخ",140));
        grid.Columns.Add(UiKit.GridText("Supplier","المورد",170));
        grid.Columns.Add(UiKit.GridText("Products","الأصناف والكميات",300));
        grid.Columns.Add(UiKit.GridText("Total","قيمة المرتجع",120));
        grid.Columns.Add(UiKit.GridText("Cash","المستلم نقدًا",120));
        grid.Columns.Add(UiKit.GridText("Reason","السبب",180));
        foreach(var r in BusinessManagementStore.LoadPurchaseReturnArchive())
            grid.Rows.Add(r.Id,r.Id,r.CreatedAt.ToString("yyyy-MM-dd HH:mm"),r.SupplierName,r.Products,r.Total.ToString("N2"),r.CashRefund.ToString("N2"),r.Reason);
        grid.CellDoubleClick+=(_,e)=>{
            if(e.RowIndex<0)return;
            var row=BusinessManagementStore.LoadPurchaseReturnArchive().FirstOrDefault(x=>x.Id==Convert.ToInt64(grid.Rows[e.RowIndex].Cells["Id"].Value,CultureInfo.InvariantCulture));
            if(row==null)return;
            ShowArchiveText($"تفاصيل مرتجع المورد رقم {row.Id}",
                $"رقم المرتجع: {row.Id}\nالمورد: {row.SupplierName}\nالتاريخ: {row.CreatedAt:yyyy-MM-dd HH:mm}\nالأصناف والكميات: {row.Products}\nإجمالي قيمة المرتجع: {row.Total:N2} ج.م\nالمستلم نقدًا من المورد: {row.CashRefund:N2} ج.م\nالمخصوم من حساب المورد: {row.Total-row.CashRefund:N2} ج.م\nالسبب: {row.Reason}");
        };
        var close=UiKit.Button("إغلاق",Color.FromArgb(108,117,125),100,40);
        close.Dock=DockStyle.Bottom;close.Click+=(_,_)=>form.Close();
        form.Controls.Add(grid);form.Controls.Add(close);form.ShowDialog(this);
    }

    private static void EnableEscapeClose(Form form)
    {
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            e.Handled = true; e.SuppressKeyPress = true;
            form.Close();
        };
    }

    private TabPage BuildReturnsTab()
    {
        var page = NewTab("↩️ المرتجعات");
        var sections = new TabControl { Dock = DockStyle.Fill, RightToLeft = RightToLeft.Yes, RightToLeftLayout = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };

        // مرتجعات المبيعات — نفس الشاشة الحالية دون تغيير منطقها.
        var salesPage = new TabPage("مرتجعات المبيعات") { BackColor = UiKit.Background, Padding = new Padding(8) };
        var salesRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        salesRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        salesRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        salesRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        returnSearch.Width = 270; returnSearch.PlaceholderText = "بحث باسم الصنف";
        var find = UiKit.Button("🔎 تحديث النتائج", UiKit.Primary, 140, 40); find.Click += (_, _) => LoadReturnRows();
        var diagnose = UiKit.Button("🩺 تشخيص المرتجعات", Color.FromArgb(75, 85, 99), 155, 40); diagnose.Click += (_, _) => RunReturnDiagnostics();
        bar.Controls.Add(returnSearch); bar.Controls.Add(find); bar.Controls.Add(diagnose);
        bar.Controls.Add(MakeHint("مرتجعات البيع النقدي والآجل؛ تخصم قيمة المرتجع من دين العميل أولًا، ويُرد الفائض نقدًا."));
        salesRoot.Controls.Add(bar, 0, 0);
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
        salesRoot.Controls.Add(returnGrid, 0, 1);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(3) };
        returnQty.Minimum = 1; returnQty.Maximum = 100000; returnQty.Value = 1; returnQty.DecimalPlaces = 0; returnQty.Width = 90;
        returnReason.Width = 260; returnReason.PlaceholderText = "سبب المرتجع (إجباري)";
        var save = UiKit.Button("تأكيد المرتجع ورد المبلغ", UiKit.Danger, 210, 42); save.Click += (_, _) => ProcessReturn();
        footer.Controls.Add(LabeledControl("الكمية المرتجعة", returnQty, 115));
        footer.Controls.Add(LabeledControl("السبب", returnReason, 270));
        footer.Controls.Add(save);
        salesRoot.Controls.Add(footer, 0, 2);
        salesPage.Controls.Add(salesRoot);

        // مرتجعات المشتريات — منفصلة تمامًا عن مرتجعات البيع.
        var purchasesPage = new TabPage("مرتجعات المشتريات") { BackColor = UiKit.Background, Padding = new Padding(10) };
        var purchaseRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        purchaseRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        purchaseRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        purchaseRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var purchaseBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var newPurchaseReturn = UiKit.Button("＋ إنشاء مرتجع مشتريات", UiKit.Danger, 205, 42);
        newPurchaseReturn.Click += (_, _) => { ShowPurchaseReturnDialog(); LoadPurchaseReturnArchiveGrid(); };
        var refreshPurchaseReturns = UiKit.Button("⟳ تحديث الأرشيف", UiKit.Primary, 150, 42);
        var archiveHint = MakeHint("اختر فاتورة مورد واحدة أو كل فواتير المورد، ثم حدد الأصناف والكميات. تُخصم قيمة المرتجع من حساب المورد أولًا.");
        refreshPurchaseReturns.Click += (_, _) => LoadPurchaseReturnArchiveGrid();
        purchaseBar.Controls.Add(newPurchaseReturn);
        purchaseBar.Controls.Add(refreshPurchaseReturns);
        purchaseBar.Controls.Add(archiveHint);
        purchaseRoot.Controls.Add(purchaseBar, 0, 0);

        var purchaseReturnGrid = new DataGridView { Name = "purchaseReturnArchiveGrid", Dock = DockStyle.Fill, AllowUserToAddRows = false, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        StyleBusinessGrid(purchaseReturnGrid);
        purchaseReturnGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        purchaseReturnGrid.Columns.Add(UiKit.GridText("Return", "رقم المرتجع", 90));
        purchaseReturnGrid.Columns.Add(UiKit.GridText("Date", "التاريخ", 140));
        purchaseReturnGrid.Columns.Add(UiKit.GridText("Supplier", "المورد", 150));
        purchaseReturnGrid.Columns.Add(UiKit.GridText("Products", "الأصناف والكميات", 300));
        purchaseReturnGrid.Columns.Add(UiKit.GridText("Total", "قيمة المرتجع", 110));
        purchaseReturnGrid.Columns.Add(UiKit.GridText("Cash", "المسترد نقدًا", 110));
        purchaseReturnGrid.Columns.Add(UiKit.GridText("Reason", "ملاحظات", 150));
        purchaseReturnGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            long id = Convert.ToInt64(purchaseReturnGrid.Rows[e.RowIndex].Cells["Id"].Value, CultureInfo.InvariantCulture);
            var row = BusinessManagementStore.LoadPurchaseReturnArchive().FirstOrDefault(x => x.Id == id);
            if (row == null) return;
            ShowArchiveText($"تفاصيل مرتجع مشتريات رقم {row.Id}",
                $"رقم المرتجع: {row.Id}\nالمورد: {row.SupplierName}\nالتاريخ: {row.CreatedAt:yyyy-MM-dd HH:mm}\nالأصناف والكميات: {row.Products}\nإجمالي المرتجع: {row.Total:N2} ج.م\nالمسترد نقدًا: {row.CashRefund:N2} ج.م\nالمخصوم من حساب المورد: {row.Total-row.CashRefund:N2} ج.م\nالسبب: {row.Reason}");
        };
        purchaseRoot.Controls.Add(purchaseReturnGrid, 0, 1);
        purchaseRoot.Controls.Add(MakeHint("دبل كليك على أي مرتجع لعرض تفاصيله."), 0, 2);
        purchasesPage.Controls.Add(purchaseRoot);

        void LoadPurchaseReturnArchiveGrid()
        {
            purchaseReturnGrid.Rows.Clear();
            foreach (var r in BusinessManagementStore.LoadPurchaseReturnArchive())
                purchaseReturnGrid.Rows.Add(r.Id, r.Id, r.CreatedAt.ToString("yyyy-MM-dd HH:mm"), r.SupplierName, r.Products, r.Total.ToString("N2"), r.CashRefund.ToString("N2"), r.Reason);
        }

        sections.TabPages.Add(salesPage);
        sections.TabPages.Add(purchasesPage);
        sections.SelectedIndexChanged += (_, _) => { if (sections.SelectedTab == purchasesPage) LoadPurchaseReturnArchiveGrid(); };
        page.Controls.Add(sections);
        return page;
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
        profitSummary.Padding = new Padding(18); profitSummary.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        profitSummary.TextAlign = ContentAlignment.MiddleRight;
        profitSummary.Text = "اختر الفترة واضغط حساب النتائج.\n\nسيظهر هامش المبيعات بتكلفة الشراء التاريخية، والمرتجعات والعمولات والمصروفات، مع تفصيل إيرادات الطباعة حسب الفئة واللون والوجهين. تكلفة الورق والحبر غير مسجلة حاليًا، لذلك لا يتم وصف إيرادات الطباعة بأنها صافي ربح.";
        root.Controls.Add(profitSummary, 0, 1);

        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 8, 0, 0) };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));

        var productsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
        productsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); productsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        productsPanel.Controls.Add(MakeHint("ربحية الأصناف — بعد خصم المرتجعات"), 0, 0);
        StyleBusinessGrid(profitProductGrid);
        profitProductGrid.Columns.Add(UiKit.GridText("Product", "الصنف", 180));
        profitProductGrid.Columns.Add(UiKit.GridText("Revenue", "المبيعات", 85));
        profitProductGrid.Columns.Add(UiKit.GridText("Cost", "التكلفة", 85));
        profitProductGrid.Columns.Add(UiKit.GridText("Returns", "المرتجعات", 85));
        profitProductGrid.Columns.Add(UiKit.GridText("Profit", "الربح", 85));
        productsPanel.Controls.Add(profitProductGrid, 0, 1);

        var employeePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
        employeePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); employeePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        employeePanel.Controls.Add(MakeHint("مبيعات الأصناف حسب الموظف"), 0, 0);
        StyleBusinessGrid(profitEmployeeGrid);
        profitEmployeeGrid.Columns.Add(UiKit.GridText("User", "الموظف", 105));
        profitEmployeeGrid.Columns.Add(UiKit.GridText("Sales", "المبيعات", 85));
        profitEmployeeGrid.Columns.Add(UiKit.GridText("Profit", "هامش معروف", 95));
        profitEmployeeGrid.Columns.Add(UiKit.GridText("Count", "العمليات", 70));
        profitEmployeeGrid.Columns.Add(UiKit.GridText("Unknown", "بلا تكلفة", 75));
        employeePanel.Controls.Add(profitEmployeeGrid, 0, 1);

        var printingPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
        printingPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); printingPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        printingPanel.Controls.Add(MakeHint("إيرادات الطباعة حسب الفئة واللون"), 0, 0);
        StyleBusinessGrid(profitPrintingGrid);
        profitPrintingGrid.Columns.Add(UiKit.GridText("Category", "الفئة", 90));
        profitPrintingGrid.Columns.Add(UiKit.GridText("Color", "اللون", 75));
        profitPrintingGrid.Columns.Add(UiKit.GridText("Duplex", "الوجهين", 85));
        profitPrintingGrid.Columns.Add(UiKit.GridText("Jobs", "عمليات", 65));
        profitPrintingGrid.Columns.Add(UiKit.GridText("Revenue", "الإيراد", 95));
        printingPanel.Controls.Add(profitPrintingGrid, 0, 1);

        detail.Controls.Add(productsPanel, 0, 0);
        detail.Controls.Add(employeePanel, 1, 0);
        detail.Controls.Add(printingPanel, 2, 0);
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
        var archiveTabs = new TabControl { Dock = DockStyle.Fill, RightToLeft = RightToLeft.Yes, RightToLeftLayout = true };

        var salesPage = NewTab("فواتير البيع");
        var salesRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        salesRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); salesRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var salesBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        invoiceSearch.Width = 350; invoiceSearch.PlaceholderText = "رقم الفاتورة أو اسم العميل";
        var salesFind = UiKit.Button("بحث", UiKit.Primary, 100, 40); salesFind.Click += (_, _) => LoadInvoiceRows();
        var salesOpen = UiKit.Button("فتح ومعاينة الفاتورة", UiKit.Success, 180, 40); salesOpen.Click += (_, _) => OpenSelectedInvoice();
        invoiceSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadInvoiceRows(); } };
        salesBar.Controls.Add(invoiceSearch); salesBar.Controls.Add(salesFind); salesBar.Controls.Add(salesOpen); salesRoot.Controls.Add(salesBar, 0, 0);
        StyleBusinessGrid(invoiceGrid);
        invoiceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        invoiceGrid.Columns.Add(UiKit.GridText("Number", "رقم الفاتورة", 130));
        invoiceGrid.Columns.Add(UiKit.GridText("Customer", "العميل", 280));
        invoiceGrid.Columns.Add(UiKit.GridText("Total", "الإجمالي", 150));
        invoiceGrid.Columns.Add(UiKit.GridText("Date", "التاريخ", 210));
        invoiceGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelectedInvoice(); };
        salesRoot.Controls.Add(invoiceGrid, 0, 1); salesPage.Controls.Add(salesRoot);
        archiveTabs.TabPages.Add(salesPage);

        var purchasesPage = NewTab("فواتير الشراء");
        var purchasesRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        purchasesRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); purchasesRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var purchasesBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        purchaseArchiveSearch.Width = 350; purchaseArchiveSearch.PlaceholderText = "رقم فاتورة الشراء أو اسم المورد";
        var purchaseFind = UiKit.Button("بحث", UiKit.Primary, 100, 40); purchaseFind.Click += (_, _) => LoadPurchaseArchive();
        var purchaseOpen = UiKit.Button("تفاصيل الفاتورة والدفعات", UiKit.Success, 205, 40); purchaseOpen.Click += (_, _) => OpenSelectedPurchaseArchive();
        purchaseArchiveSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadPurchaseArchive(); } };
        purchasesBar.Controls.Add(purchaseArchiveSearch); purchasesBar.Controls.Add(purchaseFind); purchasesBar.Controls.Add(purchaseOpen);
        purchasesRoot.Controls.Add(purchasesBar, 0, 0);
        StyleBusinessGrid(purchaseArchiveGrid);
        purchaseArchiveGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        purchaseArchiveGrid.Columns.Add(UiKit.GridText("Number", "رقم فاتورة المورد", 150));
        purchaseArchiveGrid.Columns.Add(UiKit.GridText("Supplier", "المورد", 220));
        purchaseArchiveGrid.Columns.Add(UiKit.GridText("Total", "الإجمالي", 120));
        purchaseArchiveGrid.Columns.Add(UiKit.GridText("Paid", "المدفوع", 120));
        purchaseArchiveGrid.Columns.Add(UiKit.GridText("Remaining", "المتبقي", 120));
        purchaseArchiveGrid.Columns.Add(UiKit.GridText("Date", "التاريخ", 175));
        purchaseArchiveGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelectedPurchaseArchive(); };
        purchasesRoot.Controls.Add(purchaseArchiveGrid, 0, 1); purchasesPage.Controls.Add(purchasesRoot);
        archiveTabs.TabPages.Add(purchasesPage);

        var returnsPage = NewTab("فواتير المرتجعات");
        var returnsRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        returnsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); returnsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var returnsBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        returnArchiveSearch.Width = 350; returnArchiveSearch.PlaceholderText = "رقم المرتجع أو الصنف أو رقم البيع";
        var returnFind = UiKit.Button("بحث", UiKit.Primary, 100, 40); returnFind.Click += (_, _) => LoadReturnArchive();
        var returnOpen = UiKit.Button("تفاصيل فاتورة المرتجع", UiKit.Success, 190, 40); returnOpen.Click += (_, _) => OpenSelectedReturnArchive();
        returnArchiveSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadReturnArchive(); } };
        returnsBar.Controls.Add(returnArchiveSearch); returnsBar.Controls.Add(returnFind); returnsBar.Controls.Add(returnOpen);
        returnsRoot.Controls.Add(returnsBar, 0, 0);
        StyleBusinessGrid(returnArchiveGrid);
        returnArchiveGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        returnArchiveGrid.Columns.Add(UiKit.GridText("Number", "رقم المرتجع", 120));
        returnArchiveGrid.Columns.Add(UiKit.GridText("Original", "رقم عملية البيع", 150));
        returnArchiveGrid.Columns.Add(UiKit.GridText("Products", "الأصناف والكميات", 290));
        returnArchiveGrid.Columns.Add(UiKit.GridText("Refund", "المبلغ المردود", 130));
        returnArchiveGrid.Columns.Add(UiKit.GridText("Reason", "السبب", 180));
        returnArchiveGrid.Columns.Add(UiKit.GridText("Date", "التاريخ", 175));
        returnArchiveGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelectedReturnArchive(); };
        returnsRoot.Controls.Add(returnArchiveGrid, 0, 1); returnsPage.Controls.Add(returnsRoot);
        archiveTabs.TabPages.Add(returnsPage);

        page.Controls.Add(archiveTabs);
        return page;
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
                long purchaseId = r.GetInt64(0);
                string invoiceNumber = r.GetString(2);
                supplierPurchaseGrid.Rows.Add(purchaseId, date.ToString("yyyy-MM-dd HH:mm"),
                    string.IsNullOrWhiteSpace(invoiceNumber) ? "#" + purchaseId.ToString(CultureInfo.InvariantCulture) : invoiceNumber,
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
        try { BusinessManagementStore.PaySupplier(currentUser, id, dialog.Amount, dialog.Note); LoadSuppliers(); LoadPurchaseArchive(); MessageBox.Show(this, "تم تسجيل السداد وتحديث رصيد المورد.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "سداد مورد", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }


    private void LoadSupplierPayments()
    {
        if (supplierPaymentGrid.Columns.Count == 0) return;
        supplierPaymentGrid.Rows.Clear();
        if (supplierGrid.CurrentRow == null) return;
        try
        {
            long supplierId = Convert.ToInt64(supplierGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
            foreach (var payment in BusinessManagementStore.LoadSupplierPaymentHistory(supplierId))
                supplierPaymentGrid.Rows.Add(payment.Id, payment.PurchaseId,
                    payment.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    string.IsNullOrWhiteSpace(payment.InvoiceNumber) ? "#" + payment.PurchaseId.ToString(CultureInfo.InvariantCulture) : payment.InvoiceNumber,
                    payment.Amount.ToString("N2"), payment.Note, payment.UserName);
        }
        catch (Exception ex) { AppLog.Error("تعذر تحميل دفعات المورد", ex); }
    }

    private void ShowSelectedSupplierDetails()
    {
        if (supplierGrid.CurrentRow == null) return;
        long supplierId = Convert.ToInt64(supplierGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
        var details = BusinessManagementStore.LoadSupplierDetails(supplierId);
        if (details == null) { MessageBox.Show(this, "المورد غير موجود.", "تفاصيل المورد", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        using var dialog = new Form
        {
            Text = "تفاصيل المورد والدفعات", StartPosition = FormStartPosition.CenterParent,
            Size = new Size(860, 650), MinimumSize = new Size(760, 560),
            RightToLeft = RightToLeft.Yes, RightToLeftLayout = true,
            Font = new Font("Segoe UI", 10F), BackColor = Color.White
        };
        EnableEscapeClose(dialog);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        TextBox name = new() { Dock = DockStyle.Fill, Text = details.Name, ReadOnly = true };
        TextBox phone = new() { Dock = DockStyle.Fill, Text = details.Phone, ReadOnly = true };
        TextBox address = new() { Dock = DockStyle.Fill, Text = details.Address, ReadOnly = true };
        LabelField(fields, "اسم المورد", name, 0, 0); LabelField(fields, "الهاتف", phone, 2, 0);
        LabelField(fields, "العنوان", address, 0, 1);
        var balance = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(0, 121, 107), Text = $"الرصيد المستحق: {details.Balance:N2} ج.م" };
        fields.Controls.Add(balance, 2, 1); fields.SetColumnSpan(balance, 2);
        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(MakeHint("سجل دفعات المورد — دبل كليك على الدفعة لفتح فاتورة الشراء المرتبطة بها"), 0, 1);

        var payments = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
        StyleBusinessGrid(payments);
        payments.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
        payments.Columns.Add(new DataGridViewTextBoxColumn { Name = "PurchaseId", Visible = false });
        payments.Columns.Add(UiKit.GridText("Date", "التاريخ", 160));
        payments.Columns.Add(UiKit.GridText("Invoice", "رقم الفاتورة", 130));
        payments.Columns.Add(UiKit.GridText("Amount", "المبلغ", 120));
        payments.Columns.Add(UiKit.GridText("Note", "البيان", 220));
        payments.Columns.Add(UiKit.GridText("User", "المستخدم", 120));
        foreach (var p in BusinessManagementStore.LoadSupplierPaymentHistory(supplierId))
            payments.Rows.Add(p.Id, p.PurchaseId, p.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                string.IsNullOrWhiteSpace(p.InvoiceNumber) ? "#" + p.PurchaseId.ToString(CultureInfo.InvariantCulture) : p.InvoiceNumber,
                p.Amount.ToString("N2"), p.Note, p.UserName);
        payments.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            long purchaseId = Convert.ToInt64(payments.Rows[e.RowIndex].Cells["PurchaseId"].Value, CultureInfo.InvariantCulture);
            ShowPurchaseInvoiceDetails(purchaseId);
        };
        root.Controls.Add(payments, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var edit = UiKit.Button("🔐 تعديل بيانات المورد", Color.FromArgb(111, 66, 193), 190, 40);
        var save = UiKit.Button("💾 حفظ التعديل", UiKit.Success, 145, 40); save.Visible = false;
        var close = UiKit.Button("إغلاق", Color.FromArgb(108, 117, 125), 100, 40);
        edit.Click += (_, _) =>
        {
            using var password = new AdminPasswordPromptForm();
            if (password.ShowDialog(dialog) != DialogResult.OK) return;
            if (!AdminSecurity.ValidateAdminPassword(password.Password))
            {
                MessageBox.Show(dialog, "كلمة مرور الإدارة غير صحيحة. لم يتم فتح التعديل.", "رفض التعديل", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!AdminPasswordRotation.EnsureRotatedIfDefault(dialog))
                return;
            name.ReadOnly = false; phone.ReadOnly = false; address.ReadOnly = false;
            save.Visible = true; edit.Enabled = false; name.Focus();
        };
        save.Click += (_, _) =>
        {
            try
            {
                BusinessManagementStore.UpdateSupplier(currentUser, supplierId, name.Text, phone.Text, address.Text);
                name.ReadOnly = true; phone.ReadOnly = true; address.ReadOnly = true;
                save.Visible = false; edit.Enabled = true;
                LoadSuppliers(); LoadSupplierPurchaseHistory(); LoadSupplierPayments();
                MessageBox.Show(dialog, "تم تحديث بيانات المورد وتسجيل التعديل في سجل المراجعة.", "تم الحفظ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "تعديل المورد", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        close.Click += (_, _) => dialog.Close();
        buttons.Controls.Add(close); buttons.Controls.Add(save); buttons.Controls.Add(edit);
        root.Controls.Add(buttons, 0, 3);
        dialog.Controls.Add(root);
        dialog.ShowDialog(this);
    }

    private static void LabelField(TableLayoutPanel host, string label, Control input, int column, int row)
    {
        var caption = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
        host.Controls.Add(caption, column, row);
        host.Controls.Add(input, column + 1, row);
    }

    private void ShowPurchaseInvoiceDetailsFromGrid(DataGridView grid)
    {
        if (grid.CurrentRow == null) return;
        string idColumn = grid.Columns.Contains("PurchaseId") ? "PurchaseId" : "Id";
        object? value = grid.CurrentRow.Cells[idColumn].Value;
        if (value == null) return;
        ShowPurchaseInvoiceDetails(Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }

    private void ShowPurchaseInvoiceDetails(long purchaseId)
    {
        var header = BusinessManagementStore.LoadPurchaseInvoiceHeader(purchaseId);
        if (header == null) { MessageBox.Show(this, "فاتورة الشراء غير موجودة.", "أرشيف المشتريات", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var sb = new StringBuilder();
        sb.AppendLine($"فاتورة شراء رقم: {header.InvoiceNumber} (سجل #{header.Id})");
        sb.AppendLine($"المورد: {header.SupplierName}");
        sb.AppendLine($"التاريخ: {header.CreatedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"المستخدم: {header.UserName}");
        sb.AppendLine($"الإجمالي: {header.Total:N2} ج.م | المدفوع: {header.Paid:N2} ج.م | المتبقي: {header.Remaining:N2} ج.م");
        if (!string.IsNullOrWhiteSpace(header.Notes)) sb.AppendLine($"ملاحظات: {header.Notes}");
        sb.AppendLine("\nالأصناف:");
        foreach (var item in BusinessManagementStore.LoadPurchaseInvoiceItems(purchaseId))
            sb.AppendLine($"• {item.ProductName} | {item.Quantity} × {item.UnitCost:N2} = {item.Total:N2} ج.م");
        sb.AppendLine("\nالدفعات المسجلة:");
        var payments = BusinessManagementStore.LoadPurchaseInvoicePayments(purchaseId);
        if (payments.Count == 0) sb.AppendLine("لا توجد دفعات مسجلة.");
        foreach (var payment in payments)
            sb.AppendLine($"• {payment.CreatedAt:yyyy-MM-dd HH:mm} | {payment.Amount:N2} ج.م | {payment.Note} | {payment.UserName}");
        ShowArchiveText("تفاصيل فاتورة الشراء", sb.ToString());
    }

    private void ShowArchiveText(string title, string content)
    {
        using var form = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, Size = new Size(1040, 760),
            MinimumSize = new Size(820, 600), RightToLeft = RightToLeft.Yes, RightToLeftLayout = true,
            Font = new Font("Segoe UI", 11F), BackColor = Color.FromArgb(245, 247, 250)
        };
        EnableEscapeClose(form);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        var bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18), BorderStyle = BorderStyle.FixedSingle };
        var body = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
            DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
            Text = content, Font = new Font("Segoe UI", 12F), RightToLeft = RightToLeft.Yes,
            BackColor = Color.White, ForeColor = Color.FromArgb(40, 50, 62), ZoomFactor = 1.0f
        };
        bodyHost.Controls.Add(body);
        root.Controls.Add(bodyHost, 0, 0);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        var close = UiKit.Button("إغلاق  (Esc)", Color.FromArgb(52, 73, 94), 140, 42);
        close.Click += (_, _) => form.Close();
        footer.Controls.Add(close);
        root.Controls.Add(footer, 0, 1);
        form.Controls.Add(root);
        form.ShowDialog(this);
    }

    private void LoadPurchaseArchive()
    {
        purchaseArchiveGrid.Rows.Clear();
        foreach (var p in BusinessManagementStore.LoadPurchaseArchiveRows(purchaseArchiveSearch.Text))
            purchaseArchiveGrid.Rows.Add(p.Id,
                string.IsNullOrWhiteSpace(p.InvoiceNumber) ? "#" + p.Id.ToString(CultureInfo.InvariantCulture) : p.InvoiceNumber,
                p.SupplierName, p.Total.ToString("N2"), p.Paid.ToString("N2"), p.Remaining.ToString("N2"), p.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
    }

    private void OpenSelectedPurchaseArchive()
    {
        if (purchaseArchiveGrid.CurrentRow == null) return;
        long id = Convert.ToInt64(purchaseArchiveGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
        ShowPurchaseInvoiceDetails(id);
    }

    private void LoadReturnArchive()
    {
        returnArchiveGrid.Rows.Clear();
        foreach (var r in BusinessManagementStore.LoadSalesReturnArchiveRows(returnArchiveSearch.Text))
            returnArchiveGrid.Rows.Add(r.Id, r.Id, r.OriginalTransactions, r.Products, r.RefundAmount.ToString("N2"), r.Reason, r.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
    }

    private void OpenSelectedReturnArchive()
    {
        if (returnArchiveGrid.CurrentRow == null) return;
        long id = Convert.ToInt64(returnArchiveGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
        var header = BusinessManagementStore.LoadSalesReturnHeader(id);
        if (header == null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"فاتورة مرتجع رقم: {header.Id}");
        sb.AppendLine($"تاريخ المرتجع: {header.CreatedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"العميل: {header.CustomerName}");
        sb.AppendLine($"رقم عملية البيع الأصلية: {header.OriginalTransactions}");
        sb.AppendLine($"المبلغ المردود: {header.RefundAmount:N2} ج.م");
        sb.AppendLine($"السبب: {header.Reason}");
        sb.AppendLine($"المستخدم: {header.UserName}");
        sb.AppendLine("\nالأصناف والكميات:");
        foreach (var item in BusinessManagementStore.LoadSalesReturnItems(id))
            sb.AppendLine($"• {item.ProductName} | الكمية: {item.Quantity} | المبلغ: {item.RefundAmount:N2} ج.م | تكلفة مرتجعة: {(item.ReturnedCost.HasValue ? item.ReturnedCost.Value.ToString("N2") : "غير معروفة")}");
        ShowArchiveText("تفاصيل فاتورة المرتجع", sb.ToString());
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
            decimal total = Math.Round(purchaseLines.Sum(x => x.Quantity * x.UnitCost), 2, MidpointRounding.AwayFromZero);
            if (purchaseLines.Count == 0) throw new InvalidOperationException("أضف صنفًا واحدًا على الأقل قبل حفظ الفاتورة.");
            if (purchasePaid.Value > total) throw new InvalidOperationException("المدفوع أكبر من إجمالي فاتورة الشراء.");
            if (purchasePaid.Value + 0.005m < total)
            {
                decimal difference = total - purchasePaid.Value;
                var decision = MessageBox.Show(this,
                    $"إجمالي فاتورة الشراء: {total:N2} ج.م\nالمدفوع: {purchasePaid.Value:N2} ج.م\nالفرق: {difference:N2} ج.م\n\nنعم = الفرق دين مستحق للمورد وسجّل الفاتورة.\nلا = يوجد خطأ محتمل في الأصناف أو الأسعار؛ ارجع لمراجعة الفاتورة.\nإلغاء = عدم الحفظ الآن.",
                    "مراجعة إجمالي فاتورة الشراء", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (decision == DialogResult.Cancel) return;
                if (decision == DialogResult.No)
                {
                    tabs.SelectedIndex = 1;
                    purchaseGrid.Focus();
                    MessageBox.Show(this, "لم يتم تسجيل الفاتورة. راجع الأصناف والكميات والأسعار ثم أعد حساب الإجمالي.", "مراجعة الفاتورة", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            long id = BusinessManagementStore.CreatePurchase(currentUser, supplier.Id, purchaseNumber.Text, purchaseNotes.Text, purchasePaid.Value,
                purchaseLines.Select(x => new PurchaseLineInput { ProductId = x.ProductId, Quantity = x.Quantity, UnitCost = x.UnitCost }).ToList());
            MessageBox.Show(this, $"تم حفظ فاتورة الشراء رقم {id}.\nتم تحديث كميات المخزن ومتوسط تكلفة الشراء تلقائيًا.", "تم حفظ المشتريات", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ClearPurchase(); LoadSuppliers(); LoadPurchaseProducts(purchaseProductSearch.Text); LoadPurchaseArchive();
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


    private void RunReturnDiagnostics()
    {
        try
        {
            using var c = AppDatabase.OpenConnection();
            long Scalar(string sql)
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt64(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
            }
            bool HasTable(string name)
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
                cmd.Parameters.AddWithValue("$name", name);
                return Convert.ToInt64(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture) > 0;
            }

            bool returnsTable = HasTable("SalesReturns");
            bool itemsTable = HasTable("SalesReturnItems");
            bool inventoryTable = HasTable("InventoryMovements");
            bool treasuryTable = HasTable("TreasuryMovements");
            long saleLines = Scalar("SELECT COUNT(*) FROM Transactions WHERE TransactionType='SALE' AND ProductId IS NOT NULL");
            long creditSales = Scalar(@"SELECT COUNT(*) FROM Transactions
WHERE TransactionType='SALE' AND ProductId IS NOT NULL AND COALESCE(RemainingAmount,0)>0.005");
            long openShift;
            using (var shift = c.CreateCommand())
            {
                shift.CommandText = @"SELECT COUNT(*) FROM DailySessions ds
JOIN DailyShifts sh ON sh.SessionId=ds.Id
WHERE ds.BusinessDate=$date AND ds.Status='OPEN' AND sh.Status='OPEN' AND sh.UserName=$user";
                shift.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                shift.Parameters.AddWithValue("$user", currentUser);
                openShift = Convert.ToInt64(shift.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
            }
            long returnDocuments = returnsTable ? Scalar("SELECT COUNT(*) FROM SalesReturns") : 0;
            long returnItems = itemsTable ? Scalar("SELECT COUNT(*) FROM SalesReturnItems") : 0;
            long returnedUnits = itemsTable ? Scalar("SELECT COALESCE(SUM(Quantity),0) FROM SalesReturnItems") : 0;
            long availableLines = returnsTable && itemsTable
                ? BusinessManagementStore.LoadReturnableSales("").Count
                : 0;
            long overReturnedLines = itemsTable
                ? Scalar(@"SELECT COUNT(*) FROM (
SELECT ri.OriginalTransactionId
FROM SalesReturnItems ri JOIN Transactions t ON t.Id=ri.OriginalTransactionId
GROUP BY ri.OriginalTransactionId
HAVING SUM(ri.Quantity)>MAX(COALESCE(t.Quantity,1))
)")
                : 0;
            long missingStockMovements = returnsTable && itemsTable && inventoryTable
                ? Scalar(@"SELECT COUNT(*) FROM SalesReturns sr
WHERE EXISTS(SELECT 1 FROM SalesReturnItems ri WHERE ri.ReturnId=sr.Id)
AND NOT EXISTS(SELECT 1 FROM InventoryMovements im WHERE im.ReferenceType='SalesReturn' AND im.ReferenceId=sr.Id)")
                : 0;
            long missingTreasuryMovements = returnsTable && treasuryTable
                ? Scalar(@"SELECT COUNT(*) FROM SalesReturns sr
WHERE sr.RefundAmount - COALESCE((
    SELECT SUM(d.Amount) FROM Debts_History d
    WHERE d.Type='PAYMENT' AND d.Note LIKE ('تسوية من مرتجع مبيعات رقم ' || sr.Id || '%')
),0) > 0.005
AND NOT EXISTS(SELECT 1 FROM TreasuryMovements tm WHERE tm.ReferenceType='SalesReturn' AND tm.ReferenceId=sr.Id)")
                : 0;

            var issues = new List<string>();
            if (!returnsTable || !itemsTable) issues.Add("جداول المرتجعات غير مكتملة؛ يلزم تهيئة قاعدة البيانات.");
            if (creditSales > 0)
                issues.Add($"يوجد {creditSales} عملية بيع آجل عليها رصيد؛ أصبحت هذه العمليات ظاهرة للإرجاع، وتُخصم قيمة المرتجع من دين العميل قبل أي رد نقدي.");
            if (openShift == 0 && missingTreasuryMovements > 0)
                issues.Add("لا يوجد شفت مفتوح للمستخدم الحالي؛ راجع المرتجعات التي تحتاج ردًا نقديًا وافتح الشفت قبل تسجيلها.");
            if (overReturnedLines > 0) issues.Add("يوجد سطر بيع تم إرجاع كمية أكبر من الكمية المباعة؛ راجع سجلات المرتجعات.");
            if (missingStockMovements > 0) issues.Add("بعض فواتير المرتجع لا يقابلها سجل حركة مخزون.");
            if (missingTreasuryMovements > 0) issues.Add("بعض فواتير المرتجع لا يقابلها سجل حركة خزينة.");
            if (issues.Count == 0) issues.Add("لم يتم العثور على خلل بنيوي أو قيد تشغيل واضح في السجلات التي فحصها التشخيص.");

            string report =
                $"تشخيص المرتجعات — {DateTime.Now:yyyy-MM-dd HH:mm}\n\n" +
                $"عمليات بيع أصناف مسجلة: {saleLines}\n" +
                $"عمليات بيع آجلة عليها رصيد (متاحة الآن للبحث والإرجاع): {creditSales}\n" +
                $"عمليات بيع متاحة للإرجاع في نتائج الشاشة (حتى 300): {availableLines}\n" +
                $"الشفت المفتوح للمستخدم الحالي: {(openShift > 0 ? "نعم" : "لا")}\n" +
                $"فواتير مرتجعات مسجلة: {returnDocuments}\n" +
                $"سطور مرتجعات مسجلة: {returnItems}\n" +
                $"إجمالي الوحدات المرتجعة: {returnedUnits}\n" +
                $"سطور تجاوزت الكمية المباعة: {overReturnedLines}\n" +
                $"مرتجعات بلا حركة مخزون مقابلة: {missingStockMovements}\n" +
                $"مرتجعات بلا حركة خزينة مقابلة: {missingTreasuryMovements}\n\n" +
                "نتيجة الفحص:\n• " + string.Join("\n• ", issues) +
                "\n\nملاحظة: التشخيص يقرأ السجلات فقط ولا يغيّر الكميات أو الأرصدة.";
            ShowArchiveText("تقرير تشخيص المرتجعات", report);
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر تشخيص المرتجعات", ex);
            MessageBox.Show(this, "تعذر إكمال فحص المرتجعات:\n" + ex.Message, "تشخيص المرتجعات", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ProcessReturn()
    {
        if (returnGrid.CurrentRow?.Tag is not ReturnableSaleRow sale) { MessageBox.Show(this, "اختر عملية بيع من الجدول.", "مرتجع", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        int qty = Convert.ToInt32(returnQty.Value, CultureInfo.InvariantCulture);
        if (qty > sale.AvailableQuantity) { MessageBox.Show(this, $"المتاح للإرجاع {sale.AvailableQuantity} فقط.", "الكمية", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            var answer = MessageBox.Show(this, $"تأكيد إرجاع {qty} من ({sale.ProductName})؟\nسيتم إضافة الكمية للمخزن، وخصم قيمة المرتجع من دين العميل أولًا، ورد أي فائض نقدًا فقط.", "تأكيد المرتجع", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            var result = BusinessManagementStore.RecordSaleReturn(currentUser, sale.TransactionId, qty, returnReason.Text);
            returnReason.Clear(); LoadReturnRows(); LoadReturnArchive();
            MessageBox.Show(this,
                $"تم تسجيل المرتجع رقم {result.ReturnId}.\nقيمة المرتجع: {result.Refund:N2} ج.م\nالمخصوم من دين العميل: {result.DebtOffset:N2} ج.م\nالمردود نقدًا: {result.CashRefund:N2} ج.م",
                "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            profitPrintingGrid.Rows.Clear();
            foreach (var item in p.PrintingBreakdown)
                profitPrintingGrid.Rows.Add(item.Category, item.ColorType, item.DuplexMode, item.Jobs, item.Revenue.ToString("N2"));
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
            sb.AppendLine(string.Join(",", new[] { "الباركود", "اسم الصنف", "الوصف", "الفئة", "سعر الشراء", "سعر البيع", "الكمية", "الحد الأدنى" }.Select(CsvEscape)));
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
            string CanonicalHeader(string value) => value.Trim().ToLowerInvariant() switch
            {
                "barcode" or "الباركود" or "باركود" => "Barcode",
                "name" or "اسم الصنف" or "الصنف" => "Name",
                "description" or "الوصف" => "Description",
                "category" or "الفئة" or "التصنيف" => "Category",
                "purchaseprice" or "سعر الشراء" or "تكلفة الشراء" => "PurchasePrice",
                "saleprice" or "سعر البيع" => "SalePrice",
                "quantity" or "الكمية" => "Quantity",
                "minquantity" or "الحد الأدنى" or "الحد الادنى" => "MinQuantity",
                _ => value.Trim()
            };
            header = header.Select(CanonicalHeader).ToList();
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
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; Close(); } };
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


internal sealed class AdminPasswordPromptForm : Form
{
    private readonly TextBox password = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12F) };
    public string Password => password.Text;

    public AdminPasswordPromptForm()
    {
        Text = "مصادقة الإدارة";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        Size = new Size(390, 190);
        RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F); BackColor = Color.White;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); } };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = "أدخل كلمة مرور الإدارة للسماح بتعديل بيانات المورد:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
        root.Controls.Add(password, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var ok = UiKit.Button("متابعة", UiKit.Success, 105, 36); ok.Click += (_, _) => { if (string.IsNullOrWhiteSpace(password.Text)) { MessageBox.Show(this, "أدخل كلمة المرور.", "مصادقة الإدارة", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } DialogResult = DialogResult.OK; Close(); };
        var cancel = UiKit.Button("إلغاء", Color.FromArgb(108, 117, 125), 95, 36); cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 2);
        Controls.Add(root); AcceptButton = ok; CancelButton = cancel;
        Shown += (_, _) => password.Focus();
    }
}


internal sealed class PurchaseInvoiceChoice
{
    public long? PurchaseId { get; }
    private readonly string label;
    public PurchaseInvoiceChoice(long? purchaseId,string label){PurchaseId=purchaseId;this.label=label;}
    public override string ToString()=>label;
}
