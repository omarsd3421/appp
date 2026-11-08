using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace LibraryPrintApp
{
    internal static class Program
    {
        // نسخة واحدة بس من البرنامج: النسخة التانية (مثلًا من كليك يمين) بتسلّم الملفات للأولى وتقفل.
        private static Mutex? singleInstance;

        [STAThread]
        static void Main(string[] args)
        {
            // أوامر تسجيل/إلغاء أمر كليك يمين (بتشتغل وتقفل من غير ما تفتح البرنامج).
            if (ShellMenu.HasSwitch(args, "--register-shell"))
            {
                ShellMenu.Register();
                return;
            }

            if (ShellMenu.HasSwitch(args, "--unregister-shell"))
            {
                ShellMenu.Unregister(rememberChoice: true);
                return;
            }

            List<string> filesToAdd = ShellMenu.ParseFiles(args);

            singleInstance = new Mutex(true, @"Local\LibraryPrintApp.SingleInstance", out bool isPrimary);
            if (!isPrimary)
            {
                ApplicationConfiguration.Initialize();

                if (filesToAdd.Count > 0)
                {
                    List<string> failed = ShellMenu.ForwardToRunningInstance(filesToAdd);
                    if (failed.Count > 0)
                    {
                        MessageBox.Show(
                            "تعذر إضافة الملفات دي لبرنامج الطباعة:\n\n" + string.Join("\n", failed),
                            "إضافة إلى برنامج الطباعة",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    MessageBox.Show(
                        "البرنامج شغال بالفعل.",
                        "مكتبة الشريف",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            ApplicationConfiguration.Initialize();
            // توحيد سلوك Esc في النوافذ المنبثقة على مستوى البرنامج كله.
            Application.AddMessageFilter(new ModalEscapeMessageFilter());
            AppLog.InstallGlobalHandlers();

            try
            {
                // نسخة أمان للإصدار السابق قبل أي ترحيل محتمل لبنية قاعدة البيانات.
                DatabaseBackup.BackupBeforeAppVersionChange();
                AppDatabase.Initialize();
                if (!DatabaseBackup.BackupIfNotToday(out string backupWarning))
                {
                    MessageBox.Show(
                        "تحذير مهم: البرنامج سيعمل، لكن النسخة الاحتياطية اليومية لم تكتمل.\n" + backupWarning +
                        "\n\nلا تعتمد على نسخة واحدة فقط من بيانات المكتبة.",
                        "تحذير النسخ الاحتياطي", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (!string.IsNullOrWhiteSpace(backupWarning))
                {
                    MessageBox.Show("تنبيه النسخ الاحتياطي:\n" + backupWarning,
                        "النسخ الاحتياطي", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                int pendingPrintLogs = PrintLogger.GetPendingLogCount();
                if (pendingPrintLogs != 0)
                {
                    string pendingMessage = pendingPrintLogs > 0
                        ? $"يوجد {pendingPrintLogs} سجل طباعة معلّق يحتاج مراجعة أو مزامنة. قد تكون بعض العمليات مسجلة في قاعدة البيانات بالفعل، لذلك لا تُعد الطباعة لمجرد ظهور هذا التنبيه. افتح البرنامج وراجع سجل الأخطاء والخزينة، وستُعاد محاولة مزامنة عمليات اليوم عند بدء الشفت."
                        : "تعذرت قراءة قائمة عمليات الطباعة المعلقة. لا تمسح سجل الطباعة ولا تصفّر البيانات إلى أن يتم فحص ملف القائمة، حتى لا تضيع أو تعود سجلات مالية لاحقًا.";
                    MessageBox.Show(pendingMessage,
                        "مراجعة عمليات الطباعة المعلقة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // جسر إضافة المتصفح (واتساب → الطباعة)
                PrintBridge.Start();

                // ملفات جاية من كليك يمين وقت ما البرنامج كان مقفول: بتستنى لحد تسجيل الدخول.
                if (filesToAdd.Count > 0)
                    PrintBridge.EnqueueInitial(filesToAdd);

                // تسجيل أمر «إضافة إلى برنامج الطباعة» في قايمة كليك يمين (بيتحدّث لو مكان البرنامج اتغير).
                ShellMenu.EnsureRegistered();

                // حلقة الجلسات: تسجيل الخروج بيقفل الشاشات ويرجّعنا لشاشة تسجيل الدخول
                // (بدل Application.Restart اللي بيقفل البرنامج لو اتشغل عن طريق dotnet).
                while (true)
                {
                    AppSession.Reset();

                    string user;
                    using (LoginForm loginForm = new LoginForm())
                    {
                        if (loginForm.ShowDialog() != DialogResult.OK)
                            break;
                        user = loginForm.LoggedInUser;
                    }

                    AppLog.CurrentUser = user;
                    using (DashboardForm dashboard = new DashboardForm(user))
                    {
                        Application.Run(dashboard);
                    }

                    if (!AppSession.LogoutRequested)
                        break;

                    AppLog.Info($"تسجيل خروج: {user}");
                    AppLog.CurrentUser = null;
                }
            }
            catch (Exception ex)
            {
                PrintBridge.Stop();
                AppLog.Error("فشل تشغيل البرنامج", ex);
                MessageBox.Show(
                    "تعذر تشغيل البرنامج، وتم تسجيل الخطأ في سجل الأخطاء:\n\n" + AppLog.LogDirectory + "\n\n" + ex.Message,
                    "خطأ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}


internal sealed class ModalEscapeMessageFilter : IMessageFilter
{
    private const int WmKeyDown = 0x0100;
    private const int VkEscape = 0x1B;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmKeyDown || (int)m.WParam != VkEscape)
            return false;

        Form? active = Form.ActiveForm;
        if (active == null || active.IsDisposed || !active.Modal)
            return false;

        Control? control = GetDeepActiveControl(active);
        if (control is ComboBox combo && combo.DroppedDown)
        {
            combo.DroppedDown = false;
            return true;
        }
        if (control is DataGridView grid && grid.IsCurrentCellInEditMode)
        {
            grid.CancelEdit();
            return true;
        }
        if (control is TextBoxBase text && text.SelectionLength > 0)
        {
            text.SelectionLength = 0;
            return true;
        }

        active.Close();
        return true;
    }

    private static Control? GetDeepActiveControl(Control parent)
    {
        Control? current = parent;
        while (current is ContainerControl container && container.ActiveControl != null)
            current = container.ActiveControl;
        return current;
    }
}
