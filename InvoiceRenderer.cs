using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace LibraryPrintApp;

/// <summary>
/// رسم الفاتورة. كل الإحداثيات بالمليمتر، والفاتورة الواحدة بمقاس A5 طولي.
/// - الطباعة: ورقة A4 أفقية فيها نسختين جنب بعض بينهم فاصل 0.2 إنش.
/// - الـPDF: ورقة A5 واحدة (أو أكتر لو الفاتورة طويلة).
/// </summary>
internal static class InvoiceRenderer
{
    public static string LibraryName => AppOptions.Data.ShopName;
    public static string Phones => AppOptions.Data.Phones;
    public static string Address => AppOptions.Data.Address;

    public const float A5Width = 148f;
    public const float A5Height = 210f;

    // مقاس A4 الأفقي وفاصل 0.2 إنش بين الفاتورتين.
    private const float A4Width = 297f;
    private const float A4Height = 210f;
    private const float GapMm = 0.2f * 25.4f;

    private const float Margin = 8f;
    private const float BodyTop = 44f;
    private const float BodyBottom = 186f;
    private const float BodyHeight = BodyBottom - BodyTop;
    private const float TotalsHeight = 19f;

    private enum ItemKind { Title, Header, Row }
    private enum Table { Sales, Print }

    private sealed class Item
    {
        public ItemKind Kind;
        public Table Table;
        public int Index;
        public string Text = "";
        public float Height;
    }

    // ------------------------------------------------------------
    // التقسيم على صفحات
    // ------------------------------------------------------------
    private static List<Item> BuildItems(InvoiceData d, float s)
    {
        bool both = d.Sales.Count > 0 && d.Prints.Count > 0;
        var items = new List<Item>();

        if (d.Sales.Count > 0)
        {
            if (both) items.Add(new Item { Kind = ItemKind.Title, Table = Table.Sales, Text = "المبيعات", Height = 7.5f * s });
            items.Add(new Item { Kind = ItemKind.Header, Table = Table.Sales, Height = 7.5f * s });
            for (int i = 0; i < d.Sales.Count; i++)
                items.Add(new Item { Kind = ItemKind.Row, Table = Table.Sales, Index = i, Height = 7f * s });
        }

        if (d.Prints.Count > 0)
        {
            if (both) items.Add(new Item { Kind = ItemKind.Title, Table = Table.Print, Text = "الطباعة والتصوير", Height = 7.5f * s });
            items.Add(new Item { Kind = ItemKind.Header, Table = Table.Print, Height = 7.5f * s });
            for (int i = 0; i < d.Prints.Count; i++)
                items.Add(new Item { Kind = ItemKind.Row, Table = Table.Print, Index = i, Height = 7f * s });
        }

        return items;
    }

    private static List<List<Item>> Layout(InvoiceData d, out float scale)
    {
        // نصغّر الخط والصفوف تدريجيًا (حتى 70%) عشان الفاتورة تتلم في صفحة واحدة.
        for (int step = 0; step <= 6; step++)
        {
            scale = 1f - step * 0.05f;
            var items = BuildItems(d, scale);
            if (items.Sum(i => i.Height) + TotalsHeight <= BodyHeight)
                return new List<List<Item>> { items };
        }

        // لو لسه مش داخلة: نكمل على صفحة تانية بنفس الحجم الأدنى.
        scale = 0.75f;
        var all = BuildItems(d, scale);
        var pages = new List<List<Item>>();
        var current = new List<Item>();
        float used = 0f;

        foreach (var item in all)
        {
            if (used + item.Height > BodyHeight + 0.01f && current.Count > 0)
            {
                pages.Add(current);
                current = new List<Item>();
                used = 0f;
                if (item.Kind == ItemKind.Row)
                {
                    var header = new Item { Kind = ItemKind.Header, Table = item.Table, Height = 7.5f * scale };
                    current.Add(header);
                    used += header.Height;
                }
            }
            current.Add(item);
            used += item.Height;
        }

        if (used + TotalsHeight > BodyHeight)
        {
            pages.Add(current);
            current = new List<Item>();
        }
        pages.Add(current);
        return pages;
    }

    public static int PageCount(InvoiceData d) => Layout(d, out _).Count;

    // ------------------------------------------------------------
    // أدوات الرسم
    // ------------------------------------------------------------
    private static Font MakeFont(float mm, bool bold = false)
        => new Font("Segoe UI", mm, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Millimeter);

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

        // نصغّر الخط لحد حد أدنى لو النص أطول من الخانة.
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

    private static float[] SalesColumns(float width)
    {
        // من اليمين: م | البيان | سعر الوحدة | العدد | الإجمالي
        float[] fixedCols = { 9f, 0f, 24f, 14f, 26f };
        fixedCols[1] = width - (9f + 24f + 14f + 26f);
        return fixedCols;
    }

    private static float[] PrintColumns(float width)
    {
        // من اليمين: م | البيان | عدد الصفحات | عدد الأوراق | سعر الورقة | الإجمالي
        float[] cols = { 9f, 0f, 17f, 17f, 19f, 22f };
        cols[1] = width - (9f + 17f + 17f + 19f + 22f);
        return cols;
    }

