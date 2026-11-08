using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

/// <summary>
/// نسخ احتياطي شامل + استعادة.
///
/// النسخة الاحتياطية = ملف zip واحد فيه كل بيانات البرنامج:
///   library.db     كل الجداول (عملاء، ديون، مبيعات، مخزن، خزينة، ماكينات، يوميات، فواتير، سجل الطباعة...)
///   pricing.json   الأسعار وأقصى خصم
///   security.json  كلمات المرور (مشفرة Hash)
///   PrintLog.csv   سجل الطباعة القديم
///   manifest.json  بيانات النسخة (التاريخ + أعداد السجلات)
///   local-files/ + roaming-files/ بقية ملفات بيانات التطبيق والإعدادات والسجلات
///
/// - تلقائي: مرة في اليوم عند فتح البرنامج، وبعد قفل اليومية.
/// - يدوي: من إعدادات الإدارة › النظام (في المجلد الاحتياطي أو في أي مكان تختاره).
/// - الاستعادة: ترفع ملف النسخة (zip أو db القديم) فيرجع كل شيء كما كان.
///   قبل أي استعادة بيتعمل نسخة أمان تلقائيًا من الوضع الحالي.
/// بيتحتفظ بآخر 30 نسخة في كل مكان.
/// </summary>
internal static class DatabaseBackup
{
    private const int KeepCount = 30;

    public const string DbEntry = "library.db";
    public const string PricingEntry = "pricing.json";
    public const string SecurityEntry = "security.json";
    public const string PrintLogEntry = "PrintLog.csv";
    public const string ManifestEntry = "manifest.json";
    private const string LocalFilesPrefix = "local-files/";
    private const string RoamingFilesPrefix = "roaming-files/";
    private const string SnapshotMarker = ".snapshot-complete";

    private sealed class BackupSettings
    {
        public string ExtraFolder { get; set; } = "";
    }

    private sealed class Manifest
    {
        public string Format { get; set; } = "LibraryPrintBackup";
        public int Version { get; set; } = 2;
        public string CreatedAt { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Machine { get; set; } = "";
        public int Customers { get; set; }
        public int Products { get; set; }
        public int Transactions { get; set; }
        public List<string> Files { get; set; } = new();
    }

    /// <summary> معلومات نسخة تم فحصها قبل الاستعادة. </summary>
    internal sealed class BackupInfo
    {
        public string Path { get; init; } = "";
        public bool IsZip { get; init; }
        public DateTime? CreatedAt { get; init; }
        public int Customers { get; init; }
        public int Products { get; init; }
        public int Transactions { get; init; }
        public bool HasPricing { get; init; }
        public bool HasSecurity { get; init; }
        public bool HasPrintLog { get; init; }
        public bool HasFullData { get; init; }
    }

    internal sealed class RestoreResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public string? SafetyBackupPath { get; init; }
    }

    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LibraryPrintApp");

    public static string BackupDirectory => Path.Combine(DataDirectory, "backups");
    public static string LastBackupWarning { get; private set; } = string.Empty;

    private static string SettingsFile => Path.Combine(DataDirectory, "backup-settings.json");

    private static string PricingFile => Path.Combine(DataDirectory, "pricing.json");
    private static string SecurityFile => Path.Combine(DataDirectory, "security.json");

    private static string RoamingDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LibraryPrintApp");

