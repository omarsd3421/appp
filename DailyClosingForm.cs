using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal sealed class DailyClosingForm : Form
{
    private static readonly (string Key, string Name)[] Accounts =
    {
        ("OPAY", "OPay"),
        ("BEE", "باب رزق"),
        ("CASH", "فودافون كاش / محافظ أخرى"),
        ("INSTAPAY", "إنستا باي"),
        ("SALES", "المبيعات"),
        ("PRINTING", "الطباعة والتصوير"),
        ("PERCENTAGE", "النسبة (عمولات فقط)"),
        // «النقدية في الدرج» هي أهم خانة: آخر صف وهو الأكبر والأوضح
        ("DRAWER", "النقدية في الدرج")
    };

    private readonly string currentUser;
    private Label statusLabel = null!, expectedTotal = null!, differenceTotal = null!;
    private TableLayoutPanel summaryTable = null!;
    // زر واحد كبير أسفل الشاشة يتحكم في كل شيء: فتح اليومية وبدء الشفت، ثم
    // تسليم الشفت الصباحي، ثم إنهاء الشفت المسائي مع اعتماد وإغلاق اليومية —
    // بدل ما كان زرين منفصلين (زر شفت صغير فوق + زر إغلاق كبير تحت).
    private Button primaryButton = null!;
    private ComboBox shiftSelector = null!;
    private Label shiftLabel = null!;
    private DataGridView historyGrid = null!;
    private long? currentSessionId;

    public DailyClosingForm(string userName)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        LoadCurrentDay();
        LoadHistory();
        InputFocusHelper.Apply(this);
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "🔒 اليومية وإغلاق الخزينة", 1020, 760);
        UiKit.EnableEscape(this);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        var header = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Color.White, Padding = new Padding(20, 8, 20, 8) };
        header.Controls.Add(UiKit.Title($"🔒 اليومية — {DateTime.Now:yyyy-MM-dd}", 18));

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
        var currentTab = new TabPage("📅 اليومية الحالية") { BackColor = UiKit.Background, Padding = new Padding(18) };
        var historyTab = new TabPage("📚 سجل اليوميات") { BackColor = UiKit.Background, Padding = new Padding(18) };
        BuildCurrentTab(currentTab);
        BuildHistoryTab(historyTab);
        tabs.TabPages.Add(currentTab);
        tabs.TabPages.Add(historyTab);
        // مهم: عنصر Fill يجب إضافته أولًا ثم الرأس، وإلا يغطي الرأس أول جزء من التابات
        // (وهذا سبب اختفاء سطر حالة اليومية الأخضر تحت العنوان).
        Controls.Add(tabs);
        Controls.Add(header);
    }

    private void BuildCurrentTab(TabPage tab)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UiKit.Background,
            Padding = new Padding(0, 0, 0, 4)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var statusPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(0, 0, 0, 4)
        };
        statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            BackColor = Color.White,
            ForeColor = UiKit.Primary,
            AutoEllipsis = false,
            AutoSize = false,
            Padding = new Padding(8, 2, 8, 2)
        };
        statusPanel.Controls.Add(statusLabel);

        var shiftBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(2), BackColor = Color.White
        };
        UiKit.EnableToolbarScroll(shiftBar);
        shiftLabel = new Label { AutoSize = true, Text = "الشفت: —", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = UiKit.Primary, Margin = new Padding(6, 7, 6, 4) };
        shiftSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 3, 4, 3) };
        shiftSelector.Items.Add(new ShiftOption("MORNING", "🌅 صباحي"));
        shiftSelector.Items.Add(new ShiftOption("EVENING", "🌙 مسائي"));
        shiftSelector.SelectedIndex = DateTime.Now.Hour < 15 ? 0 : 1;
        shiftSelector.SelectedIndexChanged += (_, _) => UpdateShiftButtonForPendingStart();
        // اخترنا عمدًا عدم وضع زر هنا: كل التحكم (فتح اليومية وبدء الشفت، ثم
        // تسليم، ثم إنهاء وإغلاق) أصبح من خلال الزر الكبير الواحد أسفل الشاشة
        // (primaryButton) بدل تكرار نفس المنطق في زر صغير هنا أيضًا.
        shiftBar.Controls.Add(shiftSelector); shiftBar.Controls.Add(shiftLabel);
        statusPanel.Controls.Add(shiftBar);
        root.Controls.Add(statusPanel, 0, 0);

        summaryTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = Accounts.Length + 1, BackColor = Color.White, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 5) };
        summaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        summaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        summaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        summaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        summaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        summaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        AddHeader(summaryTable, "الحساب", 0);
        AddHeader(summaryTable, "البداية", 1);
        AddHeader(summaryTable, "داخل", 2);
        AddHeader(summaryTable, "خارج", 3);
        AddHeader(summaryTable, "المتوقع", 4);
        AddHeader(summaryTable, "الفعلي عند الإغلاق", 5);
        for (int i = 0; i < Accounts.Length; i++)
        {
            AddHeader(summaryTable, Accounts[i].Name, 0, i + 1, false);
            for (int c = 1; c <= 5; c++)
                summaryTable.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Text = "—", Padding = new Padding(2) }, c, i + 1);
        }

        // الصفوف العادية بحجم المحتوى، وآخر صف (النقدية في الدرج) ياخد باقي المساحة
        // وبخط أكبر وخلفية مميزة لأنه أهم خانة.
        summaryTable.RowStyles.Clear();
        for (int r = 0; r <= Accounts.Length; r++)
            summaryTable.RowStyles.Add(r == Accounts.Length
                ? new RowStyle(SizeType.Percent, 100)
                : new RowStyle(SizeType.AutoSize));
        int drawerRow = Array.FindIndex(Accounts, x => x.Key == "DRAWER") + 1;
        for (int c = 0; c <= 5; c++)
        {
            var cell = summaryTable.GetControlFromPosition(c, drawerRow);
            if (cell == null) continue;
            cell.Font = new Font("Segoe UI", c == 0 ? 12F : 13F, FontStyle.Bold);
            cell.BackColor = Color.FromArgb(232, 245, 233);
            cell.ForeColor = Color.FromArgb(21, 87, 36);
        }
        root.Controls.Add(summaryTable, 0, 1);

        var totalPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(10, 3, 10, 3),
            Margin = new Padding(0, 0, 0, 5)
        };
        UiKit.EnableToolbarScroll(totalPanel);
        expectedTotal = TotalLabel("إجمالي المتوقع: 0.00 ج.م", UiKit.Primary);
        differenceTotal = TotalLabel("إجمالي الفرق: 0.00 ج.م", UiKit.Danger);
        totalPanel.Controls.Add(expectedTotal);
        totalPanel.Controls.Add(differenceTotal);
        root.Controls.Add(totalPanel, 0, 2);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 69));

        var actionPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5, 5, 12, 5) };
        primaryButton = UiKit.Button("🔓 فتح اليومية", UiKit.Success, 210, 42);
        primaryButton.Dock = DockStyle.Top;
        primaryButton.Height = 44;
        primaryButton.Margin = new Padding(0);
        primaryButton.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        primaryButton.Click += PrimaryButton_Click;
        actionPanel.Controls.Add(primaryButton);
        bottom.Controls.Add(actionPanel, 0, 0);

        var guide = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(14, 6, 14, 6),
            Margin = new Padding(8, 5, 0, 5)
        };
        var guideText = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 9.2F, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 70, 70),
            Text = "طريقة العمل: رصيد البداية = الموجود عند فتح اليوم.\nالحركات الفعلية تُجمع من الخزينة حسب الحساب.\nعند الإغلاق اكتب الرصيد الموجود فعليًا؛ النظام يحسب الفرق ويحفظه في سجل اليومية.",
            Padding = new Padding(4, 2, 4, 2)
        };
        guide.Controls.Add(guideText);
        bottom.Controls.Add(guide, 1, 0);
        root.Controls.Add(bottom, 0, 3);

        tab.Controls.Add(root);
    }

    private void BuildHistoryTab(TabPage tab)
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiKit.Background, Padding = new Padding(5) };
        historyGrid = new DataGridView();
        UiKit.StyleGrid(historyGrid);
        historyGrid.Dock = DockStyle.Fill;
        historyGrid.AutoGenerateColumns = false;
        historyGrid.AllowUserToAddRows = false;
        historyGrid.AllowUserToDeleteRows = false;
        historyGrid.ReadOnly = true;
        historyGrid.RowHeadersVisible = false;
        historyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        historyGrid.MultiSelect = false;
        historyGrid.Columns.Add(UiKit.GridText("Date", "التاريخ", 150));
        historyGrid.Columns.Add(UiKit.GridText("Status", "الحالة", 110));
        historyGrid.Columns.Add(UiKit.GridText("OpenedBy", "فتحها", 130));
        historyGrid.Columns.Add(UiKit.GridText("OpenedAt", "وقت الفتح", 180));
        historyGrid.Columns.Add(UiKit.GridText("ClosedBy", "أغلقها", 130));
        historyGrid.Columns.Add(UiKit.GridText("ClosedAt", "وقت الإغلاق", 180));
        var idColumn = UiKit.GridText("Id", "Id", 1);
        idColumn.Visible = false;
        historyGrid.Columns.Add(idColumn);
        historyGrid.CellDoubleClick += HistoryGrid_CellDoubleClick;
        historyGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
            var v = historyGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            e.Value = v == "CLOSED" ? "مغلقة" : v == "LEGACY" ? "سجل قديم" : "مفتوحة";
        };
        root.Controls.Add(historyGrid);
        tab.Controls.Add(root);
    }

    private void LoadCurrentDay()
    {
        using var c = AppDatabase.OpenConnection();
        var session = AppDatabase.GetDailySession(c, DateTime.Now.Date);
        if (session == null)
        {
            currentSessionId = null;
            var suggested = AppDatabase.GetSuggestedOpeningBalances(c, DateTime.Now.Date);
            statusLabel.Text = $"لا توجد يومية مفتوحة اليوم — بداية الحسابات: OPay {suggested["OPAY"]:F2} | باب رزق {suggested["BEE"]:F2} | فودافون كاش {suggested["CASH"]:F2} | إنستا باي {suggested["INSTAPAY"]:F2} | المبيعات {suggested["SALES"]:F2} | الطباعة والتصوير {suggested["PRINTING"]:F2} | النقدية في الدرج {suggested["DRAWER"]:F2} | النسبة 0.00 ج.م";
            statusLabel.ForeColor = UiKit.Primary;
            shiftSelector.Enabled = true;
            shiftLabel.Text = "لم تُفتح اليومية بعد — اختر الشفت";
            primaryButton.Enabled = true;
            UpdateShiftButtonForPendingStart();
            RenderEmptySummary();
            return;
        }

        currentSessionId = session.Id;
        statusLabel.Text = session.Status == "CLOSED"
            ? $"اليومية مغلقة ✅ — فتحها {session.OpenedBy} وإغلاقها {session.ClosedBy}"
            : $"اليومية مفتوحة ✅ — فتحها {session.OpenedBy} في {session.OpenedAt:HH:mm} — اليومية واحدة والشفتات داخلها";
        statusLabel.ForeColor = session.Status == "CLOSED" ? UiKit.Danger : UiKit.SuccessText;
        RefreshShiftUi(session);
        RefreshSummary();
    }

    private string GetSelectedShiftKey() => shiftSelector.SelectedItem is ShiftOption option ? option.Key : "EVENING";

    /// <summary>
    /// يضبط شكل/نص الزر الكبير الموحّد (primaryButton) حسب حالة الشفت الحالية:
    /// لا يوجد شفت مفتوح ← "بدء الشفت"، شفت صباحي لي ← "تسليم الشفت"،
    /// شفت مسائي لي ← "إنهاء الشفت واعتماد وإغلاق اليومية"، شفت مفتوح لمستخدم آخر ← الزر معطل.
    /// </summary>
    private void RefreshShiftUi(AppDatabase.DailySessionInfo session)
    {
        if (session.Status == "CLOSED")
        {
            shiftLabel.Text = "الشفتات: اليومية مغلقة";
            shiftSelector.Enabled = false;
            primaryButton.Enabled = false;
            primaryButton.Text = "✅ اليومية مغلقة";
            primaryButton.BackColor = Color.FromArgb(150, 150, 150);
            return;
        }
        using var c = AppDatabase.OpenConnection();
        var activeMine = AppDatabase.GetActiveShiftForSession(c, session.Id, currentUser);
        var all = AppDatabase.GetDailyShifts(c, session.Id);
        if (activeMine != null)
        {
            shiftLabel.Text = $"الشفت الحالي: {activeMine.ShiftName} — {currentUser}";
            shiftSelector.SelectedIndex = activeMine.ShiftKey == "MORNING" ? 0 : 1;
            shiftSelector.Enabled = false;
            primaryButton.Enabled = true;
            if (activeMine.ShiftKey == "MORNING")
            {
                primaryButton.Text = "↔ تسليم الشفت الصباحي";
                primaryButton.BackColor = Color.FromArgb(58, 84, 112);
            }
            else
            {
                primaryButton.Text = "🔒 إنهاء الشفت واعتماد وإغلاق اليومية";
                primaryButton.BackColor = UiKit.Danger;
            }
            return;
        }
        var openOther = all.FirstOrDefault(x => x.Status == "OPEN");
        if (openOther != null)
        {
            shiftLabel.Text = $"الشفت المفتوح: {openOther.ShiftName} — {openOther.UserName}";
            shiftSelector.Enabled = false;
            primaryButton.Enabled = false;
            primaryButton.Text = "⏳ شفت مفتوح لمستخدم آخر";
            primaryButton.BackColor = Color.FromArgb(150, 150, 150);
            return;
        }
        shiftLabel.Text = "لا يوجد شفت مفتوح — اختر صباحي أو مسائي";
        shiftSelector.Enabled = true;
        primaryButton.Enabled = true;
        UpdateShiftButtonForPendingStart();
    }

    /// <summary>
    /// يحدّث نص/لون الزر الكبير الموحّد ليعكس الشفت المختار حاليًا في القائمة،
    /// سواء قبل فتح اليومية أصلًا (فتح + بدء شفت معًا) أو بعد فتحها وقبل بدء أي شفت.
    /// </summary>
    private void UpdateShiftButtonForPendingStart()
    {
        if (!shiftSelector.Enabled) return;
        var shiftName = GetSelectedShiftKey() == "MORNING" ? "الصباحي" : "المسائي";
        if (!currentSessionId.HasValue)
        {
            primaryButton.Text = $"🔓 فتح اليومية وبدء الشفت {shiftName}";
        }
        else
        {
            if (!primaryButton.Enabled) return;
            primaryButton.Text = $"▶ بدء الشفت {shiftName}";
        }
        primaryButton.BackColor = UiKit.Success;
    }

    private sealed class ShiftOption
    {
        public string Key { get; }
        public string Text { get; }
        public ShiftOption(string key, string text) { Key = key; Text = text; }
        public override string ToString() => Text;
    }

    /// <summary>
    /// معالج ضغطة واحد للزر الكبير الموحّد يغطي كل مراحل اليوم:
    /// 1) لا توجد يومية اليوم بعد ← يفتحها ويبدأ الشفت المختار معًا.
    /// 2) يومية مفتوحة وبلا شفت لي ولا لغيري ← يبدأ الشفت المختار.
    /// 3) شفت صباحي مفتوح لي ← يسلّمه (إتاحة بدء المسائي).
    /// 4) شفت مسائي مفتوح لي ← ينهيه ويعتمد ويغلق اليومية في خطوة واحدة.
    /// الحالة الحالية تُحدَّد من قاعدة البيانات مباشرة بدل الاعتماد على نص الزر،
    /// وكل حالة تطلب تأكيدًا صريحًا من المستخدم قبل التنفيذ.
    /// </summary>
    private void PrimaryButton_Click(object? sender, EventArgs e)
    {
        try
        {
            using var c = AppDatabase.OpenConnection();
            var session = AppDatabase.GetDailySession(c, DateTime.Now.Date);

            // 1) لا توجد يومية اليوم بعد: افتحها وابدأ الشفت المختار مباشرة.
            if (session == null)
            {
                // لا توجد حقول إدخال لرصيد البداية. يتم أخذ الأرصدة الحالية مباشرة من الإدارة والخزينة.
                var balances = AppDatabase.GetSuggestedOpeningBalances(c, DateTime.Now.Date);
                // رصيد الماكينات (OPay / باب رزق) ممكن يبقى سالب لأن الماكينة بتسمح بالسلف من الشركة؛
                // الممنوع بس إن باقي الحسابات تبقى سالبة.
                var negatives = balances
                    .Where(kv => kv.Value < 0m && !AppDatabase.AllowsNegativeBalance(kv.Key))
                    .Select(kv => Accounts.FirstOrDefault(a => a.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)).Name ?? kv.Key)
                    .ToList();
                if (negatives.Count > 0)
                    throw new InvalidOperationException("الرصيد لا يمكن أن يكون سالبًا في: " + string.Join("، ", negatives));
                var openKey = GetSelectedShiftKey();
                var openName = openKey == "MORNING" ? "الصباحي" : "المسائي";
                if (MessageBox.Show(this, $"سيتم فتح يومية اليوم وبدء الشفت {openName}. هل تريد المتابعة؟", "فتح اليومية", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                AppDatabase.OpenDailySession(c, currentUser, DateTime.Now.Date, balances);
                currentSessionId = AppDatabase.GetDailySession(c, DateTime.Now.Date)?.Id;
                if (currentSessionId.HasValue)
                    AppDatabase.StartDailyShift(c, currentUser, currentSessionId.Value, openKey);
                LoadCurrentDay();
                LoadHistory();
                MessageBox.Show(this, "تم فتح يومية اليوم وبدء الشفت الحالي. كل الحركات المالية من الآن ستُربط باليومية والشفت والحساب الصحيح.", "فتح اليومية", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (session.Status == "CLOSED") return; // الزر معطل أصلًا في هذه الحالة.

            var activeMine = AppDatabase.GetActiveShiftForSession(c, session.Id, currentUser);

            // 2) لا يوجد شفت مفتوح لي الآن: هذه ضغطة "بدء الشفت".
            if (activeMine == null)
            {
                var openOther = AppDatabase.GetDailyShifts(c, session.Id).FirstOrDefault(x => x.Status == "OPEN");
                if (openOther != null) return; // الزر معطل أصلًا في هذه الحالة.

                var key = GetSelectedShiftKey();
                var name = key == "MORNING" ? "الصباحي" : "المسائي";
                if (MessageBox.Show(this, $"سيتم بدء الشفت {name}. هل تريد المتابعة؟", "بدء الشفت", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                AppDatabase.StartDailyShift(c, currentUser, session.Id, key);
                int pendingPrintLogs = PrintLogger.FlushPendingLogsForToday();
                LoadCurrentDay();
                if (pendingPrintLogs > 0)
                    MessageBox.Show(this,
                        $"يوجد {pendingPrintLogs} سجل طباعة ما زال بحاجة إلى مراجعة. تمت إعادة محاولة تسجيل عمليات اليوم فقط؛ العمليات من تواريخ سابقة لم تُنقل تلقائيًا إلى الشفت الحالي.",
                        "مراجعة سجل الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 3) شفت صباحي مفتوح لي الآن: هذه ضغطة "تسليم الشفت".
            if (string.Equals(activeMine.ShiftKey, "MORNING", StringComparison.OrdinalIgnoreCase))
            {
                if (MessageBox.Show(this, "سيتم تسليم الشفت الصباحي وإتاحته لبدء الشفت المسائي. هل تريد المتابعة؟", "تسليم الشفت", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                AppDatabase.EndDailyShift(c, currentUser, activeMine.Id);
                LoadCurrentDay();
                return;
            }

            // 4) شفت مسائي مفتوح لي الآن: هذه ضغطة "إنهاء الشفت واعتماد وإغلاق اليومية" — الاثنان معًا.
            if (MessageBox.Show(this, "سيتم إنهاء الشفت المسائي، ثم اعتماد وإغلاق اليومية نهائيًا لهذا اليوم. هل تريد المتابعة؟", "إغلاق اليومية", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            var summary = AppDatabase.GetDailyAccountSummaries(c, session.Id);
            using var dlg = new ActualBalanceDialog(summary);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            AppDatabase.EndDailyShift(c, currentUser, activeMine.Id);
            AppDatabase.CloseDailySession(c, currentUser, session.Id, dlg.ActualBalances);
            string? closeBackup = DatabaseBackup.BackupNow("close"); // نسخة احتياطية بعد قفل اليومية
            LoadCurrentDay();
            LoadHistory();
            string closingMessage = "تم إنهاء الشفت المسائي واعتماد وإغلاق اليومية. لن يتم السماح بحركات مالية إضافية على هذا اليوم، وسيتم إنشاء يومية اليوم التالي عند فتحها.";
            if (closeBackup == null)
                MessageBox.Show(this, closingMessage + "\n\nتحذير: فشل إنشاء النسخة الاحتياطية بعد الإغلاق. راجع سجل الأخطاء وأنشئ نسخة يدويًا.", "تم الإغلاق مع تحذير النسخ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (!string.IsNullOrWhiteSpace(DatabaseBackup.LastBackupWarning))
                MessageBox.Show(this, closingMessage + "\n\n" + DatabaseBackup.LastBackupWarning, "تم الإغلاق مع تحذير النسخ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show(this, closingMessage, "تم الإغلاق", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ في اليومية", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshSummary()
    {
        if (!currentSessionId.HasValue) return;
        using var c = AppDatabase.OpenConnection();
        var data = AppDatabase.GetDailyAccountSummaries(c, currentSessionId.Value);
        decimal expected = 0m, difference = 0m;
        foreach (var a in data)
        {
            int row = Array.FindIndex(Accounts, x => x.Key.Equals(a.AccountKey, StringComparison.OrdinalIgnoreCase)) + 1;
            if (row <= 0) continue;
            var c1 = summaryTable.GetControlFromPosition(1, row);
            var c2 = summaryTable.GetControlFromPosition(2, row);
            var c3 = summaryTable.GetControlFromPosition(3, row);
            var c4 = summaryTable.GetControlFromPosition(4, row);
            var c5 = summaryTable.GetControlFromPosition(5, row);
            if (c1 == null || c2 == null || c3 == null || c4 == null || c5 == null) continue;
            c1.Text = $"{a.Opening:F2}";
            c2.Text = $"{a.Incoming:F2}";
            c3.Text = $"{a.Outgoing:F2}";
            c4.Text = $"{a.Expected:F2}";
            c5.Text = a.Actual.HasValue ? $"{a.Actual.Value:F2}" : "عند الإغلاق";
            // «النسبة» تقرير للعمولات التي تم تحصيلها بالفعل ضمن الدرج؛ لا تُجمع مرة ثانية في إجمالي الأموال.
            if (!string.Equals(a.AccountKey, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
            {
                expected += a.Expected;
                if (a.Difference.HasValue) difference += a.Difference.Value;
            }
        }
        expectedTotal.Text = $"إجمالي المتوقع: {expected:F2} ج.م";
        differenceTotal.Text = $"إجمالي الفرق: {difference:F2} ج.م";
        differenceTotal.ForeColor = difference == 0 ? UiKit.Success : UiKit.Danger;
    }

    private void RenderEmptySummary()
    {
        foreach (Control control in summaryTable.Controls)
        {
            if (summaryTable.GetPositionFromControl(control).Row == 0) continue;
            if (summaryTable.GetPositionFromControl(control).Column >= 1)
                control.Text = "—";
        }
        expectedTotal.Text = "إجمالي المتوقع: 0.00 ج.م";
        differenceTotal.Text = "إجمالي الفرق: 0.00 ج.م";
    }

    private void LoadHistory()
    {
        using var c = AppDatabase.OpenConnection();
        var rows = AppDatabase.GetDailySessionHistory(c);
        historyGrid.Rows.Clear();
        foreach (var x in rows)
            historyGrid.Rows.Add(x.BusinessDate.ToString("yyyy-MM-dd"), x.Status, x.OpenedBy, x.OpenedAt.ToString("yyyy-MM-dd HH:mm"), x.ClosedBy ?? "—", x.ClosedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—", x.Id);
    }

    private void HistoryGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (!long.TryParse(Convert.ToString(historyGrid.Rows[e.RowIndex].Cells[6].Value, CultureInfo.InvariantCulture), out var id)) return;
        if (id <= 0)
        {
            MessageBox.Show(this, "هذا يوم تاريخي يحتوي على حركات خزينة قبل إنشاء سجل اليومية. الحركات ظاهرة في سجل الخزينة، ولا توجد له جلسة يومية قابلة للإغلاق.", "سجل تاريخي", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            using var c = AppDatabase.OpenConnection();
            var session = AppDatabase.GetDailySessionHistory(c).FirstOrDefault(x => x.Id == id);
            if (session == null) return;
            var balances = AppDatabase.GetDailyAccountBalances(c, id);
            using var dlg = new DailyHistoryDetailsDialog(session, balances);
            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Label TotalLabel(string text, Color color) => new() { AutoSize = true, Text = text, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), ForeColor = color, Margin = new Padding(15, 8, 15, 8) };

    private static void AddHeader(TableLayoutPanel table, string text, int col, int row = 0, bool header = true)
    {
        table.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), BackColor = header ? Color.FromArgb(52, 73, 94) : Color.White, ForeColor = header ? Color.White : Color.Black, Text = text }, col, row);
    }

    private sealed class ActualBalanceDialog : Form
    {
        public Dictionary<string, decimal> ActualBalances { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextBox> boxes = new(StringComparer.OrdinalIgnoreCase);
        public ActualBalanceDialog(List<AppDatabase.DailyAccountSummary> summaries)
        {
            UiKit.PrepareForm(this, "💰 الرصيد الفعلي عند الإغلاق", 620, 480);
            UiKit.EnableEscape(this);
            RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(25), ColumnCount = 2, RowCount = summaries.Count + 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            int row = 0;
            foreach (var item in summaries)
            {
                string label = item.AccountKey switch { "OPAY" => "OPay", "BEE" => "باب رزق", "CASH" => "فودافون كاش / محافظ أخرى", "INSTAPAY" => "إنستا باي", "SALES" => "المبيعات", "PRINTING" => "الطباعة والتصوير", "DRAWER" => "النقدية في الدرج", "PERCENTAGE" => "النسبة (للمراجعة فقط)", _ => item.AccountKey };
                root.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold) }, 0, row);
                var box = new TextBox { Dock = DockStyle.Fill, Text = item.Expected.ToString("F2", CultureInfo.InvariantCulture), TextAlign = HorizontalAlignment.Center, RightToLeft = RightToLeft.No };
                if (string.Equals(item.AccountKey, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
                {
                    box.ReadOnly = true;
                    box.BackColor = Color.FromArgb(232, 245, 233);
                    box.TabStop = false;
                }
                boxes[item.AccountKey] = box; root.Controls.Add(box, 1, row); row++;
            }
            var note = new Label { Text = "اكتب الرصيد الفعلي لكل حساب. «النسبة» بند متابعة للعمولات فقط، ولا تُضاف مرة ثانية لرصيد الخزينة.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiKit.Muted, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            root.Controls.Add(note, 0, row); root.SetColumnSpan(note, 2); row++;
            var ok = UiKit.Button("✅ اعتماد وإغلاق", UiKit.Success, 190, 44);
            ok.Click += (_, _) =>
            {
                foreach (var kv in boxes)
                {
                    if (string.Equals(kv.Key, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
                    {
                        // هذا البند تفصيلي للعمولة الموجودة بالفعل في الدرج، وليس رصيدًا نقديًا منفصلًا.
                        ActualBalances[kv.Key] = summaries.First(x => x.AccountKey == kv.Key).Expected;
                        continue;
                    }
                    if (!decimal.TryParse(kv.Value.Text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || (amount < 0 && !AppDatabase.AllowsNegativeBalance(kv.Key)))
                    { MessageBox.Show(this, "هناك رصيد فعلي غير صحيح.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    ActualBalances[kv.Key] = amount;
                }
                DialogResult = DialogResult.OK; Close();
            };
            root.Controls.Add(ok, 1, row); Controls.Add(root); AcceptButton = ok;
        }
    }

    private sealed class DailyHistoryDetailsDialog : Form
    {
        public DailyHistoryDetailsDialog(AppDatabase.DailySessionInfo session, List<AppDatabase.DailyAccountSummary> balances)
        {
            UiKit.PrepareForm(this, $"📚 تفاصيل يومية {session.BusinessDate:yyyy-MM-dd}", 760, 540);
            UiKit.EnableEscape(this);
            RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 6, RowCount = balances.Count + 2, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            string[] headers = { "الحساب", "البداية", "داخل", "خارج", "المتوقع", "الفعلي / الفرق" };
            for (int i=0;i<headers.Length;i++) AddHeader(root, headers[i], i, 0, true);
            for (int i=0;i<balances.Count;i++)
            {
                var b = balances[i];
                string label = b.AccountKey switch { "OPAY"=>"OPay", "BEE"=>"باب رزق", "CASH"=>"فودافون كاش / محافظ أخرى", "INSTAPAY"=>"إنستا باي", "SALES"=>"المبيعات", "PRINTING"=>"الطباعة والتصوير", "DRAWER"=>"النقدية في الدرج", "PERCENTAGE"=>"النسبة (عمولات فقط)", _=>b.AccountKey };
                root.Controls.Add(Cell(label),0,i+1); root.Controls.Add(Cell($"{b.Opening:F2}"),1,i+1); root.Controls.Add(Cell($"{b.Incoming:F2}"),2,i+1); root.Controls.Add(Cell($"{b.Outgoing:F2}"),3,i+1); root.Controls.Add(Cell($"{b.Expected:F2}"),4,i+1); root.Controls.Add(Cell(b.Actual.HasValue ? $"{b.Actual.Value:F2} / {b.Difference.GetValueOrDefault():F2}" : "—"),5,i+1);
            }
            var shiftsText = "لا توجد بيانات شفتات";
            try
            {
                using var sc = AppDatabase.OpenConnection();
                var shifts = AppDatabase.GetDailyShifts(sc, session.Id);
                if (shifts.Count > 0)
                    shiftsText = string.Join("   |   ", shifts.Select(x => $"{x.ShiftName}: {x.UserName} {x.StartedAt:HH:mm}-{(x.EndedAt.HasValue ? x.EndedAt.Value.ToString("HH:mm") : "مفتوح")}"));
            }
            catch { }
            var info = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Text = $"الحالة: {(session.Status == "CLOSED" ? "مغلقة" : "مفتوحة")}   |   فتحها: {session.OpenedBy}   |   أغلقها: {session.ClosedBy ?? "—"}\r\n{shiftsText}" };
            root.Controls.Add(info,0,balances.Count+1); root.SetColumnSpan(info,6); Controls.Add(root);
        }
        private static Label Cell(string text)=>new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Text=text,Font=new Font("Segoe UI",9.5F,FontStyle.Bold)};
    }
}