    private static void DrawCells(Graphics g, float right, float y, float height, float[] widths,
        string[] cells, float fontSize, bool header, Pen pen, Brush text, Brush headFill)
    {
        for (int i = 0; i < widths.Length; i++)
        {
            var cell = new RectangleF(right - widths[i], y, widths[i], height);
            if (header) g.FillRectangle(headFill, cell);
            g.DrawRectangle(pen, cell.X, cell.Y, cell.Width, cell.Height);
            DrawText(g, cells[i], fontSize, header, text, cell,
                i == 1 ? StringAlignment.Near : StringAlignment.Center);
            right -= widths[i];
        }
    }

    // ------------------------------------------------------------
    // رسم صفحة فاتورة واحدة داخل مستطيل (بالمليمتر)
    // ------------------------------------------------------------
    public static void DrawPage(Graphics g, InvoiceData d, int pageIndex, RectangleF area)
    {
        var pages = Layout(d, out float scale);
        if (pageIndex < 0 || pageIndex >= pages.Count) return;

        var state = g.Save();
        try
        {
            g.TranslateTransform(area.X, area.Y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float left = Margin;
            float width = area.Width - Margin * 2f;
            float right = left + width;

            using var black = new SolidBrush(Color.Black);
            using var gray = new SolidBrush(Color.FromArgb(90, 90, 90));
            using var headFill = new SolidBrush(Color.FromArgb(232, 232, 232));
            using var totalFill = new SolidBrush(Color.FromArgb(240, 240, 240));
            using var thin = new Pen(Color.FromArgb(60, 60, 60), 0.2f);
            using var thick = new Pen(Color.Black, 0.45f);

            // ---------- الترويسة ----------
            DrawText(g, LibraryName, 7.4f, true, black, new RectangleF(left, 7f, width, 11f), StringAlignment.Center);
            DrawText(g, "هاتف: " + Phones, 3.4f, false, black, new RectangleF(left, 18.5f, width, 6f), StringAlignment.Center);
            g.DrawLine(thick, left, 25.5f, right, 25.5f);

            string number = d.Number > 0 ? d.Number.ToString("D4", CultureInfo.InvariantCulture) : "—";
            // رقم الفاتورة على اليمين والتاريخ على الشمال.
            DrawText(g, "رقم الفاتورة: " + number, 3.5f, true, black,
                new RectangleF(left + width / 2f, 27.5f, width / 2f, 6f), StringAlignment.Near);
            DrawText(g, "التاريخ: " + d.Date.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture), 3.2f, false, black,
                new RectangleF(left, 27.5f, width / 2f, 6f), StringAlignment.Far);
            string customer = string.IsNullOrWhiteSpace(d.CustomerName) ? "—" : d.CustomerName.Trim();
            DrawText(g, "العميل: " + customer, 3.6f, true, black, new RectangleF(left, 34f, width, 6.2f), StringAlignment.Near);
            g.DrawLine(thin, left, 41f, right, 41f);

            if (pages.Count > 1)
                DrawText(g, $"صفحة {pageIndex + 1} من {pages.Count}", 2.8f, false, gray,
                    new RectangleF(left, 1.5f, width, 5f), StringAlignment.Far);

            // ---------- الجداول ----------
            float y = BodyTop;
            float fontSize = 3.1f * scale;
            float[] salesCols = SalesColumns(width);
            float[] printCols = PrintColumns(width);

            foreach (var item in pages[pageIndex])
            {
                switch (item.Kind)
                {
                    case ItemKind.Title:
                        DrawText(g, item.Text, 3.6f * scale, true, black, new RectangleF(left, y, width, item.Height), StringAlignment.Near);
                        break;

                    case ItemKind.Header:
                        if (item.Table == Table.Sales)
                            DrawCells(g, right, y, item.Height, salesCols,
                                new[] { "م", "البيان", "سعر الوحدة", "العدد", "الإجمالي" },
                                fontSize, true, thin, black, headFill);
                        else
                            DrawCells(g, right, y, item.Height, printCols,
                                new[] { "م", "البيان", "عدد الصفحات", "عدد الأوراق", "سعر الورقة", "الإجمالي" },
                                fontSize, true, thin, black, headFill);
                        break;

                    case ItemKind.Row:
                        if (item.Table == Table.Sales)
                        {
                            var line = d.Sales[item.Index];
                            DrawCells(g, right, y, item.Height, salesCols,
                                new[]
                                {
                                    (item.Index + 1).ToString(CultureInfo.InvariantCulture),
                                    line.Name,
                                    OperationText.Money(line.UnitPrice),
                                    line.Quantity.ToString(CultureInfo.InvariantCulture),
                                    OperationText.Money(line.Total)
                                },
                                fontSize, false, thin, black, headFill);
                        }
                        else
                        {
                            var line = d.Prints[item.Index];
                            DrawCells(g, right, y, item.Height, printCols,
                                new[]
                                {
                                    (item.Index + 1).ToString(CultureInfo.InvariantCulture),
                                    line.Name,
                                    line.Pages.ToString(CultureInfo.InvariantCulture),
                                    line.Sheets.ToString(CultureInfo.InvariantCulture),
                                    OperationText.Money(line.SheetPrice),
                                    OperationText.Money(line.Total)
                                },
                                fontSize, false, thin, black, headFill);
                        }
                        break;
                }
                y += item.Height;
            }

            // ---------- الإجماليات (آخر صفحة فقط) ----------
            if (pageIndex == pages.Count - 1)
            {
                y += 3f;
                var totalRect = new RectangleF(left, y, width, 8f);
                g.FillRectangle(totalFill, totalRect);
                g.DrawRectangle(thick, totalRect.X, totalRect.Y, totalRect.Width, totalRect.Height);
                DrawText(g, "الإجمالي الكلي: " + OperationText.Money(d.Total) + " ج.م", 4.2f, true, black,
                    totalRect, StringAlignment.Center);
                y += 9f;

                DrawText(g, "المدفوع: " + OperationText.Money(d.Paid) + " ج.م", 3.5f, true, black,
                    new RectangleF(left + width / 2f, y, width / 2f, 6.5f), StringAlignment.Near);
                DrawText(g, "المتبقي: " + OperationText.Money(d.Remaining) + " ج.م", 3.5f, true, black,
                    new RectangleF(left, y, width / 2f, 6.5f), StringAlignment.Far);
            }

            // ---------- التذييل ----------
            g.DrawLine(thin, left, 189f, right, 189f);
            DrawText(g, "شكرًا لزيارتكم", 3.6f, true, black, new RectangleF(left, 190f, width, 6f), StringAlignment.Center);
            DrawText(g, "العنوان: " + Address, 2.9f, false, gray, new RectangleF(left, 196f, width, 6f), StringAlignment.Center);
        }
        finally
        {
            g.Restore(state);
        }
    }

