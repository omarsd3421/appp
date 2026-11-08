using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// شريط تنقل كامل بين أوراق المعاينة:
///   الصف الأول:  [‹]  ──────●──────  [›]      (سلايدر + زرار سابق/تالي)
///   الصف التاني: ورقة [ 5 ] / 12               (مربع بتكتب فيه رقم الورقة وتدوس Enter)
/// بيتستخدم في اللوحة الجانبية وفي شاشة المعاينة الكبيرة.
/// الأرقام الظاهرة 1-based، والأرقام اللي بتتبعت في الحدث 0-based.
/// </summary>
internal sealed class SheetNavigator : Panel
{
    private readonly SheetSlider slider;
    private readonly Button prevButton;
    private readonly Button nextButton;
    private readonly TextBox box;
    private readonly Label captionLabel;
    private readonly Label totalLabel;
    private readonly Panel row1;
    private readonly Panel row2;
    private readonly bool large;

    private int index;
    private int count;
    private bool selectOnMouseUp;

    /// <summary> (الفهرس المطلوب 0-based، هل لسه بيسحب السلايدر؟) </summary>
    public event Action<int, bool>? IndexRequested;

    /// <summary> المستخدم دوس Esc داخل مربع الرقم. </summary>
    public event Action? EscapePressed;

    public bool BoxFocused => box.Focused;

    public SheetNavigator(bool large)
    {
        this.large = large;

        RightToLeft = RightToLeft.No;
        BackColor = Color.FromArgb(248, 249, 250);
        Padding = new Padding(6, 0, 6, 4);
        Height = large ? 100 : 84;

        int btnW = large ? 46 : 36;
        int btnH = large ? 34 : 28;
        float fs = large ? 13f : 10.5f;

        slider = new SheetSlider { Height = large ? 54 : 46 };

        prevButton = MakeButton("‹", btnW, btnH, large ? 15f : 13f);
        nextButton = MakeButton("›", btnW, btnH, large ? 15f : 13f);

        row1 = new Panel { Dock = DockStyle.Top, Height = large ? 54 : 46 };
        row1.Controls.Add(slider);
        row1.Controls.Add(prevButton);
        row1.Controls.Add(nextButton);
        row1.Resize += (s, e) => LayoutRow1();

        captionLabel = new Label
        {
            Text = "ورقة",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 98, 107),
            Font = new Font("Segoe UI", fs - 1f, FontStyle.Regular)
        };

        box = new TextBox
        {
            Width = large ? 72 : 58,
            TextAlign = HorizontalAlignment.Center,
            MaxLength = 5,
            Font = new Font("Segoe UI", fs, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(40, 50, 60),
            ShortcutsEnabled = true
        };

        totalLabel = new Label
        {
            Text = "/ -",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 98, 107),
            Font = new Font("Segoe UI", fs, FontStyle.Bold)
        };

        row2 = new Panel { Dock = DockStyle.Fill };
        row2.Controls.Add(captionLabel);
        row2.Controls.Add(box);
        row2.Controls.Add(totalLabel);
        row2.Resize += (s, e) => CenterRow2();

        // Fill أولًا ثم Top (ترتيب الإضافة بيحدد أولوية الـDock).
        Controls.Add(row2);
        Controls.Add(row1);

        slider.UserValueChanged += (v, drag) => RequestIndex(v, drag);
        prevButton.Click += (s, e) => RequestIndex(index - 1, false);
        nextButton.Click += (s, e) => RequestIndex(index + 1, false);

        box.KeyPress += (s, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                e.Handled = true;
        };

