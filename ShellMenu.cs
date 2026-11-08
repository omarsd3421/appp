using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace LibraryPrintApp;

/// <summary>
/// أمر «إضافة إلى برنامج الطباعة» في قايمة كليك يمين بتاعة Explorer (أول خيار + لوجو البرنامج).
///
/// - بيتسجل في HKCU (مستخدم ويندوز الحالي): من غير صلاحيات أدمن.
/// - بيظهر بس على الملفات اللي الطباعة بتدعمها (PDF / Word / PowerPoint / صور).
/// - الأمر بيشغّل البرنامج نفسه (WinExe) من غير نافذة cmd: البرنامج لو شغال بيستلم الملف
///   من النسخة الشغالة ويقفل فورًا، ولو مش شغال بيفتح ويضيف الملفات بعد تسجيل الدخول.
/// - على ويندوز 11 القايمة الجديدة المختصرة مبتعرضش الأوامر دي: بتظهر من
///   «عرض المزيد من الخيارات» (أو Shift + كليك يمين).
/// </summary>
internal static class ShellMenu
{
    private const string VerbKey = "LibraryPrintApp.AddToPrint";
    private const string MenuText = "إضافة إلى برنامج الطباعة";

    private static readonly string[] Extensions =
    {
        ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".pps", ".ppsx",
        ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"
    };

    private static string DisabledMarkerPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LibraryPrintApp",
            "no-shell-menu.txt");

    // ------------------------------------------------------------------
    // سطر الأوامر
    // ------------------------------------------------------------------

    /// <summary> كل الملفات الموجودة بعد --add (أو أي مسار ملف موجود فعلًا). </summary>
    public static List<string> ParseFiles(string[] args)
    {
        var files = new List<string>();
        foreach (string arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            try
            {
                string full = Path.GetFullPath(arg.Trim('"'));
                if (File.Exists(full))
                    files.Add(full);
            }
            catch
            {
            }
        }
        return files;
    }

    public static bool HasSwitch(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------
    // التسجيل
    // ------------------------------------------------------------------

    /// <summary> بيتنفذ عند تشغيل البرنامج: يسجّل الأمر أو يحدّث المسار لو اتغير. أي فشل بيتجاهل. </summary>
    public static void EnsureRegistered()
    {
        try
        {
            if (File.Exists(DisabledMarkerPath))
                return; // المستخدم شال الأمر بنفسه (--unregister-shell)
            Register();
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر تسجيل أمر «إضافة إلى برنامج الطباعة» في قايمة كليك يمين", ex);
        }
    }

    public static bool Register()
    {
        string? exe = GetExePath();
        if (exe == null)
            return false;

        foreach (string ext in Extensions)
        {
            using RegistryKey verb = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\SystemFileAssociations\{ext}\shell\{VerbKey}");

            verb.SetValue("", MenuText);
            verb.SetValue("Icon", "\"" + exe + "\",0");   // لوجو البرنامج
            verb.SetValue("Position", "Top");             // أول خيار في القايمة

            using RegistryKey command = verb.CreateSubKey("command");
            command.SetValue("", "\"" + exe + "\" --add \"%1\"");
        }

        try { if (File.Exists(DisabledMarkerPath)) File.Delete(DisabledMarkerPath); } catch { }
        return true;
    }

    public static void Unregister(bool rememberChoice)
    {
        foreach (string ext in Extensions)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    $@"Software\Classes\SystemFileAssociations\{ext}\shell\{VerbKey}",
                    false);
            }
            catch
            {
            }
        }

        if (rememberChoice)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DisabledMarkerPath)!);
                File.WriteAllText(DisabledMarkerPath, "لو عايز الأمر يرجع شغّل البرنامج بـ --register-shell");
            }
            catch
            {
            }
        }
    }

    private static string? GetExePath()
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        // لو البرنامج شغال عن طريق dotnet.exe مش هنسجّل (المسار هيبقى غلط).
        string name = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase))
            return null;

        return path;
    }

    // ------------------------------------------------------------------
    // تسليم الملفات للنسخة الشغالة
    // ------------------------------------------------------------------

    /// <summary>
    /// بيبعت الملفات لنسخة البرنامج الشغالة (جسر الطباعة المحلي). لو النسخة لسه بتفتح
    /// بنستنى لحد 45 ثانية. بيرجّع قايمة الملفات اللي فشل تسليمها مع السبب.
    /// </summary>
    public static List<string> ForwardToRunningInstance(List<string> files)
    {
        var failures = new List<string>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(45);

        foreach (string file in files)
        {
            bool delivered = false;
            string reason = "البرنامج مش بيرد";

            while (!delivered && DateTime.UtcNow < deadline)
            {
                try
                {
                    string json = "{\"path\":" + System.Text.Json.JsonSerializer.Serialize(file) + "}";
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        $"http://127.0.0.1:{PrintBridge.Port}/add-path")
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                    request.Headers.Add("X-LibraryPrint", "1");

                    using HttpResponseMessage response = http.Send(request);
                    if (response.IsSuccessStatusCode)
                    {
                        delivered = true;
                    }
                    else
                    {
                        reason = (int)response.StatusCode == 400
                            ? "نوع الملف مش مدعوم في الطباعة"
                            : "رفض البرنامج الملف";
                        break;
                    }
                }
                catch
                {
                    // النسخة الأولى لسه بتبدأ (قاعدة البيانات / تسجيل الدخول): نحاول تاني.
                    Thread.Sleep(400);
                }
            }

            if (!delivered)
                failures.Add($"{Path.GetFileName(file)} — {reason}");
        }

        return failures;
    }
}