    // ------------------------------------------------------------
    // صورة لصفحة واحدة (للمعاينة وللـPDF)
    // ------------------------------------------------------------
    public static Bitmap RenderPageBitmap(InvoiceData d, int pageIndex, int dpi)
    {
        int w = (int)Math.Round(A5Width / 25.4f * dpi);
        int h = (int)Math.Round(A5Height / 25.4f * dpi);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        bmp.SetResolution(dpi, dpi);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Millimeter;
        DrawPage(g, d, pageIndex, new RectangleF(0, 0, A5Width, A5Height));
        return bmp;
    }

    // ------------------------------------------------------------
    // حفظ PDF بمقاس A5
    // ------------------------------------------------------------
    public static void SavePdf(InvoiceData d, string path)
    {
        int count = PageCount(d);
        var temps = new List<string>();
        try
        {
            using var document = new PdfSharp.Pdf.PdfDocument();
            for (int i = 0; i < count; i++)
            {
                string temp = Path.Combine(Path.GetTempPath(), "invoice_" + Guid.NewGuid().ToString("N") + ".png");
                temps.Add(temp);
                using (var bmp = RenderPageBitmap(d, i, 220))
                    bmp.Save(temp, ImageFormat.Png);

                PdfPage page = document.AddPage();
                page.Width = XUnit.FromMillimeter(A5Width);
                page.Height = XUnit.FromMillimeter(A5Height);
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
    // الطباعة: A4 أفقي، نسختين من نفس الفاتورة جنب بعض بينهم 0.2 إنش
    // ------------------------------------------------------------
    public static PrintDocument CreateTwoUpDocument(InvoiceData d, string? printerName)
    {
        var doc = new PrintDocument { DocumentName = $"فاتورة {d.Number}" };
        if (!string.IsNullOrWhiteSpace(printerName))
            doc.PrinterSettings.PrinterName = printerName;

        doc.DefaultPageSettings.Landscape = true;
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
        int pageCount = PageCount(d);

        doc.BeginPrint += (s, e) => pageIndex = 0;
        doc.PrintPage += (s, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Millimeter;

            // نبدأ القياس من حافة الورقة الحقيقية مش من حافة المنطقة القابلة للطباعة.
            g.TranslateTransform(
                -e.PageSettings.HardMarginX * 0.254f,
                -e.PageSettings.HardMarginY * 0.254f);

            float half = (A4Width - GapMm) / 2f;
            var leftArea = new RectangleF(0, 0, half, A4Height);
            var rightArea = new RectangleF(half + GapMm, 0, half, A4Height);

            DrawPage(g, d, pageIndex, leftArea);
            DrawPage(g, d, pageIndex, rightArea);

            // خط قص خفيف في منتصف الفاصل.
            using var cut = new Pen(Color.FromArgb(170, 170, 170), 0.15f) { DashStyle = DashStyle.Dash };
            float mid = half + GapMm / 2f;
            g.DrawLine(cut, mid, 0f, mid, A4Height);

            pageIndex++;
            e.HasMorePages = pageIndex < pageCount;
        };
        return doc;
    }
}
