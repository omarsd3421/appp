using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// نافذة «تأكيد الطباعة» بالشكل الحديث (بدل رسالة Windows العادية).
/// Enter = بدء الطباعة (DialogResult.Yes) — Esc أو Backspace أو «إلغاء» = عدم الطباعة.
/// </summary>
internal sealed class PrintConfirmForm : Form
{
    private readonly Button startButton;
    private readonly Button cancelButton;

    public PrintConfirmForm(
        string headline,
        IReadOnlyList<(string Label, string Value)> rows,
        string note)
    {
        UiKit.PrepareForm(this, "تأكيد الطباعة", 640, 440);
        MinimumSize = new Size(560, 300);
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.White;
        AutoScroll = false;
        UiKit.EnableEscape(this);

        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = headline,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = UiKit.Primary,
            BackColor = Color.White
        };

        var details = OperationDoneForm.BuildDetailsTable(rows);

        var noteLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = note,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = UiKit.Muted,
            BackColor = Color.White,
            Padding = new Padding(30, 0, 30, 0)
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            RightToLeft = RightToLeft.No, // أول زر (بدء الطباعة) أقصى اليمين
            WrapContents = false,
            Padding = new Padding(30, 12, 30, 8),
            BackColor = Color.White
        };

        startButton = UiKit.Button("🖨️ بدء الطباعة", UiKit.Primary, 190, 46);
        startButton.Click += (_, _) => { DialogResult = DialogResult.Yes; Close(); };

        cancelButton = UiKit.Button("إلغاء", Color.FromArgb(120, 130, 140), 130, 46);
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.No; Close(); };

        startButton.RightToLeft = cancelButton.RightToLeft = RightToLeft.Yes;
        footer.Controls.Add(startButton);
        footer.Controls.Add(cancelButton);

        // جدول واحد بدل Dock متعدد لتفادي تداخل العناصر
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(details, 0, 1);
        root.Controls.Add(noteLabel, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);

        ClientSize = new Size(640, 78 + rows.Count * 36 + 12 + 40 + 74 + 20);

        AcceptButton = startButton;
        CancelButton = cancelButton;
        Shown += (_, _) => startButton.Focus();
    }

    // Backspace = إلغاء (زي Esc)
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Shortcuts.Matches("global.back", keyData))
        {
            DialogResult = DialogResult.No;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
