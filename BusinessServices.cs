using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal static class CashServiceManager
{
    public static void AddCashService(
        string userName,
        string serviceType,
        string provider,
        string? customerName,
        string? phone,
        decimal amount,
        decimal commissionPercent,
        decimal commission)
    {
        if (serviceType != "إيداع" && serviceType != "سحب")
            throw new ArgumentException("نوع الخدمة غير صحيح.");
        if (provider != "Vodafone Cash" && provider != "Fawry" && provider != "InstaPay")
            throw new ArgumentException("اختر Vodafone Cash أو Fawry أو InstaPay.");
        if (amount <= 0)
            throw new ArgumentException("المبلغ يجب أن يكون أكبر من صفر.");
        if (commissionPercent < 0 || commissionPercent > 100)
            throw new ArgumentException("نسبة العمولة يجب أن تكون بين 0% و100%.");

        // العمولة اللي جاية من الشاشة هي المعتمدة (ممكن تكون مبلغ مكتوب يدويًا مش نسبة).
        if (commission < 0)
            throw new ArgumentException("العمولة لا يمكن أن تكون سالبة.");
        commission = Math.Round(commission, 2, MidpointRounding.AwayFromZero);
        decimal total = amount + commission;

        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        long userId = AppDatabase.GetUserId(connection, userName, tx);
        long? customerId = null;

        if (!string.IsNullOrWhiteSpace(customerName))
            customerId = AppDatabase.GetOrCreateCustomer(connection, customerName!, phone, tx);

        using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = @"
INSERT INTO Cash_Services
(UserId,ServiceType,Provider,CustomerId,CustomerName,Phone,Amount,Commission,CommissionPercent,TotalAmount,CreatedAt)
VALUES($user,$service,$provider,$customer,$name,$phone,$amount,$commission,$commissionPercent,$total,$created);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$user", userId);
            command.Parameters.AddWithValue("$service", serviceType);
            command.Parameters.AddWithValue("$provider", provider);
            command.Parameters.AddWithValue("$customer", (object?)customerId ?? DBNull.Value);
            command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(customerName) ? DBNull.Value : customerName!.Trim());
            command.Parameters.AddWithValue("$phone", string.IsNullOrWhiteSpace(phone) ? DBNull.Value : phone!.Trim());
            command.Parameters.AddWithValue("$amount", amount);
            command.Parameters.AddWithValue("$commission", commission);
            command.Parameters.AddWithValue("$commissionPercent", commissionPercent);
            command.Parameters.AddWithValue("$total", total);
            command.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
            long id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            // فلسفة المحاسبة: ثلاثة حسابات منفصلة تمامًا لكل عملية سحب/إيداع:
            //  1) المحفظة الإلكترونية (Vodafone Cash / Fawry / InstaPay) — يخرج
            //     منها المبلغ الأساسي دائمًا (حركة "خارج" على حساب CASH حتى
            //     يظهر في صف "سحب وإيداع/النقدية" بجدول اليومية).
            //  2) النقدية في الدرج (DRAWER) — وهي النقدية الفعلية التي دخلت
            //     المحل من العميل، منفصلة عن "المبيعات" الحقيقية:
            //       - الإيداع: العميل يدفع الكاش فعليًا في يد المحل، والمحل
            //         يحوّل المبلغ للعميل عبر محفظته الإلكترونية؛ فالمبلغ +
            //         العمولة كلاهما نقدية دخلت الدرج فعليًا.
            //       - السحب: العميل يستلم كاش من الدرج مقابل ما يُخصم من محفظته،
            //         والمبلغ + العمولة كلاهما نقدية دخلت الدرج فعليًا.
            //  3) حسابات النشاط: المبيعات (SALES) للبيع الحقيقي للمنتجات،
            //     والطباعة/التصوير (PRINTING) لبندهما المستقل في اليومية.
            string walletAccountKey = string.Equals(provider, "InstaPay", StringComparison.OrdinalIgnoreCase)
                ? "INSTAPAY" : "CASH";
            // اتجاه المحفظة والدرج يختلف حسب نوع العملية:
            // الإيداع: رصيد المحفظة ينقص، والكاش الداخل للدرج = المبلغ + العمولة.
            // السحب: رصيد المحفظة يزيد، والدرج يخرج منه المبلغ وتدخل إليه العمولة فقط.
            bool deposit = serviceType == "إيداع";
            AppDatabase.AddTreasury(connection, userName, deposit ? "OUT" : "IN", "خدمات الكاش", amount,
                $"{serviceType} - {provider}", "CashService", id, tx, walletAccountKey);
            AppDatabase.AdjustWalletBalance(connection, provider, deposit ? -amount : amount, tx);

            if (deposit)
            {
                AppDatabase.AddTreasury(connection, userName, "IN", "خدمات الكاش", amount,
                    $"إيداع - {provider}", "CashService", id, tx, "DRAWER");
                if (commission > 0)
                    AppDatabase.AddTreasury(connection, userName, "IN", "خدمات الكاش", commission,
                        $"عمولة إيداع - {provider}", "CashService", id, tx, "DRAWER");
            }
            else
            {
                AppDatabase.AddTreasury(connection, userName, "OUT", "خدمات الكاش", amount,
                    $"سحب نقدي - {provider}", "CashService", id, tx, "DRAWER");
                if (commission > 0)
                    AppDatabase.AddTreasury(connection, userName, "IN", "خدمات الكاش", commission,
                        $"عمولة سحب - {provider}", "CashService", id, tx, "DRAWER");
            }

            // العمولة تظهر في سطر مستقل باسم «النسبة» للمتابعة، لكنها لا تُضاف مرة أخرى
            // إلى إجمالي الأموال؛ فهي بالفعل جزء من حركة النقدية في الدرج.
            if (commission > 0)
                AppDatabase.AddTreasury(connection, userName, "IN", "النسبة", commission,
                    $"نسبة {serviceType} - {provider}", "CashCommission", id, tx, "PERCENTAGE");

            AppDatabase.AddAuditLog(connection, tx, userName, "إنشاء", "خدمة كاش", id,
                $"{serviceType} - {provider} | المبلغ: {amount:F2} | العمولة: {commission:F2} | الإجمالي: {total:F2}");
        }

        tx.Commit();
    }
}

