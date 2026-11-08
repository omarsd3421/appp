using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// سجل الأخطاء: ملف يومي في %LocalAppData%\LibraryPrintApp\logs
/// وأي خطأ غير متوقع بيتسجل هنا بدل ما البرنامج يقفل من غير ما حد يعرف السبب.
/// </summary>
internal static class AppLog
{
    private static readonly object Sync = new();

    public static string? CurrentUser { get; set; }

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LibraryPrintApp", "logs");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string context, Exception? ex = null) => Write("ERROR", context, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] [").Append(level).Append("] ");
            if (!string.IsNullOrEmpty(CurrentUser)) sb.Append('[').Append(CurrentUser).Append("] ");
            sb.AppendLine(message);
            if (ex != null) sb.AppendLine(ex.ToString());
            sb.AppendLine();

            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log"),
                    sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // السجل نفسه لازم ما يوقفش البرنامج أبدًا.
        }
    }

    /// <summary> حذف ملفات السجل الأقدم من عدد الأيام المحدد. </summary>
    public static void CleanOld(int keepDays = 60)
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            foreach (string file in Directory.GetFiles(LogDirectory, "app-*.log"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-keepDays))
                    File.Delete(file);
        }
        catch { }
    }

    /// <summary> يركّب معالجات الأخطاء العامة (لازم قبل فتح أي نافذة). </summary>
    public static void InstallGlobalHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) =>
        {
            Error("خطأ غير متوقع في الواجهة", e.Exception);
            ShowFriendly(e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Error("خطأ قاتل غير معالج", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Error("خطأ غير معالج في مهمة خلفية", e.Exception);
            e.SetObserved();
        };

        CleanOld();
    }

    private static void ShowFriendly(Exception ex)
    {
        try
        {
            MessageBox.Show(
                "حصل خطأ غير متوقع، وتم تسجيله في سجل الأخطاء.\n" +
                "تقدر تكمل شغل، ولو اتكرر ابعت ملف السجل للمطوّر.\n\n" +
                "التفاصيل: " + ex.Message + "\n\nمكان السجل:\n" + LogDirectory,
                "خطأ غير متوقع",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
        catch { }
    }
}
