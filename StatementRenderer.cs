using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace LibraryPrintApp;

/// <summary> سطر واحد في كشف حساب العميل. </summary>
internal sealed record StatementLine(DateTime Date, string Type, decimal Amount, decimal Balance, string Note);

/// <summary>
/// رسم كشف حساب العميل على ورقة A4 طولي (كل الإحداثيات بالمليمتر).
/// - الطباعة: من نافذة المعاينة (فيها زر الطباعة).
/// - الـPDF: صفحة A4 لكل صفحة كشف.
/// </summary>
internal static class StatementRenderer
{
    private const float PageW = 210f;
    private const float PageH = 297f;
    private const float Margin = 12f;
    private const float RowH = 8f;
    private const float HeadH = 9f;
    private const float TableTop = 68f;
    private const float TableBottom = 262f;

    private static int RowsPerPage => (int)((TableBottom - TableTop - HeadH) / RowH);

    public static int PageCount(int rows) => Math.Max(1, (rows + RowsPerPage - 1) / RowsPerPage);

    private static Font MakeFont(float mm, bool bold = false)
        => new Font("Segoe UI", mm, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Millimeter);

    private static string Money(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);

    private static void DrawText(Graphics g, string text, float sizeMm, bool bold, Brush brush,
        RectangleF rect, StringAlignment alignment)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var format = new StringFormat(StringFormatFlags.DirectionRightToLeft | StringFormatFlags.NoWrap)
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        float size = sizeMm;
        float minSize = sizeMm * 0.62f;
        float available = Math.Max(1f, rect.Width - 1.2f);
        while (size > minSize)
        {
            using var probe = MakeFont(size, bold);
            float width = g.MeasureString(text, probe, new PointF(0, 0), format).Width;
            if (width <= available) break;
            size -= 0.1f;
        }

