using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// تنبيهات بتظهر مرة واحدة بعد فتح لوحة التحكم (مش بتتكرر لو سجّلت خروج ودخول تاني):
///  1) أصناف المخزن اللي وصلت لحد التنبيه أو أقل (الحد بيتحدد لكل صنف من شاشة المخزن).
///  2) النسخ الاحتياطي: لو مفيش مكان نسخ خارجي، أو المكان المحدد مش متاح (فلاشة مش متوصلة).
///     تنبيه «مفيش مكان خارجي» بيظهر مرة واحدة في اليوم بس.
/// </summary>
internal static class StartupNotices
{
    private static bool shownThisRun;

    private static string MarkerFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LibraryPrintApp", "startup-notice.txt");

    public static void Show(IWin32Window owner, Action openInventory)
    {
        if (shownThisRun) return;
        shownThisRun = true;
        try
        {
            var lowItems = LoadLowStock(out int totalLow);
            string? backupWarning = BuildBackupWarning();
            if (lowItems.Count == 0 && backupWarning == null) return;

            using var form = new StartupNoticeForm(lowItems, totalLow, backupWarning);
            form.ShowDialog(owner);
            if (form.OpenInventoryRequested) openInventory();
        }
        catch (Exception ex)
        {
            AppLog.Error("فشل عرض تنبيهات بداية التشغيل", ex);
        }
    }

    private static List<string> LoadLowStock(out int total)
    {
        var items = new List<string>();
        total = 0;
        using var c = AppDatabase.OpenConnection();
        using (var count = c.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM Products WHERE Quantity <= MinQuantity";
            total = Convert.ToInt32(count.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        }
        if (total == 0) return items;

        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Name,Quantity,MinQuantity FROM Products WHERE Quantity <= MinQuantity ORDER BY Quantity,Name COLLATE NOCASE LIMIT 8";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string name = r.IsDBNull(0) ? "" : r.GetString(0);
            int qty = Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture);
            items.Add(qty <= 0 ? $"{name} — خلص" : $"{name} — فاضل {qty}");
        }
        return items;
    }

    private static string? BuildBackupWarning()
    {
        string extra = DatabaseBackup.ExtraFolder;
        if (string.IsNullOrWhiteSpace(extra))
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            try
            {
                if (File.Exists(MarkerFile) && File.ReadAllText(MarkerFile).Trim() == today) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(MarkerFile)!);
                File.WriteAllText(MarkerFile, today);
            }
            catch { /* لو الكتابة فشلت نعرض التنبيه عادي */ }
            return "مفيش مكان نسخ احتياطي خارجي (فلاشة أو هارد أو مجلد على جهاز تاني). لو الجهاز ده اتعطل ممكن تخسر الحسابات كلها. " +
                   "اضبط المكان من: إعدادات الإدارة › النظام.";
        }

        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(extra));
            bool reachable = Directory.Exists(extra) || (!string.IsNullOrEmpty(root) && Directory.Exists(root));
            if (!reachable)
                return "مكان النسخ الاحتياطي الخارجي مش متاح دلوقتي (الفلاشة مش متوصلة؟): " + extra +
                       "\nالنسخ الجاية هتتحفظ على الجهاز بس لحد ما توصّله.";
        }
        catch
        {
            return "مكان النسخ الاحتياطي الخارجي مش متاح دلوقتي: " + extra;
        }
        return null;
    }
}

internal sealed class StartupNoticeForm : Form
{
    public bool OpenInventoryRequested { get; private set; }

    public StartupNoticeForm(List<string> lowItems, int totalLow, string? backupWarning)
    {
        UiKit.PrepareForm(this, "تنبيهات", 640, 200);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10.5f);
        UiKit.EnableEscape(this);
        AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true,
            Padding = new Padding(26, 22, 26, 18), RightToLeft = RightToLeft.Yes, BackColor = Color.White
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        if (lowItems.Count > 0)
        {
            root.Controls.Add(Heading("⚠️ أصناف قربت تخلص أو خلصت (" + totalLow + ")", Color.FromArgb(170, 95, 0)));
            string list = string.Join("\n", lowItems.ConvertAll(x => "•  " + x));
            if (totalLow > lowItems.Count) list += $"\n•  و{totalLow - lowItems.Count} صنف تاني...";
            root.Controls.Add(Body(list));
        }

        if (backupWarning != null)
        {
            root.Controls.Add(Heading("💾 النسخ الاحتياطي", Color.FromArgb(192, 57, 43)));
            root.Controls.Add(Body(backupWarning));
        }

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Margin = new Padding(0, 14, 0, 0), Dock = DockStyle.Fill
        };
        var ok = UiKit.Button("تمام", UiKit.Primary, 110, 42);
        ok.Click += (_, _) => Close();
        buttons.Controls.Add(ok);
        if (lowItems.Count > 0)
        {
            var open = UiKit.Button("📦 فتح المخزن", Color.FromArgb(0, 105, 105), 160, 42);
            open.Click += (_, _) => { OpenInventoryRequested = true; Close(); };
            buttons.Controls.Add(open);
        }
        root.Controls.Add(buttons);
        AcceptButton = ok; CancelButton = ok;

        // نظبط ارتفاع النافذة على المحتوى
        Shown += (_, _) =>
        {
            int wanted = root.PreferredSize.Height + (Height - ClientSize.Height);
            Height = Math.Min(Math.Max(wanted, 200), Screen.FromControl(this).WorkingArea.Height - 60);
        };
    }

    private static Label Heading(string text, Color color) => new()
    {
        Text = text, AutoSize = true, ForeColor = color, Margin = new Padding(0, 8, 0, 4),
        Font = new Font("Segoe UI", 12.5f, FontStyle.Bold), MaximumSize = new Size(560, 0)
    };

    private static Label Body(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = UiKit.Text, Margin = new Padding(0, 0, 0, 6),
        Font = new Font("Segoe UI", 10.5f), MaximumSize = new Size(560, 0)
    };
}