internal static class SalesService
{
    public static long CompleteSale(
        string userName,
        string description,
        decimal originalAmount,
        decimal discountPercent,
        decimal required,
        decimal paid,
        long? customerId,
        long? productId,
        out decimal remaining,
        out decimal change,
        int quantity = 1)
    {
        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        long transactionId = CompleteSaleWithinTransaction(
            connection, tx, userName, description, originalAmount, discountPercent,
            required, paid, customerId, productId, out remaining, out change, quantity);
        tx.Commit();
        return transactionId;
    }

    /// <summary>
    /// يسجل البيع داخل معاملة يملكها المستدعي؛ يفيد عند جمع الطباعة والخصم والدين والإضافة في عملية واحدة ذرّية.
    /// لا ينفذ Commit بنفسه.
    /// </summary>
    internal static long CompleteSaleWithinTransaction(
        SqliteConnection connection,
        SqliteTransaction tx,
        string userName,
        string description,
        decimal originalAmount,
        decimal discountPercent,
        decimal required,
        decimal paid,
        long? customerId,
        long? productId,
        out decimal remaining,
        out decimal change,
        int quantity = 1)
    {
        if (quantity < 1)
            throw new ArgumentException("العدد يجب أن يكون رقمًا صحيحًا لا يقل عن 1.");
        if (originalAmount <= 0)
            throw new ArgumentException("السعر قبل الخصم يجب أن يكون أكبر من صفر.");
        if (discountPercent < 0 || discountPercent > 100)
            throw new ArgumentException("نسبة الخصم غير صحيحة.");
        if (required <= 0)
            throw new ArgumentException("المبلغ المطلوب بعد الخصم يجب أن يكون أكبر من صفر.");
        if (paid < 0)
            throw new ArgumentException("المبلغ المدفوع لا يمكن أن يكون سالبًا.");

        remaining = Math.Max(0, required - paid);
        change = Math.Max(0, paid - required);
        long userId = AppDatabase.GetUserId(connection, userName, tx);
        decimal? purchaseCostTotal = null;
        if (productId.HasValue)
        {
            using var costQuery = connection.CreateCommand();
            costQuery.Transaction = tx;
            costQuery.CommandText = "SELECT PurchasePrice FROM Products WHERE Id=$id";
            costQuery.Parameters.AddWithValue("$id", productId.Value);
            object? costValue = costQuery.ExecuteScalar();
            if (costValue == null || costValue == DBNull.Value)
                throw new InvalidOperationException("الصنف غير موجود في المخزن.");
            purchaseCostTotal = Convert.ToDecimal(costValue, CultureInfo.InvariantCulture) * quantity;
        }

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"
INSERT INTO Transactions
(CustomerId,ProductId,UserId,TransactionType,Description,RequiredAmount,PaidAmount,RemainingAmount,ChangeAmount,OriginalAmount,DiscountPercent,Quantity,PurchaseCostTotal,CreatedAt)
VALUES($customer,$product,$user,$type,$description,$required,$paid,$remaining,$change,$original,$discount,$quantity,$purchaseCost,$created);
SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$customer", (object?)customerId ?? DBNull.Value);
        command.Parameters.AddWithValue("$product", (object?)productId ?? DBNull.Value);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$type", "SALE");
        command.Parameters.AddWithValue("$description", description.Trim());
        command.Parameters.AddWithValue("$required", required);
        command.Parameters.AddWithValue("$paid", paid);
        command.Parameters.AddWithValue("$remaining", remaining);
        command.Parameters.AddWithValue("$change", change);
        command.Parameters.AddWithValue("$original", originalAmount);
        command.Parameters.AddWithValue("$discount", discountPercent);
        command.Parameters.AddWithValue("$quantity", quantity);
        command.Parameters.AddWithValue("$purchaseCost", (object?)purchaseCostTotal ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        long transactionId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        if (paid > 0)
            AppDatabase.AddTreasury(connection, userName, "IN", "مبيعات", paid,
                description, "Transaction", transactionId, tx, "SALES");
        if (change > 0)
            AppDatabase.AddTreasury(connection, userName, "OUT", "باقي مبيعات", change,
                $"باقي نقدي مُعاد للعميل — {description}", "SaleChange", transactionId, tx, "SALES");

        if (remaining > 0 && customerId.HasValue)
        {
            decimal balance = AppDatabase.GetCustomerDebt(connection, customerId.Value, tx, transactionId) + remaining;
            using var debt = connection.CreateCommand();
            debt.Transaction = tx;
            debt.CommandText = @"
INSERT INTO Debts_History(CustomerId,TransactionId,UserId,Type,Amount,BalanceAfter,Note,CreatedAt)
VALUES($customer,$transaction,$user,'DEBT',$amount,$balance,$note,$created)";
            debt.Parameters.AddWithValue("$customer", customerId.Value);
            debt.Parameters.AddWithValue("$transaction", transactionId);
            debt.Parameters.AddWithValue("$user", userId);
            debt.Parameters.AddWithValue("$amount", remaining);
            debt.Parameters.AddWithValue("$balance", balance);
            debt.Parameters.AddWithValue("$note", description.Trim());
            debt.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
            debt.ExecuteNonQuery();
        }

        if (productId.HasValue)
        {
            using var stock = connection.CreateCommand();
            stock.Transaction = tx;
            stock.CommandText = "UPDATE Products SET Quantity=Quantity-$q, UpdatedAt=$u WHERE Id=$id AND Quantity >= $q";
            stock.Parameters.AddWithValue("$id", productId.Value);
            stock.Parameters.AddWithValue("$q", quantity);
            stock.Parameters.AddWithValue("$u", DateTime.Now.ToString("O"));
            if (stock.ExecuteNonQuery() == 0)
                throw new InvalidOperationException(quantity > 1
                    ? $"الكمية المطلوبة ({quantity}) أكبر من المتاح في المخزن أو الصنف غير موجود."
                    : "الكمية غير متاحة أو الصنف غير موجود.");
            BusinessManagementStore.RecordStockMovement(connection, tx, userName, productId.Value,
                "SALE_OUT", -quantity, purchaseCostTotal.HasValue ? purchaseCostTotal.Value / quantity : null,
                "Transaction", transactionId, "خصم كمية نتيجة إتمام عملية بيع");
        }

        AppDatabase.AddAuditLog(connection, tx, userName, "إنشاء", "بيع", transactionId,
            $"{description} | الكمية: {quantity} | الإجمالي: {required:F2} | المدفوع: {paid:F2} | الدين: {remaining:F2}");
        return transactionId;
    }

    public static void PayDebt(string userName, long customerId, decimal amount, string note)
    {
        if (amount <= 0) throw new ArgumentException("مبلغ السداد يجب أن يكون أكبر من صفر.");
        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        decimal current = AppDatabase.GetCustomerDebt(connection, customerId, tx);
        if (current <= 0) throw new InvalidOperationException("العميل ليس عليه دين حالي.");
        if (amount > current) throw new InvalidOperationException($"أقصى سداد مسموح هو {current:F2} ج.م.");

        decimal balance = current - amount;
        long userId = AppDatabase.GetUserId(connection, userName, tx);
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"
INSERT INTO Debts_History(CustomerId,TransactionId,UserId,Type,Amount,BalanceAfter,Note,CreatedAt)
VALUES($customer,NULL,$user,'PAYMENT',$amount,$balance,$note,$created)";
        command.Parameters.AddWithValue("$customer", customerId);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$balance", balance);
        command.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(note) ? "سداد دين" : note.Trim());
        command.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        command.ExecuteNonQuery();

        AppDatabase.AddTreasury(connection, userName, "IN", "تحصيل ديون", amount,
            "سداد دين", "DebtPayment", customerId, tx);
        AppDatabase.AddAuditLog(connection, tx, userName, "سداد", "دين عميل", customerId,
            $"المبلغ المسدد: {amount:F2} | الرصيد بعد السداد: {balance:F2} | {note}");
        tx.Commit();
    }

