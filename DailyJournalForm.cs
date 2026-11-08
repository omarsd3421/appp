using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// شاشة سجل اليومية والشفتات — أُعيدت كتابتها بالكامل من الصفر (V29) لحل مشكلة
/// ظهور الجدول فارغًا تمامًا التي استمرت عبر عدة محاولات إصلاح سابقة (V24-V28).
///
/// السبب الجذري كان في مكان لم تلمسه أي من المحاولات السابقة: عندما يكون
/// RightToLeftLayout=true على الفورم، يفعّل نظام التشغيل النمط الموسّع
/// WS_EX_LAYOUTRTL على نافذة الفورم، وهذا النمط يُورَّث تلقائيًا إلى كل نافذة
/// فرعية بما فيها مقبض DataGridView الداخلي، ما لم تُستثنَ صراحةً. بما أن
/// DataGridView يرسم كل شيء يدويًا (owner-drawn)، فإن هذا التوريث يُفشل الرسم
/// بالكامل وبصمت تام (لا استثناء ولا رسالة خطأ) — تمامًا كما كان يحدث هنا.
/// تم حل ذلك جذريًا عبر UiKit.SafeDataGridView التي تستثني نفسها من هذا
/// التوريث عند إنشاء المقبض مباشرة.
///
/// كذلك أُعيد بناء التخطيط بالكامل باستخدام TableLayoutPanel بصفوف ثابتة بدل
/// تكديس عدة عناصر بـ Dock=Top/Bottom/Fill داخل بعضها، لتفادي أي تعارض توقيت
/// أو تراكب بصري كان يحدث سابقًا بين رسالة "لا توجد بيانات" والجدول نفسه.
/// </summary>
internal sealed class DailyJournalForm : Form
{
    private readonly string currentUser;
    private DateTimePicker fromDate = null!, toDate = null!;
    private DataGridView journalGrid = null!;
    private Panel gridHost = null!;
    private Label summaryLabel = null!, emptyLabel = null!;

    private sealed class JournalRow
    {
        public long? SessionId { get; init; }
        public DateTime Date { get; init; }
        public string Status { get; init; } = "LEGACY";
        public string OpenedBy { get; init; } = "—";
        public DateTime? OpenedAt { get; init; }
        public string ClosedBy { get; init; } = "—";
        public DateTime? ClosedAt { get; init; }
        public decimal Opening { get; init; }
        public decimal Incoming { get; init; }
        public decimal Outgoing { get; init; }
        public decimal Expected { get; init; }
        public decimal? Actual { get; init; }
        public decimal? Difference { get; init; }
        public string Shifts { get; init; } = "—";
    }

