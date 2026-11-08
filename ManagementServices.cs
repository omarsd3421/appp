using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal sealed class StockCartLine
{
    public long ProductId { get; init; }
    public string Name { get; init; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal Total => Math.Round(UnitPrice * Quantity * (1m - DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
    public decimal OriginalTotal => UnitPrice * Quantity;
}

internal sealed class ProductLookupRow
{
    public long Id { get; init; }
    public string Barcode { get; init; } = "";
    public string Name { get; init; } = "";
    public decimal SalePrice { get; init; }
    public decimal PurchasePrice { get; init; }
    public int Quantity { get; init; }
    public override string ToString() => $"{Name}  |  {SalePrice:N2} ج.م  |  المتاح: {Quantity}";
}

internal sealed class SuspendedSaleInfo
{
    public long Id { get; init; }
    public string CustomerName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public decimal Total { get; init; }
    public int LineCount { get; init; }
    public string UserName { get; init; } = "";
    public override string ToString() => $"#{Id} | {CreatedAt:yyyy-MM-dd HH:mm} | {CustomerName} | {Total:N2} ج.م | {LineCount} صنف";
}

internal sealed class SuspendedSaleDraft
{
    public long Id { get; init; }
    public long? CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public List<StockCartLine> Lines { get; init; } = new();
}

internal sealed class SupplierLookupRow
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Phone { get; init; } = "";
    public decimal Balance { get; init; }
    public override string ToString() => string.IsNullOrWhiteSpace(Phone) ? Name : $"{Name} — {Phone}";
}


internal sealed class SupplierDetailsRow
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Address { get; init; } = "";
    public string Notes { get; init; } = "";
    public decimal Balance { get; init; }
}

internal sealed class SupplierPaymentHistoryRow
{
    public long Id { get; init; }
    public long PurchaseId { get; init; }
    public DateTime CreatedAt { get; init; }
    public string InvoiceNumber { get; init; } = "";
    public decimal Amount { get; init; }
    public string Note { get; init; } = "";
    public string UserName { get; init; } = "";
}

internal class PurchaseArchiveRow
{
    public long Id { get; init; }
    public string InvoiceNumber { get; init; } = "";
    public string SupplierName { get; init; } = "";
    public decimal Total { get; init; }
    public decimal Paid { get; init; }
    public decimal Remaining { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class PurchaseInvoiceHeaderRow : PurchaseArchiveRow
{
    public string Notes { get; init; } = "";
    public string UserName { get; init; } = "";
}

internal sealed class PurchaseInvoiceItemRow
{
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal Total { get; init; }
}

internal class SalesReturnArchiveRow
{
    public long Id { get; init; }
    public string OriginalTransactions { get; init; } = "";
    public string Products { get; init; } = "";
    public decimal RefundAmount { get; init; }
    public string Reason { get; init; } = "";
    public string UserName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

internal sealed class SalesReturnHeaderRow : SalesReturnArchiveRow
{
    public string CustomerName { get; init; } = "";
}

internal sealed class SalesReturnItemRow
{
    public long OriginalTransactionId { get; init; }
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
    public decimal RefundAmount { get; init; }
    public decimal? ReturnedCost { get; init; }
}

internal sealed class ReturnableSaleRow
{
    public long TransactionId { get; init; }
    public long ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public int SoldQuantity { get; init; }
    public int ReturnedQuantity { get; init; }
    public int AvailableQuantity => Math.Max(0, SoldQuantity - ReturnedQuantity);
    public decimal NetAmount { get; init; }
    public decimal PurchaseCostTotal { get; init; }
}


internal sealed class ProfitPrintingCategoryRow
{
    public string Category { get; init; } = "";
    public string ColorType { get; init; } = "";
    public string DuplexMode { get; init; } = "";
    public int Jobs { get; init; }
    public int Faces { get; init; }
    public int Sheets { get; init; }
    public decimal Revenue { get; init; }
}

internal sealed class ProfitProductRow
{
    public string ProductName { get; init; } = "";
    public decimal Revenue { get; init; }
    public decimal Cost { get; init; }
    public decimal Refunds { get; init; }
    public decimal ReturnedCost { get; init; }
    public decimal NetProfit => (Revenue - Cost) - (Refunds - ReturnedCost);
}

internal sealed class ProfitEmployeeRow
{
    public string UserName { get; init; } = "";
    public decimal SalesRevenue { get; init; }
    public decimal KnownGrossProfit { get; init; }
    public int SaleCount { get; init; }
    public int UncostedCount { get; init; }
}

internal sealed class ProfitSnapshot
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public decimal KnownSalesRevenue { get; init; }
    public decimal KnownCostOfGoods { get; init; }
    public decimal SalesGrossProfit { get; init; }
    public decimal ReturnedSales { get; init; }
    public decimal ReturnCost { get; init; }
    public decimal CashServiceCommissions { get; init; }
    public decimal OtherServiceCommissions { get; init; }
    public decimal OperatingExpenses { get; init; }
    public decimal PrintingRevenue { get; init; }
    public decimal UncostedLegacySales { get; init; }
    public decimal UncostedReturns { get; init; }
    public int UncostedLegacyCount { get; init; }
    public List<ProfitProductRow> ProductBreakdown { get; init; } = new();
    public List<ProfitEmployeeRow> EmployeeBreakdown { get; init; } = new();
    public List<ProfitPrintingCategoryRow> PrintingBreakdown { get; init; } = new();
    public decimal KnownNetProfit => SalesGrossProfit - (ReturnedSales - ReturnCost)
        + CashServiceCommissions + OtherServiceCommissions - OperatingExpenses;
}

internal static class BusinessManagementStore
{
    public static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Suppliers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Phone TEXT NULL,
    Address TEXT NULL,
    Notes TEXT NULL,
    CreatedAt TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS IX_Suppliers_Name ON Suppliers(Name);
CREATE TABLE IF NOT EXISTS Purchases (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SupplierId INTEGER NOT NULL,
    InvoiceNumber TEXT NULL,
    TotalAmount NUMERIC NOT NULL,
    PaidAmount NUMERIC NOT NULL DEFAULT 0,
    RemainingAmount NUMERIC NOT NULL DEFAULT 0,
    Notes TEXT NULL,
    UserId INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id),
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id)
);
CREATE INDEX IF NOT EXISTS IX_Purchases_SupplierDate ON Purchases(SupplierId,CreatedAt);
CREATE TABLE IF NOT EXISTS PurchaseItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PurchaseId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ProductNameSnapshot TEXT NOT NULL,
    Quantity INTEGER NOT NULL,
    UnitCost NUMERIC NOT NULL,
    LineTotal NUMERIC NOT NULL,
    FOREIGN KEY(PurchaseId) REFERENCES Purchases(Id) ON DELETE CASCADE,
    FOREIGN KEY(ProductId) REFERENCES Products(Id)
);
CREATE TABLE IF NOT EXISTS SupplierPayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SupplierId INTEGER NOT NULL,
    PurchaseId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    Amount NUMERIC NOT NULL,
    Note TEXT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id),
    FOREIGN KEY(PurchaseId) REFERENCES Purchases(Id),
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id)
);
CREATE INDEX IF NOT EXISTS IX_SupplierPayments_Supplier ON SupplierPayments(SupplierId,CreatedAt);
CREATE TABLE IF NOT EXISTS PurchaseReturns (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SupplierId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    TotalAmount NUMERIC NOT NULL,
    CashRefund NUMERIC NOT NULL DEFAULT 0,
    Reason TEXT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id),
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id)
);
CREATE TABLE IF NOT EXISTS PurchaseReturnItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnId INTEGER NOT NULL,
    PurchaseId INTEGER NOT NULL,
    PurchaseItemId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ProductNameSnapshot TEXT NOT NULL,
    Quantity INTEGER NOT NULL,
    UnitCost NUMERIC NOT NULL,
    LineTotal NUMERIC NOT NULL,
    FOREIGN KEY(ReturnId) REFERENCES PurchaseReturns(Id) ON DELETE CASCADE,
    FOREIGN KEY(PurchaseId) REFERENCES Purchases(Id),
    FOREIGN KEY(PurchaseItemId) REFERENCES PurchaseItems(Id),
    FOREIGN KEY(ProductId) REFERENCES Products(Id)
);
CREATE INDEX IF NOT EXISTS IX_PurchaseReturns_SupplierDate ON PurchaseReturns(SupplierId,CreatedAt);
CREATE INDEX IF NOT EXISTS IX_PurchaseReturnItems_PurchaseItem ON PurchaseReturnItems(PurchaseItemId);
CREATE TABLE IF NOT EXISTS SupplierCredits (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SupplierId INTEGER NOT NULL,
    SourceReturnId INTEGER NOT NULL,
    OriginalAmount NUMERIC NOT NULL,
    RemainingAmount NUMERIC NOT NULL,
    CreatedAt TEXT NOT NULL,
    Note TEXT NULL,
    FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id),
    FOREIGN KEY(SourceReturnId) REFERENCES PurchaseReturns(Id)
);
CREATE INDEX IF NOT EXISTS IX_SupplierCredits_Supplier ON SupplierCredits(SupplierId,CreatedAt);