    /// <summary>
    /// تسجيل دين قديم على العميل من قبل استخدام البرنامج.
    /// بيتسجل كقيد DEBT في كشف الحساب بدون ما يتحرك أي مبلغ في الخزنة أو اليومية
    /// (لأن الفلوس دي اتصرفت قبل البرنامج).
    /// </summary>
    internal const string OpeningDebtNotePrefix = "دين قديم قبل استخدام البرنامج";

    public static void AddOpeningDebt(string userName, long customerId, decimal amount, string note)
    {
        if (amount <= 0) throw new ArgumentException("مبلغ الدين القديم لازم يكون أكبر من صفر.");
        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        decimal current = AppDatabase.GetCustomerDebt(connection, customerId, tx);
        decimal balance = current + amount;
        long userId = AppDatabase.GetUserId(connection, userName, tx);
        string finalNote = BuildOpeningDebtNote(note);

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"
INSERT INTO Debts_History(CustomerId,TransactionId,UserId,Type,Amount,BalanceAfter,Note,CreatedAt)
VALUES($customer,NULL,$user,'DEBT',$amount,$balance,$note,$created)";
        command.Parameters.AddWithValue("$customer", customerId);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$balance", balance);
        command.Parameters.AddWithValue("$note", finalNote);
        command.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        command.ExecuteNonQuery();

        AppDatabase.AddAuditLog(connection, tx, userName, "إنشاء", "دين قديم", customerId,
            $"مبلغ الدين القديم: {amount:F2} | الرصيد بعد الإضافة: {balance:F2} | {finalNote}");
        tx.Commit();
    }