    public DailyJournalForm(string userName)
    {
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        fromDate.Value = DateTime.Today.AddYears(-20);
        toDate.Value = DateTime.Today;
        LoadJournal();
        InputFocusHelper.Apply(this);
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, "📚 سجل اليومية والشفتات", 1250, 820);
        UiKit.EnableEscape(this);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        // تخطيط رئيسي بصفوف ثابتة بدل تكديس Dock متعدد — يمنع أي تعارض توقيت
        // في التخطيط ويضمن أن كل جزء من الشاشة له مساحته المحجوزة بوضوح.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UiKit.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));  // العنوان
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));  // شريط الأدوات
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // شريط الملخص
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // الجدول
        Controls.Add(root);

        var titleBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        titleBar.Controls.Add(UiKit.Title("📚 سجل اليومية والشفتات", 18));
        root.Controls.Add(titleBar, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(10, 8, 10, 8),
            BackColor = Color.White
        };
        UiKit.EnableToolbarScroll(toolbar);
        toolbar.Controls.Add(MakeLabel("من تاريخ"));
        fromDate = new DateTimePicker { Width = 140, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Margin = new Padding(5) };
        toolbar.Controls.Add(fromDate);
        toolbar.Controls.Add(MakeLabel("إلى تاريخ"));
        toDate = new DateTimePicker { Width = 140, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Margin = new Padding(5) };
        toolbar.Controls.Add(toDate);

        var today = UiKit.Button("📅 اليوم", UiKit.Success, 105, 38);
        today.Click += (_, _) => { fromDate.Value = DateTime.Today; toDate.Value = DateTime.Today; LoadJournal(); };
        var show = UiKit.Button("🔎 عرض", UiKit.Primary, 105, 38);
        show.Click += (_, _) => LoadJournal();
        var all = UiKit.Button("📖 كل السجل", Color.FromArgb(58, 84, 112), 120, 38);
        all.Click += (_, _) => { fromDate.Value = DateTime.Today.AddYears(-20); toDate.Value = DateTime.Today.AddDays(1); LoadJournal(); };
        toolbar.Controls.Add(today);
        toolbar.Controls.Add(show);
        toolbar.Controls.Add(all);
        root.Controls.Add(toolbar, 0, 1);

        summaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(244, 246, 249),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 132, 73),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(8)
        };
        root.Controls.Add(summaryLabel, 0, 2);

        gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 246, 249), Padding = new Padding(14) };
        root.Controls.Add(gridHost, 0, 3);

        // UiKit.SafeDataGridView: نفس DataGridView العادي، لكنه يستثني نفسه من
        // توريث تخطيط RTL على مستوى نظام التشغيل حتى يُرسم بشكل طبيعي دائمًا
        // (راجع التعليق أعلى الملف والتعليق داخل UiKit.cs لتفاصيل السبب).
        journalGrid = new SafeDataGridView();
        UiKit.StyleGrid(journalGrid);
        journalGrid.Dock = DockStyle.Fill;

        journalGrid.Columns.Add(UiKit.GridText("Date", "التاريخ", 110));
        journalGrid.Columns.Add(UiKit.GridText("Status", "الحالة", 90));
        journalGrid.Columns.Add(UiKit.GridText("Shifts", "الشفتات", 220, true));
        journalGrid.Columns.Add(UiKit.GridText("Opening", "رصيد البداية", 105));
        journalGrid.Columns.Add(UiKit.GridText("Incoming", "الداخل", 100));
        journalGrid.Columns.Add(UiKit.GridText("Outgoing", "الخارج", 100));
        journalGrid.Columns.Add(UiKit.GridText("Expected", "المتوقع", 105));
        journalGrid.Columns.Add(UiKit.GridText("Actual", "الفعلي", 105));
        journalGrid.Columns.Add(UiKit.GridText("Difference", "الفرق", 95));
        journalGrid.Columns.Add(UiKit.GridText("OpenedBy", "فتحها", 105));
        journalGrid.Columns.Add(UiKit.GridText("ClosedBy", "أغلقها", 105));
        var idColumn = UiKit.GridText("Id", "Id", 1);
        idColumn.Visible = false;
        journalGrid.Columns.Add(idColumn);

        journalGrid.CellFormatting += JournalGrid_CellFormatting;
        journalGrid.CellDoubleClick += JournalGrid_CellDoubleClick;
        gridHost.Controls.Add(journalGrid);

        emptyLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "لا توجد يوميات أو حركات في الفترة المحددة.",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(115, 120, 126),
            BackColor = Color.White,
            Visible = false
        };
        gridHost.Controls.Add(emptyLabel);
    }

    private void JournalGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        try
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 1)
            {
                string v = Convert.ToString(journalGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? "";
                e.Value = v switch { "CLOSED" => "مغلقة", "OPEN" => "مفتوحة", _ => "سجل قديم" };
            }
            if (e.ColumnIndex == 8 && decimal.TryParse(Convert.ToString(journalGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value), NumberStyles.Number, CultureInfo.InvariantCulture, out var diff))
                if (e.CellStyle != null) e.CellStyle.ForeColor = diff == 0 ? UiKit.Success : UiKit.Danger;
        }
        catch
        {
            // لا نسمح لأي خطأ في تنسيق خلية واحدة بمنع عرض باقي الجدول.
        }
    }

    private static Label MakeLabel(string text) => new Label
    {
        AutoSize = true,
        Text = text,
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        ForeColor = Color.FromArgb(70, 70, 70),
        Margin = new Padding(8, 11, 4, 4)
    };

    private void LoadJournal()
    {
        try
        {
            DateTime from = fromDate.Value.Date;
            DateTime to = toDate.Value.Date;
            if (to < from) { MessageBox.Show(this, "تاريخ النهاية يجب أن يكون بعد أو مساويًا لتاريخ البداية.", "التواريخ", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            using var c = AppDatabase.OpenConnection();
            var rows = BuildRows(c, from, to);

            journalGrid.Rows.Clear();
            foreach (var row in rows)
            {
                journalGrid.Rows.Add(
                    row.Date.ToString("yyyy-MM-dd"), row.Status, row.Shifts,
                    row.Opening.ToString("N2", CultureInfo.InvariantCulture),
                    row.Incoming.ToString("N2", CultureInfo.InvariantCulture),
                    row.Outgoing.ToString("N2", CultureInfo.InvariantCulture),
                    row.Expected.ToString("N2", CultureInfo.InvariantCulture),
                    row.Actual.HasValue ? row.Actual.Value.ToString("N2", CultureInfo.InvariantCulture) : "—",
                    row.Difference.HasValue ? row.Difference.Value.ToString("N2", CultureInfo.InvariantCulture) : "—",
                    row.OpenedBy, row.ClosedBy,
                    row.SessionId.HasValue ? row.SessionId.Value.ToString(CultureInfo.InvariantCulture) : "0");
            }

            bool isEmpty = journalGrid.Rows.Count == 0;
            emptyLabel.Visible = isEmpty;
            journalGrid.Visible = !isEmpty;

            decimal opening = rows.Sum(x => x.Opening);
            decimal incoming = rows.Sum(x => x.Incoming);
            decimal outgoing = rows.Sum(x => x.Outgoing);
            decimal expected = rows.Sum(x => x.Expected);
            int closed = rows.Count(x => x.Status == "CLOSED");
            summaryLabel.Text = $"📚 الأيام: {rows.Count}   |   مغلقة: {closed}   |   البداية: {opening:N2} ج.م   |   الداخل: {incoming:N2} ج.م   |   الخارج: {outgoing:N2} ج.م   |   المتوقع: {expected:N2} ج.م";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر تحميل سجل اليومية:\n\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static List<JournalRow> BuildRows(SqliteConnection c, DateTime from, DateTime to)
    {
        var dict = new Dictionary<DateTime, JournalRow>();

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT s.Id,s.BusinessDate,s.Status,s.OpenedBy,s.OpenedAt,s.ClosedBy,s.ClosedAt,
       COALESCE((SELECT SUM(b.OpeningAmount) FROM DailyBalances b WHERE b.SessionId=s.Id),0),
       COALESCE((SELECT SUM(CASE WHEN t.MovementType='IN' THEN t.Amount ELSE 0 END)
                 FROM TreasuryMovements t
                 WHERE substr(t.CreatedAt,1,10)=substr(s.BusinessDate,1,10)
                   AND COALESCE(t.AccountKey,'')<>'PERCENTAGE'
                   AND (t.ShiftId IS NULL OR EXISTS (SELECT 1 FROM DailyShifts ds WHERE ds.Id=t.ShiftId AND ds.SessionId=s.Id))),0),
       COALESCE((SELECT SUM(CASE WHEN t.MovementType='OUT' THEN t.Amount ELSE 0 END)
                 FROM TreasuryMovements t
                 WHERE substr(t.CreatedAt,1,10)=substr(s.BusinessDate,1,10)
                   AND COALESCE(t.AccountKey,'')<>'PERCENTAGE'
                   AND (t.ShiftId IS NULL OR EXISTS (SELECT 1 FROM DailyShifts ds WHERE ds.Id=t.ShiftId AND ds.SessionId=s.Id))),0),
       (
           COALESCE((SELECT SUM(b.OpeningAmount) FROM DailyBalances b WHERE b.SessionId=s.Id),0)
           +
           COALESCE((SELECT SUM(CASE WHEN t.MovementType='IN' THEN t.Amount ELSE -t.Amount END)
                     FROM TreasuryMovements t
                     WHERE substr(t.CreatedAt,1,10)=substr(s.BusinessDate,1,10)
                       AND COALESCE(t.AccountKey,'')<>'PERCENTAGE'
                       AND (t.ShiftId IS NULL OR EXISTS (SELECT 1 FROM DailyShifts ds WHERE ds.Id=t.ShiftId AND ds.SessionId=s.Id))),0)
       ),
       (SELECT CASE WHEN COUNT(*)=0 OR SUM(CASE WHEN b.ActualAmount IS NOT NULL AND b.AccountKey<>'PERCENTAGE' THEN 1 ELSE 0 END)=0 THEN NULL ELSE SUM(CASE WHEN b.AccountKey<>'PERCENTAGE' THEN COALESCE(b.ActualAmount,0) ELSE 0 END) END FROM DailyBalances b WHERE b.SessionId=s.Id),
       (SELECT CASE WHEN COUNT(*)=0 OR SUM(CASE WHEN b.Difference IS NOT NULL AND b.AccountKey<>'PERCENTAGE' THEN 1 ELSE 0 END)=0 THEN NULL ELSE SUM(CASE WHEN b.AccountKey<>'PERCENTAGE' THEN COALESCE(b.Difference,0) ELSE 0 END) END FROM DailyBalances b WHERE b.SessionId=s.Id)
FROM DailySessions s
WHERE substr(s.BusinessDate,1,10) BETWEEN $from AND $to
ORDER BY s.BusinessDate DESC;";
            cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var date = DateTime.Parse(r.GetString(1)).Date;
                dict[date] = new JournalRow
                {
                    SessionId = r.GetInt64(0), Date = date, Status = r.GetString(2), OpenedBy = r.GetString(3), OpenedAt = DateTime.Parse(r.GetString(4)), ClosedBy = r.IsDBNull(5) ? "—" : r.GetString(5), ClosedAt = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6)),
                    Opening = r.GetDecimal(7), Incoming = r.GetDecimal(8), Outgoing = r.GetDecimal(9), Expected = r.GetDecimal(10), Actual = r.IsDBNull(11) ? null : r.GetDecimal(11), Difference = r.IsDBNull(12) ? null : r.GetDecimal(12)
                };
            }
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT substr(CreatedAt,1,10),
       COALESCE(SUM(CASE WHEN MovementType='IN' THEN Amount ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN MovementType='OUT' THEN Amount ELSE 0 END),0)
FROM TreasuryMovements
WHERE substr(CreatedAt,1,10) BETWEEN $from AND $to
  AND COALESCE(AccountKey,'')<>'PERCENTAGE'
GROUP BY substr(CreatedAt,1,10)
ORDER BY 1 DESC;";
            cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var date = DateTime.Parse(r.GetString(0)).Date;
                if (dict.ContainsKey(date))
                {
                    // جلسة اليومية موجودة بالفعل لهذا اليوم: الحركات مُحتسبة معها.
                    continue;
                }
                dict[date] = new JournalRow
                {
                    Date = date, Status = "LEGACY", Incoming = r.GetDecimal(1), Outgoing = r.GetDecimal(2), Expected = r.GetDecimal(1) - r.GetDecimal(2),
                    Shifts = "حركات بدون يومية"
                };
            }
        }

        // إظهار اليوم حتى لو لم تُسجَّل فيه حركة خزينة بعد (مثل تسجيل دين أو
        // خدمة)، حتى لا يبدو السجل فارغًا لمجرد عدم وجود TreasuryMovement.
        using (var dates = c.CreateCommand())
        {
            dates.CommandText = @"
SELECT substr(CreatedAt,1,10) FROM Transactions WHERE substr(CreatedAt,1,10) BETWEEN $from AND $to
UNION
SELECT substr(CreatedAt,1,10) FROM Cash_Services WHERE substr(CreatedAt,1,10) BETWEEN $from AND $to
UNION
SELECT substr(Timestamp,1,10) FROM PrintLog WHERE substr(Timestamp,1,10) BETWEEN $from AND $to
UNION
SELECT substr(CreatedAt,1,10) FROM TreasuryMovements WHERE substr(CreatedAt,1,10) BETWEEN $from AND $to
ORDER BY 1 DESC;";
            dates.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
            dates.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
            using var dr = dates.ExecuteReader();
            while (dr.Read())
            {
                var date = DateTime.Parse(dr.GetString(0)).Date;
                if (!dict.ContainsKey(date))
                {
                    dict[date] = new JournalRow
                    {
                        Date = date, Status = "LEGACY", Incoming = 0m, Outgoing = 0m, Expected = 0m,
                        Shifts = "حركات تشغيلية بدون يومية"
                    };
                }
            }
        }

        foreach (var key in dict.Keys.ToList())
        {
            var row = dict[key];
            if (!row.SessionId.HasValue) continue;

            using var scmd = c.CreateCommand();
            scmd.CommandText = "SELECT ShiftKey,UserName,StartedAt,EndedAt,Status FROM DailyShifts WHERE SessionId=$session ORDER BY CASE ShiftKey WHEN 'MORNING' THEN 1 ELSE 2 END";
            scmd.Parameters.AddWithValue("$session", row.SessionId.Value);
            using var sr = scmd.ExecuteReader();
            var shifts = new List<string>();
            while (sr.Read())
            {
                string name = sr.GetString(1);
                string keyShift = sr.GetString(0) == "MORNING" ? "صباحي" : "مسائي";
                DateTime start = DateTime.Parse(sr.GetString(2));
                string end = sr.IsDBNull(3) ? "مفتوح" : DateTime.Parse(sr.GetString(3)).ToString("HH:mm");
                shifts.Add($"{keyShift}: {name} {start:HH:mm}-{end}");
            }

            dict[key] = new JournalRow
            {
                SessionId = row.SessionId, Date = row.Date, Status = row.Status, OpenedBy = row.OpenedBy, OpenedAt = row.OpenedAt, ClosedBy = row.ClosedBy, ClosedAt = row.ClosedAt,
                Opening = row.Opening, Incoming = row.Incoming, Outgoing = row.Outgoing, Expected = row.Expected, Actual = row.Actual, Difference = row.Difference,
                Shifts = shifts.Count == 0 ? "لا توجد شفتات" : string.Join(" | ", shifts)
            };
        }

        return dict.Values.OrderByDescending(x => x.Date).ToList();
    }

    private void JournalGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        string dateText = Convert.ToString(journalGrid.Rows[e.RowIndex].Cells[0].Value) ?? "";
        if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rowDate))
            return;

        if (!long.TryParse(Convert.ToString(journalGrid.Rows[e.RowIndex].Cells[11].Value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessionId))
            sessionId = 0;

        using var c = AppDatabase.OpenConnection();
        if (sessionId > 0)
        {
            var session = AppDatabase.GetDailySessionHistory(c).FirstOrDefault(x => x.Id == sessionId);
            if (session != null)
            {
                var balances = AppDatabase.GetDailyAccountBalances(c, sessionId);
                using var dlg = new DailyJournalDetailsForm(session, balances, currentUser);
                dlg.ShowDialog(this);
                return;
            }
        }

        // يوم تاريخي بدون Session: افتح التفاصيل الفعلية للحركات المسجلة في هذا التاريخ.
        using var legacyDlg = new LegacyDailyDetailsForm(rowDate, currentUser);
        legacyDlg.ShowDialog(this);
    }
}

