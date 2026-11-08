using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

// ============================================================
// نماذج بيانات الفاتورة
// ============================================================

/// <summary> سطر في جدول المبيعات: م | البيان | سعر الوحدة | العدد | الإجمالي </summary>
internal sealed class InvoiceSaleLine
{
    public InvoiceSaleLine() { }
    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal Total { get; set; }
}

/// <summary> سطر في جدول الطباعة والتصوير: م | البيان | عدد الصفحات | عدد الأوراق | سعر الورقة | الإجمالي </summary>
internal sealed class InvoicePrintLine
{
    public InvoicePrintLine() { }
    public string Name { get; set; } = "";
    public int Pages { get; set; }
    public int Sheets { get; set; }
    public decimal SheetPrice { get; set; }
    public decimal Total { get; set; }
}

internal sealed class InvoiceData
{
    public int Number { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public string CustomerName { get; set; } = "";
    public List<InvoiceSaleLine> Sales { get; } = new();
    public List<InvoicePrintLine> Prints { get; } = new();
    public decimal Remaining { get; set; }

    public decimal SalesTotal => Sales.Sum(x => x.Total);
    public decimal PrintTotal => Prints.Sum(x => x.Total);
    public decimal Total => SalesTotal + PrintTotal;
    public decimal Paid => Math.Max(0m, Total - Remaining);
}

/// <summary> عملية واحدة من عمليات اليوم تظهر في قائمة اختيار الفاتورة. </summary>
internal sealed class OperationRow
{
    public string SourceType { get; set; } = "";   // TX أو PRINT
    public long SourceId { get; set; }
    public DateTime Time { get; set; }
    public string UserName { get; set; } = "";
    public string Kind { get; set; } = "";          // مبيعات / خدمة / تصوير / طباعة
    public string Text { get; set; } = "";          // الوصف الموحد
    public decimal Amount { get; set; }
    public decimal Remaining { get; set; }
    public string CustomerName { get; set; } = "";
    public int? InvoiceNumber { get; set; }
    public List<InvoiceSaleLine> SaleLines { get; } = new();
    public List<InvoicePrintLine> PrintLines { get; } = new();
}

// ============================================================
// صياغة وصف العمليات بشكل موحد (يُستخدم في الفاتورة وفي قائمة العمليات)
// ============================================================
internal static class OperationText
{
    private static string Count(int n, string one, string two, string few, string many)
    {
        if (n == 1) return one;
        if (n == 2) return two;
        if (n >= 3 && n <= 10) return $"{n} {few}";
        return $"{n} {many}";
    }

    public static string Sheets(int n) => Count(n, "ورقة واحدة", "ورقتين", "ورقات", "ورقة");
    public static string Pages(int n) => Count(n, "صفحة واحدة", "صفحتين", "صفحات", "صفحة");

    public static string Color(string? color)
        => (color ?? "").Contains("ألوان") ? "ألوان" : "أبيض وأسود";

    public static bool IsDuplex(string? duplex)
        => (duplex ?? "").Contains("وظهر");

    /// <summary>
    /// مثال: «طباعة 5 ورقات ألوان» — «طباعة 20 صفحة أبيض وأسود وش وظهر».
    /// </summary>
    public static string Print(string verb, int faces, int sheets, string? color, string? duplex)
    {
        bool dup = IsDuplex(duplex);
        string amount = (!dup && faces == sheets) ? Sheets(faces) : Pages(faces);
        return $"{verb} {amount} {Color(color)}{(dup ? " وش وظهر" : "")}";
    }

    public static string Money(decimal value)
        => value.ToString("F2", CultureInfo.InvariantCulture);