    internal static string BuildOpeningDebtNote(string note)
        => string.IsNullOrWhiteSpace(note)
            ? OpeningDebtNotePrefix
            : OpeningDebtNotePrefix + ": " + note.Trim();

    /// <summary> يرجّع true لو الملاحظة بتاعة قيد دين قديم (اتسجل بزر «إضافة دين قديم»). </summary>
    internal static bool IsOpeningDebtNote(string? note)
        => !string.IsNullOrEmpty(note) && note.StartsWith(OpeningDebtNotePrefix, StringComparison.Ordinal);

    /// <summary> يشيل البادئة ويرجّع الملاحظة اللي كتبها المستخدم بس. </summary>
    internal static string StripOpeningDebtPrefix(string? note)
    {
        if (!IsOpeningDebtNote(note)) return note ?? "";
        string rest = note!.Substring(OpeningDebtNotePrefix.Length);
        return rest.TrimStart(':', ' ').Trim();
    }

    private static (long CustomerId, decimal Amount) ReadOpeningDebt(SqliteConnection connection, SqliteTransaction tx, long debtId)
    {
        using var q = connection.CreateCommand();
        q.Transaction = tx;
        q.CommandText = "SELECT CustomerId,Amount,Type,TransactionId,COALESCE(Note,'') FROM Debts_History WHERE Id=$id";
        q.Parameters.AddWithValue("$id", debtId);
        using var r = q.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("قيد الدين القديم غير موجود.");
        bool isOpening = r.GetString(2) == "DEBT" && r.IsDBNull(3) && IsOpeningDebtNote(r.GetString(4));
        if (!isOpening) throw new InvalidOperationException("القيد ده مش دين قديم، فمينفعش يتعدل أو يتلغى من هنا.");
        return (r.GetInt64(0), Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// يعيد حساب «الرصيد بعد الحركة» لكل قيود العميل بالترتيب الزمني، ويرجّع الرصيد النهائي
    /// قبل أي تقريب لصفر (لو طلع سالب يبقى العميل سدّد أكتر من اللي عليه).
    /// </summary>
    internal static decimal RecomputeBalances(SqliteConnection connection, SqliteTransaction tx, long customerId)
    {
        decimal running;
        using (var legacy = connection.CreateCommand())
        {
            legacy.Transaction = tx;
            legacy.CommandText = @"
SELECT COALESCE(SUM(COALESCE(t.RemainingAmount,0)),0)
FROM Transactions t
WHERE t.CustomerId=$c
  AND t.TransactionType IN ('SALE','COPY','SERVICE')
  AND COALESCE(t.RemainingAmount,0)>0
  AND NOT EXISTS (SELECT 1 FROM Debts_History d WHERE d.TransactionId=t.Id AND d.Type='DEBT')";
            legacy.Parameters.AddWithValue("$c", customerId);
            running = Convert.ToDecimal(legacy.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }

        var rows = new List<(long Id, string Type, decimal Amount)>();
        using (var list = connection.CreateCommand())
        {
            list.Transaction = tx;
            list.CommandText = "SELECT Id,Type,Amount FROM Debts_History WHERE CustomerId=$c ORDER BY CreatedAt,Id";
            list.Parameters.AddWithValue("$c", customerId);
            using var r = list.ExecuteReader();
            while (r.Read())
                rows.Add((r.GetInt64(0), r.GetString(1), Convert.ToDecimal(r.GetValue(2), CultureInfo.InvariantCulture)));
        }

        foreach (var row in rows)
        {
            running += row.Type == "DEBT" ? row.Amount : -row.Amount;
            using var upd = connection.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE Debts_History SET BalanceAfter=$b WHERE Id=$id";
            upd.Parameters.AddWithValue("$b", Math.Max(0m, running));
            upd.Parameters.AddWithValue("$id", row.Id);
            upd.ExecuteNonQuery();
        }
        return running;
    }

    /// <summary> تعديل مبلغ/ملاحظة دين قديم اتسجل بالغلط. </summary>
    public static void UpdateOpeningDebt(string userName, long debtId, decimal newAmount, string note)
    {
        if (newAmount <= 0) throw new ArgumentException("مبلغ الدين القديم لازم يكون أكبر من صفر.");
        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        var (customerId, oldAmount) = ReadOpeningDebt(connection, tx, debtId);
        string finalNote = BuildOpeningDebtNote(note);

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Debts_History SET Amount=$a,Note=$n WHERE Id=$id";
            cmd.Parameters.AddWithValue("$a", newAmount);
            cmd.Parameters.AddWithValue("$n", finalNote);
            cmd.Parameters.AddWithValue("$id", debtId);
            cmd.ExecuteNonQuery();
        }

        decimal total = RecomputeBalances(connection, tx, customerId);
        if (total < 0m)
            throw new InvalidOperationException("العميل سدّد بالفعل مبلغ أكبر من القيمة الجديدة، فمينفعش تقلّل الدين لكده.");

        AppDatabase.AddAuditLog(connection, tx, userName, "تعديل", "دين قديم", customerId,
            $"المبلغ من {oldAmount:F2} إلى {newAmount:F2} | الدين بعد التعديل: {Math.Max(0m, total):F2} | {finalNote}");
        tx.Commit();
    }

    /// <summary> إلغاء دين قديم اتسجل بالغلط (بيتمسح من كشف الحساب ويفضل أثره في سجل المراجعة). </summary>
    public static void DeleteOpeningDebt(string userName, long debtId)
    {
        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        var (customerId, oldAmount) = ReadOpeningDebt(connection, tx, debtId);

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM Debts_History WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", debtId);
            cmd.ExecuteNonQuery();
        }

        decimal total = RecomputeBalances(connection, tx, customerId);
        if (total < 0m)
            throw new InvalidOperationException("العميل سدّد بالفعل مبلغ على الدين ده، فمينفعش يتلغى.");

        AppDatabase.AddAuditLog(connection, tx, userName, "حذف", "دين قديم", customerId,
            $"تم إلغاء دين قديم بمبلغ {oldAmount:F2} | الدين بعد الإلغاء: {Math.Max(0m, total):F2}");
        tx.Commit();
    }
}


internal static class CopyService
{
    public static long RecordCopy(
        string userName,
        string description,
        string preset,
        int blackSimple,
        decimal blackSimplePrice,
        int blackDuplex,
        decimal blackDuplexPrice,
        int colorSimple,
        decimal colorSimplePrice,
        int colorDuplex,
        decimal colorDuplexPrice,
        decimal discountAmount,
        decimal paid,
        long? customerId,
        out decimal total,
        out decimal remaining,
        out decimal change)
    {
        if (string.IsNullOrWhiteSpace(description))
            description = "تصوير مستند";
        if (blackSimple < 0 || blackDuplex < 0 || colorSimple < 0 || colorDuplex < 0)
            throw new ArgumentException("أعداد التصوير لا يمكن أن تكون سالبة.");
        if (blackSimplePrice < 0 || blackDuplexPrice < 0 || colorSimplePrice < 0 || colorDuplexPrice < 0)
            throw new ArgumentException("أسعار التصوير لا يمكن أن تكون سالبة.");
        if (paid < 0)
            throw new ArgumentException("المبلغ المدفوع لا يمكن أن يكون سالبًا.");

        decimal subtotal =
            blackSimple * blackSimplePrice +
            blackDuplex * blackDuplexPrice +
            colorSimple * colorSimplePrice +
            colorDuplex * colorDuplexPrice;

        if (subtotal <= 0)
            throw new ArgumentException("أدخل عددًا واحدًا على الأقل من عمليات التصوير.");

        // الخصم: لازم يكون موجب ومايزيدش عن الحد الأقصى المحدد من الإدارة للطباعة والتصوير.
        if (discountAmount < 0 || discountAmount > subtotal)
            throw new ArgumentException("مبلغ الخصم غير صحيح.");
        decimal maxPrintPct = PricingStore.Load().MaxPrintDiscountPercent;
        if (!DiscountMath.IsWithinMax(subtotal, discountAmount, maxPrintPct))
            throw new InvalidOperationException($"أقصى خصم مسموح من الإدارة للتصوير والطباعة هو {maxPrintPct:0.##}%.");

        total = subtotal - discountAmount;
        if (total <= 0)
            throw new ArgumentException("الإجمالي بعد الخصم يجب أن يكون أكبر من صفر.");
        decimal discountPercent = discountAmount > 0 ? discountAmount / subtotal * 100m : 0m;

        remaining = Math.Max(0, total - paid);
        change = Math.Max(0, paid - total);

        if (remaining > 0 && !customerId.HasValue)
            throw new InvalidOperationException("يجب اختيار عميل عند وجود مبلغ متبقي ليُسجل كدين.");

        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        long userId = AppDatabase.GetUserId(connection, userName, tx);

        static string P(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
        string details =
            $"{description} | فئة: {preset} | أسود وش: {blackSimple} × {P(blackSimplePrice)} | " +
            $"أسود وش وظهر: {blackDuplex} × {P(blackDuplexPrice)} | " +
            $"ألوان وش: {colorSimple} × {P(colorSimplePrice)} | " +
            $"ألوان وش وظهر: {colorDuplex} × {P(colorDuplexPrice)}" +
            (discountAmount > 0 ? $" | خصم: {P(discountAmount)} ({P(discountPercent)}%)" : "");

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"
INSERT INTO Transactions
(CustomerId,UserId,TransactionType,Description,RequiredAmount,PaidAmount,RemainingAmount,ChangeAmount,OriginalAmount,DiscountPercent,CreatedAt,PricingCategory)
VALUES($customer,$user,'COPY',$description,$required,$paid,$remaining,$change,$original,$discount,$created,$category);
SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$customer", (object?)customerId ?? DBNull.Value);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$description", details);
        command.Parameters.AddWithValue("$required", total);
        command.Parameters.AddWithValue("$original", subtotal);
        command.Parameters.AddWithValue("$discount", discountPercent);
        command.Parameters.AddWithValue("$paid", paid);
        command.Parameters.AddWithValue("$remaining", remaining);
        command.Parameters.AddWithValue("$change", change);
        command.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("$category", preset);

        long transactionId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        if (paid > 0)
        {
            AppDatabase.AddTreasury(
                connection, userName, "IN", "التصوير", paid,
                $"تصوير: {description}", "Copy", transactionId, tx, "PRINTING");
        }

        if (remaining > 0 && customerId.HasValue)
        {
            decimal balance = AppDatabase.GetCustomerDebt(connection, customerId.Value, tx, transactionId) + remaining;
            using var debt = connection.CreateCommand();
            debt.Transaction = tx;
            debt.CommandText = @"
INSERT INTO Debts_History(CustomerId,TransactionId,UserId,Type,Amount,BalanceAfter,Note,CreatedAt)
VALUES($customer,$transaction,$user,'DEBT',$amount,$balance,$note,$created)";
            debt.Parameters.AddWithValue("$customer", customerId.Value);
            debt.Parameters.AddWithValue("$transaction", transactionId);
            debt.Parameters.AddWithValue("$user", userId);
            debt.Parameters.AddWithValue("$amount", remaining);
            debt.Parameters.AddWithValue("$balance", balance);
            debt.Parameters.AddWithValue("$note", $"دين تصوير: {description}");
            debt.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
            debt.ExecuteNonQuery();
        }

        AppDatabase.AddAuditLog(connection, tx, userName, "إنشاء", "تصوير", transactionId,
            $"{description} | الإجمالي: {total:F2} | المدفوع: {paid:F2} | الدين: {remaining:F2}");
        tx.Commit();
        return transactionId;
    }
}



internal static class ServiceTransactionManager
{
    public static long Complete(
        string userName,
        string serviceName,
        string description,
        string customerName,
        string? phone,
        decimal amount,
        decimal commission,
        decimal commissionPercent,
        decimal paid,
        long? customerId,
        out decimal remaining,
        out decimal change)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("اختر الخدمة.");
        if (amount <= 0)
            throw new ArgumentException("السعر يجب أن يكون أكبر من صفر.");
        if (commission < 0)
            throw new ArgumentException("العمولة لا يمكن أن تكون سالبة.");
        if (commissionPercent < 0 || commissionPercent > 100)
            throw new ArgumentException("نسبة العمولة يجب أن تكون بين 0% و100%.");
        if (paid < 0)
            throw new ArgumentException("المبلغ المدفوع لا يمكن أن يكون سالبًا.");