internal sealed class DailyJournalDetailsForm : Form
{
    private readonly AppDatabase.DailySessionInfo session;
    private readonly List<AppDatabase.DailyAccountSummary> balances;
    private readonly string currentUser;
    private DataGridView movesGrid = null!;

    public DailyJournalDetailsForm(AppDatabase.DailySessionInfo session, List<AppDatabase.DailyAccountSummary> balances, string userName)
    {
        this.session = session;
        this.balances = balances;
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        LoadMoves();
        InputFocusHelper.Apply(this);
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"📅 تفاصيل يومية {session.BusinessDate:yyyy-MM-dd}", 1320, 820);
        UiKit.EnableEscape(this);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = UiKit.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); // العنوان + الحالة
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190)); // جدول الأرصدة
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));  // الشفتات
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // جدول الحركات
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.Controls.Add(UiKit.Title($"📅 يومية {session.BusinessDate:yyyy-MM-dd}", 18), 0, 0);
        var info = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = session.Status == "CLOSED" ? UiKit.Danger : UiKit.Success,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = $"الحالة: {(session.Status == "CLOSED" ? "مغلقة" : "مفتوحة")}   |   فتحها: {session.OpenedBy} {session.OpenedAt:HH:mm}   |   أغلقها: {session.ClosedBy ?? "—"} {(session.ClosedAt.HasValue ? session.ClosedAt.Value.ToString("HH:mm") : "")}"
        };
        header.Controls.Add(info, 0, 1);
        root.Controls.Add(header, 0, 0);

        var balancePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Color.FromArgb(244, 246, 249) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = balances.Count + 1, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single, BackColor = Color.White };
        string[] heads = { "الحساب", "البداية", "الداخل", "الخارج", "المتوقع", "الفعلي", "الفرق" };
        for (int i = 0; i < heads.Length; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, i == 0 ? 22 : 13));
            table.Controls.Add(new Label { Dock = DockStyle.Fill, Text = heads[i], TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), BackColor = Color.FromArgb(52, 73, 94), ForeColor = Color.White }, i, 0);
        }
        for (int i = 0; i < balances.Count; i++)
        {
            var b = balances[i];
            string name = b.AccountKey switch { "OPAY" => "OPay", "BEE" => "باب رزق", "CASH" => "فودافون كاش / محافظ أخرى", "INSTAPAY" => "إنستا باي", "SALES" => "المبيعات", "PRINTING" => "الطباعة والتصوير", "DRAWER" => "النقدية في الدرج", "PERCENTAGE" => "النسبة (عمولات)", _ => b.AccountKey };
            string[] vals = { name, b.Opening.ToString("N2"), b.Incoming.ToString("N2"), b.Outgoing.ToString("N2"), b.Expected.ToString("N2"), b.Actual?.ToString("N2") ?? "—", b.Difference?.ToString("N2") ?? "—" };
            for (int col = 0; col < vals.Length; col++)
                table.Controls.Add(new Label { Dock = DockStyle.Fill, Text = vals[col], TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = col == 6 && b.Difference.HasValue && b.Difference.Value != 0 ? UiKit.Danger : Color.Black }, col, i + 1);
        }
        balancePanel.Controls.Add(table);
        root.Controls.Add(balancePanel, 0, 1);

        var shiftsLabel = new Label { Dock = DockStyle.Fill, BackColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(14, 4, 14, 4) };
        using (var c = AppDatabase.OpenConnection())
        {
            var shifts = AppDatabase.GetDailyShifts(c, session.Id);
            shiftsLabel.Text = shifts.Count == 0 ? "👤 الشفتات: لا توجد" : "👤 الشفتات: " + string.Join("   |   ", shifts.Select(x => $"{x.ShiftName} — {x.UserName} — {x.StartedAt:HH:mm}-{(x.EndedAt.HasValue ? x.EndedAt.Value.ToString("HH:mm") : "مفتوح")}"));
        }
        root.Controls.Add(shiftsLabel, 0, 2);

        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Color.FromArgb(244, 246, 249) };
        movesGrid = new SafeDataGridView();
        UiKit.StyleGrid(movesGrid);
        movesGrid.Dock = DockStyle.Fill;
        movesGrid.Columns.Add(UiKit.GridText("Time", "التاريخ والوقت", 145));
        movesGrid.Columns.Add(UiKit.GridText("Account", "الحساب", 120));
        movesGrid.Columns.Add(UiKit.GridText("Service", "الخدمة", 180));
        movesGrid.Columns.Add(UiKit.GridText("Customer", "العميل", 140));
        movesGrid.Columns.Add(UiKit.GridText("Type", "الحركة", 80));
        movesGrid.Columns.Add(UiKit.GridText("Amount", "المبلغ", 110));
        movesGrid.Columns.Add(UiKit.GridText("Description", "التفاصيل", 260, true));
        movesGrid.Columns.Add(UiKit.GridText("User", "بواسطة", 100));
        movesGrid.Columns.Add(UiKit.GridText("Shift", "الشفت", 110));
        movesGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= movesGrid.Rows.Count) return;
            if (movesGrid.Rows[e.RowIndex].Tag is not DailyMovementDetailsRef reference) return;
            using var details = new OperationDetailsForm(reference.ReferenceType, reference.ReferenceId, reference.MovementId, currentUser);
            details.ShowDialog(this);
            LoadMoves();
        };
        gridHost.Controls.Add(movesGrid);
        root.Controls.Add(gridHost, 0, 3);
    }

    private void LoadMoves()
    {
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT t.CreatedAt,t.AccountKey,t.ReferenceType,t.ReferenceId,
       COALESCE(tr.ServiceName,cs.ServiceType,t.Category,''),
       COALESCE(c.Name,cs.CustomerName,''),t.MovementType,t.Amount,COALESCE(t.Description,''),u.UserName,
       COALESCE(ds.ShiftKey,''),COALESCE(pl.FileName,''),COALESCE(pl.Pages,0),t.Id
FROM TreasuryMovements t
JOIN AppUsers u ON u.Id=t.UserId
LEFT JOIN PrintLog pl ON t.ReferenceType='PrintLog' AND t.ReferenceId=pl.Id
LEFT JOIN Transactions tr ON t.ReferenceId=tr.Id AND t.ReferenceType IN ('ServiceTransaction','Transaction','Copy')
LEFT JOIN Cash_Services cs ON t.ReferenceType='CashService' AND t.ReferenceId=cs.Id
LEFT JOIN Customers c ON tr.CustomerId=c.Id
LEFT JOIN DailyShifts ds ON ds.Id=t.ShiftId
WHERE date(t.CreatedAt)=date($date)
  AND (t.ShiftId IS NULL OR ds.SessionId=$session)
ORDER BY t.CreatedAt ASC;";
        cmd.Parameters.AddWithValue("$date", session.BusinessDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$session", session.Id);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string account = r.IsDBNull(1) ? "CASH" : r.GetString(1);
            string name = account switch { "OPAY" => "OPay", "BEE" => "باب رزق", "CASH" => "فودافون كاش / محافظ أخرى", "INSTAPAY" => "إنستا باي", "SALES" => "المبيعات", "PRINTING" => "الطباعة والتصوير", "DRAWER" => "النقدية في الدرج", "PERCENTAGE" => "النسبة (عمولات)", _ => account };
            string shift = r.IsDBNull(10) || string.IsNullOrWhiteSpace(r.GetString(10)) ? "غير مرتبط" : (r.GetString(10) == "MORNING" ? "صباحي" : "مسائي");
            string fileName = r.IsDBNull(11) ? "" : r.GetString(11);
            int pageCount = r.IsDBNull(12) ? 0 : r.GetInt32(12);
            string description = string.IsNullOrWhiteSpace(fileName)
                ? r.GetString(8)
                : OperationDetailsForm.FormatFileNameWithPages(fileName, pageCount);
            int index = movesGrid.Rows.Add(DateTime.Parse(r.GetString(0)).ToString("yyyy-MM-dd HH:mm"), name, r.GetString(4), r.GetString(5), r.GetString(6) == "IN" ? "داخل" : "خارج", r.GetDecimal(7).ToString("N2", CultureInfo.InvariantCulture), description, r.GetString(9), shift);
            movesGrid.Rows[index].Tag = new DailyMovementDetailsRef(r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? 0 : r.GetInt64(3), r.GetInt64(13));
        }
    }
}

