using System;
using System.Drawing;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// إصلاح عام لمشكلة شائعة في نوافذ الفورمز: عناصر مثل NumericUpDown وComboBox
/// "تبتلع" دوران عجلة الماوس لنفسها (تغيير القيمة / تغيير العنصر المختار) بدل
/// تمريره للأعلى، فتتوقف اللوحة صاحبة التمرير (Panel بخاصية AutoScroll) عن
/// الاستجابة كل ما كان المؤشر فوق أحد هذه الحقول - وهو سبب شعور أن أجزاء من
/// أعلى الشاشة "مقطوعة" رغم وجود مساحة تمرير فعليًا.
///
/// الاستخدام: نداء واحد بعد بناء الواجهة بالكامل:
///     WheelScrollFix.Apply(scrollHost);
/// حيث scrollHost هو الـ Panel (أو أي ScrollableControl) الذي وُضع له
/// AutoScroll = true.
/// </summary>
internal static class WheelScrollFix
{
    public static void Apply(ScrollableControl host)
    {
        Hook(host, host);
    }

    private static void Hook(ScrollableControl host, Control container)
    {
        foreach (Control child in container.Controls)
        {
            AttachWheel(host, child);
            if (child.Controls.Count > 0)
                Hook(host, child);
        }

        // بعض الحقول (مثل قائمة العملاء المختار) تُضاف ديناميكيًا بعد الإنشاء الأول.
        container.ControlAdded += (s, e) =>
        {
            Control? added = e.Control;
            if (added is null) return;
            AttachWheel(host, added);
            if (added.Controls.Count > 0)
                Hook(host, added);
        };
    }

    private static void AttachWheel(ScrollableControl host, Control c)
    {
        c.MouseWheel += (s, e) =>
        {
            // اسمح لحقل الأرقام أو القائمة المنسدلة بالاحتفاظ بسلوكه الطبيعي
            // (تغيير القيمة/العنصر بعجلة الماوس) فقط عندما يكون هو المُركَّز عليه
            // فعليًا (بعد الضغط بداخله). أما مجرد مرور المؤشر فوقه أثناء التمرير
            // العادي فيجب أن يُحرّك الصفحة، لا أن يغيّر قيمته بالخطأ.
            if ((c is NumericUpDown || c is ComboBox) && c.ContainsFocus)
                return;

            Scroll(host, e);
        };
    }

    private static void Scroll(ScrollableControl host, MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs hme)
            hme.Handled = true;

        int wheelLines = SystemInformation.MouseWheelScrollLines;
        if (wheelLines <= 0) wheelLines = 3;
        int step = Math.Max(1, host.VerticalScroll.SmallChange) * wheelLines;

        int newValue = host.VerticalScroll.Value - (e.Delta / 120) * step;
        if (newValue < host.VerticalScroll.Minimum) newValue = host.VerticalScroll.Minimum;
        if (newValue > host.VerticalScroll.Maximum) newValue = host.VerticalScroll.Maximum;

        host.AutoScrollPosition = new Point(host.HorizontalScroll.Value, newValue);
    }
}
