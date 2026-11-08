using System;
using System.IO;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// واجهة رفع واستعادة النسخة الاحتياطية (بتتستخدم من إعدادات الإدارة ومن شاشة الدخول).
/// </summary>
internal static class BackupRestoreUi
{
    private const MessageBoxOptions Rtl = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;

    /// <summary>
    /// يطلب ملف نسخة، يعرض ملخصه، ويستعيده بعد التأكيد.
    /// يرجّع true لو الاستعادة تمت (يعني البيانات اتغيّرت وعلى المتصل يرجّع لشاشة الدخول).
    /// </summary>
    public static bool RunRestore(IWin32Window owner)
    {
        if (AppSession.PrintingInProgress)
        {
            MessageBox.Show(owner,
                "فيه طباعة شغالة دلوقتي. استنى لحد ما تخلص (أو اوقفها) وبعدين استعيد النسخة.",
                "الاستعادة", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Rtl);
            return false;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "اختر ملف النسخة الاحتياطية",
            Filter = "نسخة احتياطية (*.zip;*.db)|*.zip;*.db|كل الملفات (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        try
        {
            if (Directory.Exists(DatabaseBackup.BackupDirectory))
                dialog.InitialDirectory = DatabaseBackup.BackupDirectory;
        }
        catch { }

        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;

        Cursor.Current = Cursors.WaitCursor;
        bool valid = DatabaseBackup.Inspect(dialog.FileName, out var info, out string error);
        Cursor.Current = Cursors.Default;

        if (!valid || info == null)
        {
            MessageBox.Show(owner, "الملف ده مينفعش يتستعاد منه:\n\n" + error, "ملف غير صالح",
                MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, Rtl);
            return false;
        }

        string created = info.CreatedAt.HasValue
            ? info.CreatedAt.Value.ToString("yyyy-MM-dd  HH:mm")
            : "غير معروف";
        string includes = info.IsZip
            ? "المحتويات: قاعدة البيانات"
              + (info.HasPricing ? " + الأسعار" : "")
              + (info.HasSecurity ? " + كلمات المرور" : "")
              + (info.HasPrintLog ? " + سجل الطباعة" : "")
              + (info.HasFullData ? " + إعدادات البرنامج والقوائم وسجل الأخطاء وباقي ملفات بيانات التطبيق" : "")
            : "المحتويات: قاعدة البيانات فقط (نسخة قديمة) — الأسعار وكلمات المرور والإعدادات الحالية هتفضل زي ما هي.";

        var confirm = MessageBox.Show(owner,
            "هل تريد استعادة هذه النسخة؟\n\n" +
            $"الملف: {Path.GetFileName(info.Path)}\n" +
            $"تاريخ النسخة: {created}\n" +
            $"العملاء: {info.Customers}   |   الأصناف: {info.Products}   |   العمليات: {info.Transactions}\n" +
            includes + "\n\n" +
            "⚠️ كل البيانات الحالية هتتستبدل ببيانات النسخة دي.\n" +
            "وهيتعمل تلقائيًا نسخة أمان من البيانات الحالية قبل الاستبدال، وبعدها هيرجع البرنامج لصفحة تسجيل الدخول.",
            "تأكيد الاستعادة",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, Rtl);
        if (confirm != DialogResult.Yes) return false;

        Cursor.Current = Cursors.WaitCursor;
        DatabaseBackup.RestoreResult result;
        try
        {
            result = DatabaseBackup.Restore(dialog.FileName);
        }
        finally
        {
            Cursor.Current = Cursors.Default;
        }

        if (!result.Ok)
        {
            MessageBox.Show(owner, result.Message, "فشلت الاستعادة",
                MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, Rtl);
            // لو الملفات اتغيّرت جزئيًا (رسالة فيها نسخة الأمان) الأحوط نرجّع للدخول برضو.
            return false;
        }

        MessageBox.Show(owner,
            "✅ تمت استعادة النسخة الاحتياطية بنجاح.\n\n" +
            (result.SafetyBackupPath != null
                ? "نسخة أمان من البيانات القديمة:\n" + result.SafetyBackupPath + "\n\n"
                : "") +
            "هيرجع البرنامج لصفحة تسجيل الدخول.",
            "تمت الاستعادة", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Rtl);
        return true;
    }

    /// <summary> يحفظ نسخة شاملة جديدة في مكان يختاره المستخدم (فلاشة مثلًا). </summary>
    public static void RunExport(IWin32Window owner)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "حفظ نسخة احتياطية شاملة",
            Filter = "نسخة احتياطية (*.zip)|*.zip",
            FileName = $"library_{DateTime.Now:yyyy-MM-dd_HHmmss}_manual.zip",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        Cursor.Current = Cursors.WaitCursor;
        bool ok;
        string error;
        try
        {
            ok = DatabaseBackup.BackupTo(dialog.FileName, out error);
        }
        finally
        {
            Cursor.Current = Cursors.Default;
        }

        if (ok)
            MessageBox.Show(owner, "تم حفظ النسخة الاحتياطية:\n" + dialog.FileName, "تم",
                MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Rtl);
        else
            MessageBox.Show(owner, "فشل حفظ النسخة:\n" + error, "خطأ",
                MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, Rtl);
    }
}