        box.KeyDown += (s, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    Commit();
                    e.Handled = e.SuppressKeyPress = true;
                    break;

                case Keys.Escape:
                    CancelEdit();
                    EscapePressed?.Invoke();
                    e.Handled = e.SuppressKeyPress = true;
                    break;

                case Keys.Up:
                    StepFromBox(-1);
                    e.Handled = e.SuppressKeyPress = true;
                    break;

                case Keys.Down:
                    StepFromBox(1);
                    e.Handled = e.SuppressKeyPress = true;
                    break;
            }
        };

        box.Enter += (s, e) =>
        {
            if (MouseButtons == MouseButtons.None)
                box.SelectAll();
            else
                selectOnMouseUp = true;
        };

        box.MouseUp += (s, e) =>
        {
            if (selectOnMouseUp)
            {
                selectOnMouseUp = false;
                box.SelectAll();
            }
        };

        box.Leave += (s, e) => Commit(selectAfter: false);

        LayoutRow1();
        CenterRow2();
        SetState(0, 0);
    }

    private static Button MakeButton(string text, int width, int height, float fontSize)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(52, 73, 94),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", fontSize, FontStyle.Bold),
            Cursor = Cursors.Hand,
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private void LayoutRow1()
    {
        int w = row1.ClientSize.Width;
        int h = row1.ClientSize.Height;
        int bw = prevButton.Width;
        int bh = prevButton.Height;
        int y = (int)(h - 17 - bh / 2f);

        prevButton.SetBounds(2, y, bw, bh);
        nextButton.SetBounds(Math.Max(0, w - bw - 2), y, bw, bh);

        int sx = bw + 6;
        slider.SetBounds(sx, 0, Math.Max(10, w - 2 * sx), h);
    }

    private void CenterRow2()
    {
        int gap = 8;
        int total = captionLabel.PreferredWidth + gap + box.Width + gap + totalLabel.PreferredWidth;
        int left = Math.Max(0, (row2.ClientSize.Width - total) / 2);
        int top = Math.Max(0, (row2.ClientSize.Height - box.Height) / 2 - 2);

        captionLabel.Location = new Point(
            left,
            top + (box.Height - captionLabel.PreferredHeight) / 2);

        box.Location = new Point(left + captionLabel.PreferredWidth + gap, top);

        totalLabel.Location = new Point(
            box.Right + gap,
            top + (box.Height - totalLabel.PreferredHeight) / 2);
    }

    /// <summary> تحديث الحالة من برّه (من غير ما يطلع حدث). </summary>
    public void SetState(int newIndex, int newCount)
    {
        count = Math.Max(0, newCount);
        index = count == 0 ? 0 : Math.Clamp(newIndex, 0, count - 1);

        slider.Maximum = Math.Max(0, count - 1);
        slider.Enabled = count > 1;
        box.Enabled = count > 1;
        totalLabel.Text = count > 0 ? "/ " + count : "/ -";

        SyncVisuals();
        CenterRow2();
    }

    private void SyncVisuals()
    {
        slider.Value = index;
        prevButton.Enabled = index > 0;
        nextButton.Enabled = index < count - 1;

        if (!box.Focused)
            box.Text = count <= 0 ? "" : (index + 1).ToString();
    }

    private void RequestIndex(int idx, bool dragging)
    {
        if (count <= 0)
            return;

        idx = Math.Clamp(idx, 0, count - 1);

        if (idx != index)
        {
            index = idx;
            SyncVisuals();
            IndexRequested?.Invoke(idx, dragging);
        }
        else if (!dragging)
        {
            // نفس الورقة: نبلّغ برضه (لما السلايدر يتسيّب) عشان المستقبِل يرسم فورًا.
            IndexRequested?.Invoke(idx, false);
        }
    }

    private void StepFromBox(int delta)
    {
        RequestIndex(index + delta, false);
        box.Text = (index + 1).ToString();
        box.SelectAll();
    }

    private void Commit(bool selectAfter = true)
    {
        if (count <= 0)
            return;

        if (TryParseNumber(box.Text, out int n))
        {
            int idx = Math.Clamp(n, 1, count) - 1;
            RequestIndex(idx, false);
        }

        box.Text = (index + 1).ToString();
        if (selectAfter)
            box.SelectAll();
    }

    /// <summary> رجّع مربع الرقم لرقم الورقة الحالية من غير تنفيذ. </summary>
    public void CancelEdit()
    {
        box.Text = count <= 0 ? "" : (index + 1).ToString();
    }

    /// <summary> ركّز على المربع (ولو اتبعت رقم بنبدأ بيه — بيستخدمه الكيبورد في الشاشة الكبيرة). </summary>
    public void FocusBox(char? initial = null)
    {
        if (!box.Enabled)
            return;

        box.Focus();
        selectOnMouseUp = false;

        if (initial.HasValue)
        {
            box.Text = initial.Value.ToString();
            box.SelectionStart = box.TextLength;
        }
        else
        {
            box.SelectAll();
        }
    }

    /// <summary> بيقبل الأرقام العربية (٠-٩) والإنجليزية. </summary>
    private static bool TryParseNumber(string text, out int number)
    {
        number = 0;
        var sb = new StringBuilder();

        foreach (char c in text)
        {
            if (!char.IsDigit(c))
                continue;

            int d = (int)char.GetNumericValue(c);
            if (d < 0 || d > 9)
                continue;

            sb.Append((char)('0' + d));
        }

        return sb.Length > 0 && int.TryParse(sb.ToString(), out number);
    }
}
