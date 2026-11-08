using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// بتظهر لما الطباعة تتوقف في النص (أو يحصل خطأ): بتسأل «الطباعة تمت بشكل صحيح ولا لا؟».
/// - «تمت بشكل صحيح»: الملفات المعلّمة بتتحسب.
/// - «الورق باظ»: ولا ملف بيتحسب (الزبون مش هيدفع تمن الورق الباظ).
/// المستخدم لازم يختار (مفيش إغلاق بـ X أو Esc) عشان مفيش حساب بالغلط.
/// </summary>
internal sealed class PrintOutcomeForm : Form
{
    internal readonly record struct Item(string Text, bool CheckedByDefault, decimal Price);

    private readonly CheckedListBox list;
    private readonly Label totalLabel;
    private readonly List<Item> items;

    /// <summary> لكل ملف: هل يتحسب (true) ولا لا (false)، بنفس ترتيب القائمة المبعوتة. </summary>
    public bool[] AcceptedFlags { get; private set; }

    public PrintOutcomeForm(string headline, IReadOnlyList<Item> source)
    {
        items = source.ToList();
        AcceptedFlags = new bool[items.Count]; // الافتراضي: ولا حاجة تتحسب لحد ما المستخدم يختار

        Text = "هل الطباعة تمت بشكل صحيح؟";
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
        ClientSize = new Size(700, 470);

        var title = new Label
        {
            Text = "⚠️ هل الورق اللي راح للطابعة اتطبع صح؟",
            Dock = DockStyle.Top,
            Height = 52,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = UiKit.Danger
        };

        var reason = new Label
        {
            Text = headline + "\nعلّم على الملفات اللي طُبعت صح (هسألك بعدها كام وجه طلع مظبوط)، وشيل العلامة من اللي الورق باظ فيها.",
            Dock = DockStyle.Top,
            Height = 66,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = UiKit.Muted,
            Padding = new Padding(16, 0, 16, 0)
        };

        list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 11F),
            HorizontalScrollbar = true
        };
        foreach (var item in items)
            list.Items.Add(item.Text, item.CheckedByDefault);
        list.ItemCheck += (_, _) => BeginInvoke(new Action(UpdateTotal));

        totalLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = UiKit.SuccessText
        };

        var okButton = UiKit.Button("✅ تمت بشكل صحيح — احسب المعلّم", UiKit.Success, 330, 50);
        okButton.Click += (_, _) => Finish(acceptChecked: true);

        var badButton = UiKit.Button("❌ الورق باظ — متحسبش الملفات دي", UiKit.Danger, 300, 50);
        badButton.Click += (_, _) => Finish(acceptChecked: false);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 74,
            FlowDirection = FlowDirection.RightToLeft,
            RightToLeft = RightToLeft.No, // أول زر («تمت صح») أقصى اليمين
            Padding = new Padding(20, 12, 20, 8),
            WrapContents = false
        };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(badButton);

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 0, 20, 6) };
        listHost.Controls.Add(list);

        Controls.Add(listHost);
        Controls.Add(totalLabel);
        Controls.Add(buttons);
        Controls.Add(reason);
        Controls.Add(title);

        UpdateTotal();
    }

    private void UpdateTotal()
    {
        decimal sum = 0m;
        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (!list.GetItemChecked(i)) continue;
            sum += items[i].Price;
            count++;
        }
        totalLabel.Text = count == 0
            ? "مش هيتحسب أي مبلغ"
            : $"هيتحسب: {count} ملف — {sum:F2} ج.م";
        totalLabel.ForeColor = count == 0 ? UiKit.Danger : UiKit.SuccessText;
    }

    private void Finish(bool acceptChecked)
    {
        for (int i = 0; i < items.Count; i++)
            AcceptedFlags[i] = acceptChecked && list.GetItemChecked(i);

        if (acceptChecked && !AcceptedFlags.Any(x => x))
        {
            var sure = MessageBox.Show(this,
                "مفيش أي ملف معلّم، يعني مش هيتحسب أي مبلغ. متأكد؟",
                "تأكيد", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (sure != DialogResult.Yes) return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Esc/Alt+F4 مش بيقفلوا النافذة: لازم اختيار صريح.
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
