using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp;

/// <summary>
/// تبويب «الاختصارات» في إعدادات الإدارة: كل اختصار في البرنامج له مربع تدوس عليه وتضغط
/// الزر (أو التوليفة) الجديد فيتسجل. التغييرات ما بتتطبقش إلا بعد «حفظ».
/// </summary>
internal sealed class ShortcutsEditorControl : UserControl
{
    private readonly Dictionary<string, Keys> map = Shortcuts.Snapshot();
    private readonly Dictionary<string, KeyRecorderBox> boxes = new();
    private readonly Dictionary<string, Label> defaultLabels = new();
    private readonly ToolTip tip = new();

    public ShortcutsEditorControl()
    {
        Dock = DockStyle.Fill;
        BackColor = UiKit.Background;
        RightToLeft = RightToLeft.Yes;
        BuildUi();
        RefreshStyles();
    }

    private void BuildUi()
    {
        // ----- القائمة (Fill) -----
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiKit.Background,
            Padding = new Padding(16, 8, 16, 8),
            RightToLeft = RightToLeft.Yes
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5,
            BackColor = Color.White,
            RightToLeft = RightToLeft.Yes,
            Padding = new Padding(8, 4, 8, 8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));

        // صف العناوين
        AddHeaderRow(table);

        foreach (var (groupKey, groupTitle) in ShortcutGroups.Ordered)
        {
            AddGroupRow(table, groupTitle);
            foreach (var def in Shortcuts.InGroup(groupKey))
                AddShortcutRow(table, def);
        }

        scroll.Controls.Add(table);

        // ----- الشريط العلوي (شرح) -----
        var intro = new Label
        {
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(18, 8, 18, 4),
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(52, 73, 94),
            TextAlign = ContentAlignment.MiddleRight,
            Text = "اضغط على مربع الاختصار ثم اضغط الزر اللي عايزه على الكيبورد (ممكن توليفة زي Ctrl + P)، وهيتسجل. " +
                   "«افتراضي» بيرجّع الاختصار الأصلي، و«إلغاء» بيشيل الاختصار خالص. بعدين دوس «حفظ الاختصارات».\n" +
                   "ملاحظة: الأزرار العادية (أرقام أو حروف من غير Ctrl/Alt) مش بتشتغل وأنت بتكتب جوه خانة نص. " +
                   "ولازم NumLock يكون شغال للأرقام الجانبية."
        };