CREATE TABLE IF NOT EXISTS InventoryMovements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    MovementType TEXT NOT NULL,
    QuantityDelta INTEGER NOT NULL,
    UnitCost NUMERIC NULL,
    ReferenceType TEXT NULL,
    ReferenceId INTEGER NULL,
    Note TEXT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(ProductId) REFERENCES Products(Id),
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id)
);
CREATE INDEX IF NOT EXISTS IX_InventoryMovements_ProductDate ON InventoryMovements(ProductId,CreatedAt);
CREATE TABLE IF NOT EXISTS SalesReturns (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    CustomerId INTEGER NULL,
    RefundAmount NUMERIC NOT NULL,
    Reason TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(UserId) REFERENCES AppUsers(Id),
    FOREIGN KEY(CustomerId) REFERENCES Customers(Id)
);
CREATE TABLE IF NOT EXISTS SalesReturnItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnId INTEGER NOT NULL,
    OriginalTransactionId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    Quantity INTEGER NOT NULL,
    RefundAmount NUMERIC NOT NULL,
    CostReturned NUMERIC NULL,
    ProductNameSnapshot TEXT NOT NULL,
    FOREIGN KEY(ReturnId) REFERENCES SalesReturns(Id) ON DELETE CASCADE,
    FOREIGN KEY(OriginalTransactionId) REFERENCES Transactions(Id),
    FOREIGN KEY(ProductId) REFERENCES Products(Id)
);
CREATE INDEX IF NOT EXISTS IX_SalesReturnItems_Original ON SalesReturnItems(OriginalTransactionId);
CREATE TABLE IF NOT EXISTS SuspendedSales (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NOT NULL,
    CustomerId INTEGER NULL,
    CustomerName TEXT NULL,
    TotalAmount NUMERIC NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(CustomerId) REFERENCES Customers(Id)
);
CREATE TABLE IF NOT EXISTS SuspendedSaleItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SuspendedSaleId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ProductNameSnapshot TEXT NOT NULL,
    Quantity INTEGER NOT NULL,
    UnitPrice NUMERIC NOT NULL,
    DiscountPercent NUMERIC NOT NULL DEFAULT 0,
    FOREIGN KEY(SuspendedSaleId) REFERENCES SuspendedSales(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_SuspendedSales_CreatedAt ON SuspendedSales(CreatedAt);
CREATE INDEX IF NOT EXISTS IX_SuspendedSaleItems_Sale ON SuspendedSaleItems(SuspendedSaleId);
";
        cmd.ExecuteNonQuery();
        EnsureColumn(connection, "Transactions", "PurchaseCostTotal", "NUMERIC NULL");
        EnsureColumn(connection, "Products", "StockUpdatedAt", "TEXT NULL");
    }

    private static void EnsureColumn(SqliteConnection c, string table, string column, string declaration)
    {
        using var q = c.CreateCommand();
        q.CommandText = $"PRAGMA table_info({table});";
        using var r = q.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        r.Close();
        using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        alter.ExecuteNonQuery();
    }

    public static List<ProductLookupRow> LoadProducts(string? search = null)
    {
        var result = new List<ProductLookupRow>();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT Id,COALESCE(Barcode,''),Name,SalePrice,PurchasePrice,Quantity
FROM Products WHERE $q='' OR Name LIKE $p OR COALESCE(Barcode,'') LIKE $p OR COALESCE(Category,'') LIKE $p
ORDER BY Name COLLATE NOCASE LIMIT 500";
        string q = (search ?? "").Trim();
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$p", "%" + q + "%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new ProductLookupRow
            {
                Id = r.GetInt64(0), Barcode = r.GetString(1), Name = r.GetString(2),
                SalePrice = Dec(r.GetValue(3)), PurchasePrice = Dec(r.GetValue(4)), Quantity = Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture)
            });
        return result;
    }

    public static List<ProductLookupRow> LoadQuickProducts(int limit = 6)
    {
        var result = new List<ProductLookupRow>();
        int safeLimit = Math.Clamp(limit, 1, 12);
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT p.Id,COALESCE(p.Barcode,''),p.Name,p.SalePrice,p.PurchasePrice,p.Quantity
FROM Products p
WHERE p.Quantity > 0
ORDER BY COALESCE((SELECT SUM(COALESCE(t.Quantity,1)) FROM Transactions t
                   WHERE t.ProductId=p.Id AND t.TransactionType='SALE'
                     AND substr(t.CreatedAt,1,10)>=date('now','-30 days')),0) DESC,
         p.Name COLLATE NOCASE
LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", safeLimit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new ProductLookupRow
            {
                Id = r.GetInt64(0), Barcode = r.GetString(1), Name = r.GetString(2),
                SalePrice = Dec(r.GetValue(3)), PurchasePrice = Dec(r.GetValue(4)),
                Quantity = Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture)
            });
        return result;
    }

    public static ProductLookupRow? GetProductById(long productId)
    {
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,COALESCE(Barcode,''),Name,SalePrice,PurchasePrice,Quantity FROM Products WHERE Id=$id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", productId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ProductLookupRow
        {
            Id = r.GetInt64(0), Barcode = r.GetString(1), Name = r.GetString(2),
            SalePrice = Dec(r.GetValue(3)), PurchasePrice = Dec(r.GetValue(4)),
            Quantity = Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture)
        };
    }

    public static long SaveSuspendedCart(string userName, long? customerId, string? customerName, IReadOnlyList<StockCartLine> lines, long? replaceSuspendedCartId = null)
    {
        if (lines == null || lines.Count == 0) throw new InvalidOperationException("السلة فارغة ولا يمكن تعليقها.");
        if (lines.Any(x => x.ProductId <= 0 || x.Quantity <= 0 || x.UnitPrice <= 0 || x.DiscountPercent < 0 || x.DiscountPercent > 100))
            throw new InvalidOperationException("يوجد سطر غير صالح داخل السلة.");
        decimal total = lines.Sum(x => x.Total);
        using var c = AppDatabase.OpenConnection();
        using var tx = c.BeginTransaction();
        using var head = c.CreateCommand();
        head.Transaction = tx;
        head.CommandText = @"INSERT INTO SuspendedSales(UserName,CustomerId,CustomerName,TotalAmount,CreatedAt)
VALUES($u,$customer,$name,$total,$date); SELECT last_insert_rowid();";
        head.Parameters.AddWithValue("$u", string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName.Trim());
        head.Parameters.AddWithValue("$customer", (object?)customerId ?? DBNull.Value);
        head.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(customerName) ? DBNull.Value : customerName.Trim());
        head.Parameters.AddWithValue("$total", total);
        head.Parameters.AddWithValue("$date", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        long id = Convert.ToInt64(head.ExecuteScalar(), CultureInfo.InvariantCulture);
        foreach (var line in lines)
        {
            using var item = c.CreateCommand();
            item.Transaction = tx;
            item.CommandText = @"INSERT INTO SuspendedSaleItems(SuspendedSaleId,ProductId,ProductNameSnapshot,Quantity,UnitPrice,DiscountPercent)
VALUES($sale,$product,$name,$qty,$price,$discount)";
            item.Parameters.AddWithValue("$sale", id); item.Parameters.AddWithValue("$product", line.ProductId);
            item.Parameters.AddWithValue("$name", line.Name); item.Parameters.AddWithValue("$qty", line.Quantity);
            item.Parameters.AddWithValue("$price", line.UnitPrice); item.Parameters.AddWithValue("$discount", line.DiscountPercent);
            item.ExecuteNonQuery();
        }
        AppDatabase.AddAuditLog(c, tx, userName, "تعليق", "سلة مبيعات", id,
            $"الأصناف: {lines.Count} | الإجمالي: {total:F2} | العميل: {customerName}");
        if (replaceSuspendedCartId.HasValue && replaceSuspendedCartId.Value != id)
            DeleteSuspendedCart(c, tx, userName, replaceSuspendedCartId.Value, "استبدال سلة معلّقة");
        tx.Commit();
        return id;
    }

    public static List<SuspendedSaleInfo> LoadSuspendedCarts(string? userName = null)
    {
        var result = new List<SuspendedSaleInfo>();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT s.Id,COALESCE(s.CustomerName,''),s.CreatedAt,s.TotalAmount,
(SELECT COUNT(*) FROM SuspendedSaleItems i WHERE i.SuspendedSaleId=s.Id),s.UserName
FROM SuspendedSales s WHERE $all=1 OR s.UserName=$u ORDER BY s.CreatedAt DESC,s.Id DESC LIMIT 200";
        cmd.Parameters.AddWithValue("$all", string.IsNullOrWhiteSpace(userName) ? 1 : 0);
        cmd.Parameters.AddWithValue("$u", userName ?? "");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime created = DateTime.TryParse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
            result.Add(new SuspendedSaleInfo { Id = r.GetInt64(0), CustomerName = r.GetString(1), CreatedAt = created,
                Total = Dec(r.GetValue(3)), LineCount = Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture), UserName = r.GetString(5) });
        }
        return result;
    }

    public static SuspendedSaleDraft? LoadSuspendedCart(long id)
    {
        SuspendedSaleDraft draft;
        using var c = AppDatabase.OpenConnection();
        using (var head = c.CreateCommand())
        {
            head.CommandText = "SELECT CustomerId,COALESCE(CustomerName,'') FROM SuspendedSales WHERE Id=$id";
            head.Parameters.AddWithValue("$id", id);
            using var r = head.ExecuteReader();
            if (!r.Read()) return null;
            draft = new SuspendedSaleDraft { Id = id, CustomerId = r.IsDBNull(0) ? null : r.GetInt64(0), CustomerName = r.GetString(1) };
        }
        using (var items = c.CreateCommand())
        {
            items.CommandText = @"SELECT ProductId,ProductNameSnapshot,Quantity,UnitPrice,DiscountPercent
FROM SuspendedSaleItems WHERE SuspendedSaleId=$id ORDER BY Id";
            items.Parameters.AddWithValue("$id", id);
            using var r = items.ExecuteReader();
            while (r.Read()) draft.Lines.Add(new StockCartLine { ProductId = r.GetInt64(0), Name = r.GetString(1),
                Quantity = Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture), UnitPrice = Dec(r.GetValue(3)),
                DiscountPercent = Dec(r.GetValue(4)) });
        }
        return draft;
    }

    public static void DeleteSuspendedCart(string userName, long id, string action = "استعادة")
    {
        using var c = AppDatabase.OpenConnection();
        using var tx = c.BeginTransaction();
        DeleteSuspendedCart(c, tx, userName, id, action);
        tx.Commit();
    }

    internal static void DeleteSuspendedCart(SqliteConnection c, SqliteTransaction tx, string userName, long id, string action)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        // ربط الحذف باسم المستخدم يمنع حذف مسودة تخص موظفًا آخر بالخطأ.
        cmd.CommandText = "DELETE FROM SuspendedSales WHERE Id=$id AND UserName=$u";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$u", string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName.Trim());
        if (cmd.ExecuteNonQuery() > 0)
        {
            string note = action == "استعادة"
                ? "تم تحميل السلة المعلّقة لاستكمال البيع مع الإبقاء على نسخة آمنة حتى الإتمام"
                : action == "إتمام البيع من سلة معلّقة"
                    ? "تم حذف المسودة بعد تسجيل البيع بنجاح داخل نفس المعاملة"
                    : "تم استبدال/حذف السلة المعلّقة بعد حفظ النسخة الجديدة";
            AppDatabase.AddAuditLog(c, tx, userName, action, "سلة مبيعات", id, note);
        }
    }

    public static List<SupplierLookupRow> LoadSuppliers()
    {
        var result = new List<SupplierLookupRow>();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT s.Id,s.Name,COALESCE(s.Phone,''),
COALESCE((SELECT SUM(p.RemainingAmount) FROM Purchases p WHERE p.SupplierId=s.Id),0)
- COALESCE((SELECT SUM(sc.RemainingAmount) FROM SupplierCredits sc WHERE sc.SupplierId=s.Id),0)
FROM Suppliers s
WHERE s.IsActive=1 ORDER BY s.Name COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(new SupplierLookupRow
        {
            Id = r.GetInt64(0), Name = r.GetString(1), Phone = r.GetString(2), Balance = Dec(r.GetValue(3))
        });
        return result;
    }

    public static long AddSupplier(string userName, string name, string? phone, string? address, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم المورد مطلوب.");
        using var c = AppDatabase.OpenConnection();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO Suppliers(Name,Phone,Address,Notes,CreatedAt) VALUES($n,$p,$a,$notes,$d); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name.Trim());
        cmd.Parameters.AddWithValue("$p", DbText(phone));
        cmd.Parameters.AddWithValue("$a", DbText(address));
        cmd.Parameters.AddWithValue("$notes", DbText(notes));
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        long id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        AppDatabase.AddAuditLog(c, tx, userName, "إضافة", "مورد", id, $"المورد: {name.Trim()} | الهاتف: {phone}");
        tx.Commit();
        return id;
    }


    public static SupplierDetailsRow? LoadSupplierDetails(long supplierId)
    {
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT s.Id,s.Name,COALESCE(s.Phone,''),COALESCE(s.Address,''),COALESCE(s.Notes,''),
COALESCE((SELECT SUM(p.RemainingAmount) FROM Purchases p WHERE p.SupplierId=s.Id),0)
- COALESCE((SELECT SUM(sc.RemainingAmount) FROM SupplierCredits sc WHERE sc.SupplierId=s.Id),0)
FROM Suppliers s WHERE s.Id=$id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", supplierId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SupplierDetailsRow
        {
            Id = r.GetInt64(0), Name = r.GetString(1), Phone = r.GetString(2),
            Address = r.GetString(3), Notes = r.GetString(4), Balance = Dec(r.GetValue(5))
        };
    }

    public static void UpdateSupplier(string userName, long supplierId, string name, string? phone, string? address)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم المورد مطلوب.");
        using var c = AppDatabase.OpenConnection();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"UPDATE Suppliers SET Name=$name,Phone=$phone,Address=$address WHERE Id=$id AND IsActive=1";
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$phone", DbText(phone));
        cmd.Parameters.AddWithValue("$address", DbText(address));
        cmd.Parameters.AddWithValue("$id", supplierId);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("المورد غير موجود أو تم إيقافه.");
        AppDatabase.AddAuditLog(c, tx, userName, "تعديل", "مورد", supplierId,
            $"الاسم: {name.Trim()} | الهاتف: {phone} | العنوان: {address}");
        tx.Commit();
    }

    public static List<SupplierPaymentHistoryRow> LoadSupplierPaymentHistory(long supplierId)
    {
        var result = new List<SupplierPaymentHistoryRow>();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT sp.Id,sp.PurchaseId,sp.CreatedAt,COALESCE(p.InvoiceNumber,''),sp.Amount,
COALESCE(sp.Note,''),COALESCE(u.UserName,'')
FROM SupplierPayments sp
JOIN Purchases p ON p.Id=sp.PurchaseId
LEFT JOIN AppUsers u ON u.Id=sp.UserId
WHERE sp.SupplierId=$id
ORDER BY sp.CreatedAt DESC,sp.Id DESC LIMIT 1000";
        cmd.Parameters.AddWithValue("$id", supplierId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime date = DateTime.TryParse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.MinValue;
            result.Add(new SupplierPaymentHistoryRow
            {
                Id = r.GetInt64(0), PurchaseId = r.GetInt64(1), CreatedAt = date,
                InvoiceNumber = r.GetString(3), Amount = Dec(r.GetValue(4)),
                Note = r.GetString(5), UserName = r.GetString(6)
            });
        }
        return result;
    }

    public static List<PurchaseArchiveRow> LoadPurchaseArchiveRows(string? search, long? supplierId = null)
    {
        var result = new List<PurchaseArchiveRow>();
        string q = (search ?? "").Trim();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT p.Id,COALESCE(p.InvoiceNumber,''),s.Name,p.TotalAmount,p.PaidAmount,p.RemainingAmount,p.CreatedAt
FROM Purchases p JOIN Suppliers s ON s.Id=p.SupplierId
WHERE ($supplier IS NULL OR p.SupplierId=$supplier)
AND ($q='' OR COALESCE(p.InvoiceNumber,'') LIKE $pattern OR s.Name LIKE $pattern OR CAST(p.Id AS TEXT) LIKE $pattern)
ORDER BY p.CreatedAt DESC,p.Id DESC LIMIT 1000";
        cmd.Parameters.AddWithValue("$supplier", (object?)supplierId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$pattern", "%" + q + "%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime date = DateTime.TryParse(r.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.MinValue;
            result.Add(new PurchaseArchiveRow
            {
                Id = r.GetInt64(0), InvoiceNumber = r.GetString(1), SupplierName = r.GetString(2),
                Total = Dec(r.GetValue(3)), Paid = Dec(r.GetValue(4)), Remaining = Dec(r.GetValue(5)), CreatedAt = date
            });
        }
        return result;
    }

    public static PurchaseInvoiceHeaderRow? LoadPurchaseInvoiceHeader(long purchaseId)
    {
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT p.Id,COALESCE(p.InvoiceNumber,''),s.Name,p.TotalAmount,p.PaidAmount,p.RemainingAmount,
p.CreatedAt,COALESCE(p.Notes,''),COALESCE(u.UserName,'')
FROM Purchases p JOIN Suppliers s ON s.Id=p.SupplierId
LEFT JOIN AppUsers u ON u.Id=p.UserId
WHERE p.Id=$id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        DateTime date = DateTime.TryParse(r.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.MinValue;
        return new PurchaseInvoiceHeaderRow
        {
            Id = r.GetInt64(0), InvoiceNumber = r.GetString(1), SupplierName = r.GetString(2),
            Total = Dec(r.GetValue(3)), Paid = Dec(r.GetValue(4)), Remaining = Dec(r.GetValue(5)),
            CreatedAt = date, Notes = r.GetString(7), UserName = r.GetString(8)
        };
    }

    public static List<PurchaseInvoiceItemRow> LoadPurchaseInvoiceItems(long purchaseId)
    {
        var result = new List<PurchaseInvoiceItemRow>();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT ProductNameSnapshot,Quantity,UnitCost,LineTotal
FROM PurchaseItems WHERE PurchaseId=$id ORDER BY Id";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(new PurchaseInvoiceItemRow
        {
            ProductName = r.GetString(0), Quantity = Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
            UnitCost = Dec(r.GetValue(2)), Total = Dec(r.GetValue(3))
        });
        return result;
    }

    public static List<SupplierPaymentHistoryRow> LoadPurchaseInvoicePayments(long purchaseId)
    {
        var result = new List<SupplierPaymentHistoryRow>();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT sp.Id,sp.PurchaseId,sp.CreatedAt,COALESCE(p.InvoiceNumber,''),sp.Amount,
COALESCE(sp.Note,''),COALESCE(u.UserName,'')
FROM SupplierPayments sp JOIN Purchases p ON p.Id=sp.PurchaseId
LEFT JOIN AppUsers u ON u.Id=sp.UserId
WHERE sp.PurchaseId=$id ORDER BY sp.CreatedAt,sp.Id";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime date = DateTime.TryParse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.MinValue;
            result.Add(new SupplierPaymentHistoryRow
            {
                Id = r.GetInt64(0), PurchaseId = r.GetInt64(1), CreatedAt = date,
                InvoiceNumber = r.GetString(3), Amount = Dec(r.GetValue(4)),
                Note = r.GetString(5), UserName = r.GetString(6)
            });
        }
        return result;
    }

    public static List<SalesReturnArchiveRow> LoadSalesReturnArchiveRows(string? search)
    {
        var result = new List<SalesReturnArchiveRow>();
        string q = (search ?? "").Trim();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT sr.Id,sr.CreatedAt,
COALESCE(GROUP_CONCAT(DISTINCT ri.OriginalTransactionId),''),
COALESCE(GROUP_CONCAT(ri.ProductNameSnapshot || ' × ' || ri.Quantity,'،'),''),
sr.RefundAmount,sr.Reason,COALESCE(u.UserName,'')
FROM SalesReturns sr
LEFT JOIN SalesReturnItems ri ON ri.ReturnId=sr.Id
LEFT JOIN AppUsers u ON u.Id=sr.UserId
WHERE ($q='' OR CAST(sr.Id AS TEXT) LIKE $pattern OR sr.Reason LIKE $pattern
OR EXISTS(SELECT 1 FROM SalesReturnItems x WHERE x.ReturnId=sr.Id AND
(x.ProductNameSnapshot LIKE $pattern OR CAST(x.OriginalTransactionId AS TEXT) LIKE $pattern)))
GROUP BY sr.Id,sr.CreatedAt,sr.RefundAmount,sr.Reason,u.UserName
ORDER BY sr.CreatedAt DESC,sr.Id DESC LIMIT 1000";
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$pattern", "%" + q + "%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime date = DateTime.TryParse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.MinValue;
            result.Add(new SalesReturnArchiveRow
            {
                Id = r.GetInt64(0), CreatedAt = date, OriginalTransactions = r.GetString(2),
                Products = r.GetString(3), RefundAmount = Dec(r.GetValue(4)),
                Reason = r.GetString(5), UserName = r.GetString(6)
            });
        }
        return result;
    }

    public static SalesReturnHeaderRow? LoadSalesReturnHeader(long returnId)
    {
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT sr.Id,sr.CreatedAt,
COALESCE(GROUP_CONCAT(DISTINCT ri.OriginalTransactionId),''),
COALESCE(GROUP_CONCAT(ri.ProductNameSnapshot || ' × ' || ri.Quantity,'،'),''),
sr.RefundAmount,sr.Reason,COALESCE(u.UserName,''),COALESCE(c.Name,'')
FROM SalesReturns sr
LEFT JOIN SalesReturnItems ri ON ri.ReturnId=sr.Id
LEFT JOIN AppUsers u ON u.Id=sr.UserId
LEFT JOIN Customers c ON c.Id=sr.CustomerId
WHERE sr.Id=$id
GROUP BY sr.Id,sr.CreatedAt,sr.RefundAmount,sr.Reason,u.UserName,c.Name LIMIT 1";
        cmd.Parameters.AddWithValue("$id", returnId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        DateTime date = DateTime.TryParse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.MinValue;
        return new SalesReturnHeaderRow
        {
            Id = r.GetInt64(0), CreatedAt = date, OriginalTransactions = r.GetString(2),
            Products = r.GetString(3), RefundAmount = Dec(r.GetValue(4)),
            Reason = r.GetString(5), UserName = r.GetString(6), CustomerName = r.GetString(7)
        };
    }

    public static List<SalesReturnItemRow> LoadSalesReturnItems(long returnId)
    {
        var result = new List<SalesReturnItemRow>();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT OriginalTransactionId,ProductNameSnapshot,Quantity,RefundAmount,CostReturned
FROM SalesReturnItems WHERE ReturnId=$id ORDER BY Id";
        cmd.Parameters.AddWithValue("$id", returnId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(new SalesReturnItemRow
        {
            OriginalTransactionId = r.GetInt64(0), ProductName = r.GetString(1),
            Quantity = Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
            RefundAmount = Dec(r.GetValue(3)), ReturnedCost = r.IsDBNull(4) ? null : Dec(r.GetValue(4))
        });
        return result;
    }

    public static long CreatePurchase(string userName, long supplierId, string? invoiceNumber, string? notes,
        decimal paidAmount, IReadOnlyList<PurchaseLineInput> lines)
    {
        if (lines == null || lines.Count == 0) throw new InvalidOperationException("أضف صنفًا واحدًا على الأقل إلى فاتورة الشراء.");
        if (lines.Any(x => x.Quantity <= 0 || x.UnitCost < 0 || x.ProductId <= 0)) throw new InvalidOperationException("راجع كميات وأسعار الأصناف.");
        decimal total = Math.Round(lines.Sum(x => x.Quantity * x.UnitCost), 2, MidpointRounding.AwayFromZero);
        if (total <= 0) throw new InvalidOperationException("إجمالي الفاتورة يجب أن يكون أكبر من صفر.");
        if (paidAmount < 0 || paidAmount > total) throw new InvalidOperationException("المبلغ المدفوع يجب أن يكون بين صفر وإجمالي الفاتورة.");

        using var c = AppDatabase.OpenConnection();
        using var tx = c.BeginTransaction();
        using (var exists = c.CreateCommand())
        {
            exists.Transaction = tx;
            exists.CommandText = "SELECT COUNT(*) FROM Suppliers WHERE Id=$id AND IsActive=1";
            exists.Parameters.AddWithValue("$id", supplierId);
            if (Convert.ToInt32(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) throw new InvalidOperationException("المورد غير موجود.");
        }

        long userId = AppDatabase.GetUserId(c, userName, tx);
        using var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"INSERT INTO Purchases(SupplierId,InvoiceNumber,TotalAmount,PaidAmount,RemainingAmount,Notes,UserId,CreatedAt)
VALUES($s,$no,$t,$paid,$remain,$notes,$u,$d); SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$s", supplierId);
        insert.Parameters.AddWithValue("$no", DbText(invoiceNumber));
        insert.Parameters.AddWithValue("$t", total);
        // Apply any credit balance previously created by supplier returns before creating new debt.
        decimal supplierCredit = 0m;
        using (var credits = c.CreateCommand())
        {
            credits.Transaction = tx;
            credits.CommandText = "SELECT Id,RemainingAmount FROM SupplierCredits WHERE SupplierId=$s AND RemainingAmount>0 ORDER BY CreatedAt,Id";
            credits.Parameters.AddWithValue("$s", supplierId);
            var availableCredits = new List<(long Id, decimal Remaining)>();
            using (var cr = credits.ExecuteReader())
                while (cr.Read()) availableCredits.Add((cr.GetInt64(0), Dec(cr.GetValue(1))));
            decimal creditToApply = Math.Max(0m, total - paidAmount);
            foreach (var credit in availableCredits)
            {
                if (creditToApply <= 0.005m) break;
                decimal applied = Math.Min(creditToApply, credit.Remaining);
                using var apply = c.CreateCommand();
                apply.Transaction = tx;
                apply.CommandText = "UPDATE SupplierCredits SET RemainingAmount=MAX(0,RemainingAmount-$a) WHERE Id=$id";
                apply.Parameters.AddWithValue("$a", applied);
                apply.Parameters.AddWithValue("$id", credit.Id);
                apply.ExecuteNonQuery();
                supplierCredit += applied;
                creditToApply -= applied;
            }
        }
        insert.Parameters.AddWithValue("$paid", paidAmount + supplierCredit);
        insert.Parameters.AddWithValue("$remain", Math.Max(0m, total - paidAmount - supplierCredit));
        insert.Parameters.AddWithValue("$notes", DbText(notes));
        insert.Parameters.AddWithValue("$u", userId);
        insert.Parameters.AddWithValue("$d", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        long purchaseId = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (supplierCredit > 0.005m)
        {
            using var creditPayment = c.CreateCommand();
            creditPayment.Transaction = tx;
            creditPayment.CommandText = @"INSERT INTO SupplierPayments(SupplierId,PurchaseId,UserId,Amount,Note,CreatedAt)
VALUES($s,$p,$u,$a,$n,$d)";
            creditPayment.Parameters.AddWithValue("$s", supplierId);
            creditPayment.Parameters.AddWithValue("$p", purchaseId);
            creditPayment.Parameters.AddWithValue("$u", userId);
            creditPayment.Parameters.AddWithValue("$a", supplierCredit);
            creditPayment.Parameters.AddWithValue("$n", "تسوية من رصيد دائن سابق ناتج عن مرتجع مشتريات (بدون حركة خزنة)");
            creditPayment.Parameters.AddWithValue("$d", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            creditPayment.ExecuteNonQuery();
        }

        foreach (var line in lines)
        {
            string name;
            int oldQty;
            decimal oldCost;
            using (var get = c.CreateCommand())
            {
                get.Transaction = tx;
                get.CommandText = "SELECT Name,Quantity,PurchasePrice FROM Products WHERE Id=$id";
                get.Parameters.AddWithValue("$id", line.ProductId);
                using var r = get.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException($"الصنف رقم {line.ProductId} لم يعد موجودًا.");
                name = r.GetString(0); oldQty = Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture); oldCost = Dec(r.GetValue(2));
            }
            int newQty = checked(oldQty + line.Quantity);
            decimal avgCost = newQty > 0 ? ((oldQty * oldCost) + (line.Quantity * line.UnitCost)) / newQty : line.UnitCost;
            using (var upd = c.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE Products SET Quantity=$q,PurchasePrice=$cost,UpdatedAt=$d,StockUpdatedAt=$d WHERE Id=$id";
                upd.Parameters.AddWithValue("$q", newQty);
                upd.Parameters.AddWithValue("$cost", Math.Round(avgCost, 4, MidpointRounding.AwayFromZero));
                upd.Parameters.AddWithValue("$d", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
                upd.Parameters.AddWithValue("$id", line.ProductId);
                upd.ExecuteNonQuery();
            }
            using (var item = c.CreateCommand())
            {
                item.Transaction = tx;
                item.CommandText = "INSERT INTO PurchaseItems(PurchaseId,ProductId,ProductNameSnapshot,Quantity,UnitCost,LineTotal) VALUES($p,$id,$n,$q,$cost,$total)";
                item.Parameters.AddWithValue("$p", purchaseId); item.Parameters.AddWithValue("$id", line.ProductId);
                item.Parameters.AddWithValue("$n", name); item.Parameters.AddWithValue("$q", line.Quantity);
                item.Parameters.AddWithValue("$cost", line.UnitCost); item.Parameters.AddWithValue("$total", line.Quantity * line.UnitCost);
                item.ExecuteNonQuery();
            }
            RecordInventoryMovement(c, tx, userId, line.ProductId, "PURCHASE_IN", line.Quantity, line.UnitCost, "Purchase", purchaseId, $"فاتورة شراء رقم {invoiceNumber}");
        }

        if (paidAmount > 0)
        {
            AppDatabase.AddTreasury(c, userName, "OUT", "شراء بضاعة", paidAmount,
                $"فاتورة شراء #{invoiceNumber ?? purchaseId.ToString(CultureInfo.InvariantCulture)}", "Purchase", purchaseId, tx, "DRAWER");
            using var pay = c.CreateCommand();
            pay.Transaction = tx;
            pay.CommandText = "INSERT INTO SupplierPayments(SupplierId,PurchaseId,UserId,Amount,Note,CreatedAt) VALUES($s,$p,$u,$a,$note,$d)";
            pay.Parameters.AddWithValue("$s", supplierId); pay.Parameters.AddWithValue("$p", purchaseId); pay.Parameters.AddWithValue("$u", userId);
            pay.Parameters.AddWithValue("$a", paidAmount); pay.Parameters.AddWithValue("$note", "دفعة عند تسجيل فاتورة الشراء");
            pay.Parameters.AddWithValue("$d", DateTime.Now.ToString("O", CultureInfo.InvariantCulture)); pay.ExecuteNonQuery();
        }
        AppDatabase.AddAuditLog(c, tx, userName, "إنشاء", "فاتورة شراء", purchaseId,
            $"المورد: {supplierId} | الأصناف: {lines.Count} | الإجمالي: {total:F2} | المدفوع نقدًا: {paidAmount:F2} | رصيد مورد مستخدم: {supplierCredit:F2} | المتبقي: {Math.Max(0m,total-paidAmount-supplierCredit):F2}");
        tx.Commit();
        return purchaseId;
    }



    public static List<PurchaseReturnArchiveRow> LoadPurchaseReturnArchive(long? supplierId = null)
    {
        var rows=new List<PurchaseReturnArchiveRow>();
        using var c=AppDatabase.OpenConnection(); using var cmd=c.CreateCommand();
        cmd.CommandText=@"SELECT pr.Id,pr.CreatedAt,s.Name,pr.TotalAmount,pr.CashRefund,COALESCE(pr.Reason,''),
COALESCE(GROUP_CONCAT(ri.ProductNameSnapshot || ' × ' || ri.Quantity,'،'),'')
FROM PurchaseReturns pr JOIN Suppliers s ON s.Id=pr.SupplierId
LEFT JOIN PurchaseReturnItems ri ON ri.ReturnId=pr.Id
WHERE ($s IS NULL OR pr.SupplierId=$s)
GROUP BY pr.Id,pr.CreatedAt,s.Name,pr.TotalAmount,pr.CashRefund,pr.Reason
ORDER BY pr.CreatedAt DESC,pr.Id DESC LIMIT 1000";
        cmd.Parameters.AddWithValue("$s",(object?)supplierId ?? DBNull.Value);
        using var r=cmd.ExecuteReader();
        while(r.Read()) rows.Add(new PurchaseReturnArchiveRow {
            Id=r.GetInt64(0),CreatedAt=DateTime.TryParse(r.GetString(1),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var dt)?dt:DateTime.MinValue,
            SupplierName=r.GetString(2),Total=Dec(r.GetValue(3)),CashRefund=Dec(r.GetValue(4)),Reason=r.GetString(5),Products=r.GetString(6)
        });
        return rows;
    }

    public static List<PurchaseReturnableItemRow> LoadReturnablePurchaseItems(long supplierId, long? purchaseId = null)
    {
        var rows = new List<PurchaseReturnableItemRow>();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT pi.Id,pi.PurchaseId,pi.ProductId,pi.ProductNameSnapshot,pi.Quantity,pi.UnitCost,
COALESCE(p.InvoiceNumber,''),p.CreatedAt,
COALESCE((SELECT SUM(ri.Quantity) FROM PurchaseReturnItems ri WHERE ri.PurchaseItemId=pi.Id),0)
FROM PurchaseItems pi
JOIN Purchases p ON p.Id=pi.PurchaseId
WHERE p.SupplierId=$supplier AND ($purchase IS NULL OR p.Id=$purchase)
ORDER BY p.CreatedAt,p.Id,pi.Id";
        cmd.Parameters.AddWithValue("$supplier", supplierId);
        cmd.Parameters.AddWithValue("$purchase", (object?)purchaseId ?? DBNull.Value);
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                int purchased = Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture);
                int returned = Convert.ToInt32(r.GetValue(8), CultureInfo.InvariantCulture);
                rows.Add(new PurchaseReturnableItemRow {
                    PurchaseItemId=r.GetInt64(0), PurchaseId=r.GetInt64(1), ProductId=r.GetInt64(2),
                    ProductName=r.GetString(3), PurchasedQuantity=purchased, ReturnedQuantity=returned,
                    UnreturnedQuantity=Math.Max(0,purchased-returned), UnitCost=Dec(r.GetValue(5)),
                    InvoiceNumber=r.GetString(6),
                    PurchaseDate=DateTime.TryParse(r.GetString(7),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var dt)?dt:DateTime.MinValue
                });
            }
        }

        // FIFO: تُستهلك الكميات المباعة من أقدم دفعات الشراء أولًا؛ فيبقى أحدث لوط متاحًا للرد.
        foreach (var group in rows.GroupBy(x => x.ProductId))
        {
            using var stockQuery = c.CreateCommand();
            stockQuery.CommandText = "SELECT Quantity FROM Products WHERE Id=$id";
            stockQuery.Parameters.AddWithValue("$id", group.Key);
            int stock = Math.Max(0,Convert.ToInt32(stockQuery.ExecuteScalar() ?? 0,CultureInfo.InvariantCulture));
            int unreturnedTotal = group.Sum(x => x.UnreturnedQuantity);
            int soldToAllocate = Math.Max(0,unreturnedTotal-stock);
            foreach (var row in group.OrderBy(x=>x.PurchaseDate).ThenBy(x=>x.PurchaseId).ThenBy(x=>x.PurchaseItemId))
            {
                int soldFromThisLot = Math.Min(row.UnreturnedQuantity,soldToAllocate);
                soldToAllocate -= soldFromThisLot;
                row.AvailableQuantity = Math.Max(0,row.UnreturnedQuantity-soldFromThisLot);
            }
        }
        return rows;
    }

    public static (long ReturnId, decimal Total, decimal SupplierCredit, decimal CashRefund, decimal SupplierCreditBalance) RecordPurchaseReturn(
        string userName, long supplierId, IReadOnlyList<PurchaseReturnLineInput> lines, string? reason)
    {
        if(lines==null || lines.Count==0) throw new InvalidOperationException("حدد صنفًا واحدًا على الأقل لإرجاعه.");
        if(lines.Any(x=>x.Quantity<=0 || x.PurchaseItemId<=0)) throw new InvalidOperationException("راجع كميات المرتجع.");
        using var c=AppDatabase.OpenConnection();
        using var tx=c.BeginTransaction();
        long userId=AppDatabase.GetUserId(c,userName,tx);
        decimal total=0m;
        var validated=new List<(PurchaseReturnLineInput Line,long PurchaseId,long ProductId,string Name,decimal Cost,int Available)>();
        foreach(var line in lines)
        {
            using var q=c.CreateCommand(); q.Transaction=tx;
            q.CommandText=@"SELECT pi.PurchaseId,pi.ProductId,pi.ProductNameSnapshot,pi.UnitCost,
pi.Quantity-COALESCE((SELECT SUM(ri.Quantity) FROM PurchaseReturnItems ri WHERE ri.PurchaseItemId=pi.Id),0),
p.SupplierId,pr.Quantity
FROM PurchaseItems pi JOIN Purchases p ON p.Id=pi.PurchaseId JOIN Products pr ON pr.Id=pi.ProductId
WHERE pi.Id=$id";
            q.Parameters.AddWithValue("$id",line.PurchaseItemId);
            using var r=q.ExecuteReader();
            if(!r.Read()) throw new InvalidOperationException("أحد أصناف فاتورة الشراء لم يعد موجودًا.");
            long purchaseId=r.GetInt64(0), productId=r.GetInt64(1); string name=r.GetString(2);
            decimal cost=Dec(r.GetValue(3)); int remain=Convert.ToInt32(r.GetValue(4),CultureInfo.InvariantCulture);
            long owner=r.GetInt64(5); int stock=Convert.ToInt32(r.GetValue(6),CultureInfo.InvariantCulture);
            if(owner!=supplierId) throw new InvalidOperationException("الصنف المحدد لا يتبع المورد المختار.");
            if(line.Quantity>remain || line.Quantity>stock) throw new InvalidOperationException($"الكمية المتاحة لإرجاع ({name}) هي {Math.Min(remain,stock)} فقط؛ لا يمكن إرجاع كمية تم بيعها أو سبق إرجاعها.");
            validated.Add((line,purchaseId,productId,name,cost,Math.Min(remain,stock)));
            total+=line.Quantity*cost;
        }
        if(total<=0) throw new InvalidOperationException("قيمة المرتجع يجب أن تكون أكبر من صفر.");
        using(var head=c.CreateCommand())
        {
            head.Transaction=tx;
            head.CommandText=@"INSERT INTO PurchaseReturns(SupplierId,UserId,TotalAmount,CashRefund,Reason,CreatedAt)
VALUES($s,$u,$t,0,$r,$d); SELECT last_insert_rowid();";
            head.Parameters.AddWithValue("$s",supplierId); head.Parameters.AddWithValue("$u",userId);
            head.Parameters.AddWithValue("$t",total); head.Parameters.AddWithValue("$r",DbText(reason));
            head.Parameters.AddWithValue("$d",DateTime.Now.ToString("O",CultureInfo.InvariantCulture));
            long returnId=Convert.ToInt64(head.ExecuteScalar(),CultureInfo.InvariantCulture);
            foreach(var item in validated)
            {
                using(var ins=c.CreateCommand())
                {
                    ins.Transaction=tx;
                    ins.CommandText=@"INSERT INTO PurchaseReturnItems(ReturnId,PurchaseId,PurchaseItemId,ProductId,ProductNameSnapshot,Quantity,UnitCost,LineTotal)
VALUES($r,$p,$pi,$prod,$n,$q,$c,$t)";
                    ins.Parameters.AddWithValue("$r",returnId); ins.Parameters.AddWithValue("$p",item.PurchaseId);
                    ins.Parameters.AddWithValue("$pi",item.Line.PurchaseItemId); ins.Parameters.AddWithValue("$prod",item.ProductId);
                    ins.Parameters.AddWithValue("$n",item.Name); ins.Parameters.AddWithValue("$q",item.Line.Quantity);
                    ins.Parameters.AddWithValue("$c",item.Cost); ins.Parameters.AddWithValue("$t",item.Line.Quantity*item.Cost);
                    ins.ExecuteNonQuery();
                }
                using(var upd=c.CreateCommand())
                {
                    upd.Transaction=tx; upd.CommandText="UPDATE Products SET Quantity=Quantity-$q,UpdatedAt=$d,StockUpdatedAt=$d WHERE Id=$id AND Quantity>=$q";
                    upd.Parameters.AddWithValue("$q",item.Line.Quantity); upd.Parameters.AddWithValue("$d",DateTime.Now.ToString("O",CultureInfo.InvariantCulture)); upd.Parameters.AddWithValue("$id",item.ProductId);
                    if(upd.ExecuteNonQuery()!=1) throw new InvalidOperationException("تغير رصيد المخزون أثناء تسجيل المرتجع؛ أعد تحميل البيانات.");
                }
                RecordInventoryMovement(c,tx,userId,item.ProductId,"PURCHASE_RETURN_OUT",-item.Line.Quantity,item.Cost,"PurchaseReturn",returnId,$"مرتجع للمورد — فاتورة شراء #{item.PurchaseId}");
            }
            decimal creditLeft=total;
            var invoices=new List<(long Id,decimal Remaining)>();
            var sourcePurchaseIds=validated.Select(x=>x.PurchaseId).Distinct().ToList();
            string preferredIds=string.Join(",",sourcePurchaseIds);
            using(var q=c.CreateCommand())
            {
                q.Transaction=tx;
                q.CommandText=$"SELECT Id,RemainingAmount FROM Purchases WHERE SupplierId=$s AND RemainingAmount>0 ORDER BY CASE WHEN Id IN ({preferredIds}) THEN 0 ELSE 1 END,CreatedAt,Id";
                q.Parameters.AddWithValue("$s",supplierId); using var rr=q.ExecuteReader();
                while(rr.Read()) invoices.Add((rr.GetInt64(0),Dec(rr.GetValue(1))));
            }
            foreach(var inv in invoices)
            {
                if(creditLeft<=0.005m)break;
                decimal part=Math.Min(creditLeft,inv.Remaining);
                using var upd=c.CreateCommand(); upd.Transaction=tx;
                upd.CommandText="UPDATE Purchases SET TotalAmount=MAX(0,TotalAmount-$a),PaidAmount=MIN(PaidAmount,MAX(0,TotalAmount-$a)),RemainingAmount=MAX(0,RemainingAmount-$a) WHERE Id=$id";
                upd.Parameters.AddWithValue("$a",part); upd.Parameters.AddWithValue("$id",inv.Id); upd.ExecuteNonQuery();
                creditLeft-=part;
            }
            // Any amount left after clearing supplier invoices remains as a credit balance
            // owned by the shop; it is never posted as cash or treasury income.
            decimal supplierCreditBalance = Math.Max(0m,creditLeft);
            if(supplierCreditBalance>0.005m)
            {
                using var credit=c.CreateCommand(); credit.Transaction=tx;
                credit.CommandText=@"INSERT INTO SupplierCredits(SupplierId,SourceReturnId,OriginalAmount,RemainingAmount,CreatedAt,Note)
VALUES($s,$r,$a,$a,$d,$n)";
                credit.Parameters.AddWithValue("$s",supplierId);
                credit.Parameters.AddWithValue("$r",returnId);
                credit.Parameters.AddWithValue("$a",supplierCreditBalance);
                credit.Parameters.AddWithValue("$d",DateTime.Now.ToString("O",CultureInfo.InvariantCulture));
                credit.Parameters.AddWithValue("$n",$"رصيد دائن من مرتجع مشتريات رقم {returnId}");
                credit.ExecuteNonQuery();
            }
            AppDatabase.AddAuditLog(c,tx,userName,"مرتجع شراء","مرتجع مورد",returnId,
                $"المورد: {supplierId} | القيمة: {total:F2} | خصم مديونية قائمة: {total-supplierCreditBalance:F2} | رصيد دائن مرحل: {supplierCreditBalance:F2} | لا يوجد رد نقدي للخزنة");
            tx.Commit();
            return (returnId,total,total-supplierCreditBalance,0m,supplierCreditBalance);
        }
    }

    public static void PaySupplier(string userName, long supplierId, decimal amount, string? note)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "قيمة السداد يجب أن تكون أكبر من صفر.");
        using var c = AppDatabase.OpenConnection();
        using var tx = c.BeginTransaction();
        var outstanding = new List<(long PurchaseId, decimal Remaining)>();
        decimal balance = 0m;
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT Id,RemainingAmount FROM Purchases WHERE SupplierId=$s AND RemainingAmount>0 ORDER BY CreatedAt,Id";
            q.Parameters.AddWithValue("$s", supplierId);
            using var r = q.ExecuteReader();
            while (r.Read()) { decimal rem = Dec(r.GetValue(1)); outstanding.Add((r.GetInt64(0), rem)); balance += rem; }
        }
        if (amount > balance) throw new InvalidOperationException($"المتبقي للمورد هو {balance:N2} ج.م فقط.");
        long userId = AppDatabase.GetUserId(c, userName, tx);
        decimal left = amount;
        foreach (var p in outstanding)
        {
            if (left <= 0) break;
            decimal part = Math.Min(left, p.Remaining);
            using var upd = c.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE Purchases SET PaidAmount=PaidAmount+$a,RemainingAmount=RemainingAmount-$a WHERE Id=$id";
            upd.Parameters.AddWithValue("$a", part); upd.Parameters.AddWithValue("$id", p.PurchaseId); upd.ExecuteNonQuery();
            using var pay = c.CreateCommand();
            pay.Transaction = tx;
            pay.CommandText = "INSERT INTO SupplierPayments(SupplierId,PurchaseId,UserId,Amount,Note,CreatedAt) VALUES($s,$p,$u,$a,$n,$d)";
            pay.Parameters.AddWithValue("$s", supplierId); pay.Parameters.AddWithValue("$p", p.PurchaseId); pay.Parameters.AddWithValue("$u", userId);
            pay.Parameters.AddWithValue("$a", part); pay.Parameters.AddWithValue("$n", DbText(note)); pay.Parameters.AddWithValue("$d", DateTime.Now.ToString("O", CultureInfo.InvariantCulture)); pay.ExecuteNonQuery();
            left -= part;
        }
        AppDatabase.AddTreasury(c, userName, "OUT", "سداد مورد", amount, note ?? "سداد حساب مورد", "SupplierPayment", supplierId, tx, "DRAWER");
        AppDatabase.AddAuditLog(c, tx, userName, "سداد", "مورد", supplierId, $"المبلغ: {amount:F2} | الرصيد السابق: {balance:F2} | الرصيد بعد السداد: {balance-amount:F2}");
        tx.Commit();
    }

    public static List<ReturnableSaleRow> LoadReturnableSales(string? search)
    {
        var result = new List<ReturnableSaleRow>();
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        string q = (search ?? "").Trim();
        cmd.CommandText = @"SELECT t.Id,t.ProductId,COALESCE(p.Name,t.Description),t.CreatedAt,COALESCE(t.Quantity,1),
COALESCE((SELECT SUM(ri.Quantity) FROM SalesReturnItems ri WHERE ri.OriginalTransactionId=t.Id),0),
 t.RequiredAmount, t.PurchaseCostTotal
FROM Transactions t LEFT JOIN Products p ON p.Id=t.ProductId
WHERE t.TransactionType='SALE' AND t.ProductId IS NOT NULL
AND ( $q='' OR COALESCE(p.Name,t.Description) LIKE $p OR t.Description LIKE $p )
ORDER BY t.CreatedAt DESC,t.Id DESC LIMIT 300";
        cmd.Parameters.AddWithValue("$q", q); cmd.Parameters.AddWithValue("$p", "%" + q + "%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime date = DateTime.TryParse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.MinValue;
            result.Add(new ReturnableSaleRow
            {
                TransactionId = r.GetInt64(0), ProductId = r.GetInt64(1), ProductName = r.GetString(2), CreatedAt = date,
                SoldQuantity = Math.Max(1, Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture)),
                ReturnedQuantity = Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture),
                NetAmount = Dec(r.GetValue(6)), PurchaseCostTotal = r.IsDBNull(7) ? 0m : Dec(r.GetValue(7))
            });
        }
        return result.Where(x => x.AvailableQuantity > 0).ToList();
    }

    public static (long ReturnId, decimal Refund, decimal DebtOffset, decimal CashRefund) RecordSaleReturn(string userName, long transactionId, int quantity, string reason)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("سبب المرتجع مطلوب.");
        using var c = AppDatabase.OpenConnection();
        using var tx = c.BeginTransaction();
        long productId;
        long? customerId;
        string productName;
        int soldQuantity;
        int alreadyReturned;
        decimal saleTotal;
        decimal alreadyRefunded;
        decimal? costTotal;
        decimal alreadyReturnedCost;
        decimal originalRemainingDebt;
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = @"SELECT t.ProductId,t.CustomerId,COALESCE(p.Name,t.Description),COALESCE(t.Quantity,1),
 t.RequiredAmount,t.PurchaseCostTotal,
 COALESCE((SELECT SUM(ri.Quantity) FROM SalesReturnItems ri WHERE ri.OriginalTransactionId=t.Id),0),
 COALESCE((SELECT SUM(ri.RefundAmount) FROM SalesReturnItems ri WHERE ri.OriginalTransactionId=t.Id),0),
 COALESCE((SELECT SUM(ri.CostReturned) FROM SalesReturnItems ri WHERE ri.OriginalTransactionId=t.Id),0),
 t.RemainingAmount
