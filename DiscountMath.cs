using System;
using System.Globalization;

namespace LibraryPrintApp;

/// <summary>
/// حسابات الخصم المشتركة بين المبيعات والتصوير والطباعة.
/// الخصم ممكن يتكتب كنسبة (%) أو كمبلغ بالجنيه، وكل واحدة بتتحسب من التانية لحظيًا،
/// مع الالتزام بالحد الأقصى للنسبة المحدد من الإدارة.
/// </summary>
internal static class DiscountMath
{
    internal readonly record struct Result(decimal Amount, decimal Percent, bool Clamped);

    /// <summary>أكبر مبلغ خصم مسموح (مقرّب لأسفل لأقرب قرش حتى لا تزيد النسبة عن الحد).</summary>
    public static decimal MaxAmount(decimal gross, decimal maxPercent)
    {
        gross = Math.Max(0m, gross);
        maxPercent = Math.Min(100m, Math.Max(0m, maxPercent));
        return Math.Floor(gross * maxPercent) / 100m;
    }

    /// <summary>
    /// byAmount = true  → المستخدم كتب المبلغ، والنسبة بتتحسب منه.
    /// byAmount = false → المستخدم كتب النسبة، والمبلغ بيتحسب منها.
    /// Clamped = true لو القيمة المكتوبة تعدّت الحد الأقصى وتم تقليلها.
    /// </summary>
    public static Result Resolve(decimal gross, bool byAmount, decimal typedPercent, decimal typedAmount, decimal maxPercent)
    {
        gross = Math.Max(0m, gross);
        maxPercent = Math.Min(100m, Math.Max(0m, maxPercent));
        decimal cap = MaxAmount(gross, maxPercent);
        bool clamped = false;
        decimal amount;

        if (byAmount)
        {
            amount = Math.Max(0m, typedAmount);
            if (amount > cap) { amount = cap; clamped = true; }
            amount = Math.Round(amount, 2);
        }
        else
        {
            decimal pct = Math.Max(0m, typedPercent);
            if (pct > maxPercent) { pct = maxPercent; clamped = true; }
            amount = Math.Floor(gross * pct) / 100m;
            if (amount > cap) amount = cap;
        }

        decimal percent = gross > 0m ? amount / gross * 100m : 0m;
        return new Result(amount, percent, clamped);
    }

    public static bool IsWithinMax(decimal gross, decimal discountAmount, decimal maxPercent)
        => discountAmount <= MaxAmount(gross, maxPercent) + 0.0001m;

    /// <summary>قراءة رقم مكتوب (يدعم الأرقام العربية والفاصلة العشرية العربية).</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string normalized = text.Trim()
            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
            .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9')
            .Replace('٬', ',').Replace('٫', '.')
            .Replace(",", "");
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }
}
