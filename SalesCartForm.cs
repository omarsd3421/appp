using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal sealed class SalesCartForm : Form
{
    private readonly string currentUser;
    private readonly List<StockCartLine> lines = new();
    private readonly TextBox search = new();
    private readonly NumericUpDown quantity = new();
    private readonly NumericUpDown unitPrice = new();
    private readonly NumericUpDown discount = new();
    private readonly NumericUpDown paid = new();
    private readonly TextBox customerName = new();
    private readonly DataGridView grid = new();
    private readonly Label totalLabel = new();
    private readonly Label countLabel = new();
    private ProductLookupRow? selectedProduct;
    private long? selectedCustomerId;
    private decimal maxDiscountPercent = 20m;
    private bool saving;
    private int? editingLineIndex;
    private long? resumedSuspendedCartId;
    private Button? addButton;

    public SalesCartForm(string userName)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        RefreshCart();
        InputFocusHelper.Apply(this);
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"🛒 سلة المبيعات — {currentUser}", 1180, 760);
        MinimumSize = new Size(1080, 680);
        KeyPreview = true;
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White, Padding = new Padding(18, 8, 18, 8) };
        var title = UiKit.Title($"🛒 سلة البيع — {currentUser}", 19);
        title.Dock = DockStyle.Fill;
        header.Controls.Add(title);

        var addPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 116, Padding = new Padding(14, 8, 14, 6), BackColor = UiKit.Background, ColumnCount = 8, RowCount = 2 };
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        addPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        addPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        search.PlaceholderText = "امسح الباركود أو اكتب اسم الصنف...";
        search.Dock = DockStyle.Fill;
        search.Font = new Font("Segoe UI", 11F);
        search.TextAlign = HorizontalAlignment.Right;
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SelectProductFromSearch(); } };
        addPanel.Controls.Add(MakeLabel("باركود / بحث عن صنف"), 0, 0);
        addPanel.Controls.Add(search, 0, 1);

        ConfigureNumber(quantity, 1, 100000, 0, 1);
        ConfigureNumber(unitPrice, 0.01m, 100000000m, 2, 0);
        maxDiscountPercent = Math.Min(100m, Math.Max(0m, PricingStore.Load().MaxDiscountPercent));
        ConfigureNumber(discount, 0.5m, 99.5m, 2, 0);
        discount.Maximum = maxDiscountPercent;
        addPanel.Controls.Add(MakeLabel("الكمية"), 1, 0); addPanel.Controls.Add(quantity, 1, 1);
        addPanel.Controls.Add(MakeLabel("سعر الوحدة"), 2, 0); addPanel.Controls.Add(unitPrice, 2, 1);
        addPanel.Controls.Add(MakeLabel("خصم %"), 3, 0); addPanel.Controls.Add(discount, 3, 1);
        addButton = UiKit.Button("＋ إضافة للسلة", UiKit.Success, 110, 40); addButton.Dock = DockStyle.Fill; addButton.Click += (_, _) => AddFromSearch();
        addPanel.Controls.Add(addButton, 4, 1);
        var remove = UiKit.Button("حذف السطر", UiKit.Danger, 106, 40); remove.Dock = DockStyle.Fill; remove.Click += (_, _) => RemoveSelected();
        addPanel.Controls.Add(remove, 5, 1);
        var clear = UiKit.Button("تفريغ السلة", Color.FromArgb(108, 117, 125), 100, 40); clear.Dock = DockStyle.Fill; clear.Click += (_, _) => ClearCart();
        addPanel.Controls.Add(clear, 6, 1);
        var edit = UiKit.Button("تعديل المحدد", Color.FromArgb(0, 123, 167), 116, 40); edit.Dock = DockStyle.Fill; edit.Click += (_, _) => EditSelectedLine();
        addPanel.Controls.Add(edit, 7, 1);

        grid.Dock = DockStyle.Fill;
        UiKit.StyleGrid(grid);
        grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.ReadOnly = true;
        grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ColumnHeadersHeight = 48;
        grid.RowTemplate.Height = 46;
        grid.ScrollBars = ScrollBars.Vertical;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductId", Visible = false });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "الصنف", FillWeight = 40, MinimumWidth = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "سعر الوحدة", FillWeight = 15, MinimumWidth = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "الكمية", FillWeight = 10, MinimumWidth = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Discount", HeaderText = "الخصم", FillWeight = 12, MinimumWidth = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "الإجمالي", FillWeight = 18, MinimumWidth = 130 });
        UiKit.ApplyRightToLeftGridLayout(grid);
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelectedLine(); };
        grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 132, Padding = new Padding(14, 8, 14, 8), BackColor = Color.White, ColumnCount = 8, RowCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        totalLabel.Dock = DockStyle.Fill; totalLabel.TextAlign = ContentAlignment.MiddleRight; totalLabel.Font = new Font("Segoe UI", 16F, FontStyle.Bold); totalLabel.ForeColor = Color.FromArgb(21, 118, 48);
        footer.Controls.Add(totalLabel, 0, 0); footer.SetRowSpan(totalLabel, 2);
        paid.DecimalPlaces = 2; paid.Minimum = 0; paid.Maximum = 100000000; paid.Increment = 1; paid.Dock = DockStyle.Fill; paid.TextAlign = HorizontalAlignment.Center;
        footer.Controls.Add(MakeLabel("المبلغ المستلم"), 1, 0); footer.Controls.Add(paid, 1, 1);
        customerName.Dock = DockStyle.Fill; customerName.PlaceholderText = "اختياري إلا عند وجود دين"; customerName.TextAlign = HorizontalAlignment.Right;
        customerName.TextChanged += (_, _) => selectedCustomerId = null;
        var pickCustomer = UiKit.Button("👤 اختيار عميل", Color.FromArgb(111, 66, 193), 130, 38); pickCustomer.Dock = DockStyle.Fill;
        pickCustomer.Click += (_, _) => PickCustomer();
        var customerHost = new Panel { Dock = DockStyle.Fill };
        customerHost.Controls.Add(customerName); customerHost.Controls.Add(pickCustomer); customerName.Dock = DockStyle.Fill; pickCustomer.Dock = DockStyle.Right; pickCustomer.Width = 125;
        footer.Controls.Add(MakeLabel("العميل"), 2, 0); footer.Controls.Add(customerHost, 2, 1);
        countLabel.Dock = DockStyle.Fill; countLabel.TextAlign = ContentAlignment.MiddleCenter; countLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        footer.Controls.Add(countLabel, 3, 0); footer.SetRowSpan(countLabel, 2);
        var checkout = UiKit.Button("✅ إتمام البيع", UiKit.Success, 155, 48); checkout.Dock = DockStyle.Fill; checkout.Click += (_, _) => Checkout();
        footer.Controls.Add(checkout, 4, 0); footer.SetRowSpan(checkout, 2);
        var quick = UiKit.Button("بيع سريع", Color.FromArgb(58, 84, 112), 85, 40); quick.Dock = DockStyle.Fill;
        quick.Click += (_, _) => { using var f = new SalesForm(currentUser); f.ShowDialog(this); };
        footer.Controls.Add(quick, 5, 0); footer.SetRowSpan(quick, 2);
        var suspend = UiKit.Button("تعليق السلة", Color.FromArgb(108, 117, 125), 105, 40); suspend.Dock = DockStyle.Fill;
        suspend.Click += (_, _) => SuspendCurrentCart();
        footer.Controls.Add(suspend, 6, 0); footer.SetRowSpan(suspend, 2);
        var resume = UiKit.Button("استرجاع معلّقة", Color.FromArgb(0, 123, 167), 115, 40); resume.Dock = DockStyle.Fill;
        resume.Click += (_, _) => ResumeSuspendedCart();
        footer.Controls.Add(resume, 7, 0); footer.SetRowSpan(resume, 2);

        var quickPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(12, 7, 12, 5),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            RightToLeft = RightToLeft.Yes,
            BackColor = Color.White
        };
        quickPanel.Controls.Add(new Label
        {
            Text = "الأكثر مبيعًا:", Width = 112, Height = 46,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = UiKit.Text
        });
        foreach (var product in BusinessManagementStore.LoadQuickProducts(6))
        {
            var quickProduct = UiKit.Button($"{product.Name}\n{product.SalePrice:N2} ج.م", Color.FromArgb(232, 242, 248), 142, 46);
            quickProduct.ForeColor = UiKit.Text;
            quickProduct.FlatAppearance.BorderSize = 1;
            quickProduct.FlatAppearance.BorderColor = Color.FromArgb(205, 220, 230);
            quickProduct.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            quickProduct.Click += (_, _) =>
            {
                selectedProduct = product;
                quantity.Value = 1;
                unitPrice.Value = 0;
                discount.Value = 0;
                AddSelectedProduct();
            };
            quickPanel.Controls.Add(quickProduct);
        }

        Controls.Add(grid);
        Controls.Add(footer);
        Controls.Add(quickPanel);
        Controls.Add(addPanel);
        Controls.Add(header);
        AcceptButton = addButton;
    }

    private static Label MakeLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = UiKit.Text };

    private static void ConfigureNumber(NumericUpDown n, decimal increment, decimal maximum, int decimals, decimal initial)
    {
        n.Minimum = 0; n.Maximum = maximum; n.DecimalPlaces = decimals; n.Increment = increment; n.Value = initial;
        n.Dock = DockStyle.Fill; n.TextAlign = HorizontalAlignment.Center;
    }

    private void SelectProductFromSearch()
    {
        string term = search.Text.Trim();
        if (term.Length == 0) { search.Focus(); return; }
        try
        {
            var matches = BusinessManagementStore.LoadProducts(term);
            ProductLookupRow? chosen = matches.FirstOrDefault(x => string.Equals(x.Barcode, term, StringComparison.OrdinalIgnoreCase));
            if (chosen == null && matches.Count == 1) chosen = matches[0];
            if (chosen == null && matches.Count > 1)
            {
                using var picker = new ProductChoiceForm(matches);
                if (picker.ShowDialog(this) == DialogResult.OK) chosen = picker.SelectedProduct;
            }
            if (chosen == null)
            {
                MessageBox.Show(this, "لم يتم العثور على صنف مطابق. تأكد من الباركود أو جرّب جزءًا من الاسم.", "البحث عن صنف", MessageBoxButtons.OK, MessageBoxIcon.Information);
                search.Focus(); search.SelectAll(); return;
            }
            if (chosen.Quantity <= 0)
            {
                MessageBox.Show(this, "الصنف غير متاح حاليًا؛ رصيد المخزن صفر.", "المخزون", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                selectedProduct = null; return;
            }
            selectedProduct = chosen;
            unitPrice.Value = Math.Min(unitPrice.Maximum, Math.Max(unitPrice.Minimum, chosen.SalePrice));
            // عند مسح الباركود يضاف الصنف فورًا، أما البحث بالاسم فيسمح بتعديل السعر والكمية قبل الإضافة.
            if (!string.IsNullOrWhiteSpace(chosen.Barcode) && string.Equals(chosen.Barcode, term, StringComparison.OrdinalIgnoreCase))
                AddSelectedProduct();
            else
                quantity.Focus();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "تعذر البحث عن الصنف", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void AddFromSearch()
    {
        if (selectedProduct == null)
        {
            SelectProductFromSearch();
            if (selectedProduct == null) return;
        }
        AddSelectedProduct();
    }

    private void AddSelectedProduct()
    {
        if (selectedProduct == null) { MessageBox.Show(this, "ابحث عن الصنف أولًا.", "سلة المبيعات", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (AddLine(selectedProduct)) { search.Clear(); search.Focus(); }
    }

    private bool AddLine(ProductLookupRow product)
    {
        RefreshDiscountLimit();
        int q = Math.Max(1, Convert.ToInt32(quantity.Value, CultureInfo.InvariantCulture));
        int reservedElsewhere = lines.Where((x, i) => x.ProductId == product.Id && (!editingLineIndex.HasValue || i != editingLineIndex.Value)).Sum(x => x.Quantity);
        int available = Math.Max(0, product.Quantity - reservedElsewhere);
        if (q > available)
        {
            MessageBox.Show(this, $"المتاح من {product.Name}: {available} فقط.", "الكمية", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        decimal price = unitPrice.Value > 0 ? unitPrice.Value : product.SalePrice;
        decimal pct = discount.Value;
        if (price <= 0)
        {
            MessageBox.Show(this, "سعر الوحدة يجب أن يكون أكبر من صفر.", "السعر", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (pct > maxDiscountPercent)
        {
            MessageBox.Show(this, $"أقصى خصم مسموح من إعدادات الإدارة هو {maxDiscountPercent:0.##}%.", "حد الخصم", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (Math.Round(price * q * (1m - pct / 100m), 2, MidpointRounding.AwayFromZero) <= 0m)
        {
            MessageBox.Show(this, "قيمة السطر بعد الخصم يجب أن تكون أكبر من صفر.", "السعر والخصم", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var updated = new StockCartLine { ProductId = product.Id, Name = product.Name, Quantity = q, UnitPrice = price, DiscountPercent = pct };
        if (editingLineIndex.HasValue)
        {
            int index = editingLineIndex.Value;
            if (index < 0 || index >= lines.Count) { editingLineIndex = null; return false; }
            var duplicate = lines.Where((x, i) => i != index).FirstOrDefault(x => x.ProductId == product.Id && x.UnitPrice == price && x.DiscountPercent == pct);
            if (duplicate != null) { duplicate.Quantity += q; lines.RemoveAt(index); }
            else lines[index] = updated;
            editingLineIndex = null;
        }
        else
        {
            var existing = lines.FirstOrDefault(x => x.ProductId == product.Id && x.UnitPrice == price && x.DiscountPercent == pct);
            if (existing != null) existing.Quantity += q;
            else lines.Add(updated);
        }
        quantity.Value = 1; discount.Value = 0; unitPrice.Value = 0; selectedProduct = null;
        if (addButton != null) addButton.Text = "＋ إضافة للسلة";
        RefreshCart();
        return true;
    }

    private void EditSelectedLine()
    {
        if (grid.CurrentRow == null) return;
        int index = grid.CurrentRow.Index;
        if (index < 0 || index >= lines.Count) return;
        var line = lines[index];
        var product = BusinessManagementStore.GetProductById(line.ProductId);
        if (product == null)
        {
            MessageBox.Show(this, $"الصنف «{line.Name}» لم يعد موجودًا في المخزن. احذف السطر من السلة للمتابعة.", "الصنف غير موجود", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        editingLineIndex = index;
        selectedProduct = product;
        search.Text = string.IsNullOrWhiteSpace(product.Barcode) ? product.Name : product.Barcode;
        quantity.Value = Math.Min(quantity.Maximum, Math.Max(quantity.Minimum, line.Quantity));
        unitPrice.Value = Math.Min(unitPrice.Maximum, Math.Max(unitPrice.Minimum, line.UnitPrice));
        discount.Value = Math.Min(discount.Maximum, Math.Max(discount.Minimum, line.DiscountPercent));
        if (addButton != null) addButton.Text = "حفظ التعديل";
        quantity.Focus();
    }

    private void RefreshDiscountLimit()
    {
        maxDiscountPercent = Math.Min(100m, Math.Max(0m, PricingStore.Load().MaxDiscountPercent));
        if (discount.Value > maxDiscountPercent) discount.Value = maxDiscountPercent;
        discount.Maximum = maxDiscountPercent;
    }

    private void RemoveSelected()
    {
        if (grid.CurrentRow == null) return;
        int index = grid.CurrentRow.Index;
        if (index < 0 || index >= lines.Count) return;
        lines.RemoveAt(index);
        if (editingLineIndex == index) CancelLineEdit();
        else if (editingLineIndex.HasValue && editingLineIndex.Value > index) editingLineIndex--;
        RefreshCart();
    }

    private void CancelLineEdit()
    {
        editingLineIndex = null;
        selectedProduct = null;
        quantity.Value = 1; unitPrice.Value = 0; discount.Value = 0;
        if (addButton != null) addButton.Text = "＋ إضافة للسلة";
    }

    private void ClearCart()
    {
        if (lines.Count == 0) return;
        if (MessageBox.Show(this, "تفريغ جميع الأصناف من السلة؟", "تأكيد", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        lines.Clear(); paid.Value = 0; resumedSuspendedCartId = null; CancelLineEdit(); RefreshCart();
    }

    private void SuspendCurrentCart()
    {
        if (lines.Count == 0) { MessageBox.Show(this, "السلة فارغة ولا يوجد ما يمكن تعليقه.", "تعليق السلة", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            long id = BusinessManagementStore.SaveSuspendedCart(currentUser, selectedCustomerId, customerName.Text, lines, resumedSuspendedCartId);
            lines.Clear(); customerName.Clear(); selectedCustomerId = null; resumedSuspendedCartId = null; paid.Value = 0; CancelLineEdit(); RefreshCart();
            MessageBox.Show(this, $"تم حفظ السلة المعلّقة بنجاح. رقمها: #{id}\nلم يتم خصم أي مخزون أو تسجيل أي حركة خزينة.", "تعليق السلة", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر تعليق سلة المبيعات", ex);
            MessageBox.Show(this, "لم يتم تعليق السلة. لم تتغير بيانات البيع أو المخزون.\n\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResumeSuspendedCart()
    {
        if (lines.Count > 0)
        {
            MessageBox.Show(this, "السلة الحالية تحتوي على أصناف. علّقها أو أتمم بيعها أولًا قبل استرجاع سلة أخرى.", "استرجاع سلة", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var picker = new SuspendedSalePickerForm(currentUser);
        if (picker.ShowDialog(this) != DialogResult.OK || !picker.SelectedId.HasValue) return;
        try
        {
            var draft = BusinessManagementStore.LoadSuspendedCart(picker.SelectedId.Value);
            if (draft == null || draft.Lines.Count == 0) throw new InvalidOperationException("السلة المعلّقة غير موجودة أو لا تحتوي على أصناف.");
            var restored = new List<StockCartLine>();
            var missingProducts = new List<string>();
            var stockWarnings = new List<string>();
            var currentProducts = new Dictionary<long, ProductLookupRow>();
            foreach (var line in draft.Lines)
            {
                if (!currentProducts.TryGetValue(line.ProductId, out var product))
                {
                    product = BusinessManagementStore.GetProductById(line.ProductId);
                    if (product == null) { missingProducts.Add(line.Name + " (تم حذفه)"); continue; }
                    currentProducts[line.ProductId] = product;
                }
                restored.Add(new StockCartLine { ProductId = line.ProductId, Name = product.Name, Quantity = line.Quantity, UnitPrice = line.UnitPrice, DiscountPercent = line.DiscountPercent });
            }
            foreach (var group in restored.GroupBy(x => x.ProductId))
            {
                var product = currentProducts[group.Key];
                int requested = group.Sum(x => x.Quantity);
                if (requested > product.Quantity)
                    stockWarnings.Add(product.Name + $" (المطلوب {requested}، والمتاح حاليًا {product.Quantity})");
            }
            if (missingProducts.Count > 0)
            {
                MessageBox.Show(this, "تحتوي السلة المعلّقة على أصناف لم تعد موجودة:\n\n" + string.Join("\n", missingProducts) + "\n\nلم يتم استرجاعها أو حذفها. راجع المخزن أو احذف السلة يدويًا بعد التأكد.", "تعذر الاسترجاع الكامل", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (stockWarnings.Count > 0)
            {
                MessageBox.Show(this, "المخزون تغير منذ تعليق السلة:\n\n" + string.Join("\n", stockWarnings) + "\n\nيمكنك تقليل الكميات قبل إتمام البيع؛ لن يتم تسجيل كميات غير متاحة.", "مراجعة المخزون", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (restored.Count == 0) return;
            lines.AddRange(restored);
            customerName.Text = draft.CustomerName;
            selectedCustomerId = draft.CustomerId;
            // لا نحذف المسودة الأصلية الآن؛ تظل محفوظة حتى نجاح البيع أو استبدالها بسلة معلّقة أحدث.
            resumedSuspendedCartId = draft.Id;
            paid.Value = 0; CancelLineEdit(); RefreshCart();
            MessageBox.Show(this, $"تم استرجاع السلة #{draft.Id}. راجع الأسعار والكميات ثم أتمم البيع.", "استرجاع السلة", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر استرجاع سلة معلّقة", ex);
            MessageBox.Show(this, "تعذر استرجاع السلة.\n\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!e.Cancel && lines.Count > 0 && e.CloseReason == CloseReason.UserClosing)
        {
            var choice = MessageBox.Show(this, "لا تزال هناك أصناف في سلة البيع.\n\nنعم: حفظها كسلة معلّقة وإغلاق النافذة.\nلا: إغلاق النافذة دون حفظ السلة.\nإلغاء: الرجوع للبيع.", "سلة بيع غير مكتملة", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (choice == DialogResult.Cancel) { e.Cancel = true; return; }
            if (choice == DialogResult.Yes)
            {
                try { BusinessManagementStore.SaveSuspendedCart(currentUser, selectedCustomerId, customerName.Text, lines, resumedSuspendedCartId); }
                catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, "تعذر حفظ السلة، لذلك لم يتم إغلاق النافذة.\n\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            }
        }
        base.OnFormClosing(e);
    }

    private void RefreshCart()
    {
        grid.Rows.Clear();
        foreach (var line in lines)
            grid.Rows.Add(line.ProductId, line.Name, line.UnitPrice.ToString("N2"), line.Quantity,
                line.DiscountPercent.ToString("0.##") + "%", line.Total.ToString("N2"));
        decimal total = lines.Sum(x => x.Total);
        totalLabel.Text = $"الإجمالي المطلوب:\n{total:N2} ج.م";
        countLabel.Text = $"عدد الأصناف: {lines.Count}\nالوحدات: {lines.Sum(x => x.Quantity)}";
        paid.Maximum = Math.Max(100000000, total + 1000000m);
    }

    private void PickCustomer()
    {
        using var picker = new CustomerPickerForm(customerName.Text.Trim());
        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            customerName.Text = picker.SelectedName;
            selectedCustomerId = picker.CustomerId;
        }
    }

    private void Checkout()
    {
        if (saving) return;
        if (lines.Count == 0) { MessageBox.Show(this, "السلة فارغة. أضف صنفًا قبل إتمام البيع.", "المبيعات", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        saving = true;
        try
        {
            decimal total = lines.Sum(x => x.Total);
            decimal received = paid.Value;
            if (received < 0) throw new InvalidOperationException("المبلغ المستلم غير صحيح.");
            decimal debt = Math.Max(0m, total - received);
            long? customerId = selectedCustomerId;
            if (debt > 0 && !customerId.HasValue)
            {
                using var picker = new CustomerPickerForm(customerName.Text.Trim());
                if (picker.ShowDialog(this) != DialogResult.OK || !picker.CustomerId.HasValue)
                    return;
                customerId = picker.CustomerId.Value;
                customerName.Text = picker.SelectedName;
                selectedCustomerId = customerId;
            }

            using var c = AppDatabase.OpenConnection();
            using var tx = c.BeginTransaction();
            decimal paymentLeft = Math.Min(total, received);
            long lastTransactionId = 0;
            foreach (var line in lines)
            {
                decimal linePayment = Math.Min(paymentLeft, line.Total);
                decimal lineDiscounted = line.Total;
                // احتفظ بالمعرّف الذي أعادته خدمة البيع نفسها؛ فبعدها تُضاف
                // حركات خزينة ومخزون وسجل تدقيق، وبالتالي last_insert_rowid()
                // لن يظل معرّف جدول Transactions.
                lastTransactionId = SalesService.CompleteSaleWithinTransaction(c, tx, currentUser, line.Name,
                    line.OriginalTotal, line.DiscountPercent, lineDiscounted, linePayment,
                    customerId, line.ProductId, out _, out _, line.Quantity);
                paymentLeft -= linePayment;
            }
            decimal change = Math.Max(0m, received - total);
            if (change > 0)
                AppDatabase.AddTreasury(c, currentUser, "OUT", "باقي مبيعات", change,
                    "الباقي المُعاد للعميل من سلة البيع", "CartSaleChange", lastTransactionId, tx, "SALES");
            AppDatabase.AddAuditLog(c, tx, currentUser, "إتمام", "سلة مبيعات", lastTransactionId,
                $"عدد الأصناف: {lines.Count} | الإجمالي: {total:F2} | المستلم: {received:F2} | الدين: {debt:F2} | الباقي: {change:F2}");
            if (resumedSuspendedCartId.HasValue)
                BusinessManagementStore.DeleteSuspendedCart(c, tx, currentUser, resumedSuspendedCartId.Value, "إتمام البيع من سلة معلّقة");
            tx.Commit();
            resumedSuspendedCartId = null;

            string result = debt > 0
                ? $"تم تسجيل البيع بنجاح.\nالإجمالي: {total:N2} ج.م\nالمدفوع: {Math.Min(received, total):N2} ج.م\nالدين: {debt:N2} ج.م"
                : change > 0
                    ? $"تم البيع بنجاح.\nالإجمالي: {total:N2} ج.م\nالباقي للعميل: {change:N2} ج.م"
                    : $"تم البيع بنجاح.\nالإجمالي: {total:N2} ج.م";
            MessageBox.Show(this, result, "اكتملت عملية البيع", MessageBoxButtons.OK, MessageBoxIcon.Information);
            lines.Clear(); customerName.Clear(); selectedCustomerId = null; paid.Value = 0; RefreshCart(); search.Focus();
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل إتمام سلة المبيعات", ex);
            MessageBox.Show(this, "لم يتم حفظ البيع. تم التراجع عن العملية بالكامل.\n\n" + ex.Message, "خطأ في البيع", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { saving = false; }
    }
}

internal sealed class SuspendedSalePickerForm : Form
{
    private readonly string userName;
    private readonly ListBox list = new();
    public long? SelectedId { get; private set; }

    public SuspendedSalePickerForm(string user)
    {
        userName = user;
        UiKit.PrepareForm(this, "استرجاع سلة معلّقة", 720, 520);
        MinimumSize = new Size(560, 380);
        list.Dock = DockStyle.Fill; list.Font = new Font("Segoe UI", 11F); list.IntegralHeight = false;
        list.DoubleClick += (_, _) => Choose();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var choose = UiKit.Button("استرجاع المحددة", UiKit.Success, 145, 42); choose.Click += (_, _) => Choose();
        var delete = UiKit.Button("حذف المعلّقة", UiKit.Danger, 125, 42); delete.Click += (_, _) => DeleteSelected();
        var cancel = UiKit.Button("إلغاء", Color.FromArgb(108, 117, 125), 100, 42); cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        actions.Controls.Add(choose); actions.Controls.Add(delete); actions.Controls.Add(cancel);
        Controls.Add(list); Controls.Add(actions); AcceptButton = choose;
        LoadItems();
    }

    private void LoadItems()
    {
        list.Items.Clear();
        foreach (var item in BusinessManagementStore.LoadSuspendedCarts(userName)) list.Items.Add(item);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    private void Choose()
    {
        if (list.SelectedItem is not SuspendedSaleInfo item)
        { MessageBox.Show(this, "اختر سلة معلّقة أولًا.", "استرجاع", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        SelectedId = item.Id; DialogResult = DialogResult.OK; Close();
    }

    private void DeleteSelected()
    {
        if (list.SelectedItem is not SuspendedSaleInfo item) return;
        if (MessageBox.Show(this, $"حذف السلة المعلّقة رقم #{item.Id} نهائيًا؟", "تأكيد الحذف", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        BusinessManagementStore.DeleteSuspendedCart(userName, item.Id, "حذف");
        LoadItems();
    }
}

internal sealed class ProductChoiceForm : Form
{
    private readonly List<ProductLookupRow> options;
    private readonly ListBox list = new();
    public ProductLookupRow? SelectedProduct { get; private set; }

    public ProductChoiceForm(List<ProductLookupRow> rows)
    {
        options = rows;
        UiKit.PrepareForm(this, "اختيار الصنف", 650, 520);
        list.Dock = DockStyle.Fill; list.Font = new Font("Segoe UI", 11F); list.IntegralHeight = false;
        foreach (var row in options) list.Items.Add(row);
        list.DoubleClick += (_, _) => Choose();
        var ok = UiKit.Button("اختيار", UiKit.Success, 130, 42); ok.Dock = DockStyle.Bottom; ok.Click += (_, _) => Choose();
        Controls.Add(list); Controls.Add(ok); AcceptButton = ok;
    }

    private void Choose()
    {
        if (list.SelectedIndex < 0) { MessageBox.Show(this, "اختر صنفًا أولًا.", "اختيار", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        SelectedProduct = options[list.SelectedIndex]; DialogResult = DialogResult.OK; Close();
    }
}
