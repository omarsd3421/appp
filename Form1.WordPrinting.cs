using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LibraryPrintApp
{
    public partial class Form1
    {
        // ============================================================
        // Word -> PDF conversion used by the preview/printing pipeline.
        // Uses late binding so the project does not need a compile-time
        // reference to Microsoft.Office.Interop.Word.
        // Requires Microsoft Word to be installed on the PC.
        // ============================================================
        private static string ConvertWordToPdf(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("ملف الوورد غير موجود.", filePath);

            string directory = Path.Combine(
                Path.GetTempPath(),
                "LibraryPrintApp",
                "WordToPdf");
            Directory.CreateDirectory(directory);

            string outputPath = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(filePath) + "_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" +
                Guid.NewGuid().ToString("N") + ".pdf");

            Type? wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType == null)
            {
                throw new InvalidOperationException(
                    "تعذر العثور على Microsoft Word على هذا الجهاز.\n" +
                    "يجب تثبيت Microsoft Word لدعم معاينة وطباعة ملفات Word.");
            }

            object? wordApp = null;
            object? documents = null;
            object? document = null;

            try
            {
                wordApp = Activator.CreateInstance(wordType);
                if (wordApp == null)
                    throw new InvalidOperationException("تعذر تشغيل Microsoft Word.");

                ComSet(wordApp, "Visible", false);
                ComSet(wordApp, "DisplayAlerts", 0); // wdAlertsNone

                documents = ComGet(wordApp, "Documents");
                if (documents == null)
                    throw new InvalidOperationException("تعذر الوصول إلى مستندات Microsoft Word.");

                // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles)
                document = ComInvoke(
                    documents,
                    "Open",
                    filePath,
                    false,
                    true,
                    false);

                if (document == null)
                    throw new InvalidOperationException("تعذر فتح ملف الوورد داخل Microsoft Word.");

                // ExportAsFixedFormat(OutputFileName, ExportFormat)
                // wdExportFormatPDF = 17
                ComInvoke(document, "ExportAsFixedFormat", outputPath, 17);
            }
            finally
            {
                if (document != null)
                {
                    try { ComInvoke(document, "Close", false); } catch { }
                    ReleaseCom(document);
                }

                if (documents != null)
                    ReleaseCom(documents);

                if (wordApp != null)
                {
                    try { ComInvoke(wordApp, "Quit", false); } catch { }
                    ReleaseCom(wordApp);
                }
            }

            if (!File.Exists(outputPath))
                throw new InvalidOperationException("فشل تحويل ملف الوورد إلى PDF.");

            return outputPath;
        }

        private static object? ComInvoke(object target, string method, params object[] args)
        {
            return target.GetType().InvokeMember(
                method,
                BindingFlags.InvokeMethod,
                binder: null,
                target,
                args);
        }

        private static object? ComGet(object target, string property, params object[] args)
        {
            return target.GetType().InvokeMember(
                property,
                BindingFlags.GetProperty,
                binder: null,
                target,
                args);
        }

        private static void ComSet(object target, string property, object value)
        {
            target.GetType().InvokeMember(
                property,
                BindingFlags.SetProperty,
                binder: null,
                target,
                new object[] { value });
        }

        private static void ReleaseCom(object value)
        {
            try
            {
                if (Marshal.IsComObject(value))
                    Marshal.ReleaseComObject(value);
            }
            catch
            {
                // Best-effort COM cleanup.
            }
        }
    }
}