        decimal calculatedCommission = amount * commissionPercent / 100m;
        // commission and percentage are linked. The UI sends the final values,
        // but we normalize the percentage from the commission when commission was entered.
        if (commissionPercent == 0 && commission > 0)
            commissionPercent = amount <= 0 ? 0 : commission * 100m / amount;
        commission = Math.Round(commission, 2, MidpointRounding.AwayFromZero);
        commissionPercent = Math.Round(commissionPercent, 4, MidpointRounding.AwayFromZero);

        decimal total = amount + commission;

        // المبلغ المدفوع هنا قيمة نهائية قررتها الشاشة مسبقًا (ترك الخانة فارغة
        // يعني دفع الإجمالي كاملًا، وكتابة 0 صراحة تعني أن المبلغ بالكامل دين)،
        // لذلك نتعامل معه كرقم فعلي مباشرة دون أي استثناء خاص بالصفر.
        remaining = Math.Max(0, total - paid);
        change = Math.Max(0, paid - total);

        if (remaining > 0 && !customerId.HasValue && string.IsNullOrWhiteSpace(customerName))
            throw new InvalidOperationException("المبلغ المدفوع أقل من الإجمالي. اختر عميلًا لتسجيل الفرق كدين.");

        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();
        long userId = AppDatabase.GetUserId(connection, userName, tx);