FROM Transactions t LEFT JOIN Products p ON p.Id=t.ProductId
WHERE t.Id=$id AND t.TransactionType='SALE' LIMIT 1";
            q.Parameters.AddWithValue("$id", transactionId);
            using var r = q.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("عملية البيع الأصلية غير موجودة أو ليست عملية بيع مخزنية.");
            if (r.IsDBNull(0)) throw new InvalidOperationException("العملية لا تخص صنفًا مخزنيًا، ولا يمكن إرجاعها من شاشة مرتجعات البضاعة.");
            productId = r.GetInt64(0);
            customerId = r.IsDBNull(1) ? null : r.GetInt64(1);
            productName = r.GetString(2);
            soldQuantity = Math.Max(1, Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture));
            saleTotal = Dec(r.GetValue(4));
            costTotal = r.IsDBNull(5) ? null : Dec(r.GetValue(5));
            alreadyReturned = Convert.ToInt32(r.GetValue(6), CultureInfo.InvariantCulture);
            alreadyRefunded = Dec(r.GetValue(7));
            alreadyReturnedCost = Dec(r.GetValue(8));
            originalRemainingDebt = Dec(r.GetValue(9));
        }
        decimal currentCustomerDebt = customerId.HasValue
            ? AppDatabase.GetCustomerDebt(c, customerId.Value, tx)
            : 0m;
        int available = soldQuantity - alreadyReturned;
        if (quantity > available)
            throw new InvalidOperationException($"المتاح للإرجاع {Math.Max(0, available)} من أصل {soldQuantity}.");

        bool returningFinalQuantity = quantity == available;
        // في آخر مرتجع نرد الفرق المتبقي بالضبط لتجنب فروق التقريب عند تقسيم الكمية على عدة عمليات.
        decimal refund = returningFinalQuantity
            ? Math.Max(0m, Math.Round(saleTotal - alreadyRefunded, 2, MidpointRounding.AwayFromZero))
            : Math.Round((saleTotal / soldQuantity) * quantity, 2, MidpointRounding.AwayFromZero);
        decimal? returnedCost = costTotal.HasValue
            ? (returningFinalQuantity
                ? Math.Max(0m, Math.Round(costTotal.Value - alreadyReturnedCost, 4, MidpointRounding.AwayFromZero))
                : Math.Round((costTotal.Value / soldQuantity) * quantity, 4, MidpointRounding.AwayFromZero))
            : null;
        long userId = AppDatabase.GetUserId(c, userName, tx);
        using var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"INSERT INTO SalesReturns(UserId,CustomerId,RefundAmount,Reason,CreatedAt)