    public static string ExtraFolder
    {
        get
        {
            try
            {
                if (!File.Exists(SettingsFile)) return "";
                return JsonSerializer.Deserialize<BackupSettings>(File.ReadAllText(SettingsFile))?.ExtraFolder ?? "";
            }
            catch { return ""; }
        }
        set
        {
            string selected = (value ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                string selectedFull = Path.GetFullPath(selected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string dataFull = Path.GetFullPath(DataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string roamingFull = Path.GetFullPath(RoamingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                bool insideData = string.Equals(selectedFull, dataFull, StringComparison.OrdinalIgnoreCase) ||
                    selectedFull.StartsWith(dataFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                bool insideRoaming = string.Equals(selectedFull, roamingFull, StringComparison.OrdinalIgnoreCase) ||
                    selectedFull.StartsWith(roamingFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                if (insideData || insideRoaming)
                    throw new InvalidOperationException("اختار مجلدًا خارج مجلدي بيانات LibraryPrintApp (LocalAppData وRoaming) حتى لا تدخل النسخ الاحتياطية داخل بعضها.");
            }
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(SettingsFile,
                JsonSerializer.Serialize(new BackupSettings { ExtraFolder = selected }));
        }
    }

    // =====================================================================
    // إنشاء النسخة
    // =====================================================================

    /// <summary>
    /// يأخذ نسخة من بيانات الإصدار السابق قبل أن ينفّذ AppDatabase.Initialize أي ترحيل للمخطط.
    /// يعتمد على مسار/حجم/تاريخ ملف التشغيل. إذا تعذرت نسخة الأمان المطلوبة لا يسمح بترحيل القاعدة.
    /// </summary>
    public static void BackupBeforeAppVersionChange()
    {
        try
        {
            string? runningFile = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(runningFile) || !File.Exists(runningFile))
                runningFile = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(runningFile) || !File.Exists(runningFile)) return;

            var executable = new FileInfo(runningFile);
            string stamp = Path.GetFullPath(runningFile) + "\n" + executable.Length + "\n" + executable.LastWriteTimeUtc.Ticks;
            string stampFile = Path.Combine(DataDirectory, "app-build-stamp.txt");
            string previous = File.Exists(stampFile) ? File.ReadAllText(stampFile) : string.Empty;
            if (string.Equals(previous, stamp, StringComparison.Ordinal)) return;

            // على أول تشغيل نظيف لا توجد قاعدة قديمة نحتاج إلى حفظها.
            if (!File.Exists(AppDatabase.DatabasePath))
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(stampFile, stamp);
                return;
            }

            string? backup = BackupNow("before_app_update");
            if (backup == null)
                throw new InvalidOperationException("تعذر إنشاء نسخة أمان قبل تهيئة الإصدار الجديد. تم إيقاف التشغيل لحماية البيانات؛ راجع صلاحيات مجلد البيانات والمساحة المتاحة وسجل الأخطاء ثم أعد المحاولة.");

            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(stampFile, stamp);
            AppLog.Info("تم حفظ نسخة أمان قبل تشغيل/ترحيل الإصدار: " + backup);
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر إنشاء/فحص نسخة الأمان المطلوبة قبل تحديث البرنامج؛ أُوقف التشغيل لتجنب ترحيل قاعدة البيانات دون نسخة قابلة للاستعادة.", ex);
            throw;
        }
    }

    /// <summary> يعمل نسخة لو مفيش نسخة النهارده. </summary>
    public static bool BackupIfNotToday(out string error)
    {
        error = string.Empty;
        LastBackupWarning = string.Empty;
        try
        {
            string prefix = $"library_{DateTime.Now:yyyy-MM-dd}";
            if (Directory.Exists(BackupDirectory))
            {
                // لا نعتبر وجود ملف باسمه الصحيح كافيًا؛ لازم يفتح ويتحقق محتواه أولًا.
                foreach (string candidate in Directory.GetFiles(BackupDirectory, prefix + "*.zip")
                    .Concat(Directory.GetFiles(BackupDirectory, prefix + "*.db")))
                {
                    if (Inspect(candidate, out BackupInfo? info, out _) && info?.HasFullData == true)
                    {
                        // لا نكتفي بوجود نسخة محلية؛ نعيد محاولة مزامنتها للمجلد الخارجي أيضًا.
                        if (!CopyToExtraFolder(candidate, Path.GetFileName(candidate)))
                            LastBackupWarning = "النسخة المحلية موجودة وسليمة، لكن فشل نسخها إلى مجلد النسخ الخارجي. تأكد من توصيل الفلاشة/الشبكة ثم أعد فتح البرنامج أو أنشئ نسخة يدويًا.";
                        error = LastBackupWarning;
                        return true;
                    }
                }
            }
            string? path = BackupNow("startup");
            if (path == null)
            {
                error = "تعذر إنشاء النسخة الاحتياطية اليومية. راجع المساحة المتاحة وصلاحيات مجلد البيانات وسجل الأخطاء.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(LastBackupWarning)) error = LastBackupWarning;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل فحص النسخة الاحتياطية اليومية", ex);
            error = "فشل فحص/إنشاء النسخة الاحتياطية اليومية: " + ex.Message;
            return false;
        }
    }

    /// <summary> يرجّع مسار النسخة، أو null لو فشلت (والسبب في سجل الأخطاء). </summary>
    public static string? BackupNow(string reason)
    {
        LastBackupWarning = string.Empty;
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            string name = $"library_{DateTime.Now:yyyy-MM-dd_HHmmss}_{reason}.zip";
            string path = Path.Combine(BackupDirectory, name);

            CreateBackupFile(path, reason);

            Prune(BackupDirectory);
            bool externalCopyOk = CopyToExtraFolder(path, name);
            AppLog.Info($"تم عمل نسخة احتياطية: {name}");
            if (!externalCopyOk)
                LastBackupWarning = "تم إنشاء النسخة المحلية، لكن تعذر نسخها إلى مجلد النسخ الخارجي المحدد. تأكد أن القرص/الفلاشة متصلان وأن المجلد قابل للكتابة.";
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل عمل نسخة احتياطية", ex);
            return null;
        }
    }

    /// <summary> يحفظ نسخة شاملة في مسار يختاره المستخدم. يرجّع true لو نجح. </summary>
    public static bool BackupTo(string destinationFile, out string error)
    {
        error = "";
        try
        {
            string destinationFull = Path.GetFullPath(destinationFile);
            if (IsWithin(DataDirectory, destinationFull) || IsWithin(RoamingDirectory, destinationFull))
                throw new InvalidOperationException("احفظ ملف النسخة الاحتياطية في مكان خارج مجلدي بيانات LibraryPrintApp؛ لا تحفظ الأرشيف داخل البيانات التي يتم نسخها.");
            string? dir = Path.GetDirectoryName(destinationFull);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            CreateBackupFile(destinationFull, "export");
            AppLog.Info("تم حفظ نسخة احتياطية في: " + destinationFile);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل حفظ نسخة احتياطية في مكان مختار", ex);
            error = ex.Message;
            return false;
        }
    }

    /// <summary> يكتب ملف الـ zip كاملًا (على ملف مؤقت ثم نقل) عشان ما يتسابش ملف نص مكتوب. </summary>
    private static void CreateBackupFile(string destination, string reason)
    {
        string tempDb = Path.Combine(Path.GetTempPath(), "lpa_" + Guid.NewGuid().ToString("N") + ".db");
        string tempZip = destination + ".part";
        try
        {
            // VACUUM INTO = نسخة سليمة وكاملة من القاعدة حتى لو البرنامج شغال.
            using (var connection = AppDatabase.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"VACUUM INTO '{tempDb.Replace("'", "''")}'";
                command.ExecuteNonQuery();
            }

            var manifest = new Manifest
            {
                CreatedAt = DateTime.Now.ToString("O"),
                Reason = reason,
                Machine = Environment.MachineName
            };
            (manifest.Customers, manifest.Products, manifest.Transactions) = CountRows(tempDb);

            if (File.Exists(tempZip)) File.Delete(tempZip);
            using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // الملفات الأساسية ذات الأسماء المعروفة محفوظة في جذر ZIP للتوافق مع النسخ القديمة.
                AddFile(zip, DbEntry, tempDb, manifest);
                AddFile(zip, PricingEntry, PricingFile, manifest);
                AddFile(zip, SecurityEntry, SecurityFile, manifest);
                AddFile(zip, PrintLogEntry, PrintLogger.CsvPath, manifest);

                // نسخة كاملة من ملفات وإعدادات البرنامج الإضافية، مع استبعاد مجلد النسخ
                // الاحتياطية نفسه حتى لا تدخل النسخة داخل نفسها ويتضخم الحجم بلا حدود.
                string? nestedExtraRelative = null;
                try
                {
                    string extraPath = ExtraFolder;
                    string extraFull = string.IsNullOrWhiteSpace(extraPath) ? "" : Path.GetFullPath(extraPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string dataFull = Path.GetFullPath(DataDirectory)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!string.IsNullOrWhiteSpace(extraFull) &&
                        extraFull.StartsWith(dataFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        nestedExtraRelative = Path.GetRelativePath(dataFull, extraFull).Replace('\\', '/');
                }
                catch { }

                AddDirectoryFiles(zip, DataDirectory, LocalFilesPrefix, manifest,
                    relative => relative.Equals("library.db", StringComparison.OrdinalIgnoreCase)
                        || relative.Equals("library.db-wal", StringComparison.OrdinalIgnoreCase)
                        || relative.Equals("library.db-shm", StringComparison.OrdinalIgnoreCase)
                        || relative.Equals("library.db-journal", StringComparison.OrdinalIgnoreCase)
                        || relative.EndsWith(".restore.tmp", StringComparison.OrdinalIgnoreCase)
                        || relative.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                        || (Path.GetFileName(relative).StartsWith("library_", StringComparison.OrdinalIgnoreCase) &&
                            relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        || relative.Equals("pricing.json", StringComparison.OrdinalIgnoreCase)
                        || relative.Equals("security.json", StringComparison.OrdinalIgnoreCase)
                        || relative.Equals("backups", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith("backups/", StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(nestedExtraRelative) &&
                            (relative.Equals(nestedExtraRelative, StringComparison.OrdinalIgnoreCase) ||
                             relative.StartsWith(nestedExtraRelative + "/", StringComparison.OrdinalIgnoreCase))));

                // PrintLog.csv محفوظ في الجذر، ونحتفظ بأي ملفات أخرى في Roaming مستقبلًا.
                string? nestedRoamingExtraRelative = null;
                try
                {
                    string extraPath = ExtraFolder;
                    string extraFull = string.IsNullOrWhiteSpace(extraPath) ? "" : Path.GetFullPath(extraPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string roamingFull = Path.GetFullPath(RoamingDirectory)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!string.IsNullOrWhiteSpace(extraFull) &&
                        extraFull.StartsWith(roamingFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        nestedRoamingExtraRelative = Path.GetRelativePath(roamingFull, extraFull).Replace('\\', '/');
                }
                catch { }

                AddDirectoryFiles(zip, RoamingDirectory, RoamingFilesPrefix, manifest,
                    relative => relative.Equals("PrintLog.csv", StringComparison.OrdinalIgnoreCase)
                        || (Path.GetFileName(relative).StartsWith("library_", StringComparison.OrdinalIgnoreCase) &&
                            relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        || relative.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(nestedRoamingExtraRelative) &&
                            (relative.Equals(nestedRoamingExtraRelative, StringComparison.OrdinalIgnoreCase) ||
                             relative.StartsWith(nestedRoamingExtraRelative + "/", StringComparison.OrdinalIgnoreCase))));

                // علامتا اكتمال حتى نعرف أن هذا أرشيف جديد شامل، ونميّزه عن ملفات ZIP القديمة.
                AddMarker(zip, LocalFilesPrefix + SnapshotMarker, manifest);
                AddMarker(zip, RoamingFilesPrefix + SnapshotMarker, manifest);

                var entry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }

            // لا نعتمد نسخة لمجرد إن ZIP اتكتب؛ نعيد فتحها ونفحص سلامة قاعدة البيانات والمحتويات.
            if (!Inspect(tempZip, out _, out string validationError))
                throw new InvalidDataException("فشل التحقق من النسخة الاحتياطية الجديدة: " + validationError);

            File.Move(tempZip, destination, true);
        }
        finally
        {
            TryDelete(tempDb);
            TryDelete(tempZip);
        }
    }

    private static void AddFile(ZipArchive zip, string entryName, string sourcePath, Manifest manifest)
    {
        if (!File.Exists(sourcePath)) return;
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var target = entry.Open();
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        source.CopyTo(target);
        manifest.Files.Add(entryName);
    }

    private static void AddDirectoryFiles(ZipArchive zip, string sourceDirectory, string entryPrefix,
        Manifest manifest, Func<string, bool>? excludeRelativePath = null)
    {
        if (!Directory.Exists(sourceDirectory)) return;

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            if (excludeRelativePath?.Invoke(relative) == true) continue;
            string entryName = entryPrefix + relative;
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var target = entry.Open();
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            source.CopyTo(target);
            manifest.Files.Add(entryName);
        }
    }

    private static void AddMarker(ZipArchive zip, string entryName, Manifest manifest)
    {
        zip.CreateEntry(entryName, CompressionLevel.NoCompression);
        manifest.Files.Add(entryName);
    }

    private static bool CopyToExtraFolder(string source, string name)
    {
        string extra = ExtraFolder;
        if (string.IsNullOrWhiteSpace(extra)) return true;
        try
        {
            Directory.CreateDirectory(extra);
            File.Copy(source, Path.Combine(extra, name), true);
            Prune(extra);
            return true;
        }
        catch (Exception ex)
        {
            // الفلاشة ممكن تكون مش موصلة: لا نوقف النسخة الأساسية، لكن نُظهر تنبيهًا للمستخدم.
            AppLog.Error("تعذر نسخ النسخة الاحتياطية للمجلد الإضافي: " + extra, ex);
            return false;
        }
    }

    private static void Prune(string directory)
    {
        var old = Directory.GetFiles(directory, "library_*.*")
            .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Path.GetFileName)
            .Skip(KeepCount);
        foreach (string file in old)
        {
            try { File.Delete(file); } catch { }
        }
    }

    // =====================================================================
    // فحص واستعادة
    // =====================================================================

    /// <summary> يفحص الملف (zip أو db) ويتأكد إنه نسخة سليمة، من غير ما يغيّر أي حاجة. </summary>
    public static bool Inspect(string path, out BackupInfo? info, out string error)
    {
        info = null;
        error = "";
        string staging = NewStagingDirectory();
        try
        {
            info = Stage(path, staging);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>
    /// يستبدل كل بيانات البرنامج بمحتوى النسخة. لازم يتنادى والبرنامج مش بيطبع.
    /// بعد النجاح لازم الواجهة ترجع لشاشة تسجيل الدخول (البيانات اتغيرت).
    /// </summary>
    public static RestoreResult Restore(string path)
    {
        string staging = NewStagingDirectory();
        try
        {
            BackupInfo info;
            try
            {
                info = Stage(path, staging);
            }
            catch (Exception ex)
            {
                return new RestoreResult { Ok = false, Message = ex.Message };
            }

            // نسخة أمان من الوضع الحالي قبل أي تغيير.
            string? safety = null;
            if (File.Exists(AppDatabase.DatabasePath))
            {
                safety = BackupNow("before_restore");
                if (safety == null)
                    return new RestoreResult
                    {
                        Ok = false,
                        Message = "تعذر عمل نسخة أمان من البيانات الحالية قبل الاستعادة، فتم إلغاء الاستعادة عشان ما تضيعش بياناتك. راجع سجل الأخطاء."
                    };
            }

            // نقفل أي اتصال مفتوح بالقاعدة (SQLite بيحتفظ بالاتصالات في Pool).
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // 1) نجهّز النسخ الجديدة جنب الأصلية (لو فيه فشل نسخ، الأصلي لسه سليم).
            var moves = new List<(string Temp, string Final)>();
            try
            {
                Directory.CreateDirectory(DataDirectory);
                StageForMove(moves, Path.Combine(staging, DbEntry), AppDatabase.DatabasePath);
                StageForMove(moves, Path.Combine(staging, PricingEntry), PricingFile);
                StageForMove(moves, Path.Combine(staging, SecurityEntry), SecurityFile);

                string printLog = PrintLogger.CsvPath;
                string? printLogDir = Path.GetDirectoryName(printLog);
                if (!string.IsNullOrEmpty(printLogDir)) Directory.CreateDirectory(printLogDir);
                StageForMove(moves, Path.Combine(staging, PrintLogEntry), printLog);

                // استعادة كل الملفات الإضافية التي دخلت في النسخة الشاملة مع الحفاظ على مساراتها.
                StageDirectoryForMove(moves, Path.Combine(staging, LocalFilesPrefix.TrimEnd('/')), DataDirectory);
                StageDirectoryForMove(moves, Path.Combine(staging, RoamingFilesPrefix.TrimEnd('/')), RoamingDirectory);
            }
            catch (Exception ex)
            {
                foreach (var m in moves) TryDelete(m.Temp);
                AppLog.Error("فشل تجهيز ملفات الاستعادة", ex);
                return new RestoreResult
                {
                    Ok = false,
                    Message = "تعذر تجهيز ملفات الاستعادة، ولم يتم تغيير أي بيانات.\n" + ex.Message,
                    SafetyBackupPath = safety
                };
            }

            // 2) نستبدل الملفات.
            try
            {
                // في النسخ الشاملة فقط، ننظف الملفات القديمة التي لم تكن ضمن اللقطة،
                // ونحتفظ بمجلد أرشيف النسخ نفسه حتى لا نحذف وسيلة الرجوع لنسخ سابقة.
                if (info.HasFullData)
                {
                    RemoveFilesNotInSnapshot(DataDirectory, moves, preserveBackupHistory: true);
                    RemoveFilesNotInSnapshot(RoamingDirectory, moves, preserveBackupHistory: false);
                }

                TryDelete(AppDatabase.DatabasePath + "-wal");
                TryDelete(AppDatabase.DatabasePath + "-shm");
                TryDelete(AppDatabase.DatabasePath + "-journal");
                foreach (var m in moves)
                    File.Move(m.Temp, m.Final, true);
            }
            catch (Exception ex)
            {
                foreach (var m in moves) TryDelete(m.Temp);
                AppLog.Error("فشل استبدال الملفات أثناء الاستعادة", ex);
                return new RestoreResult
                {
                    Ok = false,
                    Message = "حصل خطأ أثناء استبدال الملفات. نسخة الأمان محفوظة هنا ويمكن استعادتها:\n" + safety + "\n\n" + ex.Message,
                    SafetyBackupPath = safety
                };
            }

            // 3) نعيد تحميل الإعدادات المحفوظة في الذاكرة ونحدّث شكل القاعدة لو النسخة قديمة.
            try
            {
                AdminSecurity.Reload();
                AppOptions.Reload();
                Shortcuts.Reload();
                AppDatabase.Initialize();
            }
            catch (Exception ex)
            {
                AppLog.Error("فشل تهيئة البيانات بعد الاستعادة", ex);
                return new RestoreResult
                {
                    Ok = false,
                    Message = "تمت الاستعادة لكن حصل خطأ في تهيئة البيانات:\n" + ex.Message + "\n\nنسخة الأمان: " + safety,
                    SafetyBackupPath = safety
                };
            }

            AppLog.Info("تمت استعادة نسخة احتياطية من: " + path);
            return new RestoreResult { Ok = true, Message = "تمت الاستعادة بنجاح.", SafetyBackupPath = safety };
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static void StageForMove(List<(string Temp, string Final)> moves, string source, string final)
    {
        if (!File.Exists(source)) return;
        string? directory = Path.GetDirectoryName(final);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temp = final + ".restore.tmp";
        File.Copy(source, temp, true);
        moves.Add((temp, final));
    }

    private static void RemoveFilesNotInSnapshot(string root, List<(string Temp, string Final)> moves, bool preserveBackupHistory)
    {
        if (!Directory.Exists(root)) return;
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var move in moves)
        {
            if (IsWithin(root, move.Final)) expected.Add(Path.GetFullPath(move.Final));
            if (IsWithin(root, move.Temp)) expected.Add(Path.GetFullPath(move.Temp));
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList())
        {
            string full = Path.GetFullPath(file);
            string relative = Path.GetRelativePath(normalizedRoot, full);
            string firstPart = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (preserveBackupHistory && string.Equals(firstPart, "backups", StringComparison.OrdinalIgnoreCase))
                continue;
            if (expected.Contains(full)) continue;
            File.Delete(full);
        }
    }

    private static bool IsWithin(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void StageDirectoryForMove(List<(string Temp, string Final)> moves, string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory)) return;
        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, source);
            if (relative.Equals(SnapshotMarker, StringComparison.OrdinalIgnoreCase)) continue;
            string final = Path.Combine(targetDirectory, relative);
            StageForMove(moves, source, final);
        }
    }

    /// <summary>
    /// يفك الملف في مجلد مؤقت (بأسماء ثابتة فقط — مفيش مسارات من جوه الـ zip)
    /// ويتأكد إن قاعدة البيانات سليمة وفيها الجداول الأساسية.
    /// </summary>
    private static BackupInfo Stage(string path, string staging)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("الملف غير موجود.");

        bool isZip = LooksLikeZip(path);
        DateTime? createdAt = null;
        bool hasPricing = false, hasSecurity = false, hasPrintLog = false, hasFullData = false;
        string stagedDb = Path.Combine(staging, DbEntry);

        if (isZip)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);

                var dbEntry = zip.GetEntry(DbEntry)
                    ?? throw new InvalidOperationException("الملف ده مش نسخة احتياطية للبرنامج (مفيش فيه قاعدة البيانات library.db).");
                ExtractTo(dbEntry, stagedDb);

                hasPricing = ExtractOptional(zip, PricingEntry, Path.Combine(staging, PricingEntry));
                hasSecurity = ExtractOptional(zip, SecurityEntry, Path.Combine(staging, SecurityEntry));
                hasPrintLog = ExtractOptional(zip, PrintLogEntry, Path.Combine(staging, PrintLogEntry));
                bool hasLocalSnapshot = ExtractFolderEntries(zip, LocalFilesPrefix, staging);
                bool hasRoamingSnapshot = ExtractFolderEntries(zip, RoamingFilesPrefix, staging);
                hasFullData = hasLocalSnapshot && hasRoamingSnapshot;

                var manifestEntry = zip.GetEntry(ManifestEntry);
                if (manifestEntry != null)
                {
                    try
                    {
                        using var reader = new StreamReader(manifestEntry.Open());
                        var manifest = JsonSerializer.Deserialize<Manifest>(reader.ReadToEnd());
                        if (manifest != null && DateTime.TryParse(manifest.CreatedAt, out var dt))
                            createdAt = dt;
                    }
                    catch { /* المانيفست معلومة إضافية فقط */ }
                }
            }
            catch (InvalidDataException)
            {
                throw new InvalidOperationException("ملف الـ zip تالف أو غير مكتمل.");
            }
        }
        else
        {
            File.Copy(path, stagedDb, true);
            createdAt = File.GetLastWriteTime(path);
        }

        var (customers, products, transactions) = ValidateDatabase(stagedDb);

        return new BackupInfo
        {
            Path = path,
            IsZip = isZip,
            CreatedAt = createdAt,
            Customers = customers,
            Products = products,
            Transactions = transactions,
            HasPricing = hasPricing,
            HasSecurity = hasSecurity,
            HasPrintLog = hasPrintLog,
            HasFullData = hasFullData
        };
    }