    public static string NormalizeDigits(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c >= '٠' && c <= '٩') chars[i] = (char)('0' + (c - '٠'));
            else if (c >= '۰' && c <= '۹') chars[i] = (char)('0' + (c - '۰'));
        }
        return new string(chars);
    }

    public static bool TryParseMoney(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = NormalizeDigits(text).Trim().Replace("٬", "").Replace('٫', '.');
        if (s.Contains(',') && !s.Contains('.')) s = s.Replace(',', '.');
        else s = s.Replace(",", "");
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    /// <summary> يستخرج وصف الخدمة من النص المخزن: «خدمة: X | وصف | العميل: ...». </summary>
    public static string Service(string? serviceName, string? full)
    {
        string svc = string.IsNullOrWhiteSpace(serviceName) ? "خدمة" : serviceName.Trim();
        if (string.IsNullOrWhiteSpace(full)) return svc;

        string prefix = $"خدمة: {svc} | ";
        if (full.StartsWith(prefix, StringComparison.Ordinal))
        {
            string rest = full.Substring(prefix.Length);
            int cut = rest.IndexOf(" | العميل:", StringComparison.Ordinal);
            string extra = (cut >= 0 ? rest.Substring(0, cut) : rest).Trim();
            if (extra.Length > 0 && !extra.StartsWith("العميل:", StringComparison.Ordinal))
                return $"{svc} - {extra}";
        }
        return svc;
    }

    private static readonly Regex CopyPart = new(
        @"(أسود وش وظهر|أسود وش|ألوان وش وظهر|ألوان وش)\s*:\s*(\d+)\s*×\s*([0-9٠-٩\.,٫٬]+)",
        RegexOptions.Compiled);

    /// <summary> يفك تفاصيل التصوير المخزنة في الوصف إلى أسطر جاهزة للفاتورة. </summary>
    public static List<InvoicePrintLine> CopyLines(string? details)
    {
        var result = new List<InvoicePrintLine>();
        if (string.IsNullOrWhiteSpace(details)) return result;

        foreach (Match m in CopyPart.Matches(details))
        {
            string kind = m.Groups[1].Value;
            if (!int.TryParse(NormalizeDigits(m.Groups[2].Value), NumberStyles.None, CultureInfo.InvariantCulture, out int sheets) || sheets <= 0)
                continue;
            if (!TryParseMoney(m.Groups[3].Value, out decimal price))
                continue;

            bool duplex = kind.EndsWith("وظهر", StringComparison.Ordinal);
            string color = kind.StartsWith("ألوان", StringComparison.Ordinal) ? "ألوان" : "أسود";
            int pages = duplex ? sheets * 2 : sheets;

            result.Add(new InvoicePrintLine
            {
                Name = Print("تصوير", pages, sheets, color, duplex ? "وش وظهر" : "وش واحد"),
                Pages = pages,
                Sheets = sheets,
                SheetPrice = price,
                Total = sheets * price
            });
        }
        return result;
    }
}

// ============================================================
// جلسة «ابدأ فاتورة»: تفضل شغالة حتى لو اتقفلت شاشة المبيعات
// ============================================================
internal static class InvoiceSession
{
    public static bool Active { get; private set; }
    public static DateTime Since { get; private set; }
    public static string UserName { get; private set; } = "";

    public static void Start(string userName)
    {
        Active = true;
        Since = DateTime.Now.AddSeconds(-1);
        UserName = userName;
    }

    public static void End()
    {
        Active = false;
        UserName = "";
    }
}

