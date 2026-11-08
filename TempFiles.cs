using System;
using System.IO;

namespace LibraryPrintApp;

/// <summary>
/// ملفات البرنامج المؤقتة (نسخ صور واتساب، الملفات المدمجة، البطاقات، نسخ التعديل...).
/// كلها جوه %TEMP%\LibraryPrintApp. البرنامج بيمسح بس اللي جوه المجلد ده — ومبيلمسش
/// أي ملف أصلي على جهازك (مستندات، فلاشة، تنزيلات...).
/// </summary>
internal static class TempFiles
{
    public static string Root =>
        Path.Combine(Path.GetTempPath(), "LibraryPrintApp");

    /// <summary> هل الملف ده نسخة مؤقتة تابعة للبرنامج (يعني آمن نمسحه)؟ </summary>
    public static bool IsAppOwned(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary> يمسح الملف لو نسخة مؤقتة تابعة للبرنامج (وبعدها المجلد الفرعي لو فضي). أي فشل بيتجاهل. </summary>
    public static void TryDelete(string? path)
    {
        if (!IsAppOwned(path)) return;
        try
        {
            if (File.Exists(path))
                File.Delete(path!);

            string? dir = Path.GetDirectoryName(Path.GetFullPath(path!));
            string root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
            // نمسح المجلد الفرعي (مثل bridge\<رقم>) لو فضي، ومنمسحش المجلدات الرئيسية.
            if (dir != null && IsAppOwned(dir + Path.DirectorySeparatorChar) &&
                !string.Equals(Path.GetDirectoryName(dir), root, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
            {
                Directory.Delete(dir);
            }
        }
        catch
        {
            // ملف مفتوح دلوقتي: هيتمسح في المرة الجاية (عند تشغيل البرنامج).
        }
    }

    /// <summary> يمسح محتوى مجلد فرعي معين (مثل Converted) بعد ما نخلص منه. </summary>
    public static void CleanSubfolder(string name)
    {
        try
        {
            string dir = Path.Combine(Root, name);
            if (!Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    /// <summary> عند تشغيل البرنامج: نمسح كل الملفات المؤقتة اللي فضلت من المرة اللي فاتت. </summary>
    public static void CleanAll()
    {
        try
        {
            if (!Directory.Exists(Root)) return;

            foreach (string file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); } catch { }
            }

            foreach (string dir in Directory.GetDirectories(Root))
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
        catch { }
    }
}