VALUES($u,$c,$a,$r,$d); SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$u", userId);
        insert.Parameters.AddWithValue("$c", (object?)customerId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$a", refund);
        insert.Parameters.AddWithValue("$r", reason.Trim());
        insert.Parameters.AddWithValue("$d", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        long returnId = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);

        using (var item = c.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = @"INSERT INTO SalesReturnItems(ReturnId,OriginalTransactionId,ProductId,Quantity,RefundAmount,CostReturned,ProductNameSnapshot)
VALUES($r,$t,$p,$q,$a,$cost,$name)";
            item.Parameters.AddWithValue("$r", returnId); item.Parameters.AddWithValue("$t", transactionId);
            item.Parameters.AddWithValue("$p", productId); item.Parameters.AddWithValue("$q", quantity);
            item.Parameters.AddWithValue("$a", refund); item.Parameters.AddWithValue("$cost", (object?)returnedCost ?? DBNull.Value);
            item.Parameters.AddWithValue("$name", productName); item.ExecuteNonQuery();
        }
        using (var stock = c.CreateCommand())
        {
            stock.Transaction = tx;
            stock.CommandText = "UPDATE Products SET Quantity=Quantity+$q,UpdatedAt=$d,StockUpdatedAt=$d WHERE Id=$p";
            stock.Parameters.AddWithValue("$q", quantity); stock.Parameters.AddWithValue("$d", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            stock.Parameters.AddWithValue("$p", productId);
            if (stock.ExecuteNonQuery() != 1) throw new InvalidOperationException("الصنف الأصلي لم يعد موجودًا بالمخزن.");
        }
        RecordInventoryMovement(c, tx, userId, productId, "SALE_RETURN", quantity, returnedCost.HasValue ? returnedCost.Value / quantity : null,
            "SalesReturn", returnId, $"مرتجع من عملية البيع #{transactionId} — {reason.Trim()}");
        decimal debtOffset = Math.Min(refund, currentCustomerDebt);
        decimal cashRefund = Math.Max(0m, refund - debtOffset);
        if (customerId.HasValue && debtOffset > 0.005m)
        {
            // ترحيل رصيد الفاتورة القديمة إلى سجل الديون إذا لم يكن لها قيد DEBT،
            // حتى لا يتكرر احتساب RemainingAmount بعد بدء استخدام سجل التسويات.
            using (var legacyDebt = c.CreateCommand())
            {
                legacyDebt.Transaction = tx;
                legacyDebt.CommandText = @"SELECT COUNT(*) FROM Debts_History WHERE TransactionId=$id AND Type='DEBT'";
                legacyDebt.Parameters.AddWithValue("$id", transactionId);
                bool hasDebtEntry = Convert.ToInt64(legacyDebt.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture) > 0;
                if (originalRemainingDebt > 0.005m && !hasDebtEntry)
                {
                    using var addLegacy = c.CreateCommand();
                    addLegacy.Transaction = tx;
                    addLegacy.CommandText = @"INSERT INTO Debts_History(CustomerId,TransactionId,UserId,Type,Amount,BalanceAfter,Note,CreatedAt)
VALUES($customer,$transaction,$user,'DEBT',$amount,$balance,$note,$created)";
                    addLegacy.Parameters.AddWithValue("$customer", customerId.Value);
                    addLegacy.Parameters.AddWithValue("$transaction", transactionId);
                    addLegacy.Parameters.AddWithValue("$user", userId);
                    addLegacy.Parameters.AddWithValue("$amount", originalRemainingDebt);
                    addLegacy.Parameters.AddWithValue("$balance", currentCustomerDebt);
                    addLegacy.Parameters.AddWithValue("$note", "ترحيل رصيد فاتورة قديمة قبل تسوية مرتجع");
                    addLegacy.Parameters.AddWithValue("$created", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
                    addLegacy.ExecuteNonQuery();
                }
            }

            decimal newDebtBalance = Math.Max(0m, currentCustomerDebt - debtOffset);
            using (var settleDebt = c.CreateCommand())
            {
                settleDebt.Transaction = tx;
                settleDebt.CommandText = @"INSERT INTO Debts_History(CustomerId,TransactionId,UserId,Type,Amount,BalanceAfter,Note,CreatedAt)
VALUES($customer,NULL,$user,'PAYMENT',$amount,$balance,$note,$created)";
                settleDebt.Parameters.AddWithValue("$customer", customerId.Value);
                settleDebt.Parameters.AddWithValue("$user", userId);
                settleDebt.Parameters.AddWithValue("$amount", debtOffset);
                settleDebt.Parameters.AddWithValue("$balance", newDebtBalance);
                settleDebt.Parameters.AddWithValue("$note", $"تسوية من مرتجع مبيعات رقم {returnId} للعملية الأصلية #{transactionId}");
                settleDebt.Parameters.AddWithValue("$created", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
                settleDebt.ExecuteNonQuery();
            }

            // تحديث المتبقي على الفاتورة الأصلية بقدر الجزء الذي تمت تسويته منها.
            decimal invoiceOffset = Math.Min(originalRemainingDebt, debtOffset);
            if (invoiceOffset > 0.005m)
            {
                using var updateInvoiceDebt = c.CreateCommand();
                updateInvoiceDebt.Transaction = tx;
                updateInvoiceDebt.CommandText = "UPDATE Transactions SET RemainingAmount=MAX(0,COALESCE(RemainingAmount,0)-$amount) WHERE Id=$id";
                updateInvoiceDebt.Parameters.AddWithValue("$amount", invoiceOffset);
                updateInvoiceDebt.Parameters.AddWithValue("$id", transactionId);
                updateInvoiceDebt.ExecuteNonQuery();
            }
        }
        if (cashRefund > 0.005m)
            AppDatabase.AddTreasury(c, userName, "OUT", "مرتجعات مبيعات", cashRefund,
                $"رد نقدي لمرتجع الصنف {productName} — العملية الأصلية #{transactionId}", "SalesReturn", returnId, tx, "SALES");
        AppDatabase.AddAuditLog(c, tx, userName, "إرجاع", "مرتجع مبيعات", returnId,
            $"العملية الأصلية: {transactionId} | الصنف: {productName} | الكمية: {quantity} | المبلغ: {refund:F2} | السبب: {reason.Trim()}");
        tx.Commit();
        return (returnId, refund, debtOffset, cashRefund);
    }

    public static ProfitSnapshot CalculateProfit(DateTime from, DateTime to)
    {
        if (to.Date < from.Date) throw new ArgumentException("تاريخ النهاية يجب ألا يسبق تاريخ البداية.", nameof(to));
        using var c = AppDatabase.OpenConnection();
        string start = from.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string end = to.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        decimal knownRevenue = 0, knownCost = 0, returns = 0, returnCost = 0, uncostedReturns = 0, cashCommission = 0, serviceCommission = 0, expenses = 0, printRevenue = 0, uncosted = 0;
        int uncostedCount = 0;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"SELECT
COALESCE(SUM(CASE WHEN PurchaseCostTotal IS NOT NULL THEN RequiredAmount ELSE 0 END),0),
COALESCE(SUM(CASE WHEN PurchaseCostTotal IS NOT NULL THEN PurchaseCostTotal ELSE 0 END),0),
COALESCE(SUM(CASE WHEN PurchaseCostTotal IS NULL THEN RequiredAmount ELSE 0 END),0),
COALESCE(SUM(CASE WHEN PurchaseCostTotal IS NULL THEN 1 ELSE 0 END),0)
FROM Transactions WHERE TransactionType='SALE' AND substr(CreatedAt,1,10)>=$a AND substr(CreatedAt,1,10)<$b";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { knownRevenue = Dec(r.GetValue(0)); knownCost = Dec(r.GetValue(1)); uncosted = Dec(r.GetValue(2)); uncostedCount = Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture); }
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"SELECT COALESCE(SUM(ri.RefundAmount),0),COALESCE(SUM(COALESCE(ri.CostReturned,0)),0)
FROM SalesReturnItems ri JOIN SalesReturns sr ON sr.Id=ri.ReturnId
JOIN Transactions t ON t.Id=ri.OriginalTransactionId
WHERE t.PurchaseCostTotal IS NOT NULL AND substr(sr.CreatedAt,1,10)>=$a AND substr(sr.CreatedAt,1,10)<$b";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end);
            using var r = cmd.ExecuteReader(); if (r.Read()) { returns = Dec(r.GetValue(0)); returnCost = Dec(r.GetValue(1)); }
        }
        using (var cmd = c.CreateCommand())
        {
            // المرتجعات المرتبطة بمبيعات قديمة بلا تكلفة لا تدخل صافي الربح المعروف؛ نظهرها منفصلة بدل إخفائها أو اختراع هامش ربح لها.
            cmd.CommandText = @"SELECT COALESCE(SUM(ri.RefundAmount),0)
FROM SalesReturnItems ri JOIN SalesReturns sr ON sr.Id=ri.ReturnId
JOIN Transactions t ON t.Id=ri.OriginalTransactionId
WHERE t.PurchaseCostTotal IS NULL AND substr(sr.CreatedAt,1,10)>=$a AND substr(sr.CreatedAt,1,10)<$b";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end);
            uncostedReturns = Dec(cmd.ExecuteScalar() ?? 0m);
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(SUM(Commission),0) FROM Cash_Services WHERE substr(CreatedAt,1,10)>=$a AND substr(CreatedAt,1,10)<$b";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end); cashCommission = Dec(cmd.ExecuteScalar() ?? 0m);
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(SUM(Commission),0) FROM Transactions WHERE TransactionType='SERVICE' AND substr(CreatedAt,1,10)>=$a AND substr(CreatedAt,1,10)<$b";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end); serviceCommission = Dec(cmd.ExecuteScalar() ?? 0m);
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"SELECT COALESCE(SUM(Amount),0) FROM TreasuryMovements
WHERE MovementType='OUT' AND AccountKey='DRAWER' AND substr(CreatedAt,1,10)>=$a AND substr(CreatedAt,1,10)<$b
AND Category IN ('فاتورة كهرباء','فاتورة مياه','فاتورة إنترنت','مصروفات','مرتبات','صيانة','أخرى')";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end); expenses = Dec(cmd.ExecuteScalar() ?? 0m);
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(SUM(Price),0) FROM PrintLog WHERE substr(Timestamp,1,10)>=$a AND substr(Timestamp,1,10)<$b";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end); printRevenue = Dec(cmd.ExecuteScalar() ?? 0m);
        }
        var printingBreakdown = new List<ProfitPrintingCategoryRow>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"SELECT COALESCE(NULLIF(PricingCategory,''),'غير محدد'),
COALESCE(ColorType,'غير محدد'),COALESCE(DuplexMode,'غير محدد'),
COUNT(*),COALESCE(SUM(Pages * Copies),0),COALESCE(SUM(PhysicalSheets),0),COALESCE(SUM(Price),0)
FROM PrintLog
WHERE substr(Timestamp,1,10)>=$a AND substr(Timestamp,1,10)<$b
GROUP BY COALESCE(NULLIF(PricingCategory,''),'غير محدد'),COALESCE(ColorType,'غير محدد'),COALESCE(DuplexMode,'غير محدد')
ORDER BY SUM(Price) DESC";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end);
            using var r = cmd.ExecuteReader();
            while (r.Read()) printingBreakdown.Add(new ProfitPrintingCategoryRow
            {
                Category = r.GetString(0), ColorType = r.GetString(1), DuplexMode = r.GetString(2),
                Jobs = Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture),
                Faces = Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture),
                Sheets = Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture),
                Revenue = Dec(r.GetValue(6))
            });
        }

        var productBreakdown = new List<ProfitProductRow>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"SELECT t.ProductId,COALESCE(MAX(p.Name),MAX(t.Description)),