internal sealed record DailyMovementDetailsRef(string ReferenceType, long ReferenceId, long MovementId);

internal sealed class LegacyDailyDetailsForm : Form
{
    private readonly DateTime date;
    private readonly string currentUser;
    private DataGridView grid = null!;
    private Label summary = null!;

    public LegacyDailyDetailsForm(DateTime date, string userName)
    {
        this.date = date.Date;
        currentUser = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName;
        AppDatabase.Initialize();
        BuildUi();
        LoadData();
        InputFocusHelper.Apply(this);
    }

    private void BuildUi()
    {
        UiKit.PrepareForm(this, $"📅 تفاصيل يوم {date:yyyy-MM-dd}", 1250, 760);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        UiKit.EnableEscape(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = UiKit.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(root);

        var titleBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20, 8, 20, 8) };
        titleBar.Controls.Add(UiKit.Title($"📅 تفاصيل حركة يوم {date:yyyy-MM-dd}", 18));
        root.Controls.Add(titleBar, 0, 0);

        summary = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = UiKit.Primary,
            BackColor = Color.White
        };
        root.Controls.Add(summary, 0, 1);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = UiKit.Background };
        grid = new SafeDataGridView();
        UiKit.StyleGrid(grid);
        grid.Dock = DockStyle.Fill;
        grid.Columns.Add(UiKit.GridText("Time", "التاريخ والوقت", 145));
        grid.Columns.Add(UiKit.GridText("Type", "الحركة", 85));
        grid.Columns.Add(UiKit.GridText("Category", "التصنيف", 150));
        grid.Columns.Add(UiKit.GridText("Account", "الحساب", 125));
        grid.Columns.Add(UiKit.GridText("Amount", "المبلغ", 110));
        grid.Columns.Add(UiKit.GridText("Description", "التفاصيل", 360, true));
        grid.Columns.Add(UiKit.GridText("User", "بواسطة", 110));
        grid.Columns.Add(UiKit.GridText("Shift", "الشفت", 100));
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            if (grid.Rows[e.RowIndex].Tag is not DailyMovementDetailsRef reference) return;
            using var details = new OperationDetailsForm(reference.ReferenceType, reference.ReferenceId, reference.MovementId, currentUser);
            details.ShowDialog(this);
            LoadData();
        };
        host.Controls.Add(grid);
        root.Controls.Add(host, 0, 2);
    }

    private void LoadData()
    {
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT t.CreatedAt,t.MovementType,COALESCE(pl.PricingCategory,tr.PricingCategory,t.Category,''),t.AccountKey,t.Amount,COALESCE(t.Description,''),u.UserName,COALESCE(ds.ShiftKey,''),
       t.ReferenceType,COALESCE(t.ReferenceId,0),COALESCE(pl.FileName,''),COALESCE(pl.Pages,0),t.Id
FROM TreasuryMovements t
JOIN AppUsers u ON u.Id=t.UserId
LEFT JOIN PrintLog pl ON t.ReferenceType='PrintLog' AND t.ReferenceId=pl.Id
LEFT JOIN Transactions tr ON t.ReferenceId=tr.Id AND t.ReferenceType IN ('ServiceTransaction','Transaction','Copy')
LEFT JOIN DailyShifts ds ON ds.Id=t.ShiftId
WHERE substr(t.CreatedAt,1,10)=$date
ORDER BY t.CreatedAt ASC;";
        cmd.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        decimal incoming = 0m, outgoing = 0m;
        int count = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string account = r.IsDBNull(3) ? "CASH" : r.GetString(3);
            string accountName = account switch { "OPAY" => "OPay", "BEE" => "باب رزق", "CASH" => "فودافون كاش / محافظ أخرى", "INSTAPAY" => "إنستا باي", "SALES" => "المبيعات", "PRINTING" => "الطباعة والتصوير", "DRAWER" => "النقدية في الدرج", "PERCENTAGE" => "النسبة (عمولات)", _ => account };
            string type = r.GetString(1) == "IN" ? "داخل" : "خارج";
            decimal amount = r.GetDecimal(4);
            // حركات «النسبة» معروضة للتفصيل فقط؛ لا تدخل ثانية في إجمالي النقد الداخل/الخارج.
            if (!string.Equals(account, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
            {
                if (r.GetString(1) == "IN") incoming += amount; else outgoing += amount;
            }
            string shift = r.IsDBNull(7) || string.IsNullOrWhiteSpace(r.GetString(7)) ? "غير مرتبط" : (r.GetString(7) == "MORNING" ? "صباحي" : "مسائي");
            string fileName = r.IsDBNull(10) ? "" : r.GetString(10);
            int pageCount = r.IsDBNull(11) ? 0 : r.GetInt32(11);
            string description = string.IsNullOrWhiteSpace(fileName) ? r.GetString(5) : OperationDetailsForm.FormatFileNameWithPages(fileName, pageCount);
            int index = grid.Rows.Add(DateTime.Parse(r.GetString(0)).ToString("yyyy-MM-dd HH:mm"), type, r.GetString(2), accountName, amount.ToString("N2", CultureInfo.InvariantCulture), description, r.GetString(6), shift);
            grid.Rows[index].Tag = new DailyMovementDetailsRef(r.IsDBNull(8) ? "" : r.GetString(8), r.GetInt64(9), r.GetInt64(12));
            count++;
        }
        summary.Text = $"الحركات: {count}   |   الداخل: {incoming:N2} ج.م   |   الخارج: {outgoing:N2} ج.م   |   الصافي: {(incoming - outgoing):N2} ج.م";
    }
}
