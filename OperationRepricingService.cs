using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

internal sealed class OperationRepricingResult
{
    public string OperationName { get; init; } = "العملية";
    public decimal OldPrice { get; init; }
    public decimal NewPrice { get; init; }
    public decimal OldCash { get; init; }
    public decimal NewCash { get; init; }
    public decimal OldDebt { get; init; }
    public decimal NewDebt { get; init; }
    public PrintLogger.LogEntry? CsvEntry { get; init; }

    public string ConfirmationText
    {
        get
        {
            decimal cashDelta = NewCash - OldCash;
            string cashInstruction = cashDelta > 0.0049m
                ? $"\nتنبيه: سيزيد المبلغ المسجل كدخل للخزينة بمقدار {cashDelta:N2} ج.م. لا تعتمد التعديل إلا بعد تحصيل هذا الفرق فعليًا."
                : cashDelta < -0.0049m
                    ? $"\nتنبيه: سينخفض المبلغ المسجل في الخزينة بمقدار {Math.Abs(cashDelta):N2} ج.م. تأكد من رد الفرق أو تسويته فعليًا."
                    : "";
            decimal debtDelta = NewDebt - OldDebt;
            string debtInstruction = debtDelta > 0.0049m
                ? $"\nسيُضاف {debtDelta:N2} ج.م إلى الدين المرتبط بالعملية."
                : debtDelta < -0.0049m
                    ? $"\nسيُخصم {Math.Abs(debtDelta):N2} ج.م من الدين المرتبط بالعملية."
                    : "";
            return $"سيتم إعادة تسعير {OperationName} حسب الفئة الجديدة.\n\n" +
                   $"السعر الحالي: {OldPrice:N2} ج.م\nالسعر الجديد: {NewPrice:N2} ج.م\n\n" +
                   $"داخل الخزينة: {OldCash:N2} ← {NewCash:N2} ج.م\n" +
                   $"الدين المرتبط: {OldDebt:N2} ← {NewDebt:N2} ج.م" +
                   cashInstruction + debtInstruction + "\n\n" +
                   "سيتم حفظ تعديل السعر وحركة الخزينة والدين وسجل المراجعة معًا. هل تريد المتابعة؟";
        }
    }
}

/// <summary>
/// يعيد تسعير عمليات الطباعة والتصوير عند تغيير الفئة. كل التعديلات المالية
/// تتم داخل معاملة قاعدة بيانات واحدة حتى لا ينفصل السعر عن الخزينة أو الدين.
/// </summary>
internal static class OperationRepricingService
{
    private const decimal Epsilon = 0.0049m;