COALESCE(SUM(t.RequiredAmount),0),COALESCE(SUM(t.PurchaseCostTotal),0),
COALESCE(SUM(COALESCE(ret.RefundAmount,0)),0),COALESCE(SUM(COALESCE(ret.ReturnedCost,0)),0)
FROM Transactions t LEFT JOIN Products p ON p.Id=t.ProductId
LEFT JOIN (
    SELECT ri.OriginalTransactionId,SUM(ri.RefundAmount) AS RefundAmount,SUM(COALESCE(ri.CostReturned,0)) AS ReturnedCost
    FROM SalesReturnItems ri JOIN SalesReturns sr ON sr.Id=ri.ReturnId
    WHERE substr(sr.CreatedAt,1,10)>=$a AND substr(sr.CreatedAt,1,10)<$b
    GROUP BY ri.OriginalTransactionId
) ret ON ret.OriginalTransactionId=t.Id
WHERE t.TransactionType='SALE' AND t.ProductId IS NOT NULL AND t.PurchaseCostTotal IS NOT NULL
AND substr(t.CreatedAt,1,10)>=$a AND substr(t.CreatedAt,1,10)<$b
GROUP BY t.ProductId ORDER BY (SUM(t.RequiredAmount)-SUM(t.PurchaseCostTotal)-SUM(COALESCE(ret.RefundAmount,0))+SUM(COALESCE(ret.ReturnedCost,0))) DESC LIMIT 200";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end);
            using var r = cmd.ExecuteReader();
            while (r.Read()) productBreakdown.Add(new ProfitProductRow
            {
                ProductName = r.GetString(1), Revenue = Dec(r.GetValue(2)), Cost = Dec(r.GetValue(3)),
                Refunds = Dec(r.GetValue(4)), ReturnedCost = Dec(r.GetValue(5))
            });
        }
        var employeeBreakdown = new List<ProfitEmployeeRow>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"SELECT u.UserName,COALESCE(SUM(t.RequiredAmount),0),