        if (!customerId.HasValue && !string.IsNullOrWhiteSpace(customerName))
            customerId = AppDatabase.GetOrCreateCustomer(connection, customerName, phone, tx);

        string fullDescription = string.IsNullOrWhiteSpace(description)
            ? $"خدمة: {serviceName}"
            : $"خدمة: {serviceName} | {description.Trim()}";

        if (customerId.HasValue)
        {
            using var customerCmd = connection.CreateCommand();
            customerCmd.Transaction = tx;
            customerCmd.CommandText = "SELECT Name FROM Customers WHERE Id=$id";
            customerCmd.Parameters.AddWithValue("$id", customerId.Value);
            customerName = customerCmd.ExecuteScalar()?.ToString() ?? customerName;
        }

        string transactionDetails =
            $"{fullDescription} | العميل: {(string.IsNullOrWhiteSpace(customerName) ? "غير محدد" : customerName.Trim())} | " +
            $"السعر: {amount:F2} | العمولة: {commission:F2} ({commissionPercent:F2}%) | " +
            $"الإجمالي: {total:F2} | المدفوع: {paid:F2} | " +
            $"المتبقي: {remaining:F2} | الباقي للعميل: {change:F2}";

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"
INSERT INTO Transactions
(CustomerId,UserId,TransactionType,Description,RequiredAmount,PaidAmount,RemainingAmount,ChangeAmount,OriginalAmount,DiscountPercent,ServiceName,Commission,CommissionPercent,CreatedAt)
VALUES($customer,$user,'SERVICE',$description,$required,$paid,$remaining,$change,$amount,0,$service,$commission,$commissionPercent,$created);
SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$customer", (object?)customerId ?? DBNull.Value);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$description", transactionDetails);
        command.Parameters.AddWithValue("$required", total);
        command.Parameters.AddWithValue("$paid", paid);
        command.Parameters.AddWithValue("$remaining", remaining);
        command.Parameters.AddWithValue("$change", change);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$service", serviceName);
        command.Parameters.AddWithValue("$commission", commission);
        command.Parameters.AddWithValue("$commissionPercent", commissionPercent);
        command.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        long transactionId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        // محاسبة خدمة الماكينة منفصلة تمامًا:
        // - ما دفعه العميل فعليًا (قيمة الخدمة + العمولة) يدخل "النقدية في الدرج"،
        //   وليس حساب "المبيعات" الذي أصبح مخصصًا للبيع الحقيقي (منتجات/طباعة/تصوير) فقط.
        // - قيمة الفاتورة الأساسية تخرج من OPay أو باب رزق فقط.
        // - العمولة جزء من المبلغ الداخل للدرج ولا تُخصم من رصيد الماكينة.
        // - الجزء غير المدفوع بسبب الدين لا يدخل الخزينة أصلًا.
        decimal cashIn = Math.Min(paid, total);
        if (cashIn > 0)
            // المبلغ الذي يدفعه العميل في عملية OPay / باب رزق يدخل النقدية في
            // الدرج، وليس حساب السحب والإيداع ولا حساب المبيعات. حساب الماكينة
            // نفسه يتأثر بالخارج (قيمة الخدمة الأساسية) فقط.
            AppDatabase.AddTreasury(connection, userName, "IN", "خدمات الدفع", cashIn,
                transactionDetails, "ServiceTransaction", transactionId, tx, "DRAWER");

