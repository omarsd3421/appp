using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// بتظهر بعد ما المستخدم يقول «الطباعة تمت صح» عقب إلغاء/إيقاف الطباعة:
/// الكمبيوتر بيبعت كل الأوراق للطابعة لكن مش كلها بتطلع لما العملية تتلغي،
/// فبنسأل «كام وجه طلع مظبوط؟» لكل ملف، وبيتحسب سعر الأوجه دي بس.
/// لازم المستخدم يأكد (مفيش إغلاق بـ X أو Esc) عشان مفيش حساب بالغلط.
/// </summary>
internal sealed class PrintPagesForm : Form
{
    internal readonly record struct Item(string Text, int Faces, decimal Price);

    private readonly List<Item> items;
    private readonly List<NumericUpDown> boxes = new();
    private readonly Label totalLabel;

    /// <summary> لكل ملف: عدد الأوجه اللي طلعت مظبوطة (0 = متحسبش الملف). نفس ترتيب القائمة المبعوتة. </summary>
    public int[] PrintedFaces { get; private set; }

    public PrintPagesForm(IReadOnlyList<Item> source)
    {
        items = source.ToList();
        PrintedFaces = items.Select(i => i.Faces).ToArray();

        Text = "كام وجه طلع مظبوط؟";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10.5F);
        int rowsHeight = Math.Min(items.Count, 6) * 52;
        ClientSize = new Size(700, 52 + 66 + rowsHeight + 40 + 74 + 10);

        var title = new Label
        {
            Text = "📄 كام وجه طلع مظبوط من اللي اتبعت؟",
            Dock = DockStyle.Top,
            Height = 52,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = UiKit.Primary
        };

        var hint = new Label
        {
            Text = "الكمبيوتر بعت كل الأوراق للطابعة، لكن لما الطباعة اتلغت مش كلها طلعت.\nاكتب عدد الأوجه اللي طلعت مظبوطة بس، وهي اللي هتتحسب.",
            Dock = DockStyle.Top,
            Height = 66,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = UiKit.Muted,
            Padding = new Padding(16, 0, 16, 0)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(10, 4, 10, 4)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        for (int i = 0; i < items.Count; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            grid.Controls.Add(new Label
            {
                Text = $"{items[i].Text}\nاتبعت {items[i].Faces} وجه — {items[i].Price:F2} ج.م",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            }, 0, i);

            var box = new NumericUpDown
            {
                Minimum = 0,
                Maximum = Math.Max(1, items[i].Faces),
                Value = Math.Max(1, items[i].Faces),
                DecimalPlaces = 0,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = HorizontalAlignment.Center,
                RightToLeft = RightToLeft.No,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold)
            };
            box.ValueChanged += (_, _) => UpdateTotal();
            box.TextChanged += (_, _) => UpdateTotal();
            boxes.Add(box);
            grid.Controls.Add(box, 1, i);
        }

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        host.Controls.Add(grid);

        totalLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = UiKit.SuccessText
        };

        var ok = UiKit.Button("✔ تأكيد", UiKit.Success, 200, 50);
        ok.Click += (_, _) => Confirm();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 74,
            FlowDirection = FlowDirection.RightToLeft,
            RightToLeft = RightToLeft.No,
            Padding = new Padding(20, 12, 20, 8),
            WrapContents = false
        };
        buttons.Controls.Add(ok);

        Controls.Add(host);
        Controls.Add(totalLabel);
        Controls.Add(buttons);
        Controls.Add(hint);
        Controls.Add(title);
        AcceptButton = ok;

        UpdateTotal();
    }

    private int ReadFaces(int index)
    {
        var box = boxes[index];
        int max = items[index].Faces;
        if (int.TryParse(box.Text.Trim(), out int typed))
            return Math.Max(0, Math.Min(max, typed));
        return (int)box.Value;
    }

    private void UpdateTotal()
    {
        decimal sum = 0m;
        int faces = 0;
        for (int i = 0; i < items.Count; i++)
        {
            int ok = ReadFaces(i);
            faces += ok;
            if (items[i].Faces > 0)
                sum += Math.Round(items[i].Price * ok / items[i].Faces, 2);
        }
        totalLabel.Text = faces == 0
            ? "مش هيتحسب أي مبلغ"
            : $"هيتحسب: {faces} وجه — {sum:F2} ج.م";
        totalLabel.ForeColor = faces == 0 ? UiKit.Danger : UiKit.SuccessText;
    }

    private void Confirm()
    {
        var result = new int[items.Count];
        for (int i = 0; i < items.Count; i++)
            result[i] = ReadFaces(i);

        if (result.All(x => x == 0))
        {
            var sure = MessageBox.Show(this,
                "كل الأوجه صفر، يعني مش هيتحسب أي مبلغ. متأكد؟",
                "تأكيد", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (sure != DialogResult.Yes) return;
        }

        PrintedFaces = result;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK && e.CloseReason == CloseReason.UserClosing)
            e.Cancel = true;
        base.OnFormClosing(e);
    }
}