COALESCE(SUM(CASE WHEN t.PurchaseCostTotal IS NOT NULL THEN t.RequiredAmount-t.PurchaseCostTotal-COALESCE(ret.RefundAmount,0)+COALESCE(ret.ReturnedCost,0) ELSE 0 END),0),
COUNT(*),COALESCE(SUM(CASE WHEN t.PurchaseCostTotal IS NULL THEN 1 ELSE 0 END),0)
FROM Transactions t JOIN AppUsers u ON u.Id=t.UserId
LEFT JOIN (
    SELECT ri.OriginalTransactionId,SUM(ri.RefundAmount) AS RefundAmount,SUM(COALESCE(ri.CostReturned,0)) AS ReturnedCost
    FROM SalesReturnItems ri JOIN SalesReturns sr ON sr.Id=ri.ReturnId
    WHERE substr(sr.CreatedAt,1,10)>=$a AND substr(sr.CreatedAt,1,10)<$b
    GROUP BY ri.OriginalTransactionId
) ret ON ret.OriginalTransactionId=t.Id
WHERE t.TransactionType='SALE' AND substr(t.CreatedAt,1,10)>=$a AND substr(t.CreatedAt,1,10)<$b
GROUP BY u.UserName ORDER BY SUM(t.RequiredAmount) DESC";
            cmd.Parameters.AddWithValue("$a", start); cmd.Parameters.AddWithValue("$b", end);
            using var r = cmd.ExecuteReader();
            while (r.Read()) employeeBreakdown.Add(new ProfitEmployeeRow
            {
                UserName = r.GetString(0), SalesRevenue = Dec(r.GetValue(1)), KnownGrossProfit = Dec(r.GetValue(2)),
                SaleCount = Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture), UncostedCount = Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture)
            });
        }
        return new ProfitSnapshot
        {
            From = from.Date, To = to.Date, KnownSalesRevenue = knownRevenue, KnownCostOfGoods = knownCost,
            SalesGrossProfit = knownRevenue - knownCost, ReturnedSales = returns, ReturnCost = returnCost,
            CashServiceCommissions = cashCommission, OtherServiceCommissions = serviceCommission,
            OperatingExpenses = expenses, PrintingRevenue = printRevenue,
            UncostedLegacySales = uncosted, UncostedReturns = uncostedReturns, UncostedLegacyCount = uncostedCount,
            ProductBreakdown = productBreakdown, EmployeeBreakdown = employeeBreakdown,
            PrintingBreakdown = printingBreakdown
        };
    }

    public static List<string[]> GlobalSearch(string term)
    {
        var rows = new List<string[]>();
        string q = (term ?? "").Trim();
        if (q.Length < 2) return rows;
        string p = "%" + q + "%";
        using var c = AppDatabase.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT 'صنف' AS Kind,Name AS Title,COALESCE(Barcode,''),CAST(Quantity AS TEXT),CAST(Id AS TEXT) FROM Products WHERE Name LIKE $p OR COALESCE(Barcode,'') LIKE $p
UNION ALL SELECT 'عميل',Name,COALESCE(Phone,''),'',CAST(Id AS TEXT) FROM Customers WHERE Name LIKE $p OR COALESCE(Phone,'') LIKE $p
UNION ALL SELECT 'مورد',Name,COALESCE(Phone,''),'',CAST(Id AS TEXT) FROM Suppliers WHERE Name LIKE $p OR COALESCE(Phone,'') LIKE $p
UNION ALL SELECT 'عملية بيع',Description,CreatedAt,printf('%.2f ج.م',RequiredAmount),CAST(Id AS TEXT) FROM Transactions WHERE TransactionType='SALE' AND (Description LIKE $p OR CAST(Id AS TEXT) LIKE $p)
UNION ALL SELECT 'فاتورة',COALESCE(CustomerName,''),'رقم '||Number,printf('%.2f ج.م',Total),CAST(Id AS TEXT) FROM Invoices WHERE CustomerName LIKE $p OR CAST(Number AS TEXT) LIKE $p
ORDER BY 1,2 LIMIT 500";
        cmd.Parameters.AddWithValue("$p", p);
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(new[] { r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4) });
        return rows;
    }

    private static void RecordInventoryMovement(SqliteConnection c, SqliteTransaction tx, long userId,
        long productId, string type, int delta, decimal? unitCost, string referenceType, long? referenceId, string note)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO InventoryMovements(ProductId,UserId,MovementType,QuantityDelta,UnitCost,ReferenceType,ReferenceId,Note,CreatedAt)
