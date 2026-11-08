using System;
using System.Drawing;
using System.Windows.Forms;

namespace LibraryPrintApp;

internal sealed class DashboardForm : Form
{
    private readonly string currentUser;
    private Label drawerBalanceLabel = null!;
    private Label treasuryBalanceLabel = null!;
    private Button drawerEyeButton = null!;
    private Button treasuryEyeButton = null!;
    private readonly ToolTip treasuryBalanceHelp = new();
    private decimal drawerBalanceValue;
    private decimal treasuryBalanceValue;
    private bool drawerBalanceVisible;
    private bool treasuryBalanceVisible;

    public DashboardForm(string userName)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        UpdateTreasury();
        StartLogoutWatcher();

        // إضافة المتصفح: لو الطباعة مش مفتوحة وماحدش شغال في شاشة تانية، افتحها.
        PrintBridge.OpenPrintingHandler = () =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                foreach (Form f in Application.OpenForms)
                    if (!ReferenceEquals(f, this) && f.Visible) return;
                OpenPrinting();
            }));
        };
        FormClosed += (_, _) => PrintBridge.OpenPrintingHandler = null;

        // تنبيهات بداية التشغيل (نواقص المخزن + حالة النسخ الاحتياطي) مرة واحدة بعد ظهور اللوحة
        Shown += (_, _) => BeginInvoke(new Action(() =>
        {
            StartupNotices.Show(this, OpenInventory);
            PrintBridge.Resume();   // ملفات جاية من كليك يمين وقت ما كنت مسجّل خروج
        }));
    }

    // ------------------------------------------------------------
    // تسجيل الخروج: أي شاشة (الطباعة، الإدارة، ...) تطلب AppSession.RequestLogout()،
    // فالتايمر ده بيقفل الشاشات المفتوحة واحدة واحدة، وبعدين يقفل اللوحة نفسها،
    // فيرجع Program لشاشة تسجيل الدخول.
    // ------------------------------------------------------------
    private System.Windows.Forms.Timer? logoutTimer;

    private void StartLogoutWatcher()
    {
        logoutTimer = new System.Windows.Forms.Timer { Interval = 150 };
        logoutTimer.Tick += (_, _) =>
        {
            if (!AppSession.LogoutRequested) return;

            Form? top = null;
            foreach (Form open in Application.OpenForms)
                if (!ReferenceEquals(open, this) && open.Visible && !open.IsDisposed)
                    top = open;

            if (top != null)
            {
                try { top.Close(); } catch { }
                return; // لو الشاشة رفضت القفل (مثلًا طباعة شغالة) هنحاول تاني
            }

            logoutTimer!.Stop();
            Close();
        };
        logoutTimer.Start();
        FormClosed += (_, _) => logoutTimer?.Dispose();
    }

    // تأكيد قبل قفل البرنامج (X أو Alt+F4 أو Esc). تسجيل الخروج وإغلاق ويندوز بدون سؤال.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // شاشة الطباعة ممكن تكون مصغّرة وفيه ملفات لسه بتتطبع/بتتسجل: منقفلش البرنامج عليها.
        if (e.CloseReason == CloseReason.UserClosing && AppSession.PrintingInProgress)
        {
            MessageBox.Show(
                this,
                "فيه طباعة لسه شغالة (شاشة الطباعة ممكن تكون مصغّرة). استنى لحد ما تخلص قبل ما تقفل البرنامج.",
                "الطباعة",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            e.Cancel = true;
            return;
        }

        if (!AppSession.LogoutRequested && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(
                this,
                "متأكد إنك عايز تقفل البرنامج؟",
                "تأكيد قفل البرنامج",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnFormClosing(e);
    }

    private static readonly Color PageBack = Color.FromArgb(22, 27, 36);

    // يهدّي اللون شوية (يمزجه بنسبة بسيطة مع لون الخلفية) عشان الشاشة تبقى مريحة للعين.
    private static Color Soft(Color c)
        => Color.FromArgb(
            (int)(c.R * 0.9 + PageBack.R * 0.1),
            (int)(c.G * 0.9 + PageBack.G * 0.1),
            (int)(c.B * 0.9 + PageBack.B * 0.1));

    private const int CornerRadius = 10;

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"🏪 مكتبة الشريف | المستخدم: {currentUser}", 1280, 760);
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1050, 700);
        UiKit.EnableEscape(this);
        KeyPreview = true;
        KeyDown += DashboardForm_KeyDown;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18),
            BackColor = PageBack
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(18, 8, 18, 8),
            Margin = new Padding(0, 0, 0, 12)
        };
        UiKit.RoundCorners(header, CornerRadius);

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"🏪 مكتبة الشريف  •  لوحة التحكم",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false
        };
        header.Controls.Add(title);
        root.Controls.Add(header, 0, 0);

        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            Padding = new Padding(0),
            BackColor = PageBack
        };
        for (int i = 0; i < 4; i++)
            area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 32));

        // الصف الأول: الخدمات الأساسية
        AddCard(area, "🖨️ الطباعة والنسخ", "الخدمة + سجل الطباعة والتقرير", Color.FromArgb(37, 125, 190), 0, 0, OpenPrinting);
        AddCard(area, "💸 السحب والإيداع", "Vodafone Cash / Fawry / InstaPay", Color.FromArgb(63, 155, 63), 1, 0, OpenCash);
        AddCard(area, "🛒 المبيعات", "سلة بيع متعددة الأصناف", Color.FromArgb(235, 126, 34), 2, 0, OpenSales);
        AddCard(area, "📦 المخزن", "الأصناف والباركود", Color.FromArgb(0, 105, 105), 3, 0, OpenInventory);

        // الصف الثاني: الإدارة والحسابات
        AddCard(area, "👥 العملاء والديون", "كشف الحساب والدفعات", Color.FromArgb(111, 66, 193), 0, 1, OpenCustomers);
        AddCard(area, "💰 الخزينة", "الأرصدة والحركة", Color.FromArgb(30, 132, 73), 1, 1, OpenTreasury);
        AddCard(area, "📟 ماكينة باب رزق", "BEE — الرصيد والعمليات", Color.FromArgb(225, 93, 0), 2, 1, OpenBeeMachine);
        AddCard(area, "📲 ماكينة OPay", "الرصيد والعمليات", Color.FromArgb(17, 169, 159), 3, 1, OpenOPayMachine);

        // الصف الأخير: رصيد الدرج الفعلي وإجمالي الخزينة في مستطيلين متراصين.
        // الأرصدة مخفية تلقائيًا ولا تظهر إلا بعد الضغط على زر العين لكل رصيد.
        AddBalanceCards(area, 0, 2);
        treasuryBalanceHelp.SetToolTip(drawerBalanceLabel, "رصيد الدرج المحسوب من حركات البيع والطباعة/التصوير والنقدية والتحصيلات المسجلة، مع استبعاد رصيد المحافظ الإلكترونية والماكينات.");
        treasuryBalanceHelp.SetToolTip(treasuryBalanceLabel, "إجمالي الأموال المسجلة = رصيد الدرج المحسوب + الأرصدة الحالية للمحافظ الإلكترونية + جميع الماكينات.");
        treasuryBalanceHelp.SetToolTip(drawerEyeButton, "إظهار/إخفاء رصيد الدرج الفعلي");
        treasuryBalanceHelp.SetToolTip(treasuryEyeButton, "إظهار/إخفاء إجمالي رصيد الخزينة");
        AddFooterButton(area, "↻ تحديث الخزينة", Color.FromArgb(58, 84, 112), 1, 2, 1, UpdateTreasury);
        AddManagementAndAdminButtons(area, 2, 2);

        var actionsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(9),
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0)
        };
        actionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        actionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        actionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33));

        var invoice = UiKit.Button("🧾 إصدار فاتورة", Color.FromArgb(0, 121, 107), 10, 10);
        invoice.Dock = DockStyle.Fill;
        invoice.Margin = new Padding(0, 0, 0, 4);
        UiKit.RoundCorners(invoice, CornerRadius);
        invoice.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
        invoice.Click += (s, e) => OpenInvoice();
        actionsPanel.Controls.Add(invoice, 0, 0);

        var logout = UiKit.Button("🚪 تسجيل الخروج", Color.FromArgb(190, 30, 30), 10, 10);
        logout.Dock = DockStyle.Fill;
        logout.Margin = new Padding(0, 4, 0, 4);
        UiKit.RoundCorners(logout, CornerRadius);
        logout.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
        logout.Click += (s, e) =>
        {
            if (MessageBox.Show(this, $"تسجيل الخروج من {currentUser}؟", "تأكيد", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                AppSession.RequestLogout();
        };
        actionsPanel.Controls.Add(logout, 0, 1);

        var closeDay = UiKit.Button("🔒 اعتماد وإغلاق اليومية", Color.FromArgb(192, 57, 43), 10, 10);
        closeDay.Dock = DockStyle.Fill;
        closeDay.Margin = new Padding(0, 4, 0, 0);
        UiKit.RoundCorners(closeDay, CornerRadius);
        closeDay.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
        closeDay.Click += (s, e) => OpenDailyClosing();
        actionsPanel.Controls.Add(closeDay, 0, 2);
        area.Controls.Add(actionsPanel, 3, 2);

        root.Controls.Add(area, 0, 1);
        Controls.Add(root);

        UpdateTreasury();
    }

    private static void AddCard(TableLayoutPanel parent, string title, string sub, Color color, int col, int row, Action action, int colSpan = 1)
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(9),
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };

        var b = UiKit.Button($"{title}\n{sub}", Soft(color), 10, 10);
        b.Dock = DockStyle.Fill;
        b.Margin = new Padding(0);
        b.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        b.TextAlign = ContentAlignment.MiddleCenter;
        UiKit.RoundCorners(b, CornerRadius);
        b.Click += (s, e) => action();
        p.Controls.Add(b);
        parent.Controls.Add(p, col, row);
        parent.SetColumnSpan(p, colSpan);
    }

    private void AddBalanceCards(TableLayoutPanel parent, int col, int row)
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(9),
            Padding = new Padding(0),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var drawerRow = CreateBalanceRow("💵 رصيد الدرج الفعلي", Color.FromArgb(35, 93, 68), out drawerBalanceLabel, out drawerEyeButton);
        var treasuryRow = CreateBalanceRow("💰 رصيد الخزينة الكلي", Color.FromArgb(29, 78, 105), out treasuryBalanceLabel, out treasuryEyeButton);
        stack.Controls.Add(drawerRow, 0, 0);
        stack.Controls.Add(treasuryRow, 0, 1);
        parent.Controls.Add(stack, col, row);
        parent.SetColumnSpan(stack, 1);

        drawerEyeButton.Click += (s, e) =>
        {
            drawerBalanceVisible = !drawerBalanceVisible;
            RefreshBalanceCards();
        };
        treasuryEyeButton.Click += (s, e) =>
        {
            treasuryBalanceVisible = !treasuryBalanceVisible;
            RefreshBalanceCards();
        };
        RefreshBalanceCards();
    }

    private static Panel CreateBalanceRow(string title, Color accent, out Label valueLabel, out Button eyeButton)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 3, 2, 3),
            Padding = new Padding(3),
            BackColor = accent
        };
        UiKit.RoundCorners(panel, CornerRadius);
        valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = title + "\n•••••• ج.م",
            ForeColor = Color.White,
            BackColor = accent,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(2, 0, 36, 0),
            AutoEllipsis = true
        };
        eyeButton = UiKit.Button("👁", Color.FromArgb(52, 73, 94), 36, 36);
        eyeButton.Dock = DockStyle.Left;
        eyeButton.Width = 36;
        eyeButton.MinimumSize = new Size(36, 30);
        eyeButton.Margin = new Padding(0);
        eyeButton.Font = new Font("Segoe UI Emoji", 12F, FontStyle.Regular);
        eyeButton.AccessibleName = "إظهار الرصيد";
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(eyeButton);
        return panel;
    }

    private void RefreshBalanceCards()
    {
        if (drawerBalanceLabel == null || treasuryBalanceLabel == null) return;
        drawerBalanceLabel.Text = $"💵 رصيد الدرج الفعلي\n{(drawerBalanceVisible ? drawerBalanceValue.ToString("N2") + " ج.م" : "•••••• ج.م")}";
        treasuryBalanceLabel.Text = $"💰 رصيد الخزينة الكلي\n{(treasuryBalanceVisible ? treasuryBalanceValue.ToString("N2") + " ج.م" : "•••••• ج.م")}";
        drawerEyeButton.Text = drawerBalanceVisible ? "🙈" : "👁";
        treasuryEyeButton.Text = treasuryBalanceVisible ? "🙈" : "👁";
        drawerEyeButton.AccessibleName = drawerBalanceVisible ? "إخفاء رصيد الدرج" : "إظهار رصيد الدرج";
        treasuryEyeButton.AccessibleName = treasuryBalanceVisible ? "إخفاء رصيد الخزينة" : "إظهار رصيد الخزينة";
    }

    private static void AddSmallFooterButton(TableLayoutPanel parent, string text, Color color, int col, int row, int colSpan, Action action)
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(7, 5, 7, 5),
            BackColor = Color.Transparent
        };
        var b = UiKit.Button(text, color, 10, 10);
        b.Dock = DockStyle.Top;
        b.Height = 38;
        b.Margin = new Padding(0);
        b.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        b.Click += (s, e) => action();
        p.Controls.Add(b);
        parent.Controls.Add(p, col, row);
        parent.SetColumnSpan(p, colSpan);
    }

    private static void AddCompactFooterButton(TableLayoutPanel parent, string text, Color color, int col, int row, int colSpan, Action action)
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(7, 5, 7, 5),
            BackColor = Color.Transparent
        };
        var b = UiKit.Button(text, color, 8, 8);
        b.Dock = DockStyle.Top;
        b.Height = 44;
        b.Margin = new Padding(0);
        b.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        b.Click += (s, e) => action();
        p.Controls.Add(b);
        parent.Controls.Add(p, col, row);
        parent.SetColumnSpan(p, colSpan);
    }

    private static void AddFooterButton(TableLayoutPanel parent, string text, Color color, int col, int row, int colSpan, Action action)
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(9),
            BackColor = Color.Transparent
        };
        var b = UiKit.Button(text, Soft(color), 10, 10);
        b.Dock = DockStyle.Fill;
        b.Margin = new Padding(0);
        b.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        UiKit.RoundCorners(b, CornerRadius);
        b.Click += (s, e) => action();
        p.Controls.Add(b);
        parent.Controls.Add(p, col, row);
        parent.SetColumnSpan(p, colSpan);
    }

    private void UpdateTreasury()
    {
        try
        {
            using var c = AppDatabase.OpenConnection();
            drawerBalanceValue = AppDatabase.GetActualDrawerBalance(c);
            treasuryBalanceValue = AppDatabase.GetTreasuryBalance(c);
            RefreshBalanceCards();
        }
        catch
        {
            drawerBalanceLabel.Text = "💵 رصيد الدرج الفعلي\nتعذر قراءة الرصيد";
            treasuryBalanceLabel.Text = "💰 رصيد الخزينة الكلي\nتعذر قراءة الرصيد";
        }
    }

    private void DashboardForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // اختصارات لوحة التحكم قابلة للتغيير من «إعدادات الإدارة ← الاختصارات».
        // الافتراضي: أزرار الأرقام الجانبية (يلزم تفعيل NumLock). F1 بيعرض قائمة الاختصارات
        // (بيتعامل معاها UiKit.EnableHelpKey).
        (string Id, Action Run)[] actions =
        {
            ("dash.refresh", UpdateTreasury),
            ("dash.print", OpenPrinting),
            ("dash.cash", OpenCash),
            ("dash.sales", OpenSales),
            ("dash.inventory", OpenInventory),
            ("dash.customers", OpenCustomers),
            ("dash.treasury", OpenTreasury),
            ("dash.bee", OpenBeeMachine),
            ("dash.opay", OpenOPayMachine),
            ("dash.admin", OpenAdminSettings),
            ("dash.management", OpenBusinessManagement),
            ("dash.invoice", OpenInvoice),
            ("dash.closing", OpenDailyClosing),
        };

        foreach (var (id, run) in actions)
        {
            if (!Shortcuts.Is(id, e)) continue;

            e.Handled = true;
            e.SuppressKeyPress = true;
            run();
            return;
        }
    }

    // شاشة الطباعة بتتفتح كنافذة مستقلة (مش مقفولة): تقدر تصغّرها وتكمل في باقي الخدمات
    // والطباعة شغالة. لو مفتوحة بالفعل بنرجّعها قدام بدل ما نفتح نسخة تانية.
    private Form1? printingForm;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private void OpenPrinting()
    {
        if (printingForm != null && !printingForm.IsDisposed)
        {
            if (printingForm.WindowState == FormWindowState.Minimized)
                ShowWindow(printingForm.Handle, 9); // SW_RESTORE: يرجع لحجمه السابق
            printingForm.Activate();
            return;
        }

        var f = new Form1(currentUser);
        printingForm = f;
        f.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(printingForm, f)) printingForm = null;
            if (!IsDisposed) UpdateTreasury();
        };
        f.Show();
    }
    private void OpenCash() { using var f = new CashServicesForm(currentUser); f.ShowDialog(this); UpdateTreasury(); }
    private void OpenOPayMachine() { using var f = new MachineServicesForm(currentUser, "OPay"); f.ShowDialog(this); UpdateTreasury(); }
    private void OpenBeeMachine() { using var f = new MachineServicesForm(currentUser, "BEE"); f.ShowDialog(this); UpdateTreasury(); }
    private void OpenInvoice()
    {
        try { InvoiceFlow.Run(this, currentUser, null); }
        catch (Exception ex)
        {
            AppLog.Error("تعذر فتح شاشة الفواتير", ex);
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void OpenSales() { using var f = new SalesCartForm(currentUser); f.ShowDialog(this); UpdateTreasury(); }
    private void OpenCustomers() { using var f = new CustomersForm(currentUser); f.ShowDialog(this); UpdateTreasury(); }
    private void OpenTreasury() { using var f = new TreasuryForm(currentUser); f.ShowDialog(this); UpdateTreasury(); }
    private void OpenInventory() { using var f = new InventoryForm(currentUser); f.ShowDialog(this); UpdateTreasury(); }
    private void OpenBusinessManagement() { using var f = new BusinessManagementForm(currentUser); f.ShowDialog(this); UpdateTreasury(); }

    private void AddManagementAndAdminButtons(TableLayoutPanel parent, int col, int row)
    {
        var host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(9), BackColor = Color.Transparent };
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var management = UiKit.Button("🧾 المشتريات والمرتجعات والتقارير", Soft(Color.FromArgb(0, 121, 107)), 10, 10);
        management.Dock = DockStyle.Fill; management.Margin = new Padding(0, 0, 0, 5); management.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        UiKit.RoundCorners(management, CornerRadius); management.Click += (_, _) => OpenBusinessManagement();
        var admin = UiKit.Button("⚙ إعدادات الإدارة", Soft(Color.FromArgb(111, 66, 193)), 10, 10);
        admin.Dock = DockStyle.Fill; admin.Margin = new Padding(0, 5, 0, 0); admin.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        UiKit.RoundCorners(admin, CornerRadius); admin.Click += (_, _) => OpenAdminSettings();
        stack.Controls.Add(management, 0, 0); stack.Controls.Add(admin, 0, 1); host.Controls.Add(stack);
        parent.Controls.Add(host, col, row);
    }

    private void OpenDailyClosing()
    {
        using var form = new DailyClosingForm(currentUser);
        form.ShowDialog(this);
        UpdateTreasury();
    }

    private void OpenAdminSettings()
    {
        using var prompt = new PasswordPromptForm("أدخل كلمة مرور الأدمن لفتح إعدادات الإدارة:");
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        if (!AdminSecurity.ValidateAdminPassword(prompt.Password))
        {
            MessageBox.Show(this, "كلمة مرور الأدمن غير صحيحة.", "رفض الدخول", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (!AdminPasswordRotation.EnsureRotatedIfDefault(this)) return;
        using var form = new AdminPanelForm(currentUser, PricingStore.Load(), _ => { });
        form.ShowDialog(this);
    }
}