// ============================================================
// قاعدة بيانات الفواتير + قراءة عمليات اليوم وتحويلها لأسطر فاتورة
// ============================================================
internal static class InvoiceStore
{
    public static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS Invoices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Number INTEGER NOT NULL,
    CustomerName TEXT NULL,
    Total NUMERIC NOT NULL DEFAULT 0,
    UserName TEXT NULL,
    CreatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS InvoiceItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL,
    SourceType TEXT NOT NULL,
    SourceId INTEGER NOT NULL,
    FOREIGN KEY(InvoiceId) REFERENCES Invoices(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_InvoiceItems_Source ON InvoiceItems(SourceType, SourceId);
CREATE TABLE IF NOT EXISTS InvoiceSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NULL
);";
        command.ExecuteNonQuery();
        EnsureColumn(connection, "Invoices", "SnapshotJson", "TEXT NULL");
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string declaration)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        reader.Close();
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        alter.ExecuteNonQuery();
    }

    private sealed class InvoiceSnapshotDto
    {
        public InvoiceSnapshotDto() { }
        public int Number { get; set; }
        public DateTime Date { get; set; }
        public string CustomerName { get; set; } = "";
        public decimal Remaining { get; set; }
        public List<InvoiceSaleLine> Sales { get; set; } = new();
        public List<InvoicePrintLine> Prints { get; set; } = new();
    }

    private static string CreateSnapshotJson(InvoiceData data) => JsonSerializer.Serialize(new InvoiceSnapshotDto
    {
        Number = data.Number,
        Date = data.Date,
        CustomerName = data.CustomerName ?? "",
        Remaining = data.Remaining,
        Sales = data.Sales.Select(x => new InvoiceSaleLine { Name = x.Name, UnitPrice = x.UnitPrice, Quantity = x.Quantity, Total = x.Total }).ToList(),
        Prints = data.Prints.Select(x => new InvoicePrintLine { Name = x.Name, Pages = x.Pages, Sheets = x.Sheets, SheetPrice = x.SheetPrice, Total = x.Total }).ToList()
    });

    private static InvoiceData? TryRestoreSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<InvoiceSnapshotDto>(json);
            if (snapshot == null || snapshot.Number <= 0 || ((snapshot.Sales?.Count ?? 0) + (snapshot.Prints?.Count ?? 0)) == 0) return null;
            var data = new InvoiceData { Number = snapshot.Number, Date = snapshot.Date, CustomerName = snapshot.CustomerName ?? "" };
            if (snapshot.Sales != null) data.Sales.AddRange(snapshot.Sales);
            if (snapshot.Prints != null) data.Prints.AddRange(snapshot.Prints);
            data.Remaining = Math.Min(data.Total, Math.Max(0m, snapshot.Remaining));
            return data;
        }
        catch (JsonException) { return null; }
    }

    public static string? GetSetting(string key)
    {
        try
        {
            AppDatabase.Initialize();
            using var c = AppDatabase.OpenConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Value FROM InvoiceSettings WHERE Key=$k";
            cmd.Parameters.AddWithValue("$k", key);
            object? v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        catch { return null; }
    }

    public static void SetSetting(string key, string value)
    {
        try
        {
            AppDatabase.Initialize();
            using var c = AppDatabase.OpenConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO InvoiceSettings(Key,Value) VALUES($k,$v) ON CONFLICT(Key) DO UPDATE SET Value=$v";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { AppLog.Error("تعذر حفظ إعداد الفاتورة", ex); }
    }

    public static int NextNumber()
    {
        AppDatabase.Initialize();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Number),0)+1 FROM Invoices";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static DateTime ParseTime(string? text)
        => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.Now;

    private static decimal Dec(object value)
        => value == null || value == DBNull.Value ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    /// <summary> كل عمليات يوم معيّن (مبيعات / خدمات / تصوير / طباعة) بالوصف الموحد، الأحدث أولًا. </summary>
    public static List<OperationRow> LoadOperations(DateTime day)
    {
        AppDatabase.Initialize();
        var rows = new List<OperationRow>();
        string dayText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var c = AppDatabase.OpenConnection();

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT t.Id, t.CreatedAt, u.UserName, t.TransactionType, t.Description, t.ServiceName,
       t.RequiredAmount, t.RemainingAmount, COALESCE(t.Quantity,1), COALESCE(cu.Name,''),
       (SELECT MAX(i.Number) FROM InvoiceItems ii JOIN Invoices i ON i.Id=ii.InvoiceId
        WHERE ii.SourceType='TX' AND ii.SourceId=t.Id)
FROM Transactions t
JOIN AppUsers u ON u.Id=t.UserId
LEFT JOIN Customers cu ON cu.Id=t.CustomerId
WHERE substr(t.CreatedAt,1,10)=$day AND t.TransactionType IN ('SALE','SERVICE','COPY')
ORDER BY t.CreatedAt DESC, t.Id DESC";
            cmd.Parameters.AddWithValue("$day", dayText);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new OperationRow
                {
                    SourceType = "TX",
                    SourceId = r.GetInt64(0),
                    Time = ParseTime(r.GetString(1)),
                    UserName = r.GetString(2),
                    Amount = Dec(r.GetValue(6)),
                    Remaining = Dec(r.GetValue(7)),
                    CustomerName = r.GetString(9),
                    InvoiceNumber = r.IsDBNull(10) ? null : Convert.ToInt32(r.GetValue(10), CultureInfo.InvariantCulture)
                };

                string type = r.GetString(3);
                string description = r.IsDBNull(4) ? "" : r.GetString(4);
                string? service = r.IsDBNull(5) ? null : r.GetString(5);
                int qty = Math.Max(1, Convert.ToInt32(r.GetValue(8), CultureInfo.InvariantCulture));

                if (type == "SALE")
                {
                    row.Kind = "مبيعات";
                    string name = string.IsNullOrWhiteSpace(description) ? "بيع" : description.Trim();
                    row.Text = qty > 1 ? $"{name} × {qty}" : name;
                    row.SaleLines.Add(new InvoiceSaleLine
                    {
                        Name = name,
                        Quantity = qty,
                        UnitPrice = Math.Round(row.Amount / qty, 2, MidpointRounding.AwayFromZero),
                        Total = row.Amount
                    });
                }
                else if (type == "SERVICE")
                {
                    // الخدمات (مثل شحن كارت الكهرباء): المبلغ الكلي في سطر واحد بدون ذكر العمولة.
                    row.Kind = "خدمة";
                    string name = OperationText.Service(service, description);
                    row.Text = name;
                    row.SaleLines.Add(new InvoiceSaleLine { Name = name, Quantity = 1, UnitPrice = row.Amount, Total = row.Amount });
                }
                else
                {
                    row.Kind = "تصوير";
                    var lines = OperationText.CopyLines(description);
                    if (lines.Count > 0)
                    {
                        row.PrintLines.AddRange(lines);
                        row.Text = string.Join(" + ", lines.Select(l => l.Name));
                    }
                    else
                    {
                        row.Text = "تصوير";
                        row.SaleLines.Add(new InvoiceSaleLine { Name = "تصوير", Quantity = 1, UnitPrice = row.Amount, Total = row.Amount });
                    }
                }
                rows.Add(row);
            }
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT p.Id, p.Timestamp, u.UserName, p.Pages, p.Copies, COALESCE(p.ColorType,''), COALESCE(p.DuplexMode,''),
       p.PhysicalSheets, p.Price,
       (SELECT MAX(i.Number) FROM InvoiceItems ii JOIN Invoices i ON i.Id=ii.InvoiceId
        WHERE ii.SourceType='PRINT' AND ii.SourceId=p.Id)
FROM PrintLog p
JOIN AppUsers u ON u.Id=p.UserId
WHERE substr(p.Timestamp,1,10)=$day
ORDER BY p.Timestamp DESC, p.Id DESC";
            cmd.Parameters.AddWithValue("$day", dayText);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int pages = Math.Max(1, r.GetInt32(3));
                int copies = Math.Max(1, r.GetInt32(4));
                int faces = pages * copies;
                int sheets = Math.Max(1, r.GetInt32(7));
                decimal price = Dec(r.GetValue(8));
                string name = OperationText.Print("طباعة", faces, sheets, r.GetString(5), r.GetString(6));

                var row = new OperationRow
                {
                    SourceType = "PRINT",
                    SourceId = r.GetInt64(0),
                    Time = ParseTime(r.GetString(1)),
                    UserName = r.GetString(2),
                    Kind = "طباعة",
                    Text = name,
                    Amount = price,
                    InvoiceNumber = r.IsDBNull(9) ? null : Convert.ToInt32(r.GetValue(9), CultureInfo.InvariantCulture)
                };
                row.PrintLines.Add(new InvoicePrintLine
                {
                    Name = name,
                    Pages = faces,
                    Sheets = sheets,
                    SheetPrice = Math.Round(price / sheets, 2, MidpointRounding.AwayFromZero),
                    Total = price
                });
                rows.Add(row);
            }
        }

        return rows.OrderByDescending(x => x.Time).ToList();
    }

    public static List<(long Id, int Number, string Customer, decimal Total, DateTime CreatedAt)> LoadSavedInvoices(string? search = null)
    {
        AppDatabase.Initialize();
        var result = new List<(long, int, string, decimal, DateTime)>();
        string q = (search ?? "").Trim();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT Id,Number,COALESCE(CustomerName,''),Total,CreatedAt FROM Invoices
WHERE $q='' OR CAST(Number AS TEXT) LIKE $p OR COALESCE(CustomerName,'') LIKE $p
ORDER BY CreatedAt DESC,Id DESC LIMIT 500";
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$p", "%" + q + "%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime created = ParseTime(r.GetString(4));
            result.Add((r.GetInt64(0), Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture), r.GetString(2), Dec(r.GetValue(3)), created));
        }
        return result;
    }

    public static bool TryLoadSavedInvoice(long invoiceId, out InvoiceData data, out List<OperationRow> operations)
    {
        AppDatabase.Initialize();
        data = new InvoiceData();
        operations = new List<OperationRow>();
        int number;
        string customer;
        DateTime createdAt;
        string? snapshotJson;
        var sources = new List<(string Type, long Id, string Day)>();
        using (var c = AppDatabase.OpenConnection())
        {
            using var header = c.CreateCommand();
            header.CommandText = "SELECT Number,COALESCE(CustomerName,''),CreatedAt,SnapshotJson FROM Invoices WHERE Id=$id";
            header.Parameters.AddWithValue("$id", invoiceId);
            using var r = header.ExecuteReader();
            if (!r.Read()) return false;
            number = Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture);
            customer = r.GetString(1);
            createdAt = ParseTime(r.GetString(2));
            snapshotJson = r.IsDBNull(3) ? null : r.GetString(3);
            r.Close();

            using var items = c.CreateCommand();
            items.CommandText = @"SELECT ii.SourceType,ii.SourceId,
CASE WHEN ii.SourceType='TX' THEN (SELECT substr(t.CreatedAt,1,10) FROM Transactions t WHERE t.Id=ii.SourceId)
     WHEN ii.SourceType='PRINT' THEN (SELECT substr(p.Timestamp,1,10) FROM PrintLog p WHERE p.Id=ii.SourceId)
     ELSE NULL END
FROM InvoiceItems ii WHERE ii.InvoiceId=$id";
            items.Parameters.AddWithValue("$id", invoiceId);
            using var ir = items.ExecuteReader();
            while (ir.Read())
                if (!ir.IsDBNull(2)) sources.Add((ir.GetString(0), ir.GetInt64(1), ir.GetString(2)));
        }
        var restored = TryRestoreSnapshot(snapshotJson);
        if (restored != null)
        {
            restored.CustomerName = customer;
            data = restored;
            return true;
        }

        var dates = sources.Select(x => x.Day).Distinct(StringComparer.Ordinal).ToList();
        var lookup = new Dictionary<string, OperationRow>(StringComparer.Ordinal);
        foreach (string dayText in dates)
        {
            if (!DateTime.TryParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            foreach (var row in LoadOperations(day)) lookup[row.SourceType + ":" + row.SourceId.ToString(CultureInfo.InvariantCulture)] = row;
        }
        foreach (var source in sources)
        {
            string key = source.Type + ":" + source.Id.ToString(CultureInfo.InvariantCulture);
            if (lookup.TryGetValue(key, out var row) && !operations.Any(x => x.SourceType == row.SourceType && x.SourceId == row.SourceId))
                operations.Add(row);
        }
        if (operations.Count == 0 || operations.Count != sources.Count) return false;
        data = Build(operations, number);
        data.CustomerName = customer;
        data.Date = createdAt;
        return true;
    }

    /// <summary> يجمع العمليات المختارة في فاتورة واحدة (الأقدم أولًا). </summary>
    public static InvoiceData Build(IEnumerable<OperationRow> selected, int number)
    {
        var data = new InvoiceData { Number = number, Date = DateTime.Now };
        foreach (var row in selected.OrderBy(x => x.Time))
        {
            data.Sales.AddRange(row.SaleLines);
            data.Prints.AddRange(row.PrintLines);
            data.Remaining += row.Remaining;
            if (string.IsNullOrWhiteSpace(data.CustomerName) && !string.IsNullOrWhiteSpace(row.CustomerName))
                data.CustomerName = row.CustomerName;
        }
        return data;
    }

    /// <summary>
    /// يحفظ الفاتورة (أو يحدّثها لو اتحفظت قبل كده). طباعة الفاتورة نفسها
    /// لا تُسجَّل في سجل الطباعة ولا تدخل الخزينة.
    /// </summary>
    public static long Save(long existingId, InvoiceData data, string userName, IEnumerable<OperationRow> items)
    {
        AppDatabase.Initialize();
        using var c = AppDatabase.OpenConnection();
        using var tx = c.BeginTransaction();

        long id = existingId;
        string snapshotJson = CreateSnapshotJson(data);
        if (id <= 0)
        {
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
INSERT INTO Invoices(Number,CustomerName,Total,UserName,CreatedAt,SnapshotJson) VALUES($n,$c,$t,$u,$d,$snapshot);
SELECT last_insert_rowid();";
            ins.Parameters.AddWithValue("$n", data.Number);
            ins.Parameters.AddWithValue("$c", data.CustomerName ?? "");
            ins.Parameters.AddWithValue("$t", data.Total);
            ins.Parameters.AddWithValue("$u", userName);
            ins.Parameters.AddWithValue("$d", DateTime.Now.ToString("O"));
            ins.Parameters.AddWithValue("$snapshot", snapshotJson);
            id = Convert.ToInt64(ins.ExecuteScalar(), CultureInfo.InvariantCulture);

            foreach (var item in items)
            {
                using var it = c.CreateCommand();
                it.Transaction = tx;
                it.CommandText = "INSERT INTO InvoiceItems(InvoiceId,SourceType,SourceId) VALUES($i,$s,$id)";
                it.Parameters.AddWithValue("$i", id);
                it.Parameters.AddWithValue("$s", item.SourceType);
                it.Parameters.AddWithValue("$id", item.SourceId);
                it.ExecuteNonQuery();
            }
        }
        else
        {
            using var upd = c.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE Invoices SET Number=$n, CustomerName=$c, Total=$t, SnapshotJson=$snapshot WHERE Id=$id";
            upd.Parameters.AddWithValue("$n", data.Number);
            upd.Parameters.AddWithValue("$c", data.CustomerName ?? "");
            upd.Parameters.AddWithValue("$t", data.Total);
            upd.Parameters.AddWithValue("$snapshot", snapshotJson);
            upd.Parameters.AddWithValue("$id", id);
            upd.ExecuteNonQuery();
        }

        tx.Commit();
        return id;
    }
}