    public static OperationRepricingResult RepricePrint(
        SqliteConnection connection, SqliteTransaction tx, long printLogId,
        string nextCategory, string actor)
    {
        EnsureBusinessDayOpen(connection, tx);
        PrintLogger.LogEntry entry;
        decimal oldPrice;
        string oldCategory;
        int pages, copies, physicalSheets;
        string color, duplex, optionsJson;
        PrintOptions savedOptions;

        using (var read = connection.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = @"
SELECT p.Timestamp,p.FileName,COALESCE(p.FileType,''),p.Pages,p.Copies,
       COALESCE(p.ColorType,''),COALESCE(p.DuplexMode,''),p.PhysicalSheets,p.Price,
       COALESCE(p.PricingCategory,''),COALESCE(p.PrintOptionsJson,''),u.UserName
FROM PrintLog p LEFT JOIN AppUsers u ON u.Id=p.UserId
WHERE p.Id=$id AND substr(p.Timestamp,1,10)=date('now','localtime') LIMIT 1;";
            read.Parameters.AddWithValue("$id", printLogId);
            using var r = read.ExecuteReader();
            if (!r.Read())
                throw new InvalidOperationException("سجل الطباعة غير موجود أو ليس من اليوم.");

            DateTime timestamp = DateTime.Parse(r.GetString(0), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            // PrintLog.csv يسجل الوقت بدقة الثانية، لذلك نطابق دقة الملف عند مزامنته.
            timestamp = new DateTime(timestamp.Year, timestamp.Month, timestamp.Day,
                timestamp.Hour, timestamp.Minute, timestamp.Second);
            oldPrice = ToDecimal(r.GetValue(8));
            oldCategory = r.IsDBNull(9) ? "" : r.GetString(9);
            pages = r.GetInt32(3);
            copies = r.GetInt32(4);
            physicalSheets = r.GetInt32(7);
            color = r.IsDBNull(5) ? "" : r.GetString(5);
            duplex = r.IsDBNull(6) ? "" : r.GetString(6);
            optionsJson = r.IsDBNull(10) ? "" : r.GetString(10);
            entry = new PrintLogger.LogEntry
            {
                Timestamp = timestamp,
                FileName = r.IsDBNull(1) ? "" : r.GetString(1),
                FileType = r.IsDBNull(2) ? "" : r.GetString(2),
                Pages = pages,
                Copies = copies,
                ColorType = color,
                DuplexMode = duplex,
                PhysicalSheets = r.GetInt32(7),
                Price = oldPrice,
                PricingCategory = oldCategory,
                PrintOptionsJson = optionsJson,
                UserName = r.IsDBNull(11) ? "" : r.GetString(11)
            };
        }

        if (string.IsNullOrWhiteSpace(oldCategory))
            throw new InvalidOperationException("هذه عملية قديمة ولا تحتوي على الفئة الأصلية؛ لا يمكن إعادة حساب الخصم السابق بأمان. لم يتم تغيير أي بيانات.");
        if (pages <= 0 || copies <= 0)
            throw new InvalidOperationException("عدد الصفحات أو النسخ غير محفوظ بشكل صحيح، لذلك أُوقف تعديل السعر لحماية الحسابات.");

        if (string.IsNullOrWhiteSpace(optionsJson))
            throw new InvalidOperationException("إعدادات الطباعة الأصلية غير محفوظة لهذه العملية؛ لا يمكن التأكد من عدد الصفحات في الورقة. لم يتم تغيير أي بيانات.");
        try
        {
            savedOptions = System.Text.Json.JsonSerializer.Deserialize<PrintOptions>(optionsJson)
                ?? throw new InvalidOperationException("إعدادات الطباعة المحفوظة غير قابلة للقراءة.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("تعذر قراءة إعدادات الطباعة اللازمة لإعادة التسعير؛ لم يتم تعديل أي بيانات.", ex);
        }
        int pagesPerSheet = Math.Max(1, savedOptions.PagesPerSheet);
        int sidesPerCopyForCheck = (int)Math.Ceiling(pages / (double)pagesPerSheet);
        int expectedSheetsPerCopy = string.Equals(duplex, "وش وظهر", StringComparison.Ordinal)
            ? (int)Math.Ceiling(sidesPerCopyForCheck / 2.0)
            : sidesPerCopyForCheck;
        int expectedPhysicalSheets = expectedSheetsPerCopy * copies;
        if (physicalSheets > 0 && physicalSheets != expectedPhysicalSheets)
            throw new InvalidOperationException("سجل الطباعة يبدو أنه ناتج عن طباعة جزئية أو أن إعداداته الأصلية غير متطابقة. لا يمكن إعادة حساب سعره بدقة، لذلك لم يتم تغيير أي مبلغ.");

        PricingSettings pricing = PricingStore.Load();
        decimal oldGross = ComputePrintGross(pricing, oldCategory, pages, copies, pagesPerSheet, color, duplex, savedOptions);
        decimal newGross = ComputePrintGross(pricing, nextCategory, pages, copies, pagesPerSheet, color, duplex);
        if (oldGross <= 0m)
            throw new InvalidOperationException("تعذر تحديد السعر الأساسي القديم من إعدادات الفئة؛ لم يتم إجراء أي تعديل.");

        // الإصدارات السابقة لـ v47 لا تحتوي على لقطة أسعار وقت الطباعة.
        // نسمح بإعادة تسعيرها فقط إذا كان السعر المسجل يساوي السعر الأساسي المحسوب الآن؛
        // أما وجود خصم أو اختلاف سعر مع غياب اللقطة فلا يمكن إثبات سببه، فنوقف التعديل بدل التخمين.
        bool hasFullSnapshot = savedOptions.PriceBlackSimpleSnapshot.HasValue &&
                               savedOptions.PriceBlackDuplexSnapshot.HasValue &&
                               savedOptions.PriceColorSimpleSnapshot.HasValue &&
                               savedOptions.PriceColorDuplexSnapshot.HasValue;
        bool hasPartialSnapshot = savedOptions.PriceBlackSimpleSnapshot.HasValue ||
                                  savedOptions.PriceBlackDuplexSnapshot.HasValue ||
                                  savedOptions.PriceColorSimpleSnapshot.HasValue ||
                                  savedOptions.PriceColorDuplexSnapshot.HasValue;
        if (hasPartialSnapshot && !hasFullSnapshot)
            throw new InvalidOperationException("لقطة أسعار الطباعة غير مكتملة في هذا السجل؛ لم يتم تعديل الحساب.");
        if (!hasFullSnapshot && Math.Abs(oldPrice - oldGross) > 0.009m)
            throw new InvalidOperationException("هذه العملية من إصدار أقدم ولا تحتوي على أسعار الوحدات وقت الطباعة، والسعر الحالي لا يثبت السعر الأصلي (قد يكون هناك خصم أو تغيير سابق في الأسعار). لم يتم تعديلها لتجنب إعادة تسعير غير دقيقة.");

        // يحافظ على نسبة الخصم التي كانت مطبقة على العملية قبل تغيير الفئة.
        decimal discountFactor = oldPrice / oldGross;
        if (discountFactor < -0.0001m || discountFactor > 1.0001m)
            throw new InvalidOperationException("السعر الحالي لا يتطابق مع أسعار الفئة المحفوظة (قد تكون أسعار الإدارة تغيّرت بعد الطباعة). لم يتم إعادة التسعير حتى لا يتغير الخصم بطريقة خاطئة.");
        discountFactor = Math.Min(1m, Math.Max(0m, discountFactor));
        decimal newPrice = Money(newGross * discountFactor);

        decimal oldCash = SumIncoming(connection, tx, "PrintLog", printLogId);
        var debtLinks = ReadPrintDebtLinks(connection, tx, printLogId);
        decimal oldDebt = debtLinks.Sum(x => x.Amount);
        if (debtLinks.Count == 0 && HasDebtConversionAuditForPrint(connection, tx, printLogId))
            throw new InvalidOperationException("يوجد في سجل المراجعة ما يثبت تحويل هذه الطباعة إلى دين، لكن رابط الدين غير موجود. لم يتم تغيير السعر أو الخزينة حتى لا يتحول الدين بالخطأ إلى دخل نقدي.");
        if (Math.Abs((oldCash + oldDebt) - oldPrice) > 0.02m)
            throw new InvalidOperationException($"حساب العملية غير متطابق قبل التعديل: السعر {oldPrice:0.00}، الخزينة {oldCash:0.00}، والدين {oldDebt:0.00}. أُوقف إعادة التسعير للمراجعة بدل إنشاء أو حذف مبلغ مالي غير مبرر.");
        decimal newDebt = debtLinks.Count == 0
            ? 0m
            : Math.Min(newPrice, Math.Max(0m, oldDebt + (newPrice - oldPrice)));
        decimal newCash = Money(newPrice - newDebt);

        // بعد إعادة التسعير تصبح الفئة الجديدة هي الأساس الحالي؛ نحفظ أسعار وحداتها
        // داخل PrintOptionsJson حتى يظل تعديل الفئة مرة ثانية صحيحًا حتى لو تغيّرت أسعار الإدارة لاحقًا.
        ApplyPrintRateSnapshot(savedOptions, pricing, nextCategory);
        string updatedOptionsJson = System.Text.Json.JsonSerializer.Serialize(savedOptions);

        using (var update = connection.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = "UPDATE PrintLog SET PricingCategory=$category,Price=$price,PrintOptionsJson=$options WHERE Id=$id AND substr(Timestamp,1,10)=date('now','localtime');";
            update.Parameters.AddWithValue("$category", nextCategory);
            update.Parameters.AddWithValue("$price", newPrice);
            update.Parameters.AddWithValue("$options", updatedOptionsJson);
            update.Parameters.AddWithValue("$id", printLogId);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("تعذر تحديث سجل الطباعة؛ ربما لم تعد العملية من اليوم.");
        }

        AdjustPrintDebtLinks(connection, tx, printLogId, debtLinks, newDebt);
        SetIncomingAmount(connection, tx, "PrintLog", printLogId, newCash,
            "الطباعة", entry.FileName, actor, "PRINTING");

        AppDatabase.AddAuditLog(connection, tx, actor, "إعادة تسعير", "طباعة", printLogId,
            $"الفئة: {oldCategory} ← {nextCategory} | السعر: {oldPrice:0.00} ← {newPrice:0.00} | " +
            $"الخزينة: {oldCash:0.00} ← {newCash:0.00} | الدين: {oldDebt:0.00} ← {newDebt:0.00} | " +
            $"الخصم النسبي المحفوظ: {(1m - discountFactor) * 100m:0.##}%");

        return new OperationRepricingResult
        {
            OperationName = "عملية الطباعة",
            OldPrice = oldPrice,
            NewPrice = newPrice,
            OldCash = oldCash,
            NewCash = newCash,
            OldDebt = oldDebt,
            NewDebt = newDebt,
            CsvEntry = new PrintLogger.LogEntry
            {
                Timestamp = entry.Timestamp,
                UserName = entry.UserName,
                FileName = entry.FileName,
                FileType = entry.FileType,
                Pages = entry.Pages,
                Copies = entry.Copies,
                ColorType = entry.ColorType,
                DuplexMode = entry.DuplexMode,
                PhysicalSheets = entry.PhysicalSheets,
                Price = entry.Price,
                PricingCategory = nextCategory,
                PrintOptionsJson = updatedOptionsJson
            }
        };
    }

    public static OperationRepricingResult RepriceCopy(
        SqliteConnection connection, SqliteTransaction tx, long transactionId,
        string nextCategory, string actor)
    {
        EnsureBusinessDayOpen(connection, tx);

        long? customerId;
        string description, oldCategory;
        decimal oldRequired, paid, oldRemaining, oldChange, discountPercent;

        using (var read = connection.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = @"
SELECT COALESCE(Description,''),COALESCE(PricingCategory,''),
       COALESCE(RequiredAmount,0),COALESCE(PaidAmount,0),COALESCE(RemainingAmount,0),
       COALESCE(DiscountPercent,0),COALESCE(ChangeAmount,0),CustomerId
FROM Transactions
WHERE Id=$id AND TransactionType='COPY' AND substr(CreatedAt,1,10)=date('now','localtime') LIMIT 1;";
            read.Parameters.AddWithValue("$id", transactionId);
            using var r = read.ExecuteReader();
            if (!r.Read())
                throw new InvalidOperationException("عملية التصوير غير موجودة أو ليست من اليوم.");
            description = r.GetString(0);
            oldCategory = r.GetString(1);
            if (string.IsNullOrWhiteSpace(oldCategory))
            {
                var categoryMatch = Regex.Match(description, @"فئة:\s*([^|]+)");
                if (categoryMatch.Success) oldCategory = categoryMatch.Groups[1].Value.Trim();
            }
            oldRequired = ToDecimal(r.GetValue(2));
            paid = ToDecimal(r.GetValue(3));
            oldRemaining = ToDecimal(r.GetValue(4));
            discountPercent = Math.Min(100m, Math.Max(0m, ToDecimal(r.GetValue(5))));
            oldChange = ToDecimal(r.GetValue(6));
            customerId = r.IsDBNull(7) ? null : r.GetInt64(7);
        }

        if (!TryReadCopyCounts(description, out var counts))
            throw new InvalidOperationException("وصف عملية التصوير لا يحتوي على أعداد الأنواع الأربعة بشكل يمكن قراءته؛ لم يتم تغيير أي بيانات.");

        PricingSettings pricing = PricingStore.Load();
        var rates = GetCopyRates(pricing, nextCategory);
        decimal newSubtotal = Money(
            counts.BlackSimple * rates.BlackSimple +
            counts.BlackDuplex * rates.BlackDuplex +
            counts.ColorSimple * rates.ColorSimple +
            counts.ColorDuplex * rates.ColorDuplex);
        decimal newDiscountAmount = Money(newSubtotal * discountPercent / 100m);
        if (newDiscountAmount > newSubtotal) newDiscountAmount = newSubtotal;
        decimal newRequired = Money(newSubtotal - newDiscountAmount);
        // RemainingAmount يمثل أصل الدين المرتبط بالعملية. دفعات الدين اللاحقة
        // محفوظة كسجلات PAYMENT منفصلة؛ لذلك نعدّل أصل الدين بفارق السعر فقط
        // ولا نعيد إنشاء دين تم سداده بالفعل.
        decimal newRemaining = Money(Math.Max(0m, oldRemaining + (newRequired - oldRequired)));
        // PaidAmount هو المبلغ المسلم، وChangeAmount هو ما أُعيد للعميل؛
        // نستخدم صافي ما احتفظت به الخزينة حتى لا نكرر احتساب الفكة عند تعديل الفئة مرة أخرى.
        decimal retainedCashBefore = Money(Math.Max(0m, paid - oldChange));
        decimal newCash = Money(Math.Min(retainedCashBefore, newRequired));
        decimal newChange = Money(Math.Max(0m, paid - newCash));
        decimal oldCash = SumIncoming(connection, tx, "Copy", transactionId);

        if (newRemaining > Epsilon && !customerId.HasValue)
            throw new InvalidOperationException("السعر الجديد أكبر من المبلغ المدفوع، لكن عملية التصوير ليس لها عميل مرتبط. اختر عميلًا/سجّل الدين أولًا، ولم يتم تعديل أي بيانات.");

        string baseDescription = description;
        int categoryIndex = description.IndexOf(" | فئة:", StringComparison.Ordinal);
        if (categoryIndex >= 0) baseDescription = description.Substring(0, categoryIndex);
        string newDescription = BuildCopyDescription(baseDescription, nextCategory, counts, rates,
            newDiscountAmount, discountPercent);

        using (var update = connection.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = @"
UPDATE Transactions
SET Description=$description,PricingCategory=$category,OriginalAmount=$original,
    RequiredAmount=$required,RemainingAmount=$remaining,ChangeAmount=$change
WHERE Id=$id AND TransactionType='COPY' AND substr(CreatedAt,1,10)=date('now','localtime');";
            update.Parameters.AddWithValue("$description", newDescription);
            update.Parameters.AddWithValue("$category", nextCategory);
            update.Parameters.AddWithValue("$original", newSubtotal);
            update.Parameters.AddWithValue("$required", newRequired);
            update.Parameters.AddWithValue("$remaining", newRemaining);
            update.Parameters.AddWithValue("$change", newChange);
            update.Parameters.AddWithValue("$id", transactionId);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("تعذر تحديث عملية التصوير.");
        }

        AdjustCopyDebt(connection, tx, transactionId, customerId,
            oldRemaining, newRemaining, newDescription);
        SetIncomingAmount(connection, tx, "Copy", transactionId, newCash,
            "التصوير", "تصوير: " + newDescription, actor, "PRINTING");

        if (customerId.HasValue)
        {
            decimal customerBalance = SalesService.RecomputeBalances(connection, tx, customerId.Value);
            if (customerBalance < -Epsilon)
                throw new InvalidOperationException("التعديل سيجعل رصيد العميل سالبًا لأن جزءًا من الدين تم سداده بالفعل. لم يتم اعتماد أي تغيير؛ راجع حساب العميل أولًا.");
        }

        AppDatabase.AddAuditLog(connection, tx, actor, "إعادة تسعير", "تصوير", transactionId,
            $"الفئة: {(string.IsNullOrWhiteSpace(oldCategory) ? "غير مسجلة" : oldCategory)} ← {nextCategory} | " +
            $"السعر: {oldRequired:0.00} ← {newRequired:0.00} | الخزينة: {oldCash:0.00} ← {newCash:0.00} | " +
            $"الدين: {oldRemaining:0.00} ← {newRemaining:0.00} | الخصم: {discountPercent:0.##}% محفوظ");

        return new OperationRepricingResult
        {
            OperationName = "عملية التصوير",
            OldPrice = oldRequired,
            NewPrice = newRequired,
            OldCash = oldCash,
            NewCash = newCash,
            OldDebt = oldRemaining,
            NewDebt = newRemaining
        };
    }

    private static void EnsureBusinessDayOpen(SqliteConnection connection, SqliteTransaction tx)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT Status FROM DailySessions WHERE BusinessDate=$date LIMIT 1;";
        command.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        string? status = command.ExecuteScalar()?.ToString();
        if (string.Equals(status, "CLOSED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("اليومية مغلقة بالفعل. لا يمكن تغيير سعر عملية بعد اعتماد إغلاق اليومية حتى لا تختلف أرصدة الإغلاق.");
    }

    private static bool HasDebtConversionAuditForPrint(SqliteConnection connection, SqliteTransaction tx, long printLogId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"SELECT Details FROM AuditLog
WHERE Action='تحويل إلى دين' AND EntityType='طباعة/تصوير' ORDER BY Id DESC;";
        using var reader = command.ExecuteReader();
        string target = printLogId.ToString(CultureInfo.InvariantCulture);
        while (reader.Read())
        {
            string details = reader.IsDBNull(0) ? "" : reader.GetString(0);
            Match ids = Regex.Match(details, @"أرقام سجلات الطباعة:\s*(?<ids>[^|]+)");
            if (!ids.Success) continue;
            foreach (string item in ids.Groups["ids"].Value.Split(','))
                if (string.Equals(item.Trim(), target, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static decimal ComputePrintGross(PricingSettings p, string category,
        int pages, int copies, int pagesPerSheet, string color, string duplex,
        PrintOptions? savedSnapshot = null)
    {
        bool isColor = string.Equals(color, "ألوان", StringComparison.OrdinalIgnoreCase);
        bool isDuplex = string.Equals(duplex, "وش وظهر", StringComparison.OrdinalIgnoreCase);
        int sidesPerCopy = (int)Math.Ceiling(pages / (double)Math.Max(1, pagesPerSheet));
        decimal? savedSingle = savedSnapshot == null ? null : (isColor
            ? savedSnapshot.PriceColorSimpleSnapshot : savedSnapshot.PriceBlackSimpleSnapshot);
        decimal? savedDuplex = savedSnapshot == null ? null : (isColor
            ? savedSnapshot.PriceColorDuplexSnapshot : savedSnapshot.PriceBlackDuplexSnapshot);
        decimal single = savedSingle ?? PrintRate(p, category, isColor, false);
        decimal perSide = savedDuplex ?? PrintRate(p, category, isColor, true);
        decimal oneCopy = isDuplex
            ? (sidesPerCopy / 2) * 2m * perSide + (sidesPerCopy % 2) * single
            : sidesPerCopy * single;
        return Money(oneCopy * Math.Max(1, copies));
    }

    private static void ApplyPrintRateSnapshot(PrintOptions options, PricingSettings p, string category)
    {
        options.PriceBlackSimpleSnapshot = PrintRate(p, category, color: false, duplex: false);
        options.PriceBlackDuplexSnapshot = PrintRate(p, category, color: false, duplex: true);
        options.PriceColorSimpleSnapshot = PrintRate(p, category, color: true, duplex: false);
        options.PriceColorDuplexSnapshot = PrintRate(p, category, color: true, duplex: true);
    }

    private static decimal PrintRate(PricingSettings p, string category, bool color, bool duplex)
    {
        if (category == "الطلبة")
            return duplex
                ? (color ? p.StudentDuplexColor : p.StudentDuplexBlack)
                : (color ? p.StudentColor : p.StudentBlack);
        if (category == "مخصص")
            return duplex
                ? (color ? p.CustomDuplexColor : p.CustomDuplexBlack)
                : (color ? p.CustomColor : p.CustomBlack);
        if (category == "عادي")
            return duplex
                ? (color ? p.NormalDuplexColor : p.NormalDuplexBlack)
                : (color ? p.NormalColor : p.NormalBlack);
        throw new InvalidOperationException("الفئة القديمة غير معروفة؛ لم يتم إعادة التسعير.");
    }

    private static (decimal BlackSimple, decimal BlackDuplex, decimal ColorSimple, decimal ColorDuplex)
        GetCopyRates(PricingSettings p, string category) => category switch
        {
            "الطلبة" => (p.CopyStudentBlack, p.CopyStudentDuplexBlack, p.CopyStudentColor, p.CopyStudentDuplexColor),
            "عادي" => (p.CopyNormalBlack, p.CopyNormalDuplexBlack, p.CopyNormalColor, p.CopyNormalDuplexColor),
            "مخصص" => (p.CopyCustomBlack, p.CopyCustomDuplexBlack, p.CopyCustomColor, p.CopyCustomDuplexColor),
            _ => throw new InvalidOperationException("الفئة السعرية المختارة غير معروفة.")
        };

    private static bool TryReadCopyCounts(string description,
        out (int BlackSimple, int BlackDuplex, int ColorSimple, int ColorDuplex) counts)
    {
        counts = default;
        if (!TryCount(description, @"أسود\s+وش\s*:", out int bs) ||
            !TryCount(description, @"أسود\s+وش\s+وظهر\s*:", out int bd) ||
            !TryCount(description, @"ألوان\s+وش\s*:", out int cs) ||
            !TryCount(description, @"ألوان\s+وش\s+وظهر\s*:", out int cd))
            return false;
        counts = (bs, bd, cs, cd);
        return true;
    }

    private static bool TryCount(string description, string labelPattern, out int count)
    {
        Match match = Regex.Match(description, labelPattern + @"\s*(?<count>\d+)\s*×");
        return int.TryParse(match.Groups["count"].Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out count);
    }

    private static string BuildCopyDescription(string baseDescription, string category,
        (int BlackSimple, int BlackDuplex, int ColorSimple, int ColorDuplex) c,
        (decimal BlackSimple, decimal BlackDuplex, decimal ColorSimple, decimal ColorDuplex) p,
        decimal discountAmount, decimal discountPercent)
    {
        static string F(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
        return $"{baseDescription} | فئة: {category} | " +
               $"أسود وش: {c.BlackSimple} × {F(p.BlackSimple)} | " +
               $"أسود وش وظهر: {c.BlackDuplex} × {F(p.BlackDuplex)} | " +
               $"ألوان وش: {c.ColorSimple} × {F(p.ColorSimple)} | " +
               $"ألوان وش وظهر: {c.ColorDuplex} × {F(p.ColorDuplex)}" +
               (discountAmount > 0m ? $" | خصم: {F(discountAmount)} ({F(discountPercent)}%)" : "");
    }

    private static void AdjustCopyDebt(SqliteConnection connection, SqliteTransaction tx,
        long transactionId, long? customerId, decimal oldRemaining, decimal newRemaining, string note)
    {
        var rows = new List<(long Id, decimal Amount)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT Id,Amount FROM Debts_History WHERE TransactionId=$id AND Type='DEBT';";
            read.Parameters.AddWithValue("$id", transactionId);
            using var r = read.ExecuteReader();
            while (r.Read()) rows.Add((r.GetInt64(0), ToDecimal(r.GetValue(1))));
        }
        if (rows.Count > 1)
            throw new InvalidOperationException("تم العثور على أكثر من قيد دين لعملية التصوير نفسها؛ أُوقف تعديل السعر للمراجعة اليدوية.");
        if (rows.Count == 1 && Math.Abs(rows[0].Amount - oldRemaining) > 0.02m)
            throw new InvalidOperationException("قيمة قيد الدين لا تطابق الدين المسجل داخل عملية التصوير؛ لم يتم تعديل أي مبلغ.");
        if (newRemaining > Epsilon && !customerId.HasValue)
            throw new InvalidOperationException("لا يمكن تسجيل الدين الجديد لأن العملية لا تحتوي على عميل.");

        if (rows.Count == 1)
        {
            using var write = connection.CreateCommand();
            write.Transaction = tx;
            if (newRemaining <= Epsilon)
                write.CommandText = "DELETE FROM Debts_History WHERE Id=$id;";
            else
            {
                write.CommandText = "UPDATE Debts_History SET Amount=$amount,Note=$note WHERE Id=$id;";
                write.Parameters.AddWithValue("$amount", newRemaining);
                write.Parameters.AddWithValue("$note", "دين تصوير: " + note);
            }
            write.Parameters.AddWithValue("$id", rows[0].Id);
            write.ExecuteNonQuery();
        }
    }

    private static List<(long LinkId, long DebtId, decimal Amount)> ReadPrintDebtLinks(
        SqliteConnection connection, SqliteTransaction tx, long printLogId)
    {
        var rows = new List<(long LinkId, long DebtId, decimal Amount)>();
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT Id,DebtId,Amount FROM PrintDebtLinks WHERE PrintLogId=$id ORDER BY Id;";
        command.Parameters.AddWithValue("$id", printLogId);
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetInt64(1), ToDecimal(reader.GetValue(2))));
        return rows;
    }

    private static void AdjustPrintDebtLinks(SqliteConnection connection, SqliteTransaction tx,
        long printLogId, List<(long LinkId, long DebtId, decimal Amount)> oldLinks, decimal newDebt)
    {
        if (oldLinks.Count == 0)
        {
            if (newDebt > Epsilon)
                throw new InvalidOperationException("لا يوجد عميل/قيد دين مرتبط بعملية الطباعة؛ لا يمكن تحويل الزيادة إلى دين تلقائيًا.");
            return;
        }

        decimal oldTotal = oldLinks.Sum(x => x.Amount);
        var newAmounts = new decimal[oldLinks.Count];
        decimal allocated = 0m;
        for (int i = 0; i < oldLinks.Count; i++)
        {
            decimal amount;
            if (i == oldLinks.Count - 1) amount = Money(newDebt - allocated);
            else if (oldTotal > Epsilon) amount = Money(newDebt * oldLinks[i].Amount / oldTotal);
            else amount = i == 0 ? newDebt : 0m;
            amount = Math.Max(0m, amount);
            newAmounts[i] = amount;
            allocated += amount;
        }

        var debtDeltas = new Dictionary<long, decimal>();
        for (int i = 0; i < oldLinks.Count; i++)
        {
            var link = oldLinks[i];
            debtDeltas[link.DebtId] = debtDeltas.TryGetValue(link.DebtId, out decimal previous)
                ? previous + newAmounts[i] - link.Amount
                : newAmounts[i] - link.Amount;

            using var updateLink = connection.CreateCommand();
            updateLink.Transaction = tx;
            if (newAmounts[i] <= Epsilon)
                updateLink.CommandText = "DELETE FROM PrintDebtLinks WHERE Id=$id;";
            else
            {
                updateLink.CommandText = "UPDATE PrintDebtLinks SET Amount=$amount WHERE Id=$id;";
                updateLink.Parameters.AddWithValue("$amount", newAmounts[i]);
            }
            updateLink.Parameters.AddWithValue("$id", link.LinkId);
            updateLink.ExecuteNonQuery();
        }

        var customers = new HashSet<long>();
        foreach (var item in debtDeltas)
        {
            long customerId;
            decimal oldDebtAmount;
            using (var readDebt = connection.CreateCommand())
            {
                readDebt.Transaction = tx;
                readDebt.CommandText = "SELECT CustomerId,Amount FROM Debts_History WHERE Id=$id AND Type='DEBT' LIMIT 1;";
                readDebt.Parameters.AddWithValue("$id", item.Key);
                using var r = readDebt.ExecuteReader();
                if (!r.Read())
                    throw new InvalidOperationException("قيد الدين المرتبط بالطباعة غير موجود؛ أُوقف التعديل لحماية حساب العميل.");
                customerId = r.GetInt64(0);
                oldDebtAmount = ToDecimal(r.GetValue(1));
            }
            using (var validateLinks = connection.CreateCommand())
            {
                validateLinks.Transaction = tx;
                validateLinks.CommandText = "SELECT COALESCE(SUM(Amount),0) FROM PrintDebtLinks WHERE DebtId=$id;";
                validateLinks.Parameters.AddWithValue("$id", item.Key);
                decimal linkedAmount = ToDecimal(validateLinks.ExecuteScalar() ?? 0m);
                if (Math.Abs(linkedAmount - oldDebtAmount) > 0.02m)
                    throw new InvalidOperationException("روابط قيد الدين لا تطابق قيمة الدين المسجلة؛ أُوقف تعديل السعر لحماية حساب العميل.");
            }

            decimal updatedDebt = Money(oldDebtAmount + item.Value);
            if (updatedDebt < -Epsilon)
                throw new InvalidOperationException("تعديل السعر سيجعل قيد الدين أقل من الصفر.");
            if (updatedDebt <= Epsilon)
            {
                using var remainingLinks = connection.CreateCommand();
                remainingLinks.Transaction = tx;
                remainingLinks.CommandText = "SELECT COUNT(*) FROM PrintDebtLinks WHERE DebtId=$id;";
                remainingLinks.Parameters.AddWithValue("$id", item.Key);
                long count = Convert.ToInt64(remainingLinks.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
                if (count > 0)
                    throw new InvalidOperationException("قيد الدين مرتبط بعمليات طباعة أخرى؛ لا يمكن تصفيره بهذه العملية وحدها.");
                using var deleteDebt = connection.CreateCommand();
                deleteDebt.Transaction = tx;
                deleteDebt.CommandText = "DELETE FROM Debts_History WHERE Id=$id;";
                deleteDebt.Parameters.AddWithValue("$id", item.Key);
                deleteDebt.ExecuteNonQuery();
            }
            else
            {
                using var updateDebt = connection.CreateCommand();
                updateDebt.Transaction = tx;
                updateDebt.CommandText = "UPDATE Debts_History SET Amount=$amount WHERE Id=$id;";
                updateDebt.Parameters.AddWithValue("$amount", updatedDebt);
                updateDebt.Parameters.AddWithValue("$id", item.Key);
                updateDebt.ExecuteNonQuery();
            }
            customers.Add(customerId);
        }

        foreach (long customerId in customers)
        {
            decimal balance = SalesService.RecomputeBalances(connection, tx, customerId);
            if (balance < -Epsilon)
                throw new InvalidOperationException("العميل سدّد بالفعل مبلغًا يجعل تخفيض الدين الجديد غير ممكن دون رصيد سالب. لم يتم اعتماد أي تغيير.");
        }
    }

    private static decimal SumIncoming(SqliteConnection connection, SqliteTransaction tx,
        string referenceType, long referenceId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"SELECT COALESCE(SUM(Amount),0) FROM TreasuryMovements
WHERE ReferenceType=$type AND ReferenceId=$id AND MovementType='IN';";
        command.Parameters.AddWithValue("$type", referenceType);
        command.Parameters.AddWithValue("$id", referenceId);
        return ToDecimal(command.ExecuteScalar() ?? 0m);
    }

    private static void SetIncomingAmount(SqliteConnection connection, SqliteTransaction tx,
        string referenceType, long referenceId, decimal amount, string category,
        string description, string actor, string accountKey)
    {
        var movements = new List<(long Id, decimal Amount)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = @"SELECT Id,Amount FROM TreasuryMovements
WHERE ReferenceType=$type AND ReferenceId=$id AND MovementType='IN' ORDER BY CreatedAt,Id;";
            read.Parameters.AddWithValue("$type", referenceType);
            read.Parameters.AddWithValue("$id", referenceId);
            using var r = read.ExecuteReader();
            while (r.Read()) movements.Add((r.GetInt64(0), ToDecimal(r.GetValue(1))));
        }

        amount = Money(Math.Max(0m, amount));
        if (movements.Count == 0)
        {
            if (amount > Epsilon)
                AppDatabase.AddTreasury(connection, actor, "IN", category, amount,
                    description, referenceType, referenceId, tx, accountKey);
            return;
        }

        decimal oldTotal = movements.Sum(x => x.Amount);
        decimal allocated = 0m;
        for (int i = 0; i < movements.Count; i++)
        {
            decimal next;
            if (i == movements.Count - 1) next = Money(amount - allocated);
            else if (oldTotal > Epsilon) next = Money(amount * movements[i].Amount / oldTotal);
            else next = i == 0 ? amount : 0m;
            next = Math.Max(0m, next);
            allocated += next;

            using var update = connection.CreateCommand();
            update.Transaction = tx;
            if (next <= Epsilon)
                update.CommandText = "DELETE FROM TreasuryMovements WHERE Id=$id;";
            else
            {
                update.CommandText = "UPDATE TreasuryMovements SET Amount=$amount,Description=$description WHERE Id=$id;";
                update.Parameters.AddWithValue("$amount", next);
                update.Parameters.AddWithValue("$description", description ?? string.Empty);
            }
            update.Parameters.AddWithValue("$id", movements[i].Id);
            update.ExecuteNonQuery();
        }
    }

    private static decimal ToDecimal(object value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