    private static (int Customers, int Products, int Transactions) ValidateDatabase(string dbPath)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();

            using var connection = new SqliteConnection(cs);
            connection.Open();

            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check";
                string result = Convert.ToString(check.ExecuteScalar()) ?? "";
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("قاعدة البيانات داخل النسخة تالفة: " + result);
            }

            foreach (string table in new[] { "AppUsers", "Customers", "Products", "Transactions" })
            {
                using var exists = connection.CreateCommand();
                exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
                exists.Parameters.AddWithValue("$n", table);
                if (Convert.ToInt32(exists.ExecuteScalar()) == 0)
                    throw new InvalidOperationException("الملف ده مش نسخة احتياطية لبرنامج المكتبة (ناقص جدول " + table + ").");
            }

            return (Count(connection, "Customers"), Count(connection, "Products"), Count(connection, "Transactions"));
        }
        catch (SqliteException)
        {
            throw new InvalidOperationException("الملف ده مش قاعدة بيانات صالحة أو تالف.");
        }
    }

    private static (int Customers, int Products, int Transactions) CountRows(string dbPath)
    {
        try { return ValidateDatabase(dbPath); }
        catch { return (0, 0, 0); }
    }

    private static int Count(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool LooksLikeZip(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return fs.ReadByte() == 'P' && fs.ReadByte() == 'K';
    }

    private static bool ExtractFolderEntries(ZipArchive zip, string prefix, string staging)
    {
        bool foundMarker = zip.GetEntry(prefix + SnapshotMarker) != null;
        string expectedRoot = Path.GetFullPath(Path.Combine(staging, prefix.TrimEnd('/')))
            + Path.DirectorySeparatorChar;

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string relative = entry.FullName.Substring(prefix.Length).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relative) || relative == SnapshotMarker || entry.FullName.EndsWith('/')) continue;
            if (relative.StartsWith("/", StringComparison.Ordinal)
                || Path.IsPathRooted(relative)
                || relative.Split('/').Any(part => part == ".." || part == "." || part.Contains(':')))
                throw new InvalidDataException("النسخة الاحتياطية تحتوي على مسار ملف غير آمن.");

            string destination = Path.GetFullPath(Path.Combine(staging, prefix.TrimEnd('/'),
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("النسخة الاحتياطية تحتوي على مسار خارج مجلد الاستعادة.");
            ExtractTo(entry, destination);
        }
        return foundMarker;
    }

    private static void ExtractTo(ZipArchiveEntry entry, string destination)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        using var source = entry.Open();
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write);
        source.CopyTo(target);
    }

    private static bool ExtractOptional(ZipArchive zip, string entryName, string destination)
    {
        var entry = zip.GetEntry(entryName);
        if (entry == null) return false;
        ExtractTo(entry, destination);
        return true;
    }

    private static string NewStagingDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lpa_restore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }
}
