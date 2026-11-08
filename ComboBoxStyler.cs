using System;
using System.Drawing;
using System.Windows.Forms;

namespace LibraryPrintApp
{
    // ============================================================
    // شكل حديث للقوائم المنسدلة العادية (ComboBox)
    // بدل الشكل الافتراضي الممل لويندوز.
    // ============================================================
    public static class ComboBoxStyler
    {
        public static void ApplyModernStyle(
            ComboBox combo,
            Color? accentColor = null)
        {
            Color accent =
                accentColor ??
                Color.FromArgb(0, 122, 204);

            combo.FlatStyle =
                FlatStyle.Flat;

            combo.BackColor =
                Color.White;

            combo.ForeColor =
                Color.FromArgb(40, 40, 40);

            combo.DrawMode =
                DrawMode.OwnerDrawFixed;

            combo.ItemHeight =
                Math.Max(28, combo.Font.Height + 12);

            combo.DrawItem -=
                Combo_DrawItem;

            combo.DrawItem +=
                Combo_DrawItem;

            void Combo_DrawItem(
                object? sender,
                DrawItemEventArgs e)
            {
                if (e.Index < 0)
                    return;

                ComboBox cb =
                    (ComboBox)sender!;

                e.DrawBackground();

                bool selected =
                    (e.State & DrawItemState.Selected) ==
                    DrawItemState.Selected;

                using SolidBrush backBrush =
                    new SolidBrush(
                        selected
                            ? accent
                            : Color.White);

                e.Graphics.FillRectangle(
                    backBrush,
                    e.Bounds);

                string text =
                    cb.Items[e.Index]?.ToString()
                    ?? "";

                using SolidBrush foreBrush =
                    new SolidBrush(
                        selected
                            ? Color.White
                            : Color.FromArgb(40, 40, 40));

                using StringFormat sf =
                    new StringFormat
                    {
                        LineAlignment =
                            StringAlignment.Center,
                        Alignment =
                            StringAlignment.Far,
                        FormatFlags =
                            StringFormatFlags.DirectionRightToLeft
                    };

                Rectangle textRect =
                    new Rectangle(
                        e.Bounds.X + 10,
                        e.Bounds.Y,
                        e.Bounds.Width - 20,
                        e.Bounds.Height);

                e.Graphics.DrawString(
                    text,
                    cb.Font,
                    foreBrush,
                    textRect,
                    sf);

                e.DrawFocusRectangle();
            }
        }
    }
}