        // ----- الشريط السفلي (أزرار) -----
        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 66,
            BackColor = Color.White,
            Padding = new Padding(16, 10, 16, 10),
            RightToLeft = RightToLeft.Yes
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            RightToLeft = RightToLeft.Yes
        };
        var save = UiKit.Button("💾 حفظ الاختصارات", UiKit.Success, 220, 44);
        save.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
        save.Click += (_, _) => SaveAll();

        var resetAll = UiKit.Button("↩ رجّع كل الاختصارات للافتراضي", Color.FromArgb(120, 128, 136), 290, 44);
        resetAll.Click += (_, _) => ResetAll();

        flow.Controls.Add(save);
        flow.Controls.Add(resetAll);
        bottom.Controls.Add(flow);

        // ترتيب الإضافة: عنصر الـ Fill أولًا ثم الحواف.
        Controls.Add(scroll);
        Controls.Add(intro);
        Controls.Add(bottom);
    }

    private static void AddHeaderRow(TableLayoutPanel table)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        string[] titles = { "الإجراء", "الاختصار (اضغط عليه وسجّل)", "الافتراضي", "", "" };
        for (int i = 0; i < titles.Length; i++)
        {
            table.Controls.Add(new Label
            {
                Text = titles[i],
                AutoSize = true,
                Anchor = i == 0 ? AnchorStyles.Right : AnchorStyles.None,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = UiKit.Muted,
                Margin = new Padding(6, 8, 6, 8)
            }, i, row);
        }
    }

    private static void AddGroupRow(TableLayoutPanel table, string title)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Height = 36,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.FromArgb(52, 73, 94),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            Padding = new Padding(0, 0, 12, 0),
            Margin = new Padding(0, 12, 0, 4)
        };
        table.Controls.Add(label, 0, row);
        table.SetColumnSpan(label, 5);
    }

    private void AddShortcutRow(TableLayoutPanel table, ShortcutDef def)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(new Label
        {
            Text = def.Title,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI", 10.5F),
            ForeColor = UiKit.Text,
            Margin = new Padding(6, 8, 6, 8)
        }, 0, row);

        var box = new KeyRecorderBox
        {
            Width = 200,
            Anchor = AnchorStyles.None,
            Margin = new Padding(6, 4, 6, 4)
        };
        box.Value = map[def.Id];
        box.Recorded += (_, _) => Assign(def, box.Value);
        tip.SetToolTip(box, "اضغط هنا ثم اضغط الزر أو التوليفة الجديدة على الكيبورد");
        boxes[def.Id] = box;
        table.Controls.Add(box, 1, row);

        var defaultLabel = new Label
        {
            Text = Shortcuts.Describe(def.Default),
            AutoSize = true,
            Anchor = AnchorStyles.None,
            ForeColor = UiKit.Muted,
            Font = new Font("Segoe UI", 9.5F),
            Margin = new Padding(6, 8, 6, 8)
        };
        defaultLabels[def.Id] = defaultLabel;
        table.Controls.Add(defaultLabel, 2, row);

        var restore = UiKit.Button("افتراضي", Color.FromArgb(120, 128, 136), 86, 32);
        restore.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        restore.Anchor = AnchorStyles.None;
        restore.Margin = new Padding(3, 3, 3, 3);
        restore.Click += (_, _) => Assign(def, def.Default);
        table.Controls.Add(restore, 3, row);

        var clear = UiKit.Button("إلغاء", UiKit.Danger, 86, 32);
        clear.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        clear.Anchor = AnchorStyles.None;
        clear.Margin = new Padding(3, 3, 3, 3);
        clear.Click += (_, _) =>
        {
            map[def.Id] = Keys.None;
            boxes[def.Id].SetValueSilently(Keys.None);
            RefreshStyles();
        };
        table.Controls.Add(clear, 4, row);
    }

    /// <summary> تسجيل اختصار جديد مع التعامل مع التعارض لو الزر مستخدم في إجراء تاني. </summary>
    private void Assign(ShortcutDef def, Keys key)
    {
        var conflicts = Shortcuts.FindConflicts(map, def.Id, key);
        if (conflicts.Count > 0)
        {
            string names = string.Join("\n", conflicts.Select(c => "• " + ShortcutGroups.TitleOf(c.Group) + " ← " + c.Title));
            var answer = MessageBox.Show(this,
                $"الاختصار «{Shortcuts.Describe(key)}» مستخدم بالفعل في:\n{names}\n\n" +
                $"تنقله إلى «{def.Title}» وتشيله من الإجراء اللي فوق؟",
                "اختصار مكرر", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

            if (answer != DialogResult.Yes)
            {
                // نرجّع المربع للقيمة اللي كانت قبل الضغطة.
                boxes[def.Id].SetValueSilently(map[def.Id]);
                return;
            }

            foreach (var c in conflicts)
            {
                map[c.Id] = Keys.None;
                boxes[c.Id].SetValueSilently(Keys.None);
            }
        }

        map[def.Id] = key;
        boxes[def.Id].SetValueSilently(key);
        RefreshStyles();
    }

    /// <summary> أي اختصار اتغير عن الافتراضي بيتعلّم بخط عريض ولون مختلف. </summary>
    private void RefreshStyles()
    {
        foreach (var def in Shortcuts.All)
        {
            if (!boxes.TryGetValue(def.Id, out var box)) continue;
            bool changed = map[def.Id] != def.Default;
            box.Font = new Font("Segoe UI", 11F, changed ? FontStyle.Bold : FontStyle.Regular);
            box.ForeColor = map[def.Id] == Keys.None
                ? UiKit.Muted
                : (changed ? UiKit.Primary : UiKit.Text);
            if (defaultLabels.TryGetValue(def.Id, out var label))
                label.Font = new Font("Segoe UI", 9.5F, changed ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    private void ResetAll()
    {
        if (MessageBox.Show(this, "ترجّع كل الاختصارات في البرنامج للأزرار الأصلية؟ (التغيير مش هيتطبق إلا بعد ما تدوس «حفظ»)", "تأكيد",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != DialogResult.Yes) return;

        foreach (var def in Shortcuts.All)
        {
            map[def.Id] = def.Default;
            boxes[def.Id].SetValueSilently(def.Default);
        }
        RefreshStyles();
    }

    private void SaveAll()
    {
        try
        {
            Shortcuts.SaveAll(map);
            MessageBox.Show(this, "تم الحفظ ✅\nالاختصارات الجديدة شغالة فورًا في كل الشاشات.", "تم الحفظ",
                MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر حفظ الاختصارات", ex);
            MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