        string? machineAccountKey = serviceName.StartsWith("ماكينة OPay", StringComparison.OrdinalIgnoreCase) ? "OPAY" :
                                    serviceName.StartsWith("ماكينة باب رزق", StringComparison.OrdinalIgnoreCase) ||
                                    serviceName.StartsWith("ماكينة BEE", StringComparison.OrdinalIgnoreCase) ? "BEE" : null;

        if (amount > 0 && machineAccountKey != null)
            AppDatabase.AddTreasury(connection, userName, "OUT", "خدمات الدفع", amount,
                $"سداد قيمة {serviceName} | رقم العملية: {transactionId}", "ServiceTransaction", transactionId, tx, machineAccountKey);

        // تحديث رصيد الماكينة الفعلي بنفس قيمة الخارج فقط.
        if (amount > 0 && machineAccountKey != null)
            AppDatabase.AdjustMachineBalance(connection, machineAccountKey, -amount, tx);

        // عمولة OPay / باب رزق تظهر في بند «النسبة» دون احتسابها مرة أخرى
        // في إجمالي الأموال، لأنها موجودة بالفعل ضمن المبلغ المدفوع في الدرج.
        if (commission > 0 && machineAccountKey != null)
            AppDatabase.AddTreasury(connection, userName, "IN", "النسبة", commission,
                $"نسبة عمولة {serviceName} | رقم العملية: {transactionId}",
                "MachineCommission", transactionId, tx, "PERCENTAGE");

