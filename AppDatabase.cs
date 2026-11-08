using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal static class AppDatabase
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LibraryPrintApp");

    public static readonly string DatabasePath = Path.Combine(DataDirectory, "library.db");

    private static string ConnectionString => $"Data Source={DatabasePath};Foreign Keys=True";

    public static SqliteConnection OpenConnection()
    {
        Directory.CreateDirectory(DataDirectory);
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public static void AddAuditLog(SqliteConnection connection, SqliteTransaction? transaction,
        string userName, string action, string entityType, long? entityId, string details)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO AuditLog(CreatedAt,UserName,Action,EntityType,EntityId,Details)
VALUES($created,$user,$action,$entity,$id,$details);";
        command.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("$user", string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName.Trim());
        command.Parameters.AddWithValue("$action", action ?? string.Empty);
        command.Parameters.AddWithValue("$entity", entityType ?? string.Empty);
        command.Parameters.AddWithValue("$id", (object?)entityId ?? DBNull.Value);
        command.Parameters.AddWithValue("$details", details ?? string.Empty);
        command.ExecuteNonQuery();
    }

    public static void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS AppUsers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS Customers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Phone TEXT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Products (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Barcode TEXT NULL UNIQUE,
    Name TEXT NOT NULL,
    Description TEXT NULL,
    Category TEXT NULL,
    PurchasePrice NUMERIC NOT NULL DEFAULT 0,
    SalePrice NUMERIC NOT NULL DEFAULT 0,
    Quantity INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Transactions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NULL,
    ProductId INTEGER NULL,
    UserId INTEGER NOT NULL,
    TransactionType TEXT NOT NULL,
    Description TEXT NOT NULL,
    RequiredAmount NUMERIC NOT NULL DEFAULT 0,
    PaidAmount NUMERIC NOT NULL DEFAULT 0,
    RemainingAmount NUMERIC NOT NULL DEFAULT 0,
    ChangeAmount NUMERIC NOT NULL DEFAULT 0,
    OriginalAmount NUMERIC NOT NULL DEFAULT 0,
    DiscountPercent NUMERIC NOT NULL DEFAULT 0,
    ServiceName TEXT NULL,
    Commission NUMERIC NOT NULL DEFAULT 0,
    CommissionPercent NUMERIC NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id)
);

CREATE TABLE IF NOT EXISTS Debts_History (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL,
    TransactionId INTEGER NULL,
    UserId INTEGER NOT NULL,
    Type TEXT NOT NULL,
    Amount NUMERIC NOT NULL,
    BalanceAfter NUMERIC NOT NULL,
    Note TEXT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
    FOREIGN KEY(TransactionId) REFERENCES Transactions(Id),
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id)
);

CREATE TABLE IF NOT EXISTS Cash_Services (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    ServiceType TEXT NOT NULL,
    Provider TEXT NOT NULL,
    CustomerId INTEGER NULL,
    CustomerName TEXT NULL,
    Phone TEXT NULL,
    Amount NUMERIC NOT NULL,
    Commission NUMERIC NOT NULL DEFAULT 0,
    CommissionPercent NUMERIC NOT NULL DEFAULT 0,
    TotalAmount NUMERIC NOT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id),
    FOREIGN KEY(CustomerId) REFERENCES Customers(Id)
);

CREATE TABLE IF NOT EXISTS TreasuryMovements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    MovementType TEXT NOT NULL CHECK(MovementType IN ('IN','OUT')),
    Category TEXT NOT NULL,
    Amount NUMERIC NOT NULL,
    Description TEXT NULL,
    ReferenceType TEXT NULL,
    ReferenceId INTEGER NULL,
    AccountKey TEXT NOT NULL DEFAULT 'CASH',
    ShiftId INTEGER NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id)
);

CREATE TABLE IF NOT EXISTS PrintLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Timestamp TEXT NOT NULL,
    FileName TEXT NOT NULL,
    FileType TEXT NULL,
    Pages INTEGER NOT NULL,
    Copies INTEGER NOT NULL,
    ColorType TEXT NULL,
    DuplexMode TEXT NULL,
    PhysicalSheets INTEGER NOT NULL,
    Price NUMERIC NOT NULL,
    OperationToken TEXT NULL,
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id)
);

CREATE TABLE IF NOT EXISTS AuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CreatedAt TEXT NOT NULL,
    UserName TEXT NOT NULL,
    Action TEXT NOT NULL,
    EntityType TEXT NOT NULL,
    EntityId INTEGER NULL,
    Details TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS IX_AuditLog_CreatedAt ON AuditLog(CreatedAt);
CREATE INDEX IF NOT EXISTS IX_AuditLog_UserName ON AuditLog(UserName);

CREATE INDEX IF NOT EXISTS IX_Customers_Name ON Customers(Name);
CREATE INDEX IF NOT EXISTS IX_Customers_Phone ON Customers(Phone);
CREATE INDEX IF NOT EXISTS IX_Transactions_CreatedAt ON Transactions(CreatedAt);
CREATE INDEX IF NOT EXISTS IX_Debts_CustomerId ON Debts_History(CustomerId);
CREATE INDEX IF NOT EXISTS IX_CashServices_CreatedAt ON Cash_Services(CreatedAt);
CREATE INDEX IF NOT EXISTS IX_Treasury_CreatedAt ON TreasuryMovements(CreatedAt);
CREATE INDEX IF NOT EXISTS IX_PrintLog_Timestamp ON PrintLog(Timestamp);

CREATE TABLE IF NOT EXISTS DailySessions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    BusinessDate TEXT NOT NULL UNIQUE,
    Status TEXT NOT NULL CHECK(Status IN ('OPEN','CLOSED')),
    OpenedBy TEXT NOT NULL,
    OpenedAt TEXT NOT NULL,
    ClosedBy TEXT NULL,
    ClosedAt TEXT NULL
);

CREATE TABLE IF NOT EXISTS DailyBalances (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId INTEGER NOT NULL,
    AccountKey TEXT NOT NULL,
    OpeningAmount NUMERIC NOT NULL DEFAULT 0,
    ExpectedAmount NUMERIC NOT NULL DEFAULT 0,
    ActualAmount NUMERIC NULL,
    Difference NUMERIC NULL,
    UNIQUE(SessionId, AccountKey),
    FOREIGN KEY(SessionId) REFERENCES DailySessions(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_DailySessions_Date ON DailySessions(BusinessDate);
CREATE INDEX IF NOT EXISTS IX_DailyBalances_Session ON DailyBalances(SessionId);

CREATE TABLE IF NOT EXISTS DailyShifts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId INTEGER NOT NULL,
    ShiftKey TEXT NOT NULL CHECK(ShiftKey IN ('MORNING','EVENING')),
    UserId INTEGER NOT NULL,
    UserName TEXT NOT NULL,
    Status TEXT NOT NULL CHECK(Status IN ('OPEN','CLOSED')),
    StartedAt TEXT NOT NULL,
    EndedAt TEXT NULL,
    UNIQUE(SessionId, ShiftKey),
    FOREIGN KEY(SessionId) REFERENCES DailySessions(Id) ON DELETE CASCADE,
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id)
);

CREATE INDEX IF NOT EXISTS IX_DailyShifts_Session ON DailyShifts(SessionId);
CREATE INDEX IF NOT EXISTS IX_DailyShifts_UserStatus ON DailyShifts(UserId, Status);

CREATE TABLE IF NOT EXISTS MachineAccounts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MachineKey TEXT NOT NULL UNIQUE,
    DisplayName TEXT NOT NULL,
    OpeningBalance NUMERIC NOT NULL DEFAULT 0,
    CurrentBalance NUMERIC NOT NULL DEFAULT 0,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS WalletAccounts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Provider TEXT NOT NULL UNIQUE,
    OpeningBalance NUMERIC NOT NULL DEFAULT 0,
    CurrentBalance NUMERIC NOT NULL DEFAULT 0,
    UpdatedAt TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_MachineAccounts_Key ON MachineAccounts(MachineKey);
CREATE INDEX IF NOT EXISTS IX_WalletAccounts_Provider ON WalletAccounts(Provider);

INSERT OR IGNORE INTO MachineAccounts(MachineKey,DisplayName,OpeningBalance,CurrentBalance,UpdatedAt)
VALUES
    ('OPay','OPay',0,0,$now),
    ('BEE','باب رزق',0,0,$now);

INSERT OR IGNORE INTO WalletAccounts(Provider,OpeningBalance,CurrentBalance,UpdatedAt)
VALUES
    ('Vodafone Cash',0,0,$now),
    ('Fawry',0,0,$now),
    ('InstaPay',0,0,$now);

INSERT OR IGNORE INTO AppUsers(UserName) VALUES ('عمر');
INSERT OR IGNORE INTO AppUsers(UserName) VALUES ('أحمد');

