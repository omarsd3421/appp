using System;
using System.IO;
using System.Reflection;

namespace LibraryPrintApp
{
    public partial class Form1
    {
        // ============================================================
        // PowerPoint -> PDF conversion used by the preview/printing pipeline.
        // Uses late binding so no compile-time Office reference is required.
        // Requires Microsoft PowerPoint to be installed on the PC.
        // ============================================================
        private static string ConvertPowerPointToPdf(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("ملف PowerPoint غير موجود.", filePath);

            string directory = Path.Combine(
                Path.GetTempPath(),
                "LibraryPrintApp",
                "PowerPointToPdf");
            Directory.CreateDirectory(directory);

            string outputPath = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(filePath) + "_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" +
                Guid.NewGuid().ToString("N") + ".pdf");

            Type? powerPointType = Type.GetTypeFromProgID("PowerPoint.Application");
            if (powerPointType == null)
            {
                throw new InvalidOperationException(
                    "تعذر العثور على Microsoft PowerPoint على هذا الجهاز.\n" +
                    "يجب تثبيت Microsoft PowerPoint لدعم معاينة وطباعة ملفات PowerPoint.");
            }

            object? app = null;
            object? presentations = null;
            object? presentation = null;

            try
            {
                app = Activator.CreateInstance(powerPointType);
                if (app == null)
                    throw new InvalidOperationException("تعذر تشغيل Microsoft PowerPoint.");

                ComSet(app, "Visible", true); // PowerPoint may reject hiding its application window.
                ComSet(app, "DisplayAlerts", 1); // ppAlertsNone

                presentations = ComGet(app, "Presentations");
                if (presentations == null)
                    throw new InvalidOperationException("تعذر الوصول إلى عروض PowerPoint.");

                // Open(FileName, ReadOnly, Untitled, WithWindow)
                presentation = ComInvoke(
                    presentations,
                    "Open",
                    filePath,
                    true,
                    false,
                    false);

                if (presentation == null)
                    throw new InvalidOperationException("تعذر فتح ملف PowerPoint.");

                // ppSaveAsPDF = 32
                ComInvoke(presentation, "SaveAs", outputPath, 32);
            }
            finally
            {
                if (presentation != null)
                {
                    try { ComInvoke(presentation, "Close"); } catch { }
                    ReleaseCom(presentation);
                }

                if (presentations != null)
                    ReleaseCom(presentations);

                if (app != null)
                {
                    try { ComInvoke(app, "Quit"); } catch { }
                    ReleaseCom(app);
                }
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                throw new InvalidOperationException("فشل تحويل ملف PowerPoint إلى PDF.");

            return outputPath;
        }

    }
}