        using var font = MakeFont(size, bold);
        var inner = new RectangleF(rect.X + 0.6f, rect.Y, Math.Max(1f, rect.Width - 1.2f), rect.Height);
        g.DrawString(text, font, brush, inner, format);
    }

    private static float[] Columns(float width)
    {
        // من اليمين لليسار: التاريخ، النوع، المبلغ، الرصيد بعد الحركة، البيان (الباقي)
        float date = 34f, type = 24f, amount = 28f, balance = 30f;
        float note = width - date - type - amount - balance;
        return new[] { date, type, amount, balance, note };
    }

    private static void DrawCells(Graphics g, float right, float y, float height, float[] widths,
        string[] cells, float fontSize, bool header, Pen pen, Brush text, Brush fill, Brush? rowFill = null)
    {
        for (int i = 0; i < widths.Length; i++)
        {
            var cell = new RectangleF(right - widths[i], y, widths[i], height);
            if (header) g.FillRectangle(fill, cell);
            else if (rowFill != null) g.FillRectangle(rowFill, cell);
            g.DrawRectangle(pen, cell.X, cell.Y, cell.Width, cell.Height);
            DrawText(g, cells[i], fontSize, header, text, cell,
                i == 4 ? StringAlignment.Near : StringAlignment.Center);
            right -= widths[i];
        }
    }

    public static void DrawPage(Graphics g, string customerName, string phone, decimal debt,
        IReadOnlyList<StatementLine> lines, int pageIndex, int pageCount)
    {
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float left = Margin;
            float width = PageW - Margin * 2f;
            float right = left + width;

            using var black = new SolidBrush(Color.Black);
            using var gray = new SolidBrush(Color.FromArgb(90, 90, 90));
            using var red = new SolidBrush(Color.FromArgb(170, 40, 30));
            using var headFill = new SolidBrush(Color.FromArgb(226, 226, 226));
            using var payFill = new SolidBrush(Color.FromArgb(238, 249, 242));
            using var totalFill = new SolidBrush(Color.FromArgb(240, 240, 240));
            using var thin = new Pen(Color.FromArgb(60, 60, 60), 0.2f);
            using var thick = new Pen(Color.Black, 0.45f);

            // ---------- الترويسة ----------
            DrawText(g, InvoiceRenderer.LibraryName, 8.5f, true, black, new RectangleF(left, 9f, width, 12f), StringAlignment.Center);
            DrawText(g, "هاتف: " + InvoiceRenderer.Phones, 3.6f, false, black, new RectangleF(left, 21f, width, 6f), StringAlignment.Center);
            g.DrawLine(thick, left, 28.5f, right, 28.5f);

            DrawText(g, "كشف حساب عميل", 6f, true, black, new RectangleF(left, 31f, width, 10f), StringAlignment.Center);

            // اسم العميل يمين، التاريخ شمال
            string name = string.IsNullOrWhiteSpace(customerName) ? "—" : customerName.Trim();
            DrawText(g, "العميل: " + name, 4.2f, true, black,
                new RectangleF(left + width / 2f, 43f, width / 2f, 7f), StringAlignment.Near);
            DrawText(g, "تاريخ الكشف: " + DateTime.Now.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture), 3.4f, false, black,
                new RectangleF(left, 43f, width / 2f, 7f), StringAlignment.Far);
            string phoneText = string.IsNullOrWhiteSpace(phone) ? "غير مسجل" : phone.Trim();
            DrawText(g, "الرقم: " + phoneText, 3.8f, false, black,
                new RectangleF(left + width / 2f, 50f, width / 2f, 7f), StringAlignment.Near);

            // الدين الحالي في إطار
            var debtRect = new RectangleF(left, 52f, width / 2f - 6f, 9f);
            g.FillRectangle(totalFill, debtRect);
            g.DrawRectangle(thick, debtRect.X, debtRect.Y, debtRect.Width, debtRect.Height);
            DrawText(g, "الدين الحالي: " + Money(debt) + " ج.م", 4.4f, true, debt > 0m ? red : black,
                debtRect, StringAlignment.Center);

            g.DrawLine(thin, left, 63f, right, 63f);

            if (pageCount > 1)
                DrawText(g, $"صفحة {pageIndex + 1} من {pageCount}", 2.8f, false, gray,
                    new RectangleF(left, 3f, width, 5f), StringAlignment.Far);

            // ---------- الجدول ----------
            float[] cols = Columns(width);
            float y = TableTop;
            float fontSize = 3.3f;
            DrawCells(g, right, y, HeadH, cols,
                new[] { "التاريخ", "النوع", "المبلغ", "الرصيد بعد الحركة", "البيان" },
                fontSize, true, thin, black, headFill);
            y += HeadH;

            int first = pageIndex * RowsPerPage;
            int last = Math.Min(lines.Count, first + RowsPerPage);
            for (int i = first; i < last; i++)
            {
                var line = lines[i];
                bool isPayment = line.Type == "دفعة / سداد";
                DrawCells(g, right, y, RowH, cols,
                    new[]
                    {
                        line.Date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        line.Type,
                        Money(line.Amount),
                        Money(line.Balance),
                        line.Note
                    },
                    fontSize, false, thin, black, headFill, isPayment ? payFill : null);
                y += RowH;
            }

            if (lines.Count == 0)
                DrawText(g, "لا توجد حركات مسجلة لهذا العميل.", 3.6f, false, gray,
                    new RectangleF(left, y + 4f, width, 8f), StringAlignment.Center);

            // ---------- الإجمالي (آخر صفحة فقط) ----------
            if (pageIndex == pageCount - 1)
            {
                y += 4f;
                var totalRect = new RectangleF(left, y, width, 9f);
                g.FillRectangle(totalFill, totalRect);
                g.DrawRectangle(thick, totalRect.X, totalRect.Y, totalRect.Width, totalRect.Height);
                DrawText(g, "إجمالي الدين الحالي: " + Money(debt) + " ج.م", 4.6f, true, debt > 0m ? red : black,
                    totalRect, StringAlignment.Center);
            }

            // ---------- التذييل ----------
            g.DrawLine(thin, left, 283f, right, 283f);
            DrawText(g, "العنوان: " + InvoiceRenderer.Address, 3f, false, gray,
                new RectangleF(left, 284.5f, width, 6f), StringAlignment.Center);
        }
        finally
        {
            g.Restore(state);
        }
    }

    // ------------------------------------------------------------
    // صورة صفحة (للـPDF)
    // ------------------------------------------------------------
    private static Bitmap RenderPageBitmap(string name, string phone, decimal debt,
        IReadOnlyList<StatementLine> lines, int pageIndex, int pageCount, int dpi)
    {
        int w = (int)Math.Round(PageW / 25.4f * dpi);
        int h = (int)Math.Round(PageH / 25.4f * dpi);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        bmp.SetResolution(dpi, dpi);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Millimeter;
        DrawPage(g, name, phone, debt, lines, pageIndex, pageCount);
        return bmp;
    }

    public static void SavePdf(string name, string phone, decimal debt, IReadOnlyList<StatementLine> lines, string path)
    {
        int count = PageCount(lines.Count);
        var temps = new List<string>();
        try
        {
            using var document = new PdfSharp.Pdf.PdfDocument();
            for (int i = 0; i < count; i++)
            {
                string temp = Path.Combine(Path.GetTempPath(), "statement_" + Guid.NewGuid().ToString("N") + ".png");
                temps.Add(temp);
                using (var bmp = RenderPageBitmap(name, phone, debt, lines, i, count, 170))
                    bmp.Save(temp, ImageFormat.Png);

                PdfPage page = document.AddPage();
                page.Width = XUnit.FromMillimeter(PageW);
                page.Height = XUnit.FromMillimeter(PageH);
                using XGraphics gfx = XGraphics.FromPdfPage(page);
                using XImage image = XImage.FromFile(temp);
                gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            }
            document.Save(path);
        }
        finally
        {
            foreach (string temp in temps)
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    // ------------------------------------------------------------
    // الطباعة: A4 طولي
    // ------------------------------------------------------------
    public static PrintDocument CreateDocument(string name, string phone, decimal debt, IReadOnlyList<StatementLine> lines)
    {
        var doc = new PrintDocument { DocumentName = "كشف حساب " + name };
        doc.DefaultPageSettings.Landscape = false;
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        foreach (PaperSize size in doc.PrinterSettings.PaperSizes)
        {
            if (size.Kind == PaperKind.A4)
            {
                doc.DefaultPageSettings.PaperSize = size;
                break;
            }
        }

        int pageIndex = 0;
        int pageCount = PageCount(lines.Count);
        doc.BeginPrint += (s, e) => pageIndex = 0;
        doc.PrintPage += (s, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Millimeter;
            // نبدأ القياس من حافة الورقة الحقيقية مش من حافة المنطقة القابلة للطباعة.
            g.TranslateTransform(
                -e.PageSettings.HardMarginX * 0.254f,
                -e.PageSettings.HardMarginY * 0.254f);
            DrawPage(g, name, phone, debt, lines, pageIndex, pageCount);
            pageIndex++;
            e.HasMorePages = pageIndex < pageCount;
        };
        return doc;
    }
}
