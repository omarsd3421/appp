using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// تحسين سرعة إدخال البيانات بدون استخدام SendKeys.
/// SendKeys كان يمكن أن يرسل حرف "a" فعليًا مع بعض تخطيطات لوحة المفاتيح،
/// لذلك نحدد النص من خلال محرر NumericUpDown الداخلي مباشرة باستخدام Win32.
/// </summary>
internal static class InputFocusHelper
{
    private const uint EM_SETSEL = 0x00B1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr parentHandle,
        IntPtr childAfter,
        string? className,
        string? windowTitle);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    public static void Apply(Control root)
    {
        ApplyRecursive(root);
    }

    private static void ApplyRecursive(Control control)
    {
        if (control is TextBox textBox)
        {
            textBox.Enter -= TextBox_Enter;
            textBox.Enter += TextBox_Enter;
            textBox.MouseUp -= TextBox_MouseUp;
            textBox.MouseUp += TextBox_MouseUp;
        }
        else if (control is NumericUpDown numeric)
        {
            numeric.Enter -= Numeric_Enter;
            numeric.Enter += Numeric_Enter;
            numeric.MouseUp -= Numeric_MouseUp;
            numeric.MouseUp += Numeric_MouseUp;
        }

        foreach (Control child in control.Controls)
            ApplyRecursive(child);
    }

    private static void TextBox_Enter(object? sender, EventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsDisposed)
            return;

        SafeBeginInvoke(textBox, () =>
        {
            if (!textBox.IsDisposed && !textBox.Disposing && textBox.Focused)
                textBox.SelectAll();
        });
    }

    private static void TextBox_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not TextBox textBox || textBox.IsDisposed)
            return;

        // بعد انتهاء النقرة، نحدد النص كله حتى يكتب المستخدم فوق القيمة مباشرة.
        SafeBeginInvoke(textBox, () =>
        {
            if (!textBox.IsDisposed && !textBox.Disposing && textBox.Focused)
                textBox.SelectAll();
        });
    }

    private static void Numeric_Enter(object? sender, EventArgs e)
    {
        if (sender is NumericUpDown numeric)
            BeginSelectNumeric(numeric);
    }

    private static void Numeric_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not NumericUpDown numeric)
            return;

        BeginSelectNumeric(numeric);
    }

    private static void BeginSelectNumeric(NumericUpDown numeric)
    {
        if (numeric.IsDisposed)
            return;

        SafeBeginInvoke(numeric, () =>
        {
            if (!numeric.IsDisposed && !numeric.Disposing)
                SelectNumericEditor(numeric);
        });
    }


    private static void SafeBeginInvoke(Control control, Action action)
    {
        if (control.IsDisposed || control.Disposing) return;
        if (!control.IsHandleCreated)
        {
            return;
        }

        try
        {
            control.BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
            // تم التخلص من الكنترول.
        }
        catch (InvalidOperationException)
        {
            // الـHandle غير جاهز/يُغلق؛ تحسين اختياري فقط.
        }
    }

    private static void SelectNumericEditor(NumericUpDown numeric)
    {
        try
        {
            numeric.Focus();

            // NumericUpDown في WinForms يحتوي داخله على Edit control.
            // نصل إليه مباشرة بدل SendKeys حتى لا يتم إدخال حروف عرضية مثل 'a'.
            IntPtr editHandle = FindWindowEx(
                numeric.Handle,
                IntPtr.Zero,
                "Edit",
                null);

            if (editHandle != IntPtr.Zero)
            {
                SendMessage(
                    editHandle,
                    EM_SETSEL,
                    IntPtr.Zero,
                    new IntPtr(-1));
                return;
            }

            // احتياط إضافي إذا كان محرر WinForms متاحًا في Controls.
            TextBoxBase? editor = FindEditorControl(numeric);
            editor?.SelectAll();
        }
        catch
        {
            // تحسين واجهة فقط؛ لا نسمح بفشل التحديد بإيقاف البرنامج.
        }
    }

    private static TextBoxBase? FindEditorControl(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TextBoxBase textBox)
                return textBox;

            TextBoxBase? nested = FindEditorControl(child);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
