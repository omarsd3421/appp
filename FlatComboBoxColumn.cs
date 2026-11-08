using System;
using System.Drawing;
using System.Windows.Forms;

namespace LibraryPrintApp
{
    // ============================================================
    // عمود DataGridView بقائمة منسدلة بشكل حديث بدل الشكل
    // الافتراضي الممل (الصورة اللي شكلها القائمة الرمادية العادية).
    // ============================================================
    public class FlatComboBoxColumn : DataGridViewComboBoxColumn
    {
        public FlatComboBoxColumn()
        {
            CellTemplate =
                new FlatComboBoxCell();
        }
    }

    public class FlatComboBoxCell : DataGridViewComboBoxCell
    {
        public override Type EditType =>
            typeof(FlatComboBoxEditingControl);
    }

    // ============================================================
    // مهم جدًا: لازم نرث من DataGridViewComboBoxEditingControl
    // (مش من ComboBox مباشرة) لأن DataGridViewComboBoxCell بيعمل
    // cast صريح لهذا النوع بالتحديد داخليًا عشان يربط الـ Items/
    // القيمة الحالية بالـ ComboBox وقت التعديل. لو الكلاس مش وارث
    // منه، الـ cast بيفشل ويرجع null، فالإطار بيتخطى تهيئة الـ
    // Items، وبعدين بيحصل NullReferenceException وقت فتح/اختيار
    // من القائمة لملف واحد (وده اللي كان بيسبب الكراش اللي شفته،
    // بينما "تغيير للكل" ما كانش بيلمس الـ editing control خالص
    // عشان كده ما كانش بيكراش).
    // ============================================================
    public class FlatComboBoxEditingControl : DataGridViewComboBoxEditingControl
    {
        private static readonly Color AccentColor =
            Color.FromArgb(0, 122, 204);

        public FlatComboBoxEditingControl()
        {
            DropDownStyle =
                ComboBoxStyle.DropDownList;

            FlatStyle =
                FlatStyle.Flat;

            DrawMode =
                DrawMode.OwnerDrawFixed;

            ItemHeight = 30;

            Font =
                new Font("Segoe UI", 10.5F, FontStyle.Bold);

            DrawItem -=
                OnDrawItem;

            DrawItem +=
                OnDrawItem;
        }

        private void OnDrawItem(
            object? sender,
            DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            e.DrawBackground();

            bool selected =
                (e.State & DrawItemState.Selected) ==
                DrawItemState.Selected;

            using SolidBrush backBrush =
                new SolidBrush(
                    selected
                        ? AccentColor
                        : Color.White);

            e.Graphics.FillRectangle(
                backBrush,
                e.Bounds);

            string text =
                Items[e.Index]?.ToString()
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
                Font,
                foreBrush,
                textRect,
                sf);

            e.DrawFocusRectangle();
        }

        // ============================================================
        // نطبّق شكلنا فوق التهيئة الافتراضية اللي بتعملها
        // DataGridViewComboBoxEditingControl (اللي بتظبط الـ Items
        // والقيمة الحالية صح دلوقتي لأننا وارثين منها فعلًا).
        // ============================================================
        public override void ApplyCellStyleToEditingControl(
            DataGridViewCellStyle dataGridViewCellStyle)
        {
            base.ApplyCellStyleToEditingControl(
                dataGridViewCellStyle);

            Font =
                new Font("Segoe UI", 10.5F, FontStyle.Bold);

            BackColor =
                Color.White;

            ForeColor =
                Color.FromArgb(40, 40, 40);
        }
    }
}
