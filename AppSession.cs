using System;

namespace LibraryPrintApp;

/// <summary>
/// حالة الجلسة الحالية: تسجيل الخروج (الرجوع لشاشة الدخول بدل قفل البرنامج)
/// وهل فيه طباعة شغالة (عشان نمنع الاستعادة وقتها).
/// </summary>
internal static class AppSession
{
    /// <summary> اتطلب تسجيل خروج: Dashboard هيقفل كل الشاشات والـProgram يفتح شاشة الدخول من جديد. </summary>
    public static bool LogoutRequested { get; private set; }

    /// <summary> فيه إرسال للطابعة شغال حاليًا في شاشة الطباعة. </summary>
    public static bool PrintingInProgress { get; set; }

    public static void RequestLogout() => LogoutRequested = true;

    public static void Reset()
    {
        LogoutRequested = false;
        PrintingInProgress = false;
    }
}
