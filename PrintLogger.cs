using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LibraryPrintApp
{
    // ============================================================
    // سجل الطباعة: يحفظ كل عملية طباعة في ملف CSV بجانب البرنامج
    // ويوفر قراءتها لاستخدامها في شاشة الملخصات.
    // ============================================================
    public static class PrintLogger
    {
        // ============================================================
        // مجلد بيانات المستخدم (AppData\Roaming) - دايمًا قابل للكتابة
        // لأي مستخدم، حتى لو البرنامج مسطب في Program Files المحمي.
        // بيفضل السجل موجود برضو حتى لو البرنامج اتحدّث أو اتسطب
        // من جديد، لأنه مش جوه مجلد التسطيب.
        // ============================================================
        private static readonly string LogFolderPath =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "LibraryPrintApp");

        private static readonly string LogFilePath =
            Path.Combine(LogFolderPath, "PrintLog.csv");

        private static readonly string PendingLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LibraryPrintApp", "pending-print-logs.jsonl");
        private static readonly object PendingSync = new();

        /// <summary>مسار ملف PrintLog.csv (بيدخل في النسخة الاحتياطية الشاملة).</summary>
        internal static string CsvPath => LogFilePath;

        public sealed class LogEntry
        {
            public long Id { get; set; }
            public string PricingCategory { get; set; } = "";
            public string PrintOptionsJson { get; set; } = "";
            public DateTime Timestamp { get; set; }
            public string UserName { get; set; } = "";
            public string FileName { get; set; } = "";
            public string FileType { get; set; } = "";
            public int Pages { get; set; }
            public int Copies { get; set; }
            public string ColorType { get; set; } = "";
            public string DuplexMode { get; set; } = "";
            public int PhysicalSheets { get; set; }
            public decimal Price { get; set; }
            // مفتاح يمنع تكرار نفس العملية عند إعادة محاولة حفظ عملية معلقة.
            public string OperationToken { get; set; } = Guid.NewGuid().ToString("N");
            // خاص بمزامنة CSV مع قاعدة البيانات؛ لا يُعرض للمستخدم في ملف CSV.
            public bool CsvWritten { get; set; }
        }

        public static long Log(LogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entry.Timestamp == default) entry.Timestamp = DateTime.Now;
            if (string.IsNullOrWhiteSpace(entry.OperationToken)) entry.OperationToken = Guid.NewGuid().ToString("N");

            // عند توفر قاعدة البيانات والشفت، نحاول مصالحة العمليات المعلقة من نفس اليوم أولًا.
            _ = FlushPendingLogsForToday();

            try
            {
                long id = SaveEntryToDatabase(entry);
                try
                {
                    AppendCsvEntry(entry);
                    entry.CsvWritten = true;
                }
                catch (Exception csvEx)
                {
                    AppLog.Error("تم تسجيل الطباعة ماليًا في قاعدة البيانات لكن تعذر تحديث PrintLog.csv", csvEx);
                    try { EnqueuePendingEntry(entry); }
                    catch (Exception queueEx) { AppLog.Error("تعذر حفظ عملية الطباعة المعلقة لمزامنة CSV", queueEx); }
                }
                return id;
            }
            catch (Exception dbEx)
            {
                bool csvOk = false;
                try
                {
                    AppendCsvEntry(entry);
                    entry.CsvWritten = true;
                    csvOk = true;
                }
                catch (Exception csvEx)
                {
                    AppLog.Error("تعذر حفظ عملية الطباعة في CSV بعد فشل قاعدة البيانات", csvEx);
                }

                bool queued = false;
                try { EnqueuePendingEntry(entry); queued = true; }
                catch (Exception queueEx) { AppLog.Error("تعذر حفظ عملية الطباعة في قائمة الانتظار", queueEx); }

                AppLog.Error("تعذر تسجيل حركة الطباعة في قاعدة البيانات", dbEx);
                string recoveryText = queued
                    ? "تم حفظ تفاصيلها في قائمة انتظار محلية وستُعاد محاولة تسجيلها في قاعدة البيانات عند توفرها."
                    : csvOk
                        ? "تفاصيلها موجودة في PrintLog.csv، لكن لم يتم حفظها في قائمة إعادة المحاولة. راجع السجل المالي يدويًا."
                        : "تعذر حفظها في قاعدة البيانات وفي ملف السجل. راجع سجل الأخطاء قبل متابعة الحسابات.";
                throw new InvalidOperationException("تمت الطباعة فعليًا، لكن تعذر تسجيل العملية ماليًا. " + recoveryText, dbEx);
            }
        }

        private static long SaveEntryToDatabase(LogEntry entry)
        {
            using var connection = AppDatabase.OpenConnection();
            using var transaction = connection.BeginTransaction();

            // لو حدث انقطاع بعد اعتماد العملية وقبل إزالة ملف الانتظار، نعيد المعرف نفسه دون تكرار الحركة المالية.
            using (var existing = connection.CreateCommand())
            {
                existing.Transaction = transaction;
                existing.CommandText = "SELECT Id FROM PrintLog WHERE OperationToken=$token LIMIT 1";
                existing.Parameters.AddWithValue("$token", entry.OperationToken);
                object? existingId = existing.ExecuteScalar();
                if (existingId != null && existingId != DBNull.Value)
                {
                    transaction.Commit();
                    entry.Id = Convert.ToInt64(existingId, CultureInfo.InvariantCulture);
                    return entry.Id;
                }
            }

            long userId = AppDatabase.GetUserId(connection, entry.UserName, transaction);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO PrintLog(UserId,Timestamp,FileName,FileType,Pages,Copies,ColorType,DuplexMode,PhysicalSheets,Price,OperationToken,PricingCategory,PrintOptionsJson)
VALUES($user,$timestamp,$file,$type,$pages,$copies,$color,$duplex,$sheets,$price,$token,$category,$options);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$user", userId);
            command.Parameters.AddWithValue("$timestamp", entry.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$file", entry.FileName);
            command.Parameters.AddWithValue("$type", entry.FileType);
            command.Parameters.AddWithValue("$pages", entry.Pages);
            command.Parameters.AddWithValue("$copies", entry.Copies);
            command.Parameters.AddWithValue("$color", entry.ColorType);
            command.Parameters.AddWithValue("$duplex", entry.DuplexMode);
            command.Parameters.AddWithValue("$sheets", entry.PhysicalSheets);
            command.Parameters.AddWithValue("$price", entry.Price);
            command.Parameters.AddWithValue("$token", entry.OperationToken);
            command.Parameters.AddWithValue("$category", (object?)entry.PricingCategory ?? DBNull.Value);
            command.Parameters.AddWithValue("$options", (object?)entry.PrintOptionsJson ?? DBNull.Value);
            long id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            entry.Id = id;

            // الطباعة المجانية تُسجل في سجل الطباعة، لكنها لا تنشئ حركة مالية بصفر.
            if (entry.Price > 0m)
                AppDatabase.AddTreasury(connection, entry.UserName, "IN", "الطباعة", entry.Price,
                    entry.FileName, "PrintLog", id, transaction, "PRINTING");
            transaction.Commit();
            return id;
        }

        private static void AppendCsvEntry(LogEntry entry)
        {
            EnsureFileExists();
            string line = string.Join(",",
                Escape(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Escape(entry.UserName), Escape(entry.FileName), Escape(entry.FileType),
                entry.Pages.ToString(CultureInfo.InvariantCulture),
                entry.Copies.ToString(CultureInfo.InvariantCulture),
                Escape(entry.ColorType), Escape(entry.DuplexMode),
                entry.PhysicalSheets.ToString(CultureInfo.InvariantCulture),
                entry.Price.ToString("F2", CultureInfo.InvariantCulture),
                Escape(entry.PricingCategory), Escape(entry.PrintOptionsJson));
            lock (PendingSync)
            {
                EnsureCurrentCsvHeader();
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        private static void EnsureCurrentCsvHeader()
        {
            string? header = File.Exists(LogFilePath) ? File.ReadLines(LogFilePath, Encoding.UTF8).FirstOrDefault() : null;
            if (header == null || header.Contains("PricingCategory", StringComparison.Ordinal)) return;
            // ملف CSV القديم كان يحتوي على 10 أعمدة؛ نعيد كتابته مع الحفاظ على كل السجلات
            // وإضافة عمودي الفئة وإعدادات الطباعة الفارغين للسجلات القديمة.
            List<LogEntry> previousEntries = LoadAll();
            RewriteAll(previousEntries);
        }

        private static void EnqueuePendingEntry(LogEntry entry)
        {
            lock (PendingSync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PendingLogPath)!);
                var existingTokens = new HashSet<string>(StringComparer.Ordinal);
                if (File.Exists(PendingLogPath))
                {
                    foreach (string line in File.ReadAllLines(PendingLogPath, Encoding.UTF8))
                    {
                        try
                        {
                            var old = JsonSerializer.Deserialize<LogEntry>(line);
                            if (old != null && !string.IsNullOrWhiteSpace(old.OperationToken)) existingTokens.Add(old.OperationToken);
                        }
                        catch { /* نحافظ على السطر غير المفهوم في الملف ولا نوقف الطباعة. */ }
                    }
                }
                if (existingTokens.Contains(entry.OperationToken)) return;
                File.AppendAllText(PendingLogPath, JsonSerializer.Serialize(entry) + Environment.NewLine, Encoding.UTF8);
            }
        }

        /// <summary>
        /// يعيد محاولة حفظ العمليات المعلقة التي تخص اليوم الحالي فقط؛ لا ينقل عملية قديمة إلى شفت أو يوم جديد تلقائيًا.
        /// القيمة المعادة هي عدد العناصر التي ما زالت بحاجة إلى مراجعة/إعادة محاولة.
        /// </summary>
        public static int FlushPendingLogsForToday()
        {
            if (!File.Exists(PendingLogPath)) return 0;
            lock (PendingSync)
            {
                string[] lines;
                try { lines = File.ReadAllLines(PendingLogPath, Encoding.UTF8); }
                catch (Exception ex) { AppLog.Error("تعذر قراءة قائمة عمليات الطباعة المعلقة", ex); return 1; }

                var remaining = new List<string>();
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    LogEntry? entry;
                    try { entry = JsonSerializer.Deserialize<LogEntry>(line); }
                    catch { remaining.Add(line); continue; }
                    if (entry == null) { remaining.Add(line); continue; }

                    // العملية من تاريخ سابق تحتاج مراجعة بشرية بدل تسجيلها في شفت اليوم.
                    if (entry.Timestamp.Date != DateTime.Now.Date)
                    {
                        remaining.Add(line);
                        continue;
                    }

                    try
                    {
                        _ = SaveEntryToDatabase(entry);
                        if (!entry.CsvWritten)
                        {
                            AppendCsvEntry(entry);
                            entry.CsvWritten = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("ما زالت عملية طباعة معلقة ولم تُسجَّل في قاعدة البيانات", ex);
                        remaining.Add(JsonSerializer.Serialize(entry));
                    }
                }

                try
                {
                    string temp = PendingLogPath + ".tmp";
                    File.WriteAllLines(temp, remaining, Encoding.UTF8);
                    File.Move(temp, PendingLogPath, true);
                }
                catch (Exception ex)
                {
                    AppLog.Error("تعذر تحديث ملف قائمة عمليات الطباعة المعلقة", ex);
                    return Math.Max(1, remaining.Count);
                }
                return remaining.Count;
            }
        }

        public static int GetPendingLogCount()
        {
            lock (PendingSync)
            {
                try
                {
                    if (!File.Exists(PendingLogPath)) return 0;
                    return File.ReadAllLines(PendingLogPath, Encoding.UTF8).Count(line => !string.IsNullOrWhiteSpace(line));
                }
                catch (Exception ex)
                {
                    AppLog.Error("تعذر قراءة عدد عمليات الطباعة المعلقة", ex);
                    return -1; // لا نعتبر فشل القراءة دليلاً على خلو القائمة.
                }
            }
        }

        private static void RemovePendingOperationTokens(IEnumerable<string> operationTokens)
        {
            var tokens = new HashSet<string>(operationTokens.Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.Ordinal);
            if (tokens.Count == 0 || !File.Exists(PendingLogPath)) return;
            lock (PendingSync)
            {
                try
                {
                    var remaining = new List<string>();
                    foreach (string line in File.ReadAllLines(PendingLogPath, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var entry = JsonSerializer.Deserialize<LogEntry>(line);
                            if (entry != null && tokens.Contains(entry.OperationToken)) continue;
                        }
                        catch { /* لا نمسح سطرًا تالفًا لا نقدر على تحديد هويته. */ }
                        remaining.Add(line);
                    }
                    string temp = PendingLogPath + ".tmp";
                    File.WriteAllLines(temp, remaining, Encoding.UTF8);
                    File.Move(temp, PendingLogPath, true);
                }
                catch (Exception ex)
                {
                    AppLog.Error("تعذر إزالة عمليات محذوفة من قائمة الطباعة المعلقة؛ قد تحتاج مراجعة يدوية", ex);
                }
            }
        }

        public static void ClearAll()
        {
            int pendingCount = GetPendingLogCount();
            if (pendingCount != 0)
                throw new InvalidOperationException(pendingCount > 0
                    ? $"لا يمكن مسح سجل الطباعة الآن لأن هناك {pendingCount} عملية معلقة لم تُراجع بعد. عالج العمليات المعلقة أولًا حتى لا تضيع بيانات مالية."
                    : "لا يمكن مسح سجل الطباعة لأن البرنامج لم يستطع قراءة قائمة العمليات المعلقة. افحص صلاحيات ملف القائمة ثم أعد المحاولة حتى لا تضيع بيانات مالية.");

            EnsureFileExists();
            File.WriteAllText(
                LogFilePath,
                "Timestamp,UserName,FileName,FileType,Pages,Copies,ColorType,DuplexMode,PhysicalSheets,Price,PricingCategory,PrintOptionsJson" +
                Environment.NewLine,
                Encoding.UTF8);
            lock (PendingSync)
            {
                string temp = PendingLogPath + ".tmp";
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        public static void ResetFileOnly()
        {
            ClearAll();
        }

        public static int DeleteEntries(IEnumerable<LogEntry> entries, string actor)
        {
            if (entries == null) return 0;
            List<LogEntry> requested = entries.ToList();
            if (requested.Count == 0) return 0;

            // الحذف من سجل الطباعة يجب أن يحذف أيضًا السجل المالي المرتبط به،
            // وإلا يظل المبلغ محسوبًا في رصيد الخزينة بعد اختفاء السجل من الشاشة.
            int databaseDeleted = DeleteMatchingDatabaseEntries(requested, actor);
            int fileDeleted = DeleteCsvEntries(requested);
            return Math.Max(databaseDeleted, fileDeleted);
        }

        public static int DeleteDatabaseEntry(long printLogId, string actor)
        {
            if (GetPendingLogCount() < 0)
                throw new InvalidOperationException("لا يمكن حذف الطباعة لأن البرنامج لم يستطع قراءة قائمة العمليات المعلقة. افحص الملف أولًا لتجنب عودة سجل محذوف لاحقًا.");

            LogEntry? entry = null;
            string operationToken = string.Empty;
            using (var connection = AppDatabase.OpenConnection())
            using (var tx = connection.BeginTransaction())
            {
                using (var lookup = connection.CreateCommand())
                {
                    lookup.Transaction = tx;
                    lookup.CommandText = @"SELECT u.UserName,p.Timestamp,p.FileName,COALESCE(p.FileType,''),p.Pages,p.Copies,
COALESCE(p.ColorType,''),COALESCE(p.DuplexMode,''),p.PhysicalSheets,p.Price,COALESCE(p.OperationToken,'')
FROM PrintLog p JOIN AppUsers u ON u.Id=p.UserId WHERE p.Id=$id LIMIT 1";
                    lookup.Parameters.AddWithValue("$id", printLogId);
                    using var reader = lookup.ExecuteReader();
                    if (!reader.Read()) return 0;
                    DateTime timestamp = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
                    timestamp = new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute, timestamp.Second);
                    entry = new LogEntry
                    {
                        Timestamp = timestamp, UserName = reader.GetString(0), FileName = reader.GetString(2),
                        FileType = reader.GetString(3), Pages = reader.GetInt32(4), Copies = reader.GetInt32(5),
                        ColorType = reader.GetString(6), DuplexMode = reader.GetString(7),
                        PhysicalSheets = reader.GetInt32(8), Price = Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture)
                    };
                    operationToken = reader.GetString(10);
                }

                AppDatabase.RemovePrintLogDebt(connection, tx, printLogId);

                using (var movements = connection.CreateCommand())
                {
                    movements.Transaction = tx;
                    movements.CommandText = "DELETE FROM TreasuryMovements WHERE ReferenceType='PrintLog' AND ReferenceId=$id";
                    movements.Parameters.AddWithValue("$id", printLogId);
                    movements.ExecuteNonQuery();
                }
                using (var delete = connection.CreateCommand())
                {
                    delete.Transaction = tx;
                    delete.CommandText = "DELETE FROM PrintLog WHERE Id=$id";
                    delete.Parameters.AddWithValue("$id", printLogId);
                    if (delete.ExecuteNonQuery() == 0) return 0;
                }
                AppDatabase.AddAuditLog(connection, tx, actor, "حذف نهائي", "سجل الطباعة", printLogId,
                    $"الملف: {entry?.FileName ?? string.Empty} | المستخدم الأصلي: {entry?.UserName ?? string.Empty} | المبلغ: {entry?.Price:0.00}");
                tx.Commit();
            }

            if (entry != null) DeleteCsvEntries(new[] { entry });
            if (!string.IsNullOrWhiteSpace(operationToken)) RemovePendingOperationTokens(new[] { operationToken });
            return 1;
        }

        private static int DeleteMatchingDatabaseEntries(List<LogEntry> requested, string actor)
        {
            if (GetPendingLogCount() < 0)
                throw new InvalidOperationException("لا يمكن حذف سجلات الطباعة لأن البرنامج لم يستطع قراءة قائمة العمليات المعلقة. افحص الملف أولًا لتجنب عودة سجل محذوف لاحقًا.");

            int deleted = 0;
            var deletedTokens = new List<string>();
            using var connection = AppDatabase.OpenConnection();
            using var tx = connection.BeginTransaction();
            foreach (LogEntry target in requested)
            {
                long? id = null;
                string matchedToken = string.Empty;
                using (var lookup = connection.CreateCommand())
                {
                    lookup.Transaction = tx;
                    lookup.CommandText = @"SELECT p.Id, COALESCE(p.OperationToken,'') FROM PrintLog p JOIN AppUsers u ON u.Id=p.UserId
WHERE u.UserName=$user AND substr(p.Timestamp,1,19)=$timestamp
AND p.FileName=$file AND COALESCE(p.FileType,'')=$type AND p.Pages=$pages AND p.Copies=$copies
AND COALESCE(p.ColorType,'')=$color AND COALESCE(p.DuplexMode,'')=$duplex
AND p.PhysicalSheets=$sheets AND ABS(p.Price-$price)<0.005
ORDER BY p.Id LIMIT 1";
                    lookup.Parameters.AddWithValue("$user", target.UserName);
                    lookup.Parameters.AddWithValue("$timestamp", target.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
                    lookup.Parameters.AddWithValue("$file", target.FileName);
                    lookup.Parameters.AddWithValue("$type", target.FileType ?? string.Empty);
                    lookup.Parameters.AddWithValue("$pages", target.Pages);
                    lookup.Parameters.AddWithValue("$copies", target.Copies);
                    lookup.Parameters.AddWithValue("$color", target.ColorType ?? string.Empty);
                    lookup.Parameters.AddWithValue("$duplex", target.DuplexMode ?? string.Empty);
                    lookup.Parameters.AddWithValue("$sheets", target.PhysicalSheets);
                    lookup.Parameters.AddWithValue("$price", target.Price);
                    using var found = lookup.ExecuteReader();
                    if (found.Read())
                    {
                        id = found.GetInt64(0);
                        matchedToken = found.GetString(1);
                    }
                }
                if (!id.HasValue) continue;
                AppDatabase.RemovePrintLogDebt(connection, tx, id.Value);

                using (var movements = connection.CreateCommand())
                {
                    movements.Transaction = tx;
                    movements.CommandText = "DELETE FROM TreasuryMovements WHERE ReferenceType='PrintLog' AND ReferenceId=$id";
                    movements.Parameters.AddWithValue("$id", id.Value);
                    movements.ExecuteNonQuery();
                }
                using (var delete = connection.CreateCommand())
                {
                    delete.Transaction = tx;
                    delete.CommandText = "DELETE FROM PrintLog WHERE Id=$id";
                    delete.Parameters.AddWithValue("$id", id.Value);
                    int affected = delete.ExecuteNonQuery();
                    deleted += affected;
                    if (affected > 0)
                    {
                        if (!string.IsNullOrWhiteSpace(matchedToken)) deletedTokens.Add(matchedToken);
                        AppDatabase.AddAuditLog(connection, tx, actor, "حذف", "سجل الطباعة", id.Value,
                            $"الملف: {target.FileName} | المستخدم الأصلي: {target.UserName} | المبلغ: {target.Price:0.00}");
                    }
                }
            }
            tx.Commit();
            RemovePendingOperationTokens(deletedTokens);
            return deleted;
        }

        private static int DeleteCsvEntries(IEnumerable<LogEntry> entries)
        {
            List<LogEntry> requested = entries.ToList();
            if (requested.Count == 0) return 0;
            List<LogEntry> all = LoadAll();
            if (all.Count == 0) return 0;
            int deleted = 0;
            foreach (LogEntry target in requested)
            {
                int index = all.FindIndex(x => SameEntry(x, target));
                if (index < 0) continue;
                all.RemoveAt(index);
                deleted++;
            }
            if (deleted > 0) RewriteAll(all);
            return deleted;
        }

        private static bool SameEntry(LogEntry a, LogEntry b)
        {
            return a.Timestamp == b.Timestamp &&
                   string.Equals(a.UserName, b.UserName, StringComparison.Ordinal) &&
                   string.Equals(a.FileName, b.FileName, StringComparison.Ordinal) &&
                   string.Equals(a.FileType, b.FileType, StringComparison.Ordinal) &&
                   a.Pages == b.Pages &&
                   a.Copies == b.Copies &&
                   string.Equals(a.ColorType, b.ColorType, StringComparison.Ordinal) &&
                   string.Equals(a.DuplexMode, b.DuplexMode, StringComparison.Ordinal) &&
                   a.PhysicalSheets == b.PhysicalSheets &&
                   a.Price == b.Price;
        }

        private static void RewriteAll(List<LogEntry> entries)
        {
            EnsureFileExists();

            using StreamWriter writer =
                new StreamWriter(
                    LogFilePath,
                    false,
                    Encoding.UTF8);

            writer.WriteLine(
                "Timestamp,UserName,FileName,FileType,Pages,Copies,ColorType,DuplexMode,PhysicalSheets,Price,PricingCategory,PrintOptionsJson");

            foreach (LogEntry entry in entries)
            {
                string line =
                    string.Join(
                        ",",
                        Escape(entry.Timestamp.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture)),
                        Escape(entry.UserName),
                        Escape(entry.FileName),
                        Escape(entry.FileType),
                        entry.Pages.ToString(CultureInfo.InvariantCulture),
                        entry.Copies.ToString(CultureInfo.InvariantCulture),
                        Escape(entry.ColorType),
                        Escape(entry.DuplexMode),
                        entry.PhysicalSheets.ToString(CultureInfo.InvariantCulture),
                        entry.Price.ToString("F2", CultureInfo.InvariantCulture),
                        Escape(entry.PricingCategory), Escape(entry.PrintOptionsJson));

                writer.WriteLine(line);
            }
        }

        public static List<LogEntry> LoadAll()
        {
            List<LogEntry> result =
                new List<LogEntry>();

            if (!File.Exists(LogFilePath))
                return result;

            string[] lines =
                File.ReadAllLines(
                    LogFilePath,
                    Encoding.UTF8);

            // نتخطى سطر العناوين.
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                List<string> parts =
                    SplitCsvLine(lines[i]);

                if (parts.Count < 10)
                    continue;

                try
                {
                    LogEntry entry =
                        new LogEntry
                        {
                            Timestamp =
                                DateTime.Parse(
                                    parts[0],
                                    CultureInfo.InvariantCulture),

                            UserName = parts[1],
                            FileName = parts[2],
                            FileType = parts[3],

                            Pages =
                                int.Parse(
                                    parts[4],
                                    CultureInfo.InvariantCulture),

                            Copies =
                                int.Parse(
                                    parts[5],
                                    CultureInfo.InvariantCulture),

                            ColorType = parts[6],
                            DuplexMode = parts[7],

                            PhysicalSheets =
                                int.Parse(
                                    parts[8],
                                    CultureInfo.InvariantCulture),

                            Price =
                                decimal.Parse(
                                    parts[9],
                                    CultureInfo.InvariantCulture),
                            PricingCategory = parts.Count > 10 ? parts[10] : "",
                            PrintOptionsJson = parts.Count > 11 ? parts[11] : ""
                        };

                    result.Add(entry);
                }
                catch
                {
                    // نتجاهل أي سطر تالف بدل ما نوقف عرض السجل كله.
                }
            }

            return result;
        }

        /// <summary>
        /// يزامن السعر بعد الخصم مع ملف CSV الذي تعتمد عليه شاشة «سجل العمليات».
        /// تتم المطابقة بجميع حقول السجل والسعر القديم لتجنب تغيير طباعة أخرى متشابهة.
        /// </summary>
        internal static bool UpdateCsvPrice(LogEntry target, decimal oldPrice, decimal newPrice)
        {
            if (target == null) return false;

            List<LogEntry> all = LoadAll();
            var oldEntry = new LogEntry
            {
                Timestamp = target.Timestamp,
                UserName = target.UserName,
                FileName = target.FileName,
                FileType = target.FileType,
                Pages = target.Pages,
                Copies = target.Copies,
                ColorType = target.ColorType,
                DuplexMode = target.DuplexMode,
                PhysicalSheets = target.PhysicalSheets,
                // CSV prices are serialized with F2 precision.
                Price = decimal.Round(oldPrice, 2, MidpointRounding.ToEven)
            };

            int index = all.FindIndex(x => SameEntry(x, oldEntry));
            if (index < 0) return false;
            all[index].Price = newPrice;
            RewriteAll(all);
            return true;
        }

        /// <summary>يقرأ سجلات الطباعة المعتمدة من SQLite مع معرف العملية وإعداداتها الكاملة.</summary>
        public static List<LogEntry> LoadDatabaseEntries()
        {
            var result = new List<LogEntry>();
            using var connection = AppDatabase.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT p.Id,p.Timestamp,p.FileName,COALESCE(p.FileType,''),p.Pages,p.Copies,
       COALESCE(p.ColorType,''),COALESCE(p.DuplexMode,''),p.PhysicalSheets,p.Price,
       COALESCE(p.PricingCategory,''),COALESCE(p.PrintOptionsJson,''),u.UserName
FROM PrintLog p JOIN AppUsers u ON u.Id=p.UserId
ORDER BY p.Timestamp DESC,p.Id DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new LogEntry
                {
                    Id = reader.GetInt64(0),
                    Timestamp = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    FileName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    FileType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Pages = reader.GetInt32(4),
                    Copies = reader.GetInt32(5),
                    ColorType = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    DuplexMode = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    PhysicalSheets = reader.GetInt32(8),
                    Price = Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
                    PricingCategory = reader.IsDBNull(10) ? "" : reader.GetString(10),
                    PrintOptionsJson = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    UserName = reader.IsDBNull(12) ? "" : reader.GetString(12)
                });
            }
            return result;
        }

        internal static bool UpdateCsvCategory(LogEntry target, string newCategory)
        {
            if (target == null) return false;
            List<LogEntry> entries = LoadAll();
            int index = entries.FindIndex(x => SameEntry(x, target));
            if (index < 0) return false;
            entries[index].PricingCategory = newCategory ?? string.Empty;
            RewriteAll(entries);
            return true;
        }

        internal static bool UpdateCsvPrintOptions(LogEntry target, string printOptionsJson)
        {
            if (target == null) return false;
            List<LogEntry> entries = LoadAll();
            int index = entries.FindIndex(x => SameEntry(x, target));
            if (index < 0) return false;
            entries[index].PrintOptionsJson = printOptionsJson ?? string.Empty;
            RewriteAll(entries);
            return true;
        }

        private static void EnsureFileExists()
        {
            if (!Directory.Exists(LogFolderPath))
            {
                Directory.CreateDirectory(LogFolderPath);
            }

            if (File.Exists(LogFilePath))
                return;

            File.WriteAllText(
                LogFilePath,
                "Timestamp,UserName,FileName,FileType,Pages,Copies,ColorType,DuplexMode,PhysicalSheets,Price,PricingCategory,PrintOptionsJson" +
                Environment.NewLine,
                Encoding.UTF8);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(',') ||
                value.Contains('"') ||
                value.Contains('\n'))
            {
                return "\"" +
                       value.Replace("\"", "\"\"") +
                       "\"";
            }

            return value;
        }

        private static List<string> SplitCsvLine(string line)
        {
            List<string> result =
                new List<string>();

            StringBuilder current =
                new StringBuilder();

            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length &&
                            line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == ',')
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    else if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }

            result.Add(current.ToString());

            return result;
        }
    }
}