";
        command.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
        command.ExecuteNonQuery();

        // ترحيل آمن لقواعد البيانات القديمة.
        // مهم لأن CREATE TABLE IF NOT EXISTS لا يضيف أعمدة جديدة
        // إلى جدول موجود مسبقًا.
        EnsureColumn(connection, "Cash_Services", "CommissionPercent", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Transactions", "ProductId", "INTEGER NULL");
        EnsureColumn(connection, "Transactions", "OriginalAmount", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Transactions", "DiscountPercent", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Transactions", "ServiceName", "TEXT NULL");
        EnsureColumn(connection, "Transactions", "Commission", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Transactions", "CommissionPercent", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Transactions", "Quantity", "INTEGER NOT NULL DEFAULT 1");
        InvoiceStore.EnsureSchema(connection);
        BusinessManagementStore.EnsureSchema(connection);
        EnsureColumn(connection, "Products", "Barcode", "TEXT NULL");
        EnsureColumn(connection, "Products", "Description", "TEXT NULL");
        EnsureColumn(connection, "Products", "Category", "TEXT NULL");
        EnsureColumn(connection, "Products", "PurchasePrice", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Products", "SalePrice", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Products", "Quantity", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Products", "MinQuantity", "INTEGER NOT NULL DEFAULT 3");
        EnsureColumn(connection, "Products", "CreatedAt", "TEXT NULL");
        EnsureColumn(connection, "Products", "UpdatedAt", "TEXT NULL");
        EnsureColumn(connection, "TreasuryMovements", "AccountKey", "TEXT NOT NULL DEFAULT 'CASH'");
        EnsureColumn(connection, "TreasuryMovements", "ShiftId", "INTEGER NULL");
        EnsureColumn(connection, "PrintLog", "OperationToken", "TEXT NULL");
        EnsureColumn(connection, "PrintLog", "PricingCategory", "TEXT NULL");
        EnsureColumn(connection, "PrintLog", "PrintOptionsJson", "TEXT NULL");
        EnsureColumn(connection, "Transactions", "PricingCategory", "TEXT NULL");
        using (var printTokenIndex = connection.CreateCommand())
        {
            printTokenIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_PrintLog_OperationToken ON PrintLog(OperationToken) WHERE OperationToken IS NOT NULL AND OperationToken <> '';";
            printTokenIndex.ExecuteNonQuery();
        }

        // ربط قيد الدين بعمليات الطباعة اللي اتحوّلت له: لما عملية طباعة تتمسح وكانت دين،
        // نعرف نشيل نصيبها من الدين (من غير FOREIGN KEY عشان ما يعطّلش أي حذف تاني).
        bool printDebtLinksExisted;
        using (var existsCmd = connection.CreateCommand())
        {
            existsCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PrintDebtLinks'";
            printDebtLinksExisted = Convert.ToInt64(existsCmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture) > 0;
        }
        using (var linkTable = connection.CreateCommand())
        {
            linkTable.CommandText = @"
CREATE TABLE IF NOT EXISTS PrintDebtLinks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DebtId INTEGER NOT NULL,
    PrintLogId INTEGER NOT NULL,
    Amount NUMERIC NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_PrintDebtLinks_PrintLog ON PrintDebtLinks(PrintLogId);
CREATE INDEX IF NOT EXISTS IX_PrintDebtLinks_Debt ON PrintDebtLinks(DebtId);";
            linkTable.ExecuteNonQuery();
        }
        // أول تشغيل بعد التحديث: نربط الديون القديمة (الناتجة عن طباعة ملف واحد) بسجل الطباعة بتاعها.
        if (!printDebtLinksExisted)
            BackfillPrintDebtLinks(connection);

        // AccountKey is a migration column for older databases, so its index must be created only after the column exists.
        using (var idx = connection.CreateCommand())
        {
            idx.CommandText = "CREATE INDEX IF NOT EXISTS IX_Treasury_AccountDate ON TreasuryMovements(AccountKey, CreatedAt);";
            idx.ExecuteNonQuery();
        }

        // تحديث الحركات القديمة إلى الحساب المالي الصحيح. الحركات غير المعروفة تبقى في النقدية العامة.
        // هذا الترحيل يُنفَّذ مرة واحدة فقط (مضبوط عبر PRAGMA user_version) لأنه
        // كان يعمل في كل تشغيل للبرنامج، وهذا كان يعيد تصنيف حركات "خدمات الكاش"
        // (سحب/إيداع) دائمًا كـ SALES حتى بعد أن أصبحت هذه الحركات تُسجَّل عمدًا
        // على حساب CASH ضمن فلسفة فصل المحفظة عن الدرج؛ فكان الترحيل يمسح
        // التصنيف الصحيح الجديد عند كل إعادة تشغيل.
        long schemaVersion;
        using (var getVersion = connection.CreateCommand())
        {
            getVersion.CommandText = "PRAGMA user_version;";
            schemaVersion = Convert.ToInt64(getVersion.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
        if (schemaVersion < 1)
        using (var migrateAccounts = connection.CreateCommand())
        {
            migrateAccounts.CommandText = @"
UPDATE TreasuryMovements
SET AccountKey = CASE
    -- أولوية المبيعات أولًا لأن عمليات البيع تستخدم ReferenceType=Transaction أيضًا.
    WHEN Category='مبيعات' THEN 'SALES'
    WHEN ReferenceType='Transaction' AND EXISTS (
        SELECT 1 FROM Transactions tr
        WHERE tr.Id=ReferenceId AND tr.TransactionType='SALE'
    ) THEN 'SALES'
    WHEN ReferenceType='PrintLog' OR ReferenceType='Copy' THEN 'PRINTING'
    WHEN Category IN ('الطباعة','التصوير','الطباعة والتصوير') THEN 'PRINTING'
    WHEN ReferenceType='CashService' THEN 'DRAWER'
    WHEN Category='خدمات الكاش' THEN 'DRAWER'
    WHEN ReferenceType='DebtPayment' THEN 'CASH'
    WHEN Category='تحصيل ديون' THEN 'CASH'
    WHEN ReferenceType='ServiceTransaction' AND MovementType='OUT' AND EXISTS (
        SELECT 1 FROM Transactions tr
        WHERE tr.Id=ReferenceId
          AND (tr.ServiceName LIKE 'ماكينة OPay%' OR tr.ServiceName LIKE 'OPay%')
    ) THEN 'OPAY'
    WHEN ReferenceType='ServiceTransaction' AND MovementType='OUT' AND EXISTS (
        SELECT 1 FROM Transactions tr
        WHERE tr.Id=ReferenceId
          AND (tr.ServiceName LIKE 'ماكينة باب رزق%' OR tr.ServiceName LIKE 'ماكينة BEE%' OR tr.ServiceName LIKE 'BEE%')
    ) THEN 'BEE'
    -- المبلغ الذي يدفعه العميل في خدمة الماكينة يدخل النقدية في الدرج؛
    -- قيمة الخدمة الأساسية فقط هي التي تخرج من حساب الماكينة.
    WHEN ReferenceType='ServiceTransaction' AND MovementType='IN' AND EXISTS (
        SELECT 1 FROM Transactions tr
        WHERE tr.Id=ReferenceId
          AND (tr.ServiceName LIKE 'ماكينة OPay%' OR tr.ServiceName LIKE 'OPay%'
               OR tr.ServiceName LIKE 'ماكينة باب رزق%' OR tr.ServiceName LIKE 'ماكينة BEE%' OR tr.ServiceName LIKE 'BEE%')
    ) THEN 'DRAWER'
    ELSE COALESCE(NULLIF(AccountKey,''),'CASH')
END;";
            migrateAccounts.ExecuteNonQuery();

            using var setVersion = connection.CreateCommand();
            setVersion.CommandText = "PRAGMA user_version = 1;";
            setVersion.ExecuteNonQuery();
        }

        MigratePrintingAccount(connection);
        MigrateWalletAndCommissionAccounts(connection);
    }

    /// <summary>
    /// يفصل الطباعة والتصوير عن المبيعات مع الحفاظ على مجموع أرصدة اليوميات القديمة.
    /// </summary>
    private static void MigratePrintingAccount(SqliteConnection connection)
    {
        long version;
        using (var versionCmd = connection.CreateCommand())
        {
            versionCmd.CommandText = "PRAGMA user_version;";
            version = Convert.ToInt64(versionCmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
        if (version >= 2) return;

        using var tx = connection.BeginTransaction();
        var sessions = new List<(long Id, string Date, decimal SalesOpening, decimal? SalesActual)>();
        using (var sessionsCmd = connection.CreateCommand())
        {
            sessionsCmd.Transaction = tx;
            sessionsCmd.CommandText = @"
SELECT s.Id,s.BusinessDate,COALESCE(b.OpeningAmount,0),b.ActualAmount
FROM DailySessions s
LEFT JOIN DailyBalances b ON b.SessionId=s.Id AND b.AccountKey='SALES'
ORDER BY s.BusinessDate;";
            using var reader = sessionsCmd.ExecuteReader();
            while (reader.Read())
                sessions.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2), reader.IsDBNull(3) ? null : reader.GetDecimal(3)));
        }

        var printingOpenings = new Dictionary<long, decimal>();
        foreach (var session in sessions)
        {
            using var prior = connection.CreateCommand();
            prior.Transaction = tx;
            prior.CommandText = @"
SELECT COALESCE(SUM(CASE WHEN t.MovementType='IN' THEN t.Amount ELSE -t.Amount END),0)
FROM TreasuryMovements t
WHERE substr(t.CreatedAt,1,10)<$date
  AND (t.ReferenceType IN ('PrintLog','Copy')
       OR t.Category IN ('الطباعة','التصوير','الطباعة والتصوير')
       OR t.AccountKey='PRINTING'
       OR (t.ReferenceType='Transaction' AND EXISTS (
            SELECT 1 FROM Transactions tr
            WHERE tr.Id=t.ReferenceId AND tr.TransactionType='COPY'
       )));";
            prior.Parameters.AddWithValue("$date", session.Date);
            decimal historicalPrinting = Convert.ToDecimal(prior.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
            printingOpenings[session.Id] = Math.Min(session.SalesOpening, Math.Max(0m, historicalPrinting));
        }

        using (var reclassify = connection.CreateCommand())
        {
            reclassify.Transaction = tx;
            reclassify.CommandText = @"
UPDATE TreasuryMovements
SET AccountKey='PRINTING',
    Category=CASE
        WHEN ReferenceType='PrintLog' THEN 'الطباعة'
        WHEN ReferenceType='Copy' THEN 'التصوير'
        WHEN ReferenceType='Transaction' AND EXISTS (
            SELECT 1 FROM Transactions tr
            WHERE tr.Id=TreasuryMovements.ReferenceId AND tr.TransactionType='COPY'
        ) THEN 'التصوير'
        ELSE Category
    END
WHERE ReferenceType IN ('PrintLog','Copy')
   OR Category IN ('الطباعة','التصوير','الطباعة والتصوير')
   OR (ReferenceType='Transaction' AND EXISTS (
        SELECT 1 FROM Transactions tr
        WHERE tr.Id=TreasuryMovements.ReferenceId AND tr.TransactionType='COPY'
   ));";
            reclassify.ExecuteNonQuery();
        }

        foreach (var session in sessions)
        {
            decimal printingOpening = printingOpenings[session.Id];
            decimal salesOpening = Math.Max(0m, session.SalesOpening - printingOpening);
            decimal printingTodayNet;
            decimal salesTodayNet;
            using (var net = connection.CreateCommand())
            {
                net.Transaction = tx;
                net.CommandText = @"
SELECT
 COALESCE(SUM(CASE WHEN AccountKey='PRINTING' THEN CASE WHEN MovementType='IN' THEN Amount ELSE -Amount END ELSE 0 END),0),
 COALESCE(SUM(CASE WHEN AccountKey='SALES' THEN CASE WHEN MovementType='IN' THEN Amount ELSE -Amount END ELSE 0 END),0)
FROM TreasuryMovements t
WHERE substr(t.CreatedAt,1,10)=$date
  AND (t.ShiftId IS NULL OR EXISTS (SELECT 1 FROM DailyShifts ds WHERE ds.Id=t.ShiftId AND ds.SessionId=$session));";
                net.Parameters.AddWithValue("$date", session.Date);
                net.Parameters.AddWithValue("$session", session.Id);
                using var nr = net.ExecuteReader();
                nr.Read();
                printingTodayNet = nr.GetDecimal(0);
                salesTodayNet = nr.GetDecimal(1);
            }

            decimal printingExpected = printingOpening + printingTodayNet;
            decimal salesExpected = salesOpening + salesTodayNet;
            decimal? printingActual = null, salesActual = null;
            decimal? printingDifference = null, salesDifference = null;
            if (session.SalesActual.HasValue)
            {
                // حافظ على إجمالي الفعلي القديم مع إبقاء الرصيدين غير سالبين.
                decimal totalActual = Math.Max(0m, session.SalesActual.Value);
                decimal printActualValue = Math.Min(Math.Max(0m, printingExpected), totalActual);
                decimal salesActualValue = totalActual - printActualValue;
                printingActual = printActualValue;
                salesActual = salesActualValue;
                printingDifference = printActualValue - printingExpected;
                salesDifference = salesActualValue - salesExpected;
            }

            using (var updateSales = connection.CreateCommand())
            {
                updateSales.Transaction = tx;
                updateSales.CommandText = @"UPDATE DailyBalances
SET OpeningAmount=$opening,ExpectedAmount=$expected,ActualAmount=$actual,Difference=$difference
WHERE SessionId=$session AND AccountKey='SALES';";
                updateSales.Parameters.AddWithValue("$opening", salesOpening);
                updateSales.Parameters.AddWithValue("$expected", salesExpected);
                updateSales.Parameters.AddWithValue("$actual", salesActual.HasValue ? (object)salesActual.Value : DBNull.Value);
                updateSales.Parameters.AddWithValue("$difference", salesDifference.HasValue ? (object)salesDifference.Value : DBNull.Value);
                updateSales.Parameters.AddWithValue("$session", session.Id);
                updateSales.ExecuteNonQuery();
            }

            using (var insertPrinting = connection.CreateCommand())
            {
                insertPrinting.Transaction = tx;
                insertPrinting.CommandText = @"INSERT OR IGNORE INTO DailyBalances
(SessionId,AccountKey,OpeningAmount,ExpectedAmount,ActualAmount,Difference)
VALUES($session,'PRINTING',$opening,$expected,$actual,$difference);";
                insertPrinting.Parameters.AddWithValue("$session", session.Id);
                insertPrinting.Parameters.AddWithValue("$opening", printingOpening);
                insertPrinting.Parameters.AddWithValue("$expected", printingExpected);
                insertPrinting.Parameters.AddWithValue("$actual", printingActual.HasValue ? (object)printingActual.Value : DBNull.Value);
                insertPrinting.Parameters.AddWithValue("$difference", printingDifference.HasValue ? (object)printingDifference.Value : DBNull.Value);
                insertPrinting.ExecuteNonQuery();
            }
        }

        using (var setVersion = connection.CreateCommand())
        {
            setVersion.Transaction = tx;
            setVersion.CommandText = "PRAGMA user_version = 2;";
            setVersion.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Adds a distinct InstaPay account and an informational commission account.
    /// Commission movements are excluded from the overall treasury balance because
    /// those commissions are already included in the actual drawer movements.
    /// </summary>
    private static void MigrateWalletAndCommissionAccounts(SqliteConnection connection)
    {
        long version;
        using (var versionCmd = connection.CreateCommand())
        {
            versionCmd.CommandText = "PRAGMA user_version;";
            version = Convert.ToInt64(versionCmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
        if (version >= 3) return;

        using var tx = connection.BeginTransaction();
        using (var migrate = connection.CreateCommand())
        {
            migrate.Transaction = tx;
            migrate.CommandText = @"
-- Older releases did not reliably distinguish wallet-side cash-service movements.
-- Separate them by provider: InstaPay to INSTAPAY, Vodafone/Fawry to CASH;
-- the drawer-side IN movements stay in DRAWER.
UPDATE TreasuryMovements
SET AccountKey=CASE
    WHEN EXISTS (SELECT 1 FROM Cash_Services cs WHERE cs.Id=TreasuryMovements.ReferenceId AND cs.Provider='InstaPay' COLLATE NOCASE)
    THEN 'INSTAPAY' ELSE 'CASH' END
WHERE ReferenceType='CashService'
  AND MovementType='OUT'
  AND Category='خدمات الكاش'
  AND AccountKey IN ('CASH','DRAWER','INSTAPAY')
  AND EXISTS (SELECT 1 FROM Cash_Services cs WHERE cs.Id=TreasuryMovements.ReferenceId);

-- Backfill informational commission movements for operations created by older builds.
INSERT INTO TreasuryMovements
(UserId,MovementType,Category,Amount,Description,ReferenceType,ReferenceId,AccountKey,ShiftId,CreatedAt)
SELECT cs.UserId,'IN','النسبة',cs.Commission,
       'نسبة ' || cs.ServiceType || ' - ' || cs.Provider,
       'CashCommission',cs.Id,'PERCENTAGE',
       (SELECT MAX(tm.ShiftId) FROM TreasuryMovements tm WHERE tm.ReferenceType='CashService' AND tm.ReferenceId=cs.Id),
       cs.CreatedAt
FROM Cash_Services cs
WHERE COALESCE(cs.Commission,0)>0
  AND NOT EXISTS (SELECT 1 FROM TreasuryMovements tm WHERE tm.ReferenceType='CashCommission' AND tm.ReferenceId=cs.Id);

INSERT INTO TreasuryMovements
(UserId,MovementType,Category,Amount,Description,ReferenceType,ReferenceId,AccountKey,ShiftId,CreatedAt)
SELECT tr.UserId,'IN','النسبة',tr.Commission,
       'نسبة عمولة ' || COALESCE(tr.ServiceName,'خدمات الدفع') || ' | رقم العملية: ' || tr.Id,
       'MachineCommission',tr.Id,'PERCENTAGE',
       (SELECT MAX(tm.ShiftId) FROM TreasuryMovements tm WHERE tm.ReferenceType='ServiceTransaction' AND tm.ReferenceId=tr.Id),
       tr.CreatedAt
FROM Transactions tr
WHERE tr.TransactionType='SERVICE'
  AND COALESCE(tr.Commission,0)>0
  AND (tr.ServiceName LIKE 'ماكينة OPay%' OR tr.ServiceName LIKE 'OPay%'
       OR tr.ServiceName LIKE 'ماكينة باب رزق%' OR tr.ServiceName LIKE 'ماكينة BEE%' OR tr.ServiceName LIKE 'BEE%')
  AND NOT EXISTS (SELECT 1 FROM TreasuryMovements tm WHERE tm.ReferenceType='MachineCommission' AND tm.ReferenceId=tr.Id);
";
            migrate.ExecuteNonQuery();
        }

        // Add the new daily rows to existing sessions. Closed days get a readable
        // commission row but do not have their old drawer records changed beyond the
        // InstaPay split needed for separate reporting.
        var sessions = new List<(long Id, string Date, string Status, decimal CashOpening, decimal? CashActual)>();
        using (var readSessions = connection.CreateCommand())
        {
            readSessions.Transaction = tx;
            readSessions.CommandText = @"
SELECT s.Id,s.BusinessDate,s.Status,COALESCE(cash.OpeningAmount,0),cash.ActualAmount
FROM DailySessions s
LEFT JOIN DailyBalances cash ON cash.SessionId=s.Id AND cash.AccountKey='CASH'
ORDER BY s.BusinessDate;";
            using var r = readSessions.ExecuteReader();
            while (r.Read())
                sessions.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetDecimal(3), r.IsDBNull(4) ? null : r.GetDecimal(4)));
        }

        var instapayAccount = GetWalletAccount(connection, "InstaPay", tx);
        foreach (var session in sessions)
        {
            decimal instaOpening;
            if (string.Equals(session.Status, "OPEN", StringComparison.OrdinalIgnoreCase)
                && string.Equals(session.Date, DateTime.Today.ToString("yyyy-MM-dd"), StringComparison.Ordinal))
            {
                decimal todayNet = SumAccountNet(connection, "INSTAPAY", session.Date, tx);
                instaOpening = instapayAccount.current - todayNet;
            }
            else
            {
                instaOpening = instapayAccount.opening + SumAccountNetBefore(connection, "INSTAPAY", session.Date, tx);
            }

            decimal instaIncoming = SumAccountType(connection, "INSTAPAY", session.Date, "IN", tx);
            decimal instaOutgoing = SumAccountType(connection, "INSTAPAY", session.Date, "OUT", tx);
            decimal instaExpected = instaOpening + instaIncoming - instaOutgoing;
            decimal cashOpening = Math.Max(0m, session.CashOpening - instaOpening);
            decimal cashIncoming = SumAccountType(connection, "CASH", session.Date, "IN", tx);
            decimal cashOutgoing = SumAccountType(connection, "CASH", session.Date, "OUT", tx);
            decimal cashExpected = cashOpening + cashIncoming - cashOutgoing;

            decimal? instaActual = session.CashActual.HasValue ? instaExpected : null;
            decimal? cashActual = session.CashActual.HasValue ? Math.Max(0m, session.CashActual.Value - instaExpected) : null;
            decimal? instaDifference = instaActual.HasValue ? instaActual.Value - instaExpected : null;
            decimal? cashDifference = cashActual.HasValue ? cashActual.Value - cashExpected : null;

            using (var saveInstapay = connection.CreateCommand())
            {
                saveInstapay.Transaction = tx;
                saveInstapay.CommandText = @"
INSERT INTO DailyBalances(SessionId,AccountKey,OpeningAmount,ExpectedAmount,ActualAmount,Difference)
VALUES($session,'INSTAPAY',$opening,$expected,$actual,$difference)
ON CONFLICT(SessionId,AccountKey) DO UPDATE SET
OpeningAmount=excluded.OpeningAmount,ExpectedAmount=excluded.ExpectedAmount,
ActualAmount=excluded.ActualAmount,Difference=excluded.Difference;";
                saveInstapay.Parameters.AddWithValue("$session", session.Id);
                saveInstapay.Parameters.AddWithValue("$opening", Math.Max(0m, instaOpening));
                saveInstapay.Parameters.AddWithValue("$expected", instaExpected);
                saveInstapay.Parameters.AddWithValue("$actual", (object?)instaActual ?? DBNull.Value);
                saveInstapay.Parameters.AddWithValue("$difference", (object?)instaDifference ?? DBNull.Value);
                saveInstapay.ExecuteNonQuery();
            }

            using (var saveCash = connection.CreateCommand())
            {
                saveCash.Transaction = tx;
                saveCash.CommandText = @"
UPDATE DailyBalances
SET OpeningAmount=$opening,ExpectedAmount=$expected,ActualAmount=$actual,Difference=$difference
WHERE SessionId=$session AND AccountKey='CASH';";
                saveCash.Parameters.AddWithValue("$session", session.Id);
                saveCash.Parameters.AddWithValue("$opening", cashOpening);
                saveCash.Parameters.AddWithValue("$expected", cashExpected);
                saveCash.Parameters.AddWithValue("$actual", (object?)cashActual ?? DBNull.Value);
                saveCash.Parameters.AddWithValue("$difference", (object?)cashDifference ?? DBNull.Value);
                saveCash.ExecuteNonQuery();
            }

            decimal percentageToday = SumAccountType(connection, "PERCENTAGE", session.Date, "IN", tx)
                                    - SumAccountType(connection, "PERCENTAGE", session.Date, "OUT", tx);
            bool isClosed = string.Equals(session.Status, "CLOSED", StringComparison.OrdinalIgnoreCase);
            using (var savePercentage = connection.CreateCommand())
            {
                savePercentage.Transaction = tx;
                savePercentage.CommandText = @"
INSERT INTO DailyBalances(SessionId,AccountKey,OpeningAmount,ExpectedAmount,ActualAmount,Difference)
VALUES($session,'PERCENTAGE',0,$expected,$actual,$difference)
ON CONFLICT(SessionId,AccountKey) DO UPDATE SET
OpeningAmount=0,ExpectedAmount=excluded.ExpectedAmount,ActualAmount=excluded.ActualAmount,Difference=excluded.Difference;";
                savePercentage.Parameters.AddWithValue("$session", session.Id);
                savePercentage.Parameters.AddWithValue("$expected", percentageToday);
                savePercentage.Parameters.AddWithValue("$actual", isClosed ? (object)percentageToday : DBNull.Value);
                savePercentage.Parameters.AddWithValue("$difference", isClosed ? (object)0m : DBNull.Value);
                savePercentage.ExecuteNonQuery();
            }
        }

        using (var setVersion = connection.CreateCommand())
        {
            setVersion.Transaction = tx;
            setVersion.CommandText = "PRAGMA user_version = 3;";
            setVersion.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static decimal SumAccountNet(SqliteConnection connection, string accountKey, string date, SqliteTransaction tx)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"SELECT COALESCE(SUM(CASE WHEN MovementType='IN' THEN Amount ELSE -Amount END),0)
FROM TreasuryMovements WHERE AccountKey=$account AND substr(CreatedAt,1,10)=$date";
        command.Parameters.AddWithValue("$account", accountKey);
        command.Parameters.AddWithValue("$date", date);
        return Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
    }

    private static decimal SumAccountNetBefore(SqliteConnection connection, string accountKey, string date, SqliteTransaction tx)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"SELECT COALESCE(SUM(CASE WHEN MovementType='IN' THEN Amount ELSE -Amount END),0)
FROM TreasuryMovements WHERE AccountKey=$account AND substr(CreatedAt,1,10)<$date";
        command.Parameters.AddWithValue("$account", accountKey);
        command.Parameters.AddWithValue("$date", date);
        return Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
    }

    private static decimal SumAccountType(SqliteConnection connection, string accountKey, string date, string movementType, SqliteTransaction tx)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"SELECT COALESCE(SUM(Amount),0) FROM TreasuryMovements
WHERE AccountKey=$account AND substr(CreatedAt,1,10)=$date AND MovementType=$type";
        command.Parameters.AddWithValue("$account", accountKey);
        command.Parameters.AddWithValue("$date", date);
        command.Parameters.AddWithValue("$type", movementType);
        return Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info([{tableName}]);";

        bool exists = false;
        using (var reader = check.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE [{tableName}] ADD COLUMN [{columnName}] {columnDefinition};";
        alter.ExecuteNonQuery();

        // نعوض القيم الزمنية المفقودة في قواعد البيانات القديمة.
        if (tableName == "Products" && (columnName == "CreatedAt" || columnName == "UpdatedAt"))
        {
            using var fill = connection.CreateCommand();
            fill.CommandText = $"UPDATE Products SET [{columnName}] = $now WHERE [{columnName}] IS NULL OR TRIM(CAST([{columnName}] AS TEXT)) = '';";
            fill.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
            fill.ExecuteNonQuery();
        }
    }

    public static long GetUserId(SqliteConnection connection, string userName, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM AppUsers WHERE UserName = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", userName);
        var value = command.ExecuteScalar();
        if (value == null || value == DBNull.Value)
            throw new InvalidOperationException($"المستخدم '{userName}' غير موجود في قاعدة البيانات.");
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public static long GetOrCreateCustomer(SqliteConnection connection, string name, string? phone, SqliteTransaction? transaction = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("اسم العميل إجباري.");

        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

        if (phone != null)
        {
            using var lookup = connection.CreateCommand();
            lookup.Transaction = transaction;
            lookup.CommandText = "SELECT Id FROM Customers WHERE Phone = $phone LIMIT 1";
            lookup.Parameters.AddWithValue("$phone", phone);
            var existing = lookup.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
                return Convert.ToInt64(existing, CultureInfo.InvariantCulture);
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = @"
INSERT INTO Customers(Name, Phone, CreatedAt)
VALUES($name, $phone, $created);
SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$name", name.Trim());
        insert.Parameters.AddWithValue("$phone", (object?)phone ?? DBNull.Value);
        insert.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        return Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public static decimal GetCustomerDebt(SqliteConnection connection, long customerId, SqliteTransaction? transaction = null, long? excludeTransactionId = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // الرصيد الأساسي يأتي من سجل الديون والسداد. بعض الإصدارات القديمة كانت
        // تحفظ RemainingAmount في Transactions دون إنشاء قيد DEBT مقابِل؛ نضيف
        // هذه الأرصدة الناقصة فقط، ونتجنب التكرار إذا كان للعملية قيد دين بالفعل.
        command.CommandText = @"
SELECT
  COALESCE((
    SELECT SUM(CASE WHEN d.Type='DEBT' THEN d.Amount ELSE -d.Amount END)
    FROM Debts_History d WHERE d.CustomerId=$customer
  ),0)
  +
  COALESCE((
    SELECT SUM(COALESCE(t.RemainingAmount,0))
    FROM Transactions t
    WHERE t.CustomerId=$customer
      AND t.TransactionType IN ('SALE','COPY','SERVICE')
      AND COALESCE(t.RemainingAmount,0)>0
      AND ($excludeTransaction IS NULL OR t.Id<>$excludeTransaction)
      AND NOT EXISTS (
        SELECT 1 FROM Debts_History d
        WHERE d.TransactionId=t.Id AND d.Type='DEBT'
      )
  ),0);";
        command.Parameters.AddWithValue("$customer", customerId);
        command.Parameters.AddWithValue("$excludeTransaction", (object?)excludeTransactionId ?? DBNull.Value);
        decimal balance = Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        // رصيد الدين المستحق لا يمكن أن يكون سالبًا في واجهة المستخدم.
        return Math.Max(0m, balance);
    }

    public sealed class DailyShiftInfo
    {
        public long Id { get; init; }
        public long SessionId { get; init; }
        public string ShiftKey { get; init; } = "MORNING";
        public string ShiftName => ShiftKey == "MORNING" ? "صباحي" : "مسائي";
        public string UserName { get; init; } = "";
        public string Status { get; init; } = "OPEN";
        public DateTime StartedAt { get; init; }
        public DateTime? EndedAt { get; init; }
    }

    public static DailyShiftInfo? GetActiveShift(SqliteConnection connection, DateTime date, string userName, SqliteTransaction? transaction = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"SELECT s.Id,s.SessionId,s.ShiftKey,s.UserName,s.Status,s.StartedAt,s.EndedAt
FROM DailyShifts s JOIN DailySessions d ON d.Id=s.SessionId
WHERE d.BusinessDate=$date AND s.Status='OPEN' AND s.UserName=$user
ORDER BY s.StartedAt DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$date", date.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$user", userName);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DailyShiftInfo
        {
            Id = r.GetInt64(0), SessionId = r.GetInt64(1), ShiftKey = r.GetString(2), UserName = r.GetString(3),
            Status = r.GetString(4), StartedAt = DateTime.Parse(r.GetString(5)), EndedAt = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6))
        };
    }

    public static DailyShiftInfo? GetActiveShiftForSession(SqliteConnection connection, long sessionId, string userName, SqliteTransaction? transaction = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT Id,SessionId,ShiftKey,UserName,Status,StartedAt,EndedAt FROM DailyShifts WHERE SessionId=$session AND Status='OPEN' AND UserName=$user ORDER BY StartedAt DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$session", sessionId);
        cmd.Parameters.AddWithValue("$user", userName);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DailyShiftInfo { Id=r.GetInt64(0), SessionId=r.GetInt64(1), ShiftKey=r.GetString(2), UserName=r.GetString(3), Status=r.GetString(4), StartedAt=DateTime.Parse(r.GetString(5)), EndedAt=r.IsDBNull(6)?null:DateTime.Parse(r.GetString(6)) };
    }

    public static List<DailyShiftInfo> GetDailyShifts(SqliteConnection connection, long sessionId, SqliteTransaction? transaction = null)
    {
        var result = new List<DailyShiftInfo>();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT Id,SessionId,ShiftKey,UserName,Status,StartedAt,EndedAt FROM DailyShifts WHERE SessionId=$session ORDER BY CASE ShiftKey WHEN 'MORNING' THEN 1 ELSE 2 END";
        cmd.Parameters.AddWithValue("$session", sessionId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new DailyShiftInfo { Id=r.GetInt64(0), SessionId=r.GetInt64(1), ShiftKey=r.GetString(2), UserName=r.GetString(3), Status=r.GetString(4), StartedAt=DateTime.Parse(r.GetString(5)), EndedAt=r.IsDBNull(6)?null:DateTime.Parse(r.GetString(6)) });
        return result;
    }

    public static long StartDailyShift(SqliteConnection connection, string userName, long sessionId, string shiftKey)
    {
        shiftKey = shiftKey.Trim().ToUpperInvariant();
        if (shiftKey != "MORNING" && shiftKey != "EVENING") throw new ArgumentException("نوع الشفت غير صحيح.");
        using var tx = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT Status FROM DailySessions WHERE Id=$id LIMIT 1";
            check.Parameters.AddWithValue("$id", sessionId);
            var status = check.ExecuteScalar()?.ToString();
            if (!string.Equals(status, "OPEN", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("لا يمكن بدء شفت على يومية مغلقة.");
        }
        using (var prev = connection.CreateCommand())
        {
            prev.Transaction = tx;
            prev.CommandText = "SELECT Id,ShiftKey FROM DailyShifts WHERE SessionId=$session AND Status='OPEN' LIMIT 1";
            prev.Parameters.AddWithValue("$session", sessionId);
            using var r = prev.ExecuteReader();
            if (r.Read() && r.GetString(1) != shiftKey) throw new InvalidOperationException("هناك شفت آخر مفتوح. سلّم الشفت الحالي أولًا قبل بدء الشفت التالي.");
        }
        using (var exists = connection.CreateCommand())
        {
            exists.Transaction = tx;
            exists.CommandText = "SELECT Id,Status,UserName FROM DailyShifts WHERE SessionId=$session AND ShiftKey=$shift LIMIT 1";
            exists.Parameters.AddWithValue("$session", sessionId);
            exists.Parameters.AddWithValue("$shift", shiftKey);
            using var r = exists.ExecuteReader();
            if (r.Read())
            {
                var id = r.GetInt64(0); var status = r.GetString(1); var owner = r.GetString(2);
                if (status == "OPEN" && string.Equals(owner, userName, StringComparison.OrdinalIgnoreCase)) { tx.Commit(); return id; }
                if (status == "CLOSED") throw new InvalidOperationException("هذا الشفت تم إغلاقه بالفعل اليوم.");
                throw new InvalidOperationException($"الشفت {shiftKey} مفتوح بالفعل بواسطة {owner}.");
            }
        }
        long uid = GetUserId(connection, userName, tx);
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO DailyShifts(SessionId,ShiftKey,UserId,UserName,Status,StartedAt) VALUES($session,$shift,$userId,$user,'OPEN',$at); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$session", sessionId); cmd.Parameters.AddWithValue("$shift", shiftKey); cmd.Parameters.AddWithValue("$userId", uid); cmd.Parameters.AddWithValue("$user", userName); cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("O"));
        var idNew = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);

        // ربط أي حركات مالية موجودة اليوم للمستخدم نفسه ولم تكن مربوطة بشفت
        // حتى لا تضيع حركات اليوم عند تفعيل نظام الشفتات لأول مرة.
        using (var adopt = connection.CreateCommand())
        {
            adopt.Transaction = tx;
            adopt.CommandText = @"UPDATE TreasuryMovements
SET ShiftId=$shift
WHERE ShiftId IS NULL AND UserId=$user
  AND date(CreatedAt)=(SELECT BusinessDate FROM DailySessions WHERE Id=$session)";
            adopt.Parameters.AddWithValue("$shift", idNew);
            adopt.Parameters.AddWithValue("$user", uid);
            adopt.Parameters.AddWithValue("$session", sessionId);
            adopt.ExecuteNonQuery();
        }
        tx.Commit(); return idNew;
    }

    public static void EndDailyShift(SqliteConnection connection, string userName, long shiftId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT UserName,Status FROM DailyShifts WHERE Id=$id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", shiftId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("الشفت غير موجود.");
        var owner = r.GetString(0); var status = r.GetString(1);
        if (!string.Equals(owner, userName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("لا يمكنك إنهاء شفت مستخدم آخر.");
        if (status == "CLOSED") return;
        r.Close();
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE DailyShifts SET Status='CLOSED',EndedAt=$at WHERE Id=$id";
        update.Parameters.AddWithValue("$at", DateTime.Now.ToString("O")); update.Parameters.AddWithValue("$id", shiftId); update.ExecuteNonQuery();
    }

    /// <summary>
    /// الرصيد المتاح لسحب يدوي من أحد مصادر الأموال الفعلية.
    /// DRAWER يمثل كل النقد المتاح داخل الدرج (بما فيه المقبوضات النقدية)،
    /// CASH في هذا المسار يعني محفظة Vodafone Cash فقط، وINSTAPAY حساب إنستا باي.
    /// </summary>
    public static decimal GetManualWithdrawalAvailableBalance(
        SqliteConnection connection, string accountKey, SqliteTransaction? transaction = null)
    {
        string key = (accountKey ?? string.Empty).Trim().ToUpperInvariant();
        return key switch
        {
            "DRAWER" => Math.Max(0m, GetActualDrawerBalance(connection, transaction)),
            "CASH" => Math.Max(0m, GetWalletAccount(connection, "Vodafone Cash", transaction).current),
            "INSTAPAY" => Math.Max(0m, GetWalletAccount(connection, "InstaPay", transaction).current),
            _ => throw new ArgumentException("مصدر السحب غير صالح. اختر الدرج أو فودافون كاش أو إنستا باي.", nameof(accountKey))
        };
    }

    /// <summary>
    /// يسجل السحب اليدوي ويحدّث رصيد المحفظة المختارة داخل معاملة واحدة.
    /// لا يمكن خصم مبلغ أكبر من الرصيد المتاح، ولا يحدث تحديث جزئي للحساب.
    /// </summary>
    public static void RecordManualTreasuryWithdrawal(
        SqliteConnection connection, string userName, string category, decimal amount,
        string? description, string accountKey)
    {
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "المبلغ يجب أن يكون أكبر من صفر.");
        string key = (accountKey ?? string.Empty).Trim().ToUpperInvariant();
        if (key is not ("DRAWER" or "CASH" or "INSTAPAY"))
            throw new ArgumentException("مصدر السحب غير صالح.", nameof(accountKey));

        using var tx = connection.BeginTransaction();
        decimal available = GetManualWithdrawalAvailableBalance(connection, key, tx);
        if (amount > available)
        {
            string sourceName = key switch
            {
                "DRAWER" => "النقدية في الدرج",
                "CASH" => "فودافون كاش",
                _ => "إنستا باي"
            };
            throw new InvalidOperationException($"الرصيد المتاح في {sourceName} هو {available:N2} ج.م فقط، ولا يمكن سحب {amount:N2} ج.م.");
        }

        // المحفظة لها رصيد قائم مستقل، بينما النقدية في الدرج محسوبة مباشرة من دفتر الحركات.
        if (key == "CASH") AdjustWalletBalance(connection, "Vodafone Cash", -amount, tx);
        else if (key == "INSTAPAY") AdjustWalletBalance(connection, "InstaPay", -amount, tx);

        string refType = key == "DRAWER" ? "Manual" : "ManualWalletWithdrawal";
        string source = key switch
        {
            "DRAWER" => "النقدية في الدرج",
            "CASH" => "فودافون كاش",
            _ => "إنستا باي"
        };
        string details = string.IsNullOrWhiteSpace(description) ? $"مصدر السحب: {source}" : $"مصدر السحب: {source} — {description.Trim()}";
        AddTreasury(connection, userName, "OUT", category, amount, details, refType, null, tx, key);
        tx.Commit();
    }

    /// <summary>يسجل إضافة يدوية إلى الدرج أو إحدى المحافظ الإلكترونية كوحدة ذرّية واحدة.</summary>
    public static void RecordManualTreasuryDeposit(
        SqliteConnection connection, string userName, string category, decimal amount,
        string? description, string accountKey)
    {
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "المبلغ يجب أن يكون أكبر من صفر.");
        string key = (accountKey ?? string.Empty).Trim().ToUpperInvariant();
        if (key is not ("DRAWER" or "CASH" or "INSTAPAY"))
            throw new ArgumentException("حساب الإضافة غير صالح. اختر الدرج أو فودافون كاش أو إنستا باي.", nameof(accountKey));

        using var tx = connection.BeginTransaction();
        if (key == "CASH") AdjustWalletBalance(connection, "Vodafone Cash", amount, tx);
        else if (key == "INSTAPAY") AdjustWalletBalance(connection, "InstaPay", amount, tx);

        string destination = key switch
        {
            "DRAWER" => "النقدية في الدرج",
            "CASH" => "فودافون كاش",
            _ => "إنستا باي"
        };
        string details = string.IsNullOrWhiteSpace(description)
            ? $"وجهة الإضافة: {destination}"
            : $"وجهة الإضافة: {destination} — {description.Trim()}";
        AddTreasury(connection, userName, "IN", category, amount, details, "Manual", null, tx, key);
        tx.Commit();
    }

    public static void AddTreasury(SqliteConnection connection, string userName, string movementType,
        string category, decimal amount, string? description, string? referenceType, long? referenceId,
        SqliteTransaction? transaction = null, string? accountKey = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        // عند عدم تمرير معاملة من المستدعي، نجعل الحركة وسجل التدقيق عملية ذرّية واحدة.
        if (transaction == null)
        {
            using var ownTransaction = connection.BeginTransaction();
            AddTreasury(connection, userName, movementType, category, amount, description,
                referenceType, referenceId, ownTransaction, accountKey);
            ownTransaction.Commit();
            return;
        }

        // بعد اعتماد اليومية لا نسمح بإنشاء حركة جديدة على نفس اليوم،
        // حتى تظل أرقام الإغلاق مرتبطة بالحركات الفعلية فقط.
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT Status FROM DailySessions WHERE BusinessDate=$date LIMIT 1";
            check.Parameters.AddWithValue("$date", today);
            string? status = check.ExecuteScalar()?.ToString();
            if (string.Equals(status, "CLOSED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("اليومية الحالية مغلقة. افتح يومية جديدة في يوم العمل التالي قبل تسجيل حركة جديدة.");
        }

        string resolvedAccount = string.IsNullOrWhiteSpace(accountKey)
            ? ResolveTreasuryAccountKey(connection, movementType, category, referenceType, referenceId, transaction)
            : NormalizeAccountKey(accountKey!);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO TreasuryMovements(UserId, MovementType, Category, Amount, Description, ReferenceType, ReferenceId, AccountKey, ShiftId, CreatedAt)
VALUES($user, $type, $category, $amount, $description, $refType, $refId, $account, $shift, $created)";
        command.Parameters.AddWithValue("$user", GetUserId(connection, userName, transaction));
        command.Parameters.AddWithValue("$type", movementType);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$description", (object?)description ?? DBNull.Value);
        command.Parameters.AddWithValue("$refType", (object?)referenceType ?? DBNull.Value);
        command.Parameters.AddWithValue("$refId", (object?)referenceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$account", resolvedAccount);
        var activeShift = GetActiveShift(connection, DateTime.Now.Date, userName, transaction);
        if (activeShift == null) throw new InvalidOperationException("لا يوجد شفت مفتوح لهذا المستخدم اليوم. افتح اليومية ثم ابدأ الشفت الحالي قبل تسجيل أي حركة مالية.");
        command.Parameters.AddWithValue("$shift", activeShift.Id);
        command.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        command.ExecuteNonQuery();

        using var idCommand = connection.CreateCommand();
        idCommand.Transaction = transaction;
        idCommand.CommandText = "SELECT last_insert_rowid()";
        long movementId = Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        AddAuditLog(connection, transaction, userName,
            movementType == "IN" ? "إضافة حركة داخلة" : "إضافة حركة خارجة", "خزينة", movementId,
            $"التصنيف: {category} | المبلغ: {amount:0.00} | الحساب: {resolvedAccount} | التفاصيل: {description ?? string.Empty}");
    }

    private static string NormalizeAccountKey(string accountKey) => accountKey.Trim().ToUpperInvariant() switch
    {
        "OPAY" => "OPAY",
        "BEE" => "BEE",
        "PRINTING" => "PRINTING",
        "SALES" => "SALES",
        "DRAWER" => "DRAWER",
        "CASH" => "CASH",
        "INSTAPAY" => "INSTAPAY",
        "PERCENTAGE" => "PERCENTAGE",
        _ => "CASH"
    };

    private static string ResolveTreasuryAccountKey(SqliteConnection connection, string movementType, string category, string? referenceType, long? referenceId, SqliteTransaction? transaction)
    {
        if (string.Equals(referenceType, "PrintLog", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(referenceType, "Copy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "الطباعة", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "التصوير", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "الطباعة والتصوير", StringComparison.OrdinalIgnoreCase))
            return "PRINTING";

        // المبيعات لها حساب مستقل حتى لا تختلط بحساب السحب والإيداع.
        // يجب اختبارها قبل referenceType=Transaction لأن عمليات البيع تحمل نفس المرجع.
        if (string.Equals(category, "مبيعات", StringComparison.OrdinalIgnoreCase))
            return "SALES";

        // خدمات الكاش (سحب/إيداع المحافظ الإلكترونية) لا تُعتبر "مبيعات"؛ عمولتها
        // ومبلغها الداخل للمحل يذهبان إلى النقدية في الدرج، منفصلة عن المبيعات.
        if (string.Equals(referenceType, "CashService", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "خدمات الكاش", StringComparison.OrdinalIgnoreCase))
            return "DRAWER";

        if (string.Equals(referenceType, "Transaction", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(referenceType, "DebtPayment", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "تحصيل ديون", StringComparison.OrdinalIgnoreCase))
            return "CASH";

        if (string.Equals(referenceType, "ServiceTransaction", StringComparison.OrdinalIgnoreCase) && referenceId.HasValue)
        {
            using var lookup = connection.CreateCommand();
            lookup.Transaction = transaction;
            lookup.CommandText = "SELECT COALESCE(ServiceName,'') FROM Transactions WHERE Id=$id LIMIT 1";
            lookup.Parameters.AddWithValue("$id", referenceId.Value);
            string service = lookup.ExecuteScalar()?.ToString() ?? string.Empty;
            bool opay = service.StartsWith("ماكينة OPay", StringComparison.OrdinalIgnoreCase) || service.StartsWith("OPay", StringComparison.OrdinalIgnoreCase);
            bool bee = service.StartsWith("ماكينة باب رزق", StringComparison.OrdinalIgnoreCase) || service.StartsWith("ماكينة BEE", StringComparison.OrdinalIgnoreCase) || service.StartsWith("BEE", StringComparison.OrdinalIgnoreCase);
            if (opay || bee)
            {
                // في خدمة الماكينة: المال الذي يدفعه العميل يدخل النقدية في الدرج،
                // بينما قيمة العملية الخارجة تُخصم من رصيد الماكينة.
                if (string.Equals(movementType, "OUT", StringComparison.OrdinalIgnoreCase))
                    return opay ? "OPAY" : "BEE";
                return "DRAWER";
            }
        }

        return "CASH";
    }

    public sealed class DailyAccountSummary
    {
        public string AccountKey { get; init; } = "CASH";
        public decimal Opening { get; init; }
        public decimal Incoming { get; init; }
        public decimal Outgoing { get; init; }
        public decimal Expected => Opening + Incoming - Outgoing;
        public decimal? Actual { get; init; }
        public decimal? Difference { get; init; }
    }

    public sealed class DailySessionInfo
    {
        public long Id { get; init; }
        public DateTime BusinessDate { get; init; }
        public string Status { get; init; } = "OPEN";
        public string OpenedBy { get; init; } = "";
        public DateTime OpenedAt { get; init; }
        public string? ClosedBy { get; init; }
        public DateTime? ClosedAt { get; init; }
    }

    public static DailySessionInfo? GetDailySession(SqliteConnection connection, DateTime date, SqliteTransaction? transaction = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"SELECT Id,BusinessDate,Status,OpenedBy,OpenedAt,ClosedBy,ClosedAt
FROM DailySessions WHERE BusinessDate=$date LIMIT 1";
        cmd.Parameters.AddWithValue("$date", date.Date.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DailySessionInfo
        {
            Id = r.GetInt64(0),
            BusinessDate = DateTime.Parse(r.GetString(1)),
            Status = r.GetString(2),
            OpenedBy = r.GetString(3),
            OpenedAt = DateTime.Parse(r.GetString(4)),
            ClosedBy = r.IsDBNull(5) ? null : r.GetString(5),
            ClosedAt = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6))
        };
    }

    /// <summary>
    /// حسابات الماكينات (OPay وباب رزق) تسمح بالسلف من الشركة، فرصيدها يقدر يبقى سالب
    /// بشكل طبيعي. باقي الحسابات (خزينة/مبيعات/طباعة/محافظ) تفضل ممنوع تبقى سالبة.
    /// </summary>
    public static bool AllowsNegativeBalance(string accountKey)
        => string.Equals(accountKey, "OPAY", StringComparison.OrdinalIgnoreCase)
        || string.Equals(accountKey, "BEE", StringComparison.OrdinalIgnoreCase);

    public static long OpenDailySession(SqliteConnection connection, string userName, DateTime date, IReadOnlyDictionary<string, decimal> openingBalances)
    {
        string[] keys = { "OPAY", "BEE", "CASH", "INSTAPAY", "SALES", "PRINTING", "DRAWER", "PERCENTAGE" };
        foreach (string key in keys)
            if (!openingBalances.ContainsKey(key)) throw new ArgumentException($"رصيد البداية غير موجود للحساب {key}.");
        if (openingBalances.Any(kv => kv.Value < 0 && !AllowsNegativeBalance(kv.Key)))
            throw new ArgumentException("لا يمكن أن يكون رصيد البداية سالبًا (ما عدا حسابات الماكينات).");

        using var tx = connection.BeginTransaction();
        var existing = GetDailySession(connection, date, tx);
        if (existing != null)
        {
            if (existing.Status == "CLOSED") throw new InvalidOperationException("اليومية الخاصة بهذا التاريخ مغلقة بالفعل.");
            return existing.Id;
        }

        long userId = GetUserId(connection, userName, tx);
        long sessionId;
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO DailySessions(BusinessDate,Status,OpenedBy,OpenedAt)
VALUES($date,'OPEN',$user,$at); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$date", date.Date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$user", userName);
            cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("O"));
            sessionId = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        foreach (string key in keys)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO DailyBalances(SessionId,AccountKey,OpeningAmount) VALUES($session,$account,$opening)";
            cmd.Parameters.AddWithValue("$session", sessionId);
            cmd.Parameters.AddWithValue("$account", key);
            cmd.Parameters.AddWithValue("$opening", openingBalances[key]);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        _ = userId;
        return sessionId;
    }

    public static Dictionary<string, decimal> GetSuggestedOpeningBalances(SqliteConnection connection, DateTime date)
    {
        // رصيد بداية اليومية لا يأتي من يومية سابقة؛ المصدر الوحيد هو الرصيد
        // الحالي الفعلي المسجل في الإدارة/الخزينة وقت فتح اليومية.
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["OPAY"] = 0m,
            ["BEE"] = 0m,
            ["CASH"] = 0m,
            ["INSTAPAY"] = 0m,
            ["SALES"] = 0m,
            ["PRINTING"] = 0m,
            ["DRAWER"] = 0m,
            ["PERCENTAGE"] = 0m
        };

        var opay = GetMachineAccount(connection, "OPay").current;
        var bee = GetMachineAccount(connection, "BEE").current;

        // CASH keeps Vodafone Cash plus any legacy Fawry balance. InstaPay has
        // its own independent row in the daily journal.
        using (var walletCmd = connection.CreateCommand())
        {
            walletCmd.CommandText = "SELECT COALESCE(SUM(CurrentBalance),0) FROM WalletAccounts WHERE UPPER(Provider)<>'INSTAPAY'";
            result["CASH"] = Convert.ToDecimal(walletCmd.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }
        result["INSTAPAY"] = GetWalletAccount(connection, "InstaPay").current;
        // «النسبة» is an informational commission total for the day, not a second cash balance.
        result["PERCENTAGE"] = 0m;

        // المبيعات والطباعة/التصوير حسابان مستقلان. الحركات القديمة للطباعة
        // والتصوير تُعاد فهرستها في ترحيل قاعدة البيانات؛ لذلك يعتمد رصيد البداية
        // على AccountKey بعد الترحيل، وليس على تصنيف قديم مشترك مع المبيعات.
        using (var salesCmd = connection.CreateCommand())
        {
            salesCmd.CommandText = @"SELECT COALESCE(SUM(CASE WHEN MovementType='IN' THEN Amount ELSE -Amount END),0)
FROM TreasuryMovements WHERE AccountKey='SALES'";
            result["SALES"] = Convert.ToDecimal(salesCmd.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }

        using (var printingCmd = connection.CreateCommand())
        {
            printingCmd.CommandText = @"SELECT COALESCE(SUM(CASE WHEN MovementType='IN' THEN Amount ELSE -Amount END),0)
FROM TreasuryMovements WHERE AccountKey='PRINTING'";
            result["PRINTING"] = Convert.ToDecimal(printingCmd.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }

        // النقدية في الدرج = صافي حركات حساب DRAWER الفعلية (عمولات وأرباح خدمات
        // الماكينات والمحافظ الإلكترونية ومصروفات الخزينة اليدوية) — حساب منفصل.
        using (var drawerCmd = connection.CreateCommand())
        {
            drawerCmd.CommandText = @"SELECT COALESCE(SUM(CASE WHEN MovementType='IN' THEN Amount ELSE -Amount END),0)
FROM TreasuryMovements WHERE AccountKey='DRAWER'";
            result["DRAWER"] = Convert.ToDecimal(drawerCmd.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }

        result["OPAY"] = opay;
        result["BEE"] = bee;
        _ = date;
        return result;
    }

    public static List<DailyAccountSummary> GetDailyAccountBalances(SqliteConnection connection, long sessionId)
    {
        return GetDailyAccountSummaries(connection, sessionId);
    }

    public static List<DailyAccountSummary> GetDailyAccountSummaries(SqliteConnection connection, long sessionId)
    {
        var result = new List<DailyAccountSummary>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT b.AccountKey,b.OpeningAmount,
       COALESCE(SUM(CASE WHEN t.MovementType='IN' THEN t.Amount ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN t.MovementType='OUT' THEN t.Amount ELSE 0 END),0),
       b.ActualAmount,b.Difference
FROM DailyBalances b
JOIN DailySessions s ON s.Id=b.SessionId
LEFT JOIN TreasuryMovements t
  ON date(t.CreatedAt)=s.BusinessDate AND t.AccountKey=b.AccountKey
     AND (t.ShiftId IS NULL OR EXISTS (SELECT 1 FROM DailyShifts ds WHERE ds.Id=t.ShiftId AND ds.SessionId=s.Id))
WHERE b.SessionId=$session
GROUP BY b.Id,b.AccountKey,b.OpeningAmount,b.ActualAmount,b.Difference
ORDER BY CASE b.AccountKey WHEN 'OPAY' THEN 1 WHEN 'BEE' THEN 2 WHEN 'CASH' THEN 3 WHEN 'INSTAPAY' THEN 4 WHEN 'SALES' THEN 5 WHEN 'PRINTING' THEN 6 WHEN 'DRAWER' THEN 7 WHEN 'PERCENTAGE' THEN 8 ELSE 9 END;";
        cmd.Parameters.AddWithValue("$session", sessionId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new DailyAccountSummary
            {
                AccountKey = r.GetString(0),
                Opening = r.GetDecimal(1),
                Incoming = r.GetDecimal(2),
                Outgoing = r.GetDecimal(3),
                Actual = r.IsDBNull(4) ? null : r.GetDecimal(4),
                Difference = r.IsDBNull(5) ? null : r.GetDecimal(5)
            });
        }
        return result;
    }

    public static void CloseDailySession(SqliteConnection connection, string userName, long sessionId, IReadOnlyDictionary<string, decimal> actualBalances)
    {
        using var tx = connection.BeginTransaction();
        var session = GetDailySession(connection, DateTime.Now.Date, tx);
        if (session == null || session.Id != sessionId) throw new InvalidOperationException("اليومية الحالية غير موجودة.");
        if (session.Status == "CLOSED") throw new InvalidOperationException("اليومية مغلقة بالفعل.");

        var summaries = GetDailyAccountSummaries(connection, sessionId);
        foreach (var item in summaries)
        {
            if (!actualBalances.TryGetValue(item.AccountKey, out var actual))
                throw new ArgumentException($"الرصيد الفعلي غير موجود للحساب {item.AccountKey}.");
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE DailyBalances SET ExpectedAmount=$expected,ActualAmount=$actual,Difference=$difference WHERE SessionId=$session AND AccountKey=$account";
            cmd.Parameters.AddWithValue("$expected", item.Expected);
            cmd.Parameters.AddWithValue("$actual", actual);
            cmd.Parameters.AddWithValue("$difference", actual - item.Expected);
            cmd.Parameters.AddWithValue("$session", sessionId);
            cmd.Parameters.AddWithValue("$account", item.AccountKey);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE DailySessions SET Status='CLOSED',ClosedBy=$user,ClosedAt=$at WHERE Id=$id";
            cmd.Parameters.AddWithValue("$user", userName);
            cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static List<DailySessionInfo> GetDailySessionHistory(SqliteConnection connection)
    {
        var result = new List<DailySessionInfo>();
        var knownDates = new HashSet<string>(StringComparer.Ordinal);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id,BusinessDate,Status,OpenedBy,OpenedAt,ClosedBy,ClosedAt FROM DailySessions ORDER BY BusinessDate DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string businessDate = r.GetString(1);
                knownDates.Add(businessDate);
                result.Add(new DailySessionInfo
                {
                    Id = r.GetInt64(0), BusinessDate = DateTime.Parse(businessDate), Status = r.GetString(2),
                    OpenedBy = r.GetString(3), OpenedAt = DateTime.Parse(r.GetString(4)),
                    ClosedBy = r.IsDBNull(5) ? null : r.GetString(5),
                    ClosedAt = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6))
                });
            }
        }

        // الأيام القديمة التي تحتوي حركات مالية قبل تفعيل نظام اليومية تظهر أيضًا في السجل.
        // Id = 0 يعني أنها يومية تاريخية وليست Session قابلة للإغلاق.
        using (var legacy = connection.CreateCommand())
        {
            legacy.CommandText = @"
SELECT date(CreatedAt)
FROM TreasuryMovements
GROUP BY date(CreatedAt)
ORDER BY date(CreatedAt) DESC;";
            using var lr = legacy.ExecuteReader();
            while (lr.Read())
            {
                string date = lr.GetString(0);
                if (knownDates.Contains(date)) continue;
                result.Add(new DailySessionInfo
                {
                    Id = 0,
                    BusinessDate = DateTime.Parse(date),
                    Status = "LEGACY",
                    OpenedBy = "—",
                    OpenedAt = DateTime.Parse(date),
                    ClosedBy = null,
                    ClosedAt = null
                });
            }
        }

        return result.OrderByDescending(x => x.BusinessDate).ToList();
    }

    public static (decimal opening, decimal current, string displayName) GetMachineAccount(SqliteConnection connection, string machineKey, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT OpeningBalance, CurrentBalance, DisplayName FROM MachineAccounts WHERE UPPER(MachineKey)=UPPER($key) LIMIT 1";
        command.Parameters.AddWithValue("$key", machineKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return (0m, 0m, machineKey);
        return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetString(2));
    }

    public static (decimal opening, decimal current) GetWalletAccount(SqliteConnection connection, string provider, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT OpeningBalance, CurrentBalance FROM WalletAccounts WHERE Provider=$provider LIMIT 1";
        command.Parameters.AddWithValue("$provider", provider);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return (0m, 0m);
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    public static void SaveMachineAccount(SqliteConnection connection, string machineKey, string displayName, decimal openingBalance, decimal currentBalance, SqliteTransaction? transaction = null)
    {
        string normalizedKey = machineKey.Trim().ToUpperInvariant() switch
        {
            "OPAY" => "OPAY",
            "BEE" => "BEE",
            _ => machineKey.Trim().ToUpperInvariant()
        };
        string updatedAt = DateTime.Now.ToString("O");

        // Existing databases may store the OPay key as "OPay". Find it case-
        // insensitively and update that row instead of inserting a second account.
        using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT MachineKey FROM MachineAccounts WHERE UPPER(MachineKey)=UPPER($key) LIMIT 1";
        find.Parameters.AddWithValue("$key", normalizedKey);
        object? existingKey = find.ExecuteScalar();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (existingKey != null && existingKey != DBNull.Value)
        {
            command.CommandText = @"
UPDATE MachineAccounts
SET DisplayName=$name, OpeningBalance=$opening, CurrentBalance=$current, UpdatedAt=$updated
WHERE MachineKey=$existingKey";
            command.Parameters.AddWithValue("$existingKey", existingKey.ToString()!);
        }
        else
        {
            command.CommandText = @"
INSERT INTO MachineAccounts(MachineKey,DisplayName,OpeningBalance,CurrentBalance,UpdatedAt)
VALUES($key,$name,$opening,$current,$updated)";
            command.Parameters.AddWithValue("$key", normalizedKey);
        }
        command.Parameters.AddWithValue("$name", displayName);
        command.Parameters.AddWithValue("$opening", openingBalance);
        command.Parameters.AddWithValue("$current", currentBalance);
        command.Parameters.AddWithValue("$updated", updatedAt);
        command.ExecuteNonQuery();
    }

    public static void SaveWalletAccount(SqliteConnection connection, string provider, decimal openingBalance, decimal currentBalance, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO WalletAccounts(Provider,OpeningBalance,CurrentBalance,UpdatedAt)
VALUES($provider,$opening,$current,$updated)
ON CONFLICT(Provider) DO UPDATE SET
OpeningBalance=excluded.OpeningBalance,
CurrentBalance=excluded.CurrentBalance,
UpdatedAt=excluded.UpdatedAt";
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$opening", openingBalance);
        command.Parameters.AddWithValue("$current", currentBalance);
        command.Parameters.AddWithValue("$updated", DateTime.Now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public static void AdjustMachineBalance(SqliteConnection connection, string machineKey, decimal delta, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
UPDATE MachineAccounts
SET CurrentBalance=CurrentBalance+$delta, UpdatedAt=$updated
WHERE UPPER(MachineKey)=UPPER($key)";
        command.Parameters.AddWithValue("$delta", delta);
        command.Parameters.AddWithValue("$updated", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("$key", machineKey);
        int updatedRows = command.ExecuteNonQuery();
        if (updatedRows != 1)
            throw new InvalidOperationException($"تعذر تحديث رصيد الماكينة '{machineKey}': عدد الحسابات المطابقة {updatedRows}.");
    }

    public static void AdjustWalletBalance(SqliteConnection connection, string provider, decimal delta, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
UPDATE WalletAccounts
SET CurrentBalance=CurrentBalance+$delta, UpdatedAt=$updated
WHERE Provider=$provider";
        command.Parameters.AddWithValue("$delta", delta);
        command.Parameters.AddWithValue("$updated", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("$provider", provider);
        int updatedRows = command.ExecuteNonQuery();
        if (updatedRows != 1)
            throw new InvalidOperationException($"تعذر تحديث رصيد المحفظة '{provider}': لا يوجد حساب مطابق.");
    }

    /// <summary>
    /// يمسح بيانات النشاط التجاري بالكامل ويُبقي حسابات المستخدمين كما هي.
    /// الهدف: إعادة البرنامج لحالة جديدة بدون حذف كلمات مرور عمر/أحمد.
    /// </summary>
    public static void ResetBusinessData()
    {
        int pendingPrintLogs = PrintLogger.GetPendingLogCount();
        if (pendingPrintLogs != 0)
            throw new InvalidOperationException(pendingPrintLogs > 0
                ? $"لا يمكن تصفير بيانات النشاط لأن هناك {pendingPrintLogs} عملية طباعة معلقة. راجعها أو أعد مزامنتها أولًا حتى لا تضيع بيانات مالية."
                : "لا يمكن تصفير البيانات لأن البرنامج لم يستطع قراءة قائمة عمليات الطباعة المعلقة. عالج مشكلة الملف أولًا حتى لا تضيع أو تعود سجلات مالية بعد التصفير.");

        using var connection = OpenConnection();
        using var tx = connection.BeginTransaction();

        // ترتيب الحذف مهم مع Foreign Keys.
        string[] tables =
        {
            "PrintDebtLinks",
            "Debts_History",
            "Transactions",
            "Cash_Services",
            "TreasuryMovements",
            "PrintLog",
            "DailyShifts",
            "DailyBalances",
            "DailySessions",
            "Products",
            "Customers"
        };

        foreach (string table in tables)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = $"DELETE FROM [{table}];";
            command.ExecuteNonQuery();
        }

        // إعادة أرقام الـId من البداية للجداول التشغيلية.
        using (var seq = connection.CreateCommand())
        {
            seq.Transaction = tx;
            seq.CommandText = @"
DELETE FROM sqlite_sequence
WHERE name IN ('Customers','Products','Transactions','Debts_History','Cash_Services','TreasuryMovements','PrintLog','DailyBalances','DailySessions');";
            seq.ExecuteNonQuery();
        }

        using (var resetAccounts = connection.CreateCommand())
        {
            resetAccounts.Transaction = tx;
            resetAccounts.CommandText = @"
UPDATE MachineAccounts SET OpeningBalance=0, CurrentBalance=0, UpdatedAt=$now;
UPDATE WalletAccounts SET OpeningBalance=0, CurrentBalance=0, UpdatedAt=$now;";
            resetAccounts.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
            resetAccounts.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public sealed class TreasuryBucketSummary
    {
        public decimal Incoming { get; init; }
        public decimal Outgoing { get; init; }
        public decimal Net => Incoming - Outgoing;
    }

    public static Dictionary<string, TreasuryBucketSummary> GetTreasuryBuckets(SqliteConnection connection)
    {
        var result = new Dictionary<string, (decimal incoming, decimal outgoing)>(StringComparer.OrdinalIgnoreCase)
        {
            ["خدمات الكاش"] = (0m, 0m),
            ["مبيعات"] = (0m, 0m),
            ["الطباعة"] = (0m, 0m),
            ["خدمات الدفع"] = (0m, 0m),
            ["أخرى"] = (0m, 0m)
        };

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT AccountKey, Category,
       COALESCE(SUM(CASE WHEN MovementType='IN' THEN Amount ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN MovementType='OUT' THEN Amount ELSE 0 END),0)
FROM TreasuryMovements
WHERE COALESCE(AccountKey,'')<>'PERCENTAGE'
GROUP BY AccountKey, Category";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string accountKey = reader.IsDBNull(0) ? "CASH" : reader.GetString(0);
            string category = reader.IsDBNull(1) ? "" : reader.GetString(1);
            decimal incoming = reader.GetDecimal(2);
            decimal outgoing = reader.GetDecimal(3);
            // الحساب المالي هو المصدر الحقيقي للتجميع: PRINTING مستقل عن SALES،
            // حتى تدخل الطباعة والتصوير في بند اليومية الخاص بهما فقط.
            string bucket = accountKey == "PRINTING" ? "الطباعة"
                : accountKey == "SALES" ? "مبيعات"
                : category == "الطباعة" || category == "التصوير" || category == "الطباعة والتصوير" ? "الطباعة"
                : category == "خدمات الدفع" ? "خدمات الدفع"
                : category == "خدمات الكاش" ? "خدمات الكاش"
                : "أخرى";
            var current = result[bucket];
            result[bucket] = (current.incoming + incoming, current.outgoing + outgoing);
        }

        return result.ToDictionary(
            x => x.Key,
            x => new TreasuryBucketSummary { Incoming = x.Value.incoming, Outgoing = x.Value.outgoing },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// يحذف البيع أو التصوير نهائيًا (السجل الأصلي والدَّين وحركات الخزينة التابعة له).
    /// لا يقبل خدمات الدفع أو السحب والإيداع أو حركات الماكينات.
    /// </summary>
    public static bool TryGetSaleOrCopyTransactionType(SqliteConnection connection, long transactionId, out string transactionType)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TransactionType FROM Transactions WHERE Id=$id LIMIT 1";
        command.Parameters.AddWithValue("$id", transactionId);
        transactionType = command.ExecuteScalar()?.ToString() ?? string.Empty;
        return transactionType == "SALE" || transactionType == "COPY";
    }

    public static int DeleteSaleOrCopyOperation(SqliteConnection connection, long transactionId, string userName, out string transactionType, out int restoredStock)
    {
        restoredStock = 0;
        transactionType = string.Empty;
        using var tx = connection.BeginTransaction();

        long? customerId = null;
        long? productId = null;
        decimal remaining = 0m;
        using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = tx;
            lookup.CommandText = @"SELECT TransactionType,CustomerId,ProductId,COALESCE(RemainingAmount,0)
FROM Transactions WHERE Id=$id LIMIT 1";
            lookup.Parameters.AddWithValue("$id", transactionId);
            using var reader = lookup.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("العملية غير موجودة أو تم حذفها بالفعل.");
            transactionType = reader.GetString(0);
            if (transactionType != "SALE" && transactionType != "COPY")
                throw new InvalidOperationException("الحذف النهائي مسموح للمبيعات والتصوير فقط، ولا يشمل خدمات الدفع أو حركات الماكينات.");
            customerId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            productId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            remaining = Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture);
        }

        // إذا كانت الديون قد سُدّدت، لا نحذف القيد بما يجعل رصيد العميل سالبًا.
        // نظام السداد الحالي يسجل الدفعة على إجمالي دين العميل، لا على فاتورة بعينها.
        if (customerId.HasValue && remaining > 0m)
        {
            decimal currentDebt = GetCustomerDebt(connection, customerId.Value, tx);
            if (currentDebt - remaining < -0.009m)
                throw new InvalidOperationException("لا يمكن حذف العملية لأن دينها يبدو أنه سُدّد كليًا أو جزئيًا. راجع حساب العميل أولًا حتى لا يصبح رصيد الدين غير صحيح.");
        }

        using (var deleteMovements = connection.CreateCommand())
        {
            deleteMovements.Transaction = tx;
            deleteMovements.CommandText = "DELETE FROM TreasuryMovements WHERE ReferenceId=$id AND ReferenceType IN ('Transaction','Copy')";
            deleteMovements.Parameters.AddWithValue("$id", transactionId);
            deleteMovements.ExecuteNonQuery();
        }
        using (var deleteDebt = connection.CreateCommand())
        {
            deleteDebt.Transaction = tx;
            deleteDebt.CommandText = "DELETE FROM Debts_History WHERE TransactionId=$id";
            deleteDebt.Parameters.AddWithValue("$id", transactionId);
            deleteDebt.ExecuteNonQuery();
        }

        if (transactionType == "SALE" && productId.HasValue)
        {
            using var stock = connection.CreateCommand();
            stock.Transaction = tx;
            stock.CommandText = "UPDATE Products SET Quantity=Quantity+1, UpdatedAt=$now WHERE Id=$id";
            stock.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
            stock.Parameters.AddWithValue("$id", productId.Value);
            restoredStock = stock.ExecuteNonQuery();
        }

        using (var deleteTransaction = connection.CreateCommand())
        {
            deleteTransaction.Transaction = tx;
            deleteTransaction.CommandText = "DELETE FROM Transactions WHERE Id=$id AND TransactionType IN ('SALE','COPY')";
            deleteTransaction.Parameters.AddWithValue("$id", transactionId);
            int deleted = deleteTransaction.ExecuteNonQuery();
            if (deleted != 1) throw new InvalidOperationException("تعذر حذف العملية الأصلية.");
        }

        // قيد الدين اتمسح مع العملية؛ نعيد حساب «الرصيد بعد الحركة» لباقي قيود العميل
        // عشان كشف الحساب والدين الإجمالي يبقوا مظبوطين بعد الحذف.
        if (customerId.HasValue)
        {
            decimal total = SalesService.RecomputeBalances(connection, tx, customerId.Value);
            if (total < -0.009m)
                throw new InvalidOperationException("لا يمكن حذف العملية لأن دينها يبدو أنه سُدّد كليًا أو جزئيًا. راجع حساب العميل أولًا حتى لا يصبح رصيد الدين غير صحيح.");
        }

        AddAuditLog(connection, tx, userName, "حذف نهائي", transactionType == "SALE" ? "بيع" : "تصوير",
            transactionId, $"تم حذف العملية المالية نهائيًا. الكمية المعادة للمخزون: {restoredStock}.");
        tx.Commit();
        return 1;
    }

    /// <summary>
    /// ترحيل لمرة واحدة: الديون اللي اتسجلت من الطباعة قبل إضافة جدول الربط بنقرأ سجل المراجعة
    /// بتاعها، ولو الدين كان من سجل طباعة واحد بس (يعني النصيب معروف بالظبط) نربطه بيه.
    /// الديون اللي جاية من أكتر من ملف مبنخمّنش توزيعها ومبنربطهاش.
    /// </summary>
    private static void BackfillPrintDebtLinks(SqliteConnection connection)
    {
        try
        {
            var rx = new System.Text.RegularExpressions.Regex(
                @"العميل:\s*(\d+)\s*\|.*أرقام سجلات الطباعة:\s*(\d+)\s*$",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            var audits = new List<(DateTime At, long CustomerId, long PrintLogId)>();
            using (var q = connection.CreateCommand())
            {
                q.CommandText = "SELECT CreatedAt, Details FROM AuditLog WHERE Action=$a AND EntityType=$e";
                q.Parameters.AddWithValue("$a", "تحويل إلى دين");
                q.Parameters.AddWithValue("$e", "طباعة/تصوير");
                using var r = q.ExecuteReader();
                while (r.Read())
                {
                    var m = rx.Match(r.GetString(1));
                    if (!m.Success) continue;
                    if (!DateTime.TryParse(r.GetString(0), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out DateTime at)) continue;
                    audits.Add((at,
                        long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                        long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
                }
            }

            foreach (var (at, customerId, printLogId) in audits)
            {
                using (var exists = connection.CreateCommand())
                {
                    exists.CommandText = "SELECT COUNT(*) FROM PrintLog WHERE Id=$p";
                    exists.Parameters.AddWithValue("$p", printLogId);
                    if (Convert.ToInt64(exists.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture) == 0) continue;
                }

                long bestId = 0;
                decimal bestAmount = 0m;
                double bestGap = double.MaxValue;
                using (var cand = connection.CreateCommand())
                {
                    cand.CommandText = @"
SELECT Id, Amount, CreatedAt FROM Debts_History
WHERE CustomerId=$c AND TransactionId IS NULL AND Type='DEBT'
  AND COALESCE(Note,'') LIKE 'دين %' AND COALESCE(Note,'') NOT LIKE 'دين قديم%'
  AND Id NOT IN (SELECT DebtId FROM PrintDebtLinks)";
                    cand.Parameters.AddWithValue("$c", customerId);
                    using var cr = cand.ExecuteReader();
                    while (cr.Read())
                    {
                        if (!DateTime.TryParse(cr.GetString(2), CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out DateTime created)) continue;
                        double gap = Math.Abs((created - at).TotalSeconds);
                        if (gap < bestGap)
                        {
                            bestGap = gap;
                            bestId = cr.GetInt64(0);
                            bestAmount = Convert.ToDecimal(cr.GetValue(1), CultureInfo.InvariantCulture);
                        }
                    }
                }

                // الدين بيتسجل في نفس لحظة سجل المراجعة تقريبًا؛ أي فرق كبير يبقى مش هو.
                if (bestId == 0 || bestGap > 120) continue;

                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO PrintDebtLinks(DebtId,PrintLogId,Amount) VALUES($d,$p,$a)";
                insert.Parameters.AddWithValue("$d", bestId);
                insert.Parameters.AddWithValue("$p", printLogId);
                insert.Parameters.AddWithValue("$a", bestAmount);
                insert.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر ترحيل روابط ديون الطباعة القديمة (تم تجاهله)", ex);
        }
    }

    /// <summary>
    /// لما عملية طباعة تتمسح وكانت اتحوّلت لدين على عميل: يشيل نصيبها من قيد الدين
    /// (أو القيد كله لو نصيبها هو كل المبلغ) ويعيد حساب رصيد العميل. لازم يتنادى جوه نفس
    /// الـTransaction بتاعة حذف الطباعة، ولو الدين اتسدد خلاص بيرمي استثناء فيتلغي الحذف.
    /// </summary>
    public static void RemovePrintLogDebt(SqliteConnection connection, SqliteTransaction tx, long printLogId)
    {
        var links = new List<(long DebtId, decimal Amount)>();
        using (var q = connection.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT DebtId, Amount FROM PrintDebtLinks WHERE PrintLogId=$p";
            q.Parameters.AddWithValue("$p", printLogId);
            using var r = q.ExecuteReader();
            while (r.Read())
                links.Add((r.GetInt64(0), Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture)));
        }
        if (links.Count == 0) return;

        var customers = new HashSet<long>();
        foreach (var (debtId, amount) in links)
        {
            long customerId;
            decimal debtAmount;
            using (var read = connection.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT CustomerId, Amount FROM Debts_History WHERE Id=$d AND Type='DEBT'";
                read.Parameters.AddWithValue("$d", debtId);
                using var rr = read.ExecuteReader();
                if (!rr.Read()) continue; // القيد اتمسح قبل كده
                customerId = rr.GetInt64(0);
                debtAmount = Convert.ToDecimal(rr.GetValue(1), CultureInfo.InvariantCulture);
            }
            customers.Add(customerId);

            decimal left = debtAmount - amount;
            using var write = connection.CreateCommand();
            write.Transaction = tx;
            if (left <= 0.0049m)
            {
                write.CommandText = "DELETE FROM Debts_History WHERE Id=$d";
            }
            else
            {
                write.CommandText = "UPDATE Debts_History SET Amount=$a WHERE Id=$d";
                write.Parameters.AddWithValue("$a", left);
            }
            write.Parameters.AddWithValue("$d", debtId);
            write.ExecuteNonQuery();
        }

        using (var del = connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM PrintDebtLinks WHERE PrintLogId=$p";
            del.Parameters.AddWithValue("$p", printLogId);
            del.ExecuteNonQuery();
        }

        foreach (long customerId in customers)
        {
            decimal total = SalesService.RecomputeBalances(connection, tx, customerId);
            if (total < -0.009m)
                throw new InvalidOperationException("لا يمكن حذف عملية الطباعة لأن دينها يبدو أنه سُدّد كليًا أو جزئيًا. راجع حساب العميل أولًا حتى لا يصبح رصيد الدين غير صحيح.");
        }
    }

    /// <summary>
    /// يحذف حركة خزينة محددة. إذا كانت الحركة جزءًا من عملية مرتبطة
    /// (مثل خدمة كاش أو بيع/خدمة)، يتم حذف جميع حركات الخزينة التابعة لنفس
    /// ReferenceType + ReferenceId حتى لا يبقى جزء من العملية في الرصيد.
    /// هذا الحذف يطال سجل الخزينة فقط ولا يحذف السجل الأصلي من المبيعات/الخدمات.
    /// </summary>
    public static int DeleteTreasuryOperation(SqliteConnection connection, long movementId, string? referenceType, long referenceId)
    {
        using var tx = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = tx;

        if (!string.IsNullOrWhiteSpace(referenceType) && referenceId > 0)
        {
            command.CommandText = @"
DELETE FROM TreasuryMovements
WHERE ReferenceType=$refType AND ReferenceId=$refId";
            command.Parameters.AddWithValue("$refType", referenceType);
            command.Parameters.AddWithValue("$refId", referenceId);
        }
        else
        {
            command.CommandText = "DELETE FROM TreasuryMovements WHERE Id=$id";
            command.Parameters.AddWithValue("$id", movementId);
        }

        int deleted = command.ExecuteNonQuery();
        tx.Commit();
        return deleted;
    }

    /// <summary>
    /// صافي النقدية الموجودة فعليًا في درج المحل بحسب الحركات المسجلة.
    /// يشمل المبيعات والطباعة/التصوير وحركات الدرج والتحصيلات النقدية، ويستبعد
    /// حركة CASH الخاصة بتحويلات المحافظ الإلكترونية حتى لا تُحسب كأنها كاش بالدرج.
    /// </summary>
    public static decimal GetActualDrawerBalance(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"SELECT COALESCE(SUM(CASE WHEN MovementType='IN' THEN Amount ELSE -Amount END),0)
FROM TreasuryMovements
WHERE AccountKey IN ('DRAWER','SALES','PRINTING')
   OR (AccountKey='CASH'
       AND COALESCE(Category,'') <> 'خدمات الكاش'
       AND COALESCE(ReferenceType,'') <> 'ManualWalletWithdrawal')";
        return Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// إجمالي الأموال المسجلة في النشاط = النقدية المحسوبة في الدرج + الأرصدة الحالية
    /// للمحافظ الإلكترونية + الأرصدة الحالية لكل الماكينات. نستخدم الأرصدة الحالية
    /// للحسابات بدل جمع حركات التحويلات مرة أخرى حتى لا تتكرر الأموال المحولة بين الحسابات.
    /// </summary>
    public static decimal GetTreasuryBalance(SqliteConnection connection)
    {
        decimal drawer = GetActualDrawerBalance(connection);
        decimal wallets;
        decimal machines;
        using (var walletCommand = connection.CreateCommand())
        {
            walletCommand.CommandText = "SELECT COALESCE(SUM(CurrentBalance),0) FROM WalletAccounts";
            wallets = Convert.ToDecimal(walletCommand.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }
        using (var machineCommand = connection.CreateCommand())
        {
            machineCommand.CommandText = "SELECT COALESCE(SUM(CurrentBalance),0) FROM MachineAccounts";
            machines = Convert.ToDecimal(machineCommand.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }
        return drawer + wallets + machines;
    }

    // صافي تأثير الحركات فقط؛ يُستخدم لحساب الرصيد المتدرج مع الاحتفاظ بأرصدة البداية.
    private static decimal GetTreasuryMovementNet(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT COALESCE(SUM(CASE WHEN MovementType='IN' THEN Amount ELSE -Amount END),0)
FROM TreasuryMovements WHERE COALESCE(AccountKey,'')<>'PERCENTAGE'";
        return Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// رصيد الخزينة الكلي بعد كل حركة. نضيف رصيد البداية الضمني (إجمالي الرصيد الحالي
    /// ناقص صافي تأثير الحركات) حتى تتوافق آخر قيمة في السجل مع الرصيد الكلي الحالي
    /// الذي يشمل أرصدة المحافظ والماكينات الافتتاحية.
    /// </summary>
    public static Dictionary<long, decimal> GetTreasuryRunningBalances(SqliteConnection connection)
    {
        var result = new Dictionary<long, decimal>();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT Id, MovementType, Amount, COALESCE(AccountKey,'CASH') FROM TreasuryMovements ORDER BY CreatedAt, Id";
        decimal running = GetTreasuryBalance(connection) - GetTreasuryMovementNet(connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            string type = reader.GetString(1);
            decimal amount = Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture);
            string accountKey = reader.GetString(3);
            // «النسبة» تحليل للعمولات لا حركة أموال جديدة؛ تجاهلها في الرصيد المتدرج.
            if (!string.Equals(accountKey, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
                running += type == "IN" ? amount : -amount;
            result[id] = running;
        }
        return result;
    }
}
