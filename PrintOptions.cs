using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LibraryPrintApp
{
    internal enum PrintPageRangeMode
    {
        AllPages,
        Range
    }

    internal enum PrintPageOrientation
    {
        Auto,
        Portrait,
        Landscape
    }

    internal enum DuplexBindingEdge
    {
        LongEdge,
        ShortEdge
    }

    internal enum PrintScalingMode
    {
        FitToPrintableArea,
        ActualSize,
        Custom
    }

    internal sealed class PrintOptions
    {
        public PrintPageRangeMode PageRangeMode { get; set; } = PrintPageRangeMode.AllPages;

        // الاحتفاظ بهذين الحقلين للتوافق مع الإصدارات السابقة.
        // عند استخدام نطاقات متعددة يتم الاعتماد على PageRangesText.
        public int FromPage { get; set; } = 1;
        public int ToPage { get; set; } = 1;

        // صيغة مشابهة لبرامج PDF:
        // 1-5,8-11,89-98
        // ويمكن أيضًا كتابة صفحات مفردة: 1,4,9-12
        public string PageRangesText { get; set; } = "";

        public int PagesPerSheet { get; set; } = 1;
        // Physical gap between reduced pages when printing multiple pages per sheet.
        public float NUpGapInches { get; set; } = 0.200f;
        public PrintPageOrientation Orientation { get; set; } = PrintPageOrientation.Auto;
        // Long edge = التقليب من الجنب (الافتراضي)، Short edge = التقليب من فوق.
        public DuplexBindingEdge DuplexBinding { get; set; } = DuplexBindingEdge.LongEdge;
        public PrintScalingMode Scaling { get; set; } = PrintScalingMode.FitToPrintableArea;
        public float CustomScalePercent { get; set; } = 100f;

        public float MarginLeftMm { get; set; }
        public float MarginTopMm { get; set; }
        public float MarginRightMm { get; set; }
        public float MarginBottomMm { get; set; }

        public bool CenterHorizontal { get; set; } = true;
        public bool CenterVertical { get; set; } = true;

        public string PaperName { get; set; } = "افتراضي";

        // لقطة أسعار الوحدات وقت تسجيل الطباعة، حتى يمكن إعادة تسعير الفئة لاحقًا
        // دون الاعتماد على أسعار الإدارة الحالية التي قد تكون تغيرت.
        public decimal? PriceBlackSimpleSnapshot { get; set; }
        public decimal? PriceBlackDuplexSnapshot { get; set; }
        public decimal? PriceColorSimpleSnapshot { get; set; }
        public decimal? PriceColorDuplexSnapshot { get; set; }

        public PrintOptions Clone()
        {
            return new PrintOptions
            {
                PageRangeMode = PageRangeMode,
                FromPage = FromPage,
                ToPage = ToPage,
                PageRangesText = PageRangesText,
                PagesPerSheet = PagesPerSheet,
                NUpGapInches = NUpGapInches,
                Orientation = Orientation,
                DuplexBinding = DuplexBinding,
                Scaling = Scaling,
                CustomScalePercent = CustomScalePercent,
                MarginLeftMm = MarginLeftMm,
                MarginTopMm = MarginTopMm,
                MarginRightMm = MarginRightMm,
                MarginBottomMm = MarginBottomMm,
                CenterHorizontal = CenterHorizontal,
                CenterVertical = CenterVertical,
                PaperName = PaperName,
                PriceBlackSimpleSnapshot = PriceBlackSimpleSnapshot,
                PriceBlackDuplexSnapshot = PriceBlackDuplexSnapshot,
                PriceColorSimpleSnapshot = PriceColorSimpleSnapshot,
                PriceColorDuplexSnapshot = PriceColorDuplexSnapshot
            };
        }

        /// <summary>
        /// توقيع نصي لكل الإعدادات المؤثرة على شكل المعاينة — يُستخدم كمفتاح للتخزين المؤقت.
        /// </summary>
        public string CacheSignature()
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            return string.Join(
                "|",
                (int)PageRangeMode,
                FromPage.ToString(ic),
                ToPage.ToString(ic),
                PageRangesText ?? "",
                PagesPerSheet.ToString(ic),
                NUpGapInches.ToString("R", ic),
                (int)Orientation,
                (int)DuplexBinding,
                (int)Scaling,
                CustomScalePercent.ToString("R", ic),
                MarginLeftMm.ToString("R", ic),
                MarginTopMm.ToString("R", ic),
                MarginRightMm.ToString("R", ic),
                MarginBottomMm.ToString("R", ic),
                CenterHorizontal ? "1" : "0",
                CenterVertical ? "1" : "0",
                PaperName ?? "");
        }

        public void ClampToPageCount(int pageCount)
        {
            int safePageCount = Math.Max(1, pageCount);
            FromPage = Math.Clamp(FromPage, 1, safePageCount);
            ToPage = Math.Clamp(ToPage, 1, safePageCount);

            if (FromPage > ToPage)
                (FromPage, ToPage) = (ToPage, FromPage);

            PagesPerSheet = PagesPerSheet switch
            {
                2 or 4 or 6 or 9 or 16 => PagesPerSheet,
                _ => 1
            };

            NUpGapInches = Math.Clamp(NUpGapInches, 0f, 2f);
            CustomScalePercent = Math.Clamp(CustomScalePercent, 10f, 500f);
            MarginLeftMm = Math.Clamp(MarginLeftMm, 0f, 50f);
            MarginTopMm = Math.Clamp(MarginTopMm, 0f, 50f);
            MarginRightMm = Math.Clamp(MarginRightMm, 0f, 50f);
            MarginBottomMm = Math.Clamp(MarginBottomMm, 0f, 50f);

            if (Scaling != PrintScalingMode.Custom)
                CustomScalePercent = 100f;
        }

        public List<int> GetSelectedPageIndexes(int pageCount)
        {
            int safePageCount = Math.Max(1, pageCount);

            if (PageRangeMode == PrintPageRangeMode.AllPages)
                return Enumerable.Range(0, safePageCount).ToList();

            if (!PageRangeParser.TryParse(
                    PageRangesText,
                    safePageCount,
                    out List<int> indexes,
                    out _))
            {
                return new List<int>();
            }

            return indexes;
        }

        public int GetSelectedPageCount(int pageCount)
        {
            return GetSelectedPageIndexes(pageCount).Count;
        }
    }

    internal static class PageRangeParser
    {
        public static bool TryParse(
            string? text,
            int pageCount,
            out List<int> pageIndexes,
            out string error)
        {
            pageIndexes = new List<int>();
            error = "";

            int safePageCount = Math.Max(1, pageCount);
            string input = NormalizeDigitsAndSeparators(text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "اكتب نطاقات الصفحات، مثل: 1-5,8-11,89-98";
                return false;
            }

            string[] parts = input.Split(
                new[] { ',', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length == 0)
            {
                error = "لم يتم العثور على نطاق صفحات صالح.";
                return false;
            }

            HashSet<int> seen = new HashSet<int>();

            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Length == 0)
                    continue;

                string normalized = part
                    .Replace("–", "-")
                    .Replace("—", "-")
                    .Replace("−", "-")
                    .Replace("ـ", "-")
                    .Trim();

                string[] bounds = normalized.Split(
                    new[] { '-' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (bounds.Length == 1)
                {
                    if (!TryParsePositivePage(bounds[0], safePageCount, out int page, out error, part))
                        return false;

                    AddPage(page, seen, pageIndexes);
                    continue;
                }

                if (bounds.Length != 2)
                {
                    error = $"النطاق غير صالح: {part}";
                    return false;
                }

                if (!TryParsePositivePage(bounds[0], safePageCount, out int from, out error, part))
                    return false;

                if (!TryParsePositivePage(bounds[1], safePageCount, out int to, out error, part))
                    return false;

                if (from > to)
                    (from, to) = (to, from);

                for (int page = from; page <= to; page++)
                    AddPage(page, seen, pageIndexes);
            }

            if (pageIndexes.Count == 0)
            {
                error = "لم يتم العثور على صفحات للطباعة.";
                return false;
            }

            return true;
        }

        private static bool TryParsePositivePage(
            string value,
            int pageCount,
            out int page,
            out string error,
            string originalPart)
        {
            page = 0;
            error = "";

            if (!int.TryParse(
                    value.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out page) || page < 1)
            {
                error = $"رقم الصفحة غير صالح في: {originalPart}";
                return false;
            }

            if (page > pageCount)
            {
                error = $"الصفحة {page} غير موجودة. عدد صفحات الملف هو {pageCount}.";
                return false;
            }

            return true;
        }

        private static void AddPage(
            int oneBasedPage,
            HashSet<int> seen,
            List<int> result)
        {
            int zeroBased = oneBasedPage - 1;
            if (seen.Add(zeroBased))
                result.Add(zeroBased);
        }

        private static string NormalizeDigitsAndSeparators(string value)
        {
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] switch
                {
                    '٠' => '0',
                    '١' => '1',
                    '٢' => '2',
                    '٣' => '3',
                    '٤' => '4',
                    '٥' => '5',
                    '٦' => '6',
                    '٧' => '7',
                    '٨' => '8',
                    '٩' => '9',
                    '،' => ',',
                    '؛' => ';',
                    _ => chars[i]
                };
            }

            return new string(chars);
        }
    }
}