        if (remaining > 0 && customerId.HasValue)
        {
            decimal balance = AppDatabase.GetCustomerDebt(connection, customerId.Value, tx, transactionId) + remaining;
            using var debt = connection.CreateCommand();
            debt.Transaction = tx;
            debt.CommandText = @"
INSERT INTO Debts_History(CustomerId,TransactionId,UserId,Type,Amount,BalanceAfter,Note,CreatedAt)
VALUES($customer,$transaction,$user,'DEBT',$amount,$balance,$note,$created)";
            debt.Parameters.AddWithValue("$customer", customerId.Value);
            debt.Parameters.AddWithValue("$transaction", transactionId);
            debt.Parameters.AddWithValue("$user", userId);
            debt.Parameters.AddWithValue("$amount", remaining);
            debt.Parameters.AddWithValue("$balance", balance);
            debt.Parameters.AddWithValue("$note", transactionDetails);
            debt.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
            debt.ExecuteNonQuery();
        }

        AppDatabase.AddAuditLog(connection, tx, userName, "إنشاء", "خدمة دفع/ماكينة", transactionId,
            $"{serviceName} | السعر: {amount:F2} | العمولة: {commission:F2} | المدفوع: {paid:F2} | الدين: {remaining:F2}");
        tx.Commit();
        return transactionId;
    }
}


internal static class CustomerService
{
    /// <summary> تعديل اسم/رقم عميل. الرقم لازم ميكونش مسجل لعميل تاني. </summary>
    public static void Update(string userName, long customerId, string name, string? phone)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم العميل إجباري.");
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        name = name.Trim();

        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();

        string oldName = "", oldPhone = "";
        using (var read = connection.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT Name,COALESCE(Phone,'') FROM Customers WHERE Id=$id";
            read.Parameters.AddWithValue("$id", customerId);
            using var r = read.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("العميل غير موجود.");
            oldName = r.IsDBNull(0) ? "" : r.GetString(0);
            oldPhone = r.GetString(1);
        }

        if (phone != null)
        {
            using var dup = connection.CreateCommand();
            dup.Transaction = tx;
            dup.CommandText = "SELECT Name FROM Customers WHERE Phone=$p AND Id<>$id LIMIT 1";
            dup.Parameters.AddWithValue("$p", phone);
            dup.Parameters.AddWithValue("$id", customerId);
            var other = dup.ExecuteScalar();
            if (other != null && other != DBNull.Value)
                throw new InvalidOperationException($"الرقم ده مسجل بالفعل لعميل تاني: {other}.");
        }

        using (var upd = connection.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE Customers SET Name=$n,Phone=$p WHERE Id=$id";
            upd.Parameters.AddWithValue("$n", name);
            upd.Parameters.AddWithValue("$p", (object?)phone ?? DBNull.Value);
            upd.Parameters.AddWithValue("$id", customerId);
            upd.ExecuteNonQuery();
        }

        AppDatabase.AddAuditLog(connection, tx, userName, "تعديل", "عميل", customerId,
            $"الاسم: «{oldName}» ← «{name}» | الرقم: «{oldPhone}» ← «{phone ?? ""}»");
        tx.Commit();
    }

    /// <summary>
    /// حذف عميل. مسموح بس لو معندوش أي حركة (بيع/تصوير/خدمة/دين/سداد)، عشان كشوف الحساب
    /// والتقارير القديمة متتكسرش. غير كده يتعدّل بس.
    /// </summary>
    public static void Delete(string userName, long customerId)
    {
        using var connection = AppDatabase.OpenConnection();
        using var tx = connection.BeginTransaction();

        string name;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT Name FROM Customers WHERE Id=$id";
            read.Parameters.AddWithValue("$id", customerId);
            var v = read.ExecuteScalar();
            if (v == null || v == DBNull.Value) throw new InvalidOperationException("العميل غير موجود.");
            name = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        long linked = 0;
        foreach (string table in new[] { "Transactions", "Debts_History", "Cash_Services" })
        {
            using var count = connection.CreateCommand();
            count.Transaction = tx;
            count.CommandText = $"SELECT COUNT(*) FROM {table} WHERE CustomerId=$id";
            count.Parameters.AddWithValue("$id", customerId);
            linked += Convert.ToInt64(count.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
        if (linked > 0)
            throw new InvalidOperationException(
                "العميل ده عليه حركات مسجلة (مبيعات/ديون/دفعات)، فمينفعش يتحذف عشان كشف الحساب والتقارير. " +
                "لو الاسم أو الرقم غلط عدّله بزر «تعديل».");

        using (var del = connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Customers WHERE Id=$id";
            del.Parameters.AddWithValue("$id", customerId);
            del.ExecuteNonQuery();
        }

        AppDatabase.AddAuditLog(connection, tx, userName, "حذف", "عميل", customerId, $"تم حذف العميل «{name}» (بدون حركات)");
        tx.Commit();
    }
}
