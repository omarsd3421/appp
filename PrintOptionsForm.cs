using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp
{
    internal sealed class PrintOptionsForm : Form
    {
        private readonly int pageCount;
        private readonly PrintOptions initialOptions;
        private readonly Action showPrinterProperties;

        private ComboBox cmbPageRange = null!;
        private TextBox txtPageRanges = null!;
        private Label lblRangeExample = null!;
        private ComboBox cmbPagesPerSheet = null!;
        private NumericUpDown numNUpGapInches = null!;
        private ComboBox cmbOrientation = null!;
        private ComboBox cmbDuplexBinding = null!;
        private ComboBox cmbScaling = null!;
        private NumericUpDown numCustomScale = null!;
        private ComboBox cmbPaperSize = null!;
        private NumericUpDown numLeftMargin = null!;
        private NumericUpDown numTopMargin = null!;
        private NumericUpDown numRightMargin = null!;
        private NumericUpDown numBottomMargin = null!;
        private CheckBox chkCenterHorizontal = null!;
        private CheckBox chkCenterVertical = null!;
        private CheckBox chkApplyToAll = null!;
        private Label lblRangeInfo = null!;
        private Label lblPrinterInfo = null!;
        private ComboBox cmbPrintTemplates = null!;

        public PrintOptions Options { get; private set; }
        public bool ApplyToAll { get; private set; }

        public PrintOptionsForm(
            string fileName,
            int pageCount,
            PrintOptions options,
            string printerName,
            IEnumerable<PaperSize> paperSizes,
            Action showPrinterProperties)
        {
            this.pageCount = Math.Max(1, pageCount);
            initialOptions = options.Clone();
            initialOptions.ClampToPageCount(this.pageCount);
            Options = initialOptions.Clone();
            this.showPrinterProperties = showPrinterProperties;

            Text = "خيارات الطباعة المتقدمة";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(960, 760);
            MinimumSize = new Size(900, 700);
            Font = new Font("Segoe UI", 10F);
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = Color.FromArgb(244, 246, 249);

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = Color.White,
                Padding = new Padding(18, 10, 18, 8)
            };

            Label title = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = "⚙️ خيارات الطباعة المتقدمة",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                TextAlign = ContentAlignment.MiddleRight
            };

            lblPrinterInfo = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                Text = $"الملف: {fileName}    |    الصفحات: {this.pageCount}    |    الطابعة: {printerName}",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(90, 98, 107),
                TextAlign = ContentAlignment.MiddleRight
            };

            header.Controls.Add(lblPrinterInfo);
            header.Controls.Add(title);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 12, 18, 8),
                ColumnCount = 2,
                RowCount = 12,
                BackColor = Color.FromArgb(244, 246, 249)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));

            for (int i = 0; i < 12; i++)
            {
                float rowHeight = i switch
                {
                    0 => 112f,
                    8 => 0f,
                    9 => 52f,
                    10 => 32f,
                    11 => 38f,
                    _ => 39f
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
            }

            Label L(string text) => new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(4, 11, 4, 4)
            };

            Control Wrap(Control c)
            {
                Panel p = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2) };
                c.Dock = DockStyle.None;
                c.Location = new Point(4, 5);
                c.Width = 330;
                p.Controls.Add(c);
                return p;
            }

            cmbPageRange = CreateCombo(330);
            cmbPageRange.Items.AddRange(new object[] { "كل الصفحات", "نطاق صفحات مخصص" });
            cmbPageRange.SelectedIndex = initialOptions.PageRangeMode == PrintPageRangeMode.AllPages ? 0 : 1;
            cmbPageRange.SelectedIndexChanged += (s, e) => UpdateRangeControls();

            txtPageRanges = new TextBox
            {
                Width = 330,
                Height = 34,
                Font = new Font("Segoe UI", 10F),
                BorderStyle = BorderStyle.FixedSingle,
                Text = string.IsNullOrWhiteSpace(initialOptions.PageRangesText)
                    ? BuildLegacyRangeText(initialOptions, this.pageCount)
                    : initialOptions.PageRangesText,
                RightToLeft = RightToLeft.No,
                TextAlign = HorizontalAlignment.Left
            };
            txtPageRanges.TextChanged += (s, e) => UpdateRangeInfo();

            lblRangeExample = new Label
            {
                AutoSize = false,
                Width = 330,
                Height = 28,
                Text = "مثال: 1-5,8-11,89-98   |   ويمكن: 1,4,9-12",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(90, 98, 107),
                TextAlign = ContentAlignment.MiddleRight
            };

            Panel rangePanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2) };
            cmbPageRange.Location = new Point(4, 4);
            txtPageRanges.Location = new Point(4, 43);
            lblRangeExample.Location = new Point(4, 77);
            rangePanel.Controls.Add(cmbPageRange);
            rangePanel.Controls.Add(txtPageRanges);
            rangePanel.Controls.Add(lblRangeExample);

            cmbPagesPerSheet = CreateCombo(330);
            cmbPagesPerSheet.Items.AddRange(new object[]
            {
                "1 صفحة في الورقة",
                "2 صفحتان في الورقة",
                "4 صفحات في الورقة",
                "6 صفحات في الورقة",
                "9 صفحات في الورقة",
                "16 صفحة في الورقة"
            });
            cmbPagesPerSheet.SelectedIndex = PagePerSheetIndex(initialOptions.PagesPerSheet);

            numNUpGapInches = CreateNumber(0, 2, (decimal)initialOptions.NUpGapInches, 330);
            numNUpGapInches.DecimalPlaces = 3;
            numNUpGapInches.Increment = 0.025M;
            numNUpGapInches.Enabled = initialOptions.PagesPerSheet > 1;
            cmbPagesPerSheet.SelectedIndexChanged += (s, e) =>
            {
                numNUpGapInches.Enabled = PagesPerSheetFromIndex(cmbPagesPerSheet.SelectedIndex) > 1;
                UpdateRangeInfo();
            };

            cmbOrientation = CreateCombo(330);
            cmbOrientation.Items.AddRange(new object[] { "تلقائي", "عمودي", "أفقي" });
            cmbOrientation.SelectedIndex = initialOptions.Orientation switch
            {
                PrintPageOrientation.Portrait => 1,
                PrintPageOrientation.Landscape => 2,
                _ => 0
            };

            cmbDuplexBinding = CreateCombo(330);
            cmbDuplexBinding.Items.AddRange(new object[] { "من الجنب (الحافة الطويلة) — افتراضي", "من فوق (الحافة القصيرة)" });
            cmbDuplexBinding.SelectedIndex = initialOptions.DuplexBinding == DuplexBindingEdge.ShortEdge ? 1 : 0;

            cmbScaling = CreateCombo(330);
            cmbScaling.Items.AddRange(new object[]
            {
                "ملء المنطقة القابلة للطباعة (Fit)",
                "الحجم الفعلي 100%",
                "مقياس مخصص %"
            });
            cmbScaling.SelectedIndex = initialOptions.Scaling switch
            {
                PrintScalingMode.ActualSize => 1,
                PrintScalingMode.Custom => 2,
                _ => 0
            };
            cmbScaling.SelectedIndexChanged += (s, e) => numCustomScale.Enabled = cmbScaling.SelectedIndex == 2;

            numCustomScale = CreateNumber(10, 500, (decimal)initialOptions.CustomScalePercent, 330);
            numCustomScale.DecimalPlaces = 0;
            numCustomScale.Increment = 5;
            numCustomScale.Enabled = initialOptions.Scaling == PrintScalingMode.Custom;

            cmbPaperSize = CreateCombo(330);
            cmbPaperSize.Items.Add("افتراضي (من الطابعة)");
            var paperList = paperSizes
                .GroupBy(p => p.PaperName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First().PaperName)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            foreach (string name in paperList)
                cmbPaperSize.Items.Add(name);

            int paperIndex = 0;
            if (!string.IsNullOrWhiteSpace(initialOptions.PaperName) && initialOptions.PaperName != "افتراضي")
            {
                for (int i = 1; i < cmbPaperSize.Items.Count; i++)
                {
                    if (string.Equals(cmbPaperSize.Items[i]?.ToString(), initialOptions.PaperName, StringComparison.OrdinalIgnoreCase))
                    {
                        paperIndex = i;
                        break;
                    }
                }
            }
            cmbPaperSize.SelectedIndex = paperIndex;

            Panel marginsPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2) };
            (numLeftMargin, numTopMargin, numRightMargin, numBottomMargin) = CreateMarginControls(marginsPanel, initialOptions);

            chkCenterHorizontal = new CheckBox
            {
                Text = "توسيط أفقي",
                Checked = initialOptions.CenterHorizontal,
                AutoSize = true,
                Location = new Point(4, 6),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            chkCenterVertical = new CheckBox
            {
                Text = "توسيط رأسي",
                Checked = initialOptions.CenterVertical,
                AutoSize = true,
                Location = new Point(145, 6),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            Panel centerPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2) };
            centerPanel.Controls.Add(chkCenterHorizontal);
            centerPanel.Controls.Add(chkCenterVertical);

            chkApplyToAll = new CheckBox
            {
                Text = "تطبيق إعدادات الصفحات / N-up / المسافة بين الصفحات / الهوامش على كل الملفات الموجودة",
                Checked = false,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 102),
                Padding = new Padding(6)
            };

            cmbPrintTemplates = CreateCombo(145);
            cmbPrintTemplates.DropDownStyle = ComboBoxStyle.DropDownList;
            RefreshPrintTemplateList();
            cmbPrintTemplates.Width = 150;
            var loadTemplate = MakeButton("تحميل القالب", Color.FromArgb(0, 123, 167), 150);
            loadTemplate.Height = 38; loadTemplate.Click += (s, e) => LoadSelectedPrintTemplate();
            var saveTemplate = MakeButton("حفظ كقالب", Color.FromArgb(40, 167, 69), 150);
            saveTemplate.Height = 38; saveTemplate.Click += (s, e) => SaveCurrentPrintTemplate();
            var deleteTemplate = MakeButton("حذف القالب", Color.FromArgb(180, 65, 65), 150);
            deleteTemplate.Height = 38; deleteTemplate.Click += (s, e) => DeleteSelectedPrintTemplate();

            // نستغل العمود الجانبي الفارغ لقوالب الطباعة بدل حشرها أفقيًا بين الخيارات.
            var templateSidebar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(8),
                BackColor = Color.White
            };
            templateSidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            templateSidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            templateSidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            templateSidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            templateSidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            templateSidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            var templateTitle = new Label
            {
                Text = "قوالب إعدادات الطباعة",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94)
            };
            cmbPrintTemplates.Dock = DockStyle.Fill;
            templateSidebar.Controls.Add(templateTitle, 0, 0);
            templateSidebar.Controls.Add(cmbPrintTemplates, 0, 1);
            templateSidebar.Controls.Add(loadTemplate, 0, 2);
            templateSidebar.Controls.Add(saveTemplate, 0, 3);
            templateSidebar.Controls.Add(deleteTemplate, 0, 4);

            lblRangeInfo = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(90, 98, 107)
            };

            layout.Controls.Add(L("نطاق الصفحات:"), 0, 0);
            layout.Controls.Add(rangePanel, 1, 0);
            layout.Controls.Add(L("صفحات في الورقة (N-up):"), 0, 1);
            layout.Controls.Add(Wrap(cmbPagesPerSheet), 1, 1);
            layout.Controls.Add(L("اتجاه الورق:"), 0, 2);
            layout.Controls.Add(Wrap(cmbOrientation), 1, 2);
            layout.Controls.Add(L("التحجيم:"), 0, 3);
            layout.Controls.Add(Wrap(cmbScaling), 1, 3);
            layout.Controls.Add(L("النسبة المخصصة:"), 0, 4);
            layout.Controls.Add(Wrap(numCustomScale), 1, 4);
            layout.Controls.Add(L("مقاس الورق:"), 0, 5);
            layout.Controls.Add(Wrap(cmbPaperSize), 1, 5);
            layout.Controls.Add(L("هوامش حواف الورقة (مم):"), 0, 6);
            layout.Controls.Add(marginsPanel, 1, 6);
            layout.Controls.Add(L("المسافة بين الصفحات (بوصة):"), 0, 7);
            layout.Controls.Add(Wrap(numNUpGapInches), 1, 7);
            layout.Controls.Add(L("تقليب الورق وش وظهر:"), 0, 9);
            layout.Controls.Add(Wrap(cmbDuplexBinding), 1, 9);
            layout.Controls.Add(lblRangeInfo, 0, 10);
            layout.SetColumnSpan(lblRangeInfo, 2);
            layout.Controls.Add(chkApplyToAll, 0, 11);
            layout.SetColumnSpan(chkApplyToAll, 2);

            Panel bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 74,
                BackColor = Color.White,
                Padding = new Padding(18, 10, 18, 10)
            };

            Button btnPrinterProperties = MakeButton("🖨️ خصائص الطابعة", Color.FromArgb(52, 73, 94), 150);
            btnPrinterProperties.Dock = DockStyle.Left;
            btnPrinterProperties.Click += (s, e) => this.showPrinterProperties();

            Button btnReset = MakeButton("↺ استعادة الافتراضي", Color.FromArgb(108, 117, 125), 145);
            btnReset.Dock = DockStyle.Left;
            btnReset.Click += (s, e) => ResetToDefaults();

            Button btnCancel = MakeButton("إلغاء", Color.FromArgb(108, 117, 125), 110);
            btnCancel.Dock = DockStyle.Right;
            btnCancel.DialogResult = DialogResult.Cancel;

            Button btnOk = MakeButton("✔ حفظ الإعدادات", Color.FromArgb(40, 167, 69), 155);
            btnOk.Dock = DockStyle.Right;
            btnOk.Margin = new Padding(0, 0, 8, 0);
            btnOk.Click += (s, e) => SaveAndClose();

            bottom.Controls.Add(btnPrinterProperties);
            bottom.Controls.Add(btnReset);
            bottom.Controls.Add(btnCancel);
            bottom.Controls.Add(btnOk);

            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1,
                IsSplitterFixed = true,
                SplitterDistance = 178,
                Panel1MinSize = 166,
                Panel2MinSize = 620,
                RightToLeft = RightToLeft.Yes,
                BackColor = Color.FromArgb(224, 228, 233)
            };
            mainSplit.Panel1.Controls.Add(templateSidebar);
            mainSplit.Panel2.Controls.Add(layout);

            Controls.Add(mainSplit);
            Controls.Add(header);
            Controls.Add(bottom);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            UpdateRangeControls();
            UpdateRangeInfo();
        }

        private void RefreshPrintTemplateList(string? preferred = null)
        {
            if (cmbPrintTemplates == null) return;
            cmbPrintTemplates.BeginUpdate();
            cmbPrintTemplates.Items.Clear();
            foreach (string name in AppOptions.GetPrintTemplateNames()) cmbPrintTemplates.Items.Add(name);
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                for (int i = 0; i < cmbPrintTemplates.Items.Count; i++)
                    if (string.Equals(cmbPrintTemplates.Items[i]?.ToString(), preferred, StringComparison.OrdinalIgnoreCase))
                    { cmbPrintTemplates.SelectedIndex = i; break; }
            }
            if (cmbPrintTemplates.SelectedIndex < 0 && cmbPrintTemplates.Items.Count > 0) cmbPrintTemplates.SelectedIndex = 0;
            cmbPrintTemplates.EndUpdate();
        }

        private PrintOptions CaptureTemplateOptions()
        {
            return new PrintOptions
            {
                PageRangeMode = PrintPageRangeMode.AllPages,
                FromPage = 1, ToPage = 1, PageRangesText = string.Empty,
                PagesPerSheet = PagesPerSheetFromIndex(cmbPagesPerSheet.SelectedIndex),
                NUpGapInches = (float)numNUpGapInches.Value,
                Orientation = cmbOrientation.SelectedIndex switch { 1 => PrintPageOrientation.Portrait, 2 => PrintPageOrientation.Landscape, _ => PrintPageOrientation.Auto },
                DuplexBinding = cmbDuplexBinding.SelectedIndex == 1 ? DuplexBindingEdge.ShortEdge : DuplexBindingEdge.LongEdge,
                Scaling = cmbScaling.SelectedIndex switch { 1 => PrintScalingMode.ActualSize, 2 => PrintScalingMode.Custom, _ => PrintScalingMode.FitToPrintableArea },
                CustomScalePercent = (float)numCustomScale.Value,
                MarginLeftMm = (float)numLeftMargin.Value, MarginTopMm = (float)numTopMargin.Value,
                MarginRightMm = (float)numRightMargin.Value, MarginBottomMm = (float)numBottomMargin.Value,
                CenterHorizontal = chkCenterHorizontal.Checked, CenterVertical = chkCenterVertical.Checked,
                PaperName = cmbPaperSize.SelectedIndex <= 0 ? "افتراضي" : cmbPaperSize.SelectedItem?.ToString() ?? "افتراضي"
            };
        }

        private void LoadSelectedPrintTemplate()
        {
            string? name = cmbPrintTemplates.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(name) || !AppOptions.TryGetPrintTemplate(name, out var options))
            {
                MessageBox.Show(this, "اختر قالبًا محفوظًا أولًا.", "قوالب الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            cmbPageRange.SelectedIndex = 0; txtPageRanges.Clear();
            cmbPagesPerSheet.SelectedIndex = PagePerSheetIndex(options.PagesPerSheet);
            numNUpGapInches.Value = Math.Max(numNUpGapInches.Minimum, Math.Min(numNUpGapInches.Maximum, (decimal)options.NUpGapInches));
            cmbOrientation.SelectedIndex = options.Orientation switch { PrintPageOrientation.Portrait => 1, PrintPageOrientation.Landscape => 2, _ => 0 };
            cmbDuplexBinding.SelectedIndex = options.DuplexBinding == DuplexBindingEdge.ShortEdge ? 1 : 0;
            cmbScaling.SelectedIndex = options.Scaling switch { PrintScalingMode.ActualSize => 1, PrintScalingMode.Custom => 2, _ => 0 };
            numCustomScale.Value = Math.Max(numCustomScale.Minimum, Math.Min(numCustomScale.Maximum, (decimal)options.CustomScalePercent));
            numLeftMargin.Value = Math.Max(numLeftMargin.Minimum, Math.Min(numLeftMargin.Maximum, (decimal)options.MarginLeftMm));
            numTopMargin.Value = Math.Max(numTopMargin.Minimum, Math.Min(numTopMargin.Maximum, (decimal)options.MarginTopMm));
            numRightMargin.Value = Math.Max(numRightMargin.Minimum, Math.Min(numRightMargin.Maximum, (decimal)options.MarginRightMm));
            numBottomMargin.Value = Math.Max(numBottomMargin.Minimum, Math.Min(numBottomMargin.Maximum, (decimal)options.MarginBottomMm));
            chkCenterHorizontal.Checked = options.CenterHorizontal; chkCenterVertical.Checked = options.CenterVertical;
            int paper = 0;
            for (int i = 1; i < cmbPaperSize.Items.Count; i++)
                if (string.Equals(cmbPaperSize.Items[i]?.ToString(), options.PaperName, StringComparison.OrdinalIgnoreCase)) { paper = i; break; }
            cmbPaperSize.SelectedIndex = paper;
            UpdateRangeControls(); UpdateRangeInfo();
        }

        private void SaveCurrentPrintTemplate()
        {
            using var prompt = new Form
            {
                Text = "حفظ قالب طباعة",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                Size = new Size(390, 165), RightToLeft = RightToLeft.Yes, RightToLeftLayout = true,
                Font = new Font("Segoe UI", 9.5F), BackColor = Color.White
            };
            var label = new Label { Text = "اسم القالب (مثال: ملازم — وجهين):", Dock = DockStyle.Top, Height = 38, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(8, 4, 8, 0) };
            var nameBox = new TextBox { Dock = DockStyle.Top, Margin = new Padding(8), MaxLength = 50, Text = cmbPrintTemplates.SelectedItem?.ToString() ?? string.Empty };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 4, 8, 4) };
            var save = MakeButton("حفظ", Color.FromArgb(40, 167, 69), 90); save.DialogResult = DialogResult.OK;
            var cancel = MakeButton("إلغاء", Color.FromArgb(108, 117, 125), 90); cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            prompt.Controls.Add(buttons); prompt.Controls.Add(nameBox); prompt.Controls.Add(label);
            prompt.AcceptButton = save; prompt.CancelButton = cancel;
            if (prompt.ShowDialog(this) != DialogResult.OK) return;
            string name = nameBox.Text.Trim();
            if (name.Length < 2) { MessageBox.Show(this, "اكتب اسمًا واضحًا للقالب (حرفان على الأقل).", "قوالب الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            bool exists = AppOptions.GetPrintTemplateNames().Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            if (exists && MessageBox.Show(this, "يوجد قالب بنفس الاسم. هل تريد استبداله؟", "تأكيد الاستبدال", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { AppOptions.SavePrintTemplate(name, CaptureTemplateOptions()); RefreshPrintTemplateList(name); }
            catch (Exception ex) { MessageBox.Show(this, "تعذر حفظ القالب: " + ex.Message, "قوالب الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void DeleteSelectedPrintTemplate()
        {
            string? name = cmbPrintTemplates.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (MessageBox.Show(this, $"حذف قالب «{name}»؟", "تأكيد الحذف", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { AppOptions.DeletePrintTemplate(name); RefreshPrintTemplateList(); }
            catch (Exception ex) { MessageBox.Show(this, "تعذر حذف القالب: " + ex.Message, "قوالب الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void ResetToDefaults()
        {
            cmbPageRange.SelectedIndex = 0;
            txtPageRanges.Text = string.Empty;
            cmbPagesPerSheet.SelectedIndex = PagePerSheetIndex(1);
            numNUpGapInches.Value = 0.200M;
            cmbOrientation.SelectedIndex = 0;
            cmbScaling.SelectedIndex = 0;
            numCustomScale.Value = 100M;
            if (cmbPaperSize.Items.Count > 0) cmbPaperSize.SelectedIndex = 0;
            numLeftMargin.Value = 0M;
            numTopMargin.Value = 0M;
            numRightMargin.Value = 0M;
            numBottomMargin.Value = 0M;
            chkCenterHorizontal.Checked = true;
            chkCenterVertical.Checked = true;
            cmbDuplexBinding.SelectedIndex = 0;
            chkApplyToAll.Checked = false;
            UpdateRangeControls();
            UpdateRangeInfo();
        }

        private void SaveAndClose()
        {
            bool allPages = cmbPageRange.SelectedIndex == 0;
            string rangeText = txtPageRanges.Text.Trim();

            if (!allPages && !PageRangeParser.TryParse(rangeText, pageCount, out _, out string error))
            {
                MessageBox.Show(
                    error + "\n\nالصيغة الصحيحة مثل: 1-5,8-11,89-98",
                    "نطاق صفحات غير صحيح",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                txtPageRanges.Focus();
                return;
            }

            List<int> parsed;
            if (allPages)
            {
                parsed = Enumerable.Range(0, pageCount).ToList();
            }
            else if (!PageRangeParser.TryParse(rangeText, pageCount, out parsed, out _))
            {
                // تم التحقق من النطاق أعلاه، وهذا المسار احتياطي فقط.
                return;
            }

            int fromPage = parsed.Count > 0 ? parsed.Min() + 1 : 1;
            int toPage = parsed.Count > 0 ? parsed.Max() + 1 : pageCount;

            int nUp = PagesPerSheetFromIndex(cmbPagesPerSheet.SelectedIndex);
            PrintOptions result = new PrintOptions
            {
                PageRangeMode = allPages ? PrintPageRangeMode.AllPages : PrintPageRangeMode.Range,
                FromPage = fromPage,
                ToPage = toPage,
                PageRangesText = allPages ? "" : rangeText,
                PagesPerSheet = nUp,
                Orientation = cmbOrientation.SelectedIndex switch
                {
                    1 => PrintPageOrientation.Portrait,
                    2 => PrintPageOrientation.Landscape,
                    _ => PrintPageOrientation.Auto
                },
                DuplexBinding = cmbDuplexBinding.SelectedIndex == 1
                    ? DuplexBindingEdge.ShortEdge
                    : DuplexBindingEdge.LongEdge,
                Scaling = cmbScaling.SelectedIndex switch
                {
                    1 => PrintScalingMode.ActualSize,
                    2 => PrintScalingMode.Custom,
                    _ => PrintScalingMode.FitToPrintableArea
                },
                CustomScalePercent = (float)numCustomScale.Value,
                NUpGapInches = (float)numNUpGapInches.Value,
                MarginLeftMm = (float)numLeftMargin.Value,
                MarginTopMm = (float)numTopMargin.Value,
                MarginRightMm = (float)numRightMargin.Value,
                MarginBottomMm = (float)numBottomMargin.Value,
                CenterHorizontal = chkCenterHorizontal.Checked,
                CenterVertical = chkCenterVertical.Checked,
                PaperName = cmbPaperSize.SelectedIndex <= 0
                    ? "افتراضي"
                    : cmbPaperSize.SelectedItem?.ToString() ?? "افتراضي"
            };

            result.ClampToPageCount(pageCount);
            Options = result;
            ApplyToAll = chkApplyToAll.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateRangeControls()
        {
            bool custom = cmbPageRange.SelectedIndex == 1 && pageCount > 1;
            txtPageRanges.Enabled = custom;
            lblRangeExample.Enabled = custom;
            UpdateRangeInfo();
        }

        private void UpdateRangeInfo()
        {
            int selected;
            string detail;

            if (cmbPageRange.SelectedIndex == 0)
            {
                selected = pageCount;
                detail = "كل الصفحات";
            }
            else if (PageRangeParser.TryParse(txtPageRanges.Text, pageCount, out List<int> indexes, out string error))
            {
                selected = indexes.Count;
                detail = txtPageRanges.Text.Trim();
            }
            else
            {
                selected = 0;
                detail = error;
            }

            int nUp = PagesPerSheetFromIndex(cmbPagesPerSheet.SelectedIndex);
            int printPages = selected > 0
                ? (int)Math.Ceiling(selected / (double)Math.Max(1, nUp))
                : 0;

            lblRangeInfo.Text =
                selected > 0
                    ? $"النطاق: {detail}  |  سيتم طباعة {selected} صفحة فعلية، بمعدل {nUp} صفحة لكل وجه مطبوع، أي حوالي {printPages} أوجه."
                    : $"النطاق غير صالح: {detail}";

            lblRangeInfo.ForeColor = selected > 0
                ? Color.FromArgb(90, 98, 107)
                : Color.FromArgb(192, 57, 43);
        }

        private static string BuildLegacyRangeText(PrintOptions options, int pageCount)
        {
            if (options.PageRangeMode == PrintPageRangeMode.AllPages)
                return pageCount > 1 ? $"1-{pageCount}" : "1";

            int from = Math.Max(1, options.FromPage);
            int to = Math.Max(from, options.ToPage);
            return from == to ? from.ToString() : $"{from}-{to}";
        }

        private static ComboBox CreateCombo(int width)
        {
            return new ComboBox
            {
                Width = width,
                Height = 34,
                Font = new Font("Segoe UI", 10F),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };
        }

        private static NumericUpDown CreateNumber(decimal min, decimal max, decimal value, int width)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(value, min, max),
                Width = width,
                Height = 34,
                Font = new Font("Segoe UI", 10F),
                TextAlign = HorizontalAlignment.Center
            };
        }

        private static (NumericUpDown Left, NumericUpDown Top, NumericUpDown Right, NumericUpDown Bottom)
            CreateMarginControls(Panel panel, PrintOptions options)
        {
            NumericUpDown left = CreateNumber(0, 50, (decimal)options.MarginLeftMm, 62);
            NumericUpDown top = CreateNumber(0, 50, (decimal)options.MarginTopMm, 62);
            NumericUpDown right = CreateNumber(0, 50, (decimal)options.MarginRightMm, 62);
            NumericUpDown bottom = CreateNumber(0, 50, (decimal)options.MarginBottomMm, 62);

            Label l1 = SmallLabel("يمين");
            Label l2 = SmallLabel("أعلى");
            Label l3 = SmallLabel("يسار");
            Label l4 = SmallLabel("أسفل");

            right.Location = new Point(260, 18);
            l1.Location = new Point(266, 0);
            top.Location = new Point(176, 18);
            l2.Location = new Point(182, 0);
            left.Location = new Point(92, 18);
            l3.Location = new Point(98, 0);
            bottom.Location = new Point(8, 18);
            l4.Location = new Point(14, 0);

            panel.Controls.Add(right);
            panel.Controls.Add(top);
            panel.Controls.Add(left);
            panel.Controls.Add(bottom);
            panel.Controls.Add(l1);
            panel.Controls.Add(l2);
            panel.Controls.Add(l3);
            panel.Controls.Add(l4);

            return (left, top, right, bottom);
        }

        private static Label SmallLabel(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };

        private static Button MakeButton(string text, Color color, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 42,
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
        }

        private static int PagePerSheetIndex(int n)
        {
            return n switch
            {
                2 => 1,
                4 => 2,
                6 => 3,
                9 => 4,
                16 => 5,
                _ => 0
            };
        }

        private static int PagesPerSheetFromIndex(int index)
        {
            return index switch
            {
                1 => 2,
                2 => 4,
                3 => 6,
                4 => 9,
                5 => 16,
                _ => 1
            };
        }
    }
}