VALUES($p,$u,$t,$q,$cost,$rt,$rid,$n,$d)";
        cmd.Parameters.AddWithValue("$p", productId); cmd.Parameters.AddWithValue("$u", userId); cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$q", delta); cmd.Parameters.AddWithValue("$cost", (object?)unitCost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rt", referenceType); cmd.Parameters.AddWithValue("$rid", (object?)referenceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$n", note); cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    internal static void RecordStockMovement(SqliteConnection c, SqliteTransaction tx, string userName,
        long productId, string type, int delta, decimal? unitCost, string refType, long? refId, string note)
        => RecordInventoryMovement(c, tx, AppDatabase.GetUserId(c, userName, tx), productId, type, delta, unitCost, refType, refId, note);

    private static object DbText(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static decimal Dec(object value) => value == null || value == DBNull.Value ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
}

internal sealed class PurchaseLineInput
{
    public long ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitCost { get; init; }
}

internal sealed class PurchaseReturnableItemRow
{
    public long PurchaseItemId { get; init; }
    public long PurchaseId { get; init; }
    public long ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public int PurchasedQuantity { get; init; }
    public int ReturnedQuantity { get; init; }
    public int UnreturnedQuantity { get; init; }
    public int AvailableQuantity { get; set; }
    public decimal UnitCost { get; init; }
    public string InvoiceNumber { get; init; } = "";
    public DateTime PurchaseDate { get; init; }
}
internal sealed class PurchaseReturnLineInput
{
    public long PurchaseItemId { get; init; }
    public int Quantity { get; init; }
}

internal sealed class PurchaseReturnArchiveRow
{
    public long Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public string SupplierName { get; init; } = "";
    public decimal Total { get; init; }
    public decimal CashRefund { get; init; }
    public string Reason { get; init; } = "";
    public string Products { get; init; } = "";
}
