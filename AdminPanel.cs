using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LibraryPrintApp
{
    internal sealed class PricingSettings
    {
        public decimal StudentBlack { get; set; } = 1.00m;
        public decimal StudentColor { get; set; } = 3.00m;
        public decimal StudentDuplexBlack { get; set; } = 0.75m;
        public decimal StudentDuplexColor { get; set; } = 2.50m;

        public decimal NormalBlack { get; set; } = 2.00m;
        public decimal NormalColor { get; set; } = 5.00m;
        public decimal NormalDuplexBlack { get; set; } = 1.00m;
        public decimal NormalDuplexColor { get; set; } = 4.00m;

        // أسعار الفئة المخصصة: يمكن تعديلها مباشرة من شاشات الطباعة والتصوير
        // بدون الدخول إلى إعدادات الأدمن. تحفظ تلقائيًا في pricing.json.
        public decimal CustomBlack { get; set; } = 2.00m;
        public decimal CustomColor { get; set; } = 5.00m;
        public decimal CustomDuplexBlack { get; set; } = 1.00m;
        public decimal CustomDuplexColor { get; set; } = 4.00m;

        // أسعار التصوير منفصلة تمامًا عن أسعار الطباعة.
        public decimal CopyStudentBlack { get; set; } = 1.00m;
        public decimal CopyStudentColor { get; set; } = 3.00m;
        public decimal CopyStudentDuplexBlack { get; set; } = 0.75m;
        public decimal CopyStudentDuplexColor { get; set; } = 2.50m;

        public decimal CopyNormalBlack { get; set; } = 2.00m;
        public decimal CopyNormalColor { get; set; } = 5.00m;
        public decimal CopyNormalDuplexBlack { get; set; } = 1.00m;
        public decimal CopyNormalDuplexColor { get; set; } = 4.00m;

        public decimal CopyCustomBlack { get; set; } = 2.00m;
        public decimal CopyCustomColor { get; set; } = 5.00m;
        public decimal CopyCustomDuplexBlack { get; set; } = 1.00m;
        public decimal CopyCustomDuplexColor { get; set; } = 4.00m;

        // أقصى نسبة خصم يمكن استخدامها في شاشة المبيعات.
        // (الاسم قديم عشان ملف pricing.json الحالي يفضل شغال.)
        public decimal MaxDiscountPercent { get; set; } = 20.00m;

        // أقصى نسبة خصم في الطباعة والتصوير (منفصلة عن المبيعات).
        public decimal MaxPrintDiscountPercent { get; set; } = 10.00m;
    }

    internal static class PricingStore
    {
        private static readonly string DirectoryPath =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LibraryPrintApp");

        private static readonly string FilePath =
            System.IO.Path.Combine(DirectoryPath, "pricing.json");

        public static PricingSettings Load()
        {
            try
            {
                if (!System.IO.File.Exists(FilePath))
                    return new PricingSettings();

                string json = System.IO.File.ReadAllText(FilePath);
                PricingSettings? value =
                    System.Text.Json.JsonSerializer.Deserialize<PricingSettings>(json);

                return value ?? new PricingSettings();
            }
            catch
            {
                return new PricingSettings();
            }
        }

        public static void Save(PricingSettings settings)
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);

            string json =
                System.Text.Json.JsonSerializer.Serialize(
                    settings,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            System.IO.File.WriteAllText(FilePath, json);
        }
    }

    internal sealed class AdminPanelForm : Form
    {
        private readonly PricingSettings pricing;
        private readonly string currentUser;
        private readonly Action<PricingSettings> applyPricing;

        private NumericUpDown studentBlack = null!;
        private NumericUpDown studentColor = null!;
        private NumericUpDown studentDuplexBlack = null!;
        private NumericUpDown studentDuplexColor = null!;

        private NumericUpDown normalBlack = null!;
        private NumericUpDown normalColor = null!;
        private NumericUpDown normalDuplexBlack = null!;
        private NumericUpDown normalDuplexColor = null!;

        private NumericUpDown copyStudentBlack = null!;
        private NumericUpDown copyStudentColor = null!;
        private NumericUpDown copyStudentDuplexBlack = null!;
        private NumericUpDown copyStudentDuplexColor = null!;
        private NumericUpDown copyNormalBlack = null!;
        private NumericUpDown copyNormalColor = null!;
        private NumericUpDown copyNormalDuplexBlack = null!;
        private NumericUpDown copyNormalDuplexColor = null!;

        private NumericUpDown maxDiscountPercent = null!;
        private NumericUpDown maxPrintDiscountPercent = null!;

        private TextBox txtNewUserPassword = null!;
        private TextBox txtNewAdminPassword = null!;
        private NumericUpDown opayOpening = null!, opayCurrent = null!;
        private NumericUpDown beeOpening = null!, beeCurrent = null!;
        private readonly Dictionary<string, NumericUpDown> walletOpening = new();
        private readonly Dictionary<string, NumericUpDown> walletCurrent = new();

        public AdminPanelForm(
            string userName,
            PricingSettings currentPricing,
            Action<PricingSettings> applyPricing)
        {
            currentUser = userName;
            pricing = currentPricing;
            this.applyPricing = applyPricing;

            BuildUi();
        }

        private void BuildUi()
        {
            Text = "إعدادات الإدارة";
            Size = new Size(1280, 850);
            MinimumSize = new Size(1050, 720);
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = Color.FromArgb(244, 246, 249);
            Font = new Font("Segoe UI", 10.5F);
            UiKit.EnableEscape(this);

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Color.White
            };

            Label title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "🔐 لوحة تحكم الإدارة",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                TextAlign = ContentAlignment.MiddleCenter
            };

            header.Controls.Add(title);

            TabControl tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(16, 8)
            };

            TabPage pricesTab = new TabPage("💰 الأسعار");
            TabPage accountsTab = new TabPage("💳 الماكينات والحسابات");
            TabPage usersTab = new TabPage("🔑 كلمات المرور");
            TabPage historyTab = new TabPage("🗂️ السجل");
            TabPage resetTab = new TabPage("🧹 تهيئة البيانات");
            TabPage systemTab = new TabPage("💾 النظام والنسخ الاحتياطي");
            TabPage optionsTab = new TabPage("🧩 القوائم والاختيارات");
            TabPage shortcutsTab = new TabPage("⌨️ الاختصارات");

            BuildPricesTab(pricesTab);
            BuildAccountsTab(accountsTab);
            BuildUsersTab(usersTab);
            BuildHistoryTab(historyTab);
            BuildResetTab(resetTab);
            BuildSystemTab(systemTab);
            optionsTab.Controls.Add(new OptionsEditorControl());
            shortcutsTab.Controls.Add(new ShortcutsEditorControl());

            tabs.TabPages.Add(pricesTab);
            tabs.TabPages.Add(accountsTab);
            tabs.TabPages.Add(usersTab);
            tabs.TabPages.Add(historyTab);
            tabs.TabPages.Add(resetTab);
            tabs.TabPages.Add(optionsTab);
            tabs.TabPages.Add(shortcutsTab);
            tabs.TabPages.Add(systemTab);

            Controls.Add(tabs);
            Controls.Add(header);
        }

        private void BuildPricesTab(TabPage page)
        {
            Panel body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(18),
                BackColor = Color.FromArgb(244, 246, 249)
            };

            Label note = new Label
            {
                Text = "إدارة أسعار الطباعة والتصوير منفصلة. أسعار الطلبة والأسعار العادية يتم التحكم بها من هنا، بينما الفئة المخصصة يمكن تعديلها مباشرة من شاشة الخدمة.",
                Left = 18,
                Top = 10,
                Width = 1080,
                Height = 48,
                AutoSize = false,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                TextAlign = ContentAlignment.MiddleCenter
            };

            GroupBox printBox = new GroupBox
            {
                Text = "🖨️ أسعار الطباعة",
                Left = 18,
                Top = 68,
                Width = 535,
                Height = 430,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold)
            };

            GroupBox printStudent = CreatePriceGroup(
                "أسعار الطلبة",
                18, 38, 495,
                pricing.StudentBlack, pricing.StudentColor,
                pricing.StudentDuplexBlack, pricing.StudentDuplexColor,
                out studentBlack, out studentColor,
                out studentDuplexBlack, out studentDuplexColor);

            GroupBox printNormal = CreatePriceGroup(
                "الأسعار العادية",
                18, 235, 495,
                pricing.NormalBlack, pricing.NormalColor,
                pricing.NormalDuplexBlack, pricing.NormalDuplexColor,
                out normalBlack, out normalColor,
                out normalDuplexBlack, out normalDuplexColor);

            printBox.Controls.Add(printStudent);
            printBox.Controls.Add(printNormal);

            GroupBox copyBox = new GroupBox
            {
                Text = "📄 أسعار التصوير",
                Left = 571,
                Top = 68,
                Width = 535,
                Height = 430,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold)
            };

            GroupBox copyStudent = CreatePriceGroup(
                "أسعار الطلبة",
                18, 38, 495,
                pricing.CopyStudentBlack, pricing.CopyStudentColor,
                pricing.CopyStudentDuplexBlack, pricing.CopyStudentDuplexColor,
                out copyStudentBlack, out copyStudentColor,
                out copyStudentDuplexBlack, out copyStudentDuplexColor);

            GroupBox copyNormal = CreatePriceGroup(
                "الأسعار العادية",
                18, 235, 495,
                pricing.CopyNormalBlack, pricing.CopyNormalColor,
                pricing.CopyNormalDuplexBlack, pricing.CopyNormalDuplexColor,
                out copyNormalBlack, out copyNormalColor,
                out copyNormalDuplexBlack, out copyNormalDuplexColor);

            copyBox.Controls.Add(copyStudent);
            copyBox.Controls.Add(copyNormal);

            GroupBox salesBox = new GroupBox
            {
                Text = "🏷️ حدود الخصم (لكل قسم على حدة)",
                Left = 18,
                Top = 512,
                Width = 1088,
                Height = 145,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold)
            };

            maxDiscountPercent = CreatePriceEditor(Math.Min(100m, Math.Max(0m, pricing.MaxDiscountPercent)));
            maxDiscountPercent.Maximum = 100;
            maxDiscountPercent.Increment = 0.5m;
            maxDiscountPercent.Left = 220;
            maxDiscountPercent.Top = 36;

            var maxDiscountLabel = new Label
            {
                Text = "أقصى خصم للمبيعات (%)",
                Left = 25,
                Top = 40,
                Width = 180,
                Height = 30,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            var discountNote = new Label
            {
                Text = "النسبة التي يستطيع مستخدم نقطة البيع تطبيقها. الأدمن فقط يستطيع رفع الحد الأقصى.",
                Left = 375,
                Top = 34,
                Width = 650,
                Height = 48,
                AutoSize = false,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(90, 98, 107),
                TextAlign = ContentAlignment.MiddleRight
            };

            maxPrintDiscountPercent = CreatePriceEditor(Math.Min(100m, Math.Max(0m, pricing.MaxPrintDiscountPercent)));
            maxPrintDiscountPercent.Maximum = 100;
            maxPrintDiscountPercent.Increment = 0.5m;
            maxPrintDiscountPercent.Left = 220;
            maxPrintDiscountPercent.Top = 86;

            var maxPrintDiscountLabel = new Label
            {
                Text = "أقصى خصم للطباعة والتصوير (%)",
                Left = 25,
                Top = 90,
                Width = 195,
                Height = 30,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            var printDiscountNote = new Label
            {
                Text = "الحد الأقصى للخصم في شاشتي الطباعة والتصوير (النسبة أو المبلغ بالجنيه، الاتنين محكومين بنفس الحد).",
                Left = 375,
                Top = 80,
                Width = 650,
                Height = 48,
                AutoSize = false,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(90, 98, 107),
                TextAlign = ContentAlignment.MiddleRight
            };

            salesBox.Controls.Add(maxDiscountLabel);
            salesBox.Controls.Add(maxDiscountPercent);
            salesBox.Controls.Add(discountNote);
            salesBox.Controls.Add(maxPrintDiscountLabel);
            salesBox.Controls.Add(maxPrintDiscountPercent);
            salesBox.Controls.Add(printDiscountNote);

            Button save = MakeButton("💾 حفظ الأسعار وحدود الخصم", Color.FromArgb(40, 167, 69));
            save.Width = 330;
            save.Height = 48;
            save.Left = 18;
            save.Top = 675;
            save.Click += (s, e) =>
            {
                pricing.StudentBlack = studentBlack.Value;
                pricing.StudentColor = studentColor.Value;
                pricing.StudentDuplexBlack = studentDuplexBlack.Value;
                pricing.StudentDuplexColor = studentDuplexColor.Value;

                pricing.NormalBlack = normalBlack.Value;
                pricing.NormalColor = normalColor.Value;
                pricing.NormalDuplexBlack = normalDuplexBlack.Value;
                pricing.NormalDuplexColor = normalDuplexColor.Value;

                pricing.CopyStudentBlack = copyStudentBlack.Value;
                pricing.CopyStudentColor = copyStudentColor.Value;
                pricing.CopyStudentDuplexBlack = copyStudentDuplexBlack.Value;
                pricing.CopyStudentDuplexColor = copyStudentDuplexColor.Value;
                pricing.CopyNormalBlack = copyNormalBlack.Value;
                pricing.CopyNormalColor = copyNormalColor.Value;
                pricing.CopyNormalDuplexBlack = copyNormalDuplexBlack.Value;
                pricing.CopyNormalDuplexColor = copyNormalDuplexColor.Value;

                pricing.MaxDiscountPercent = maxDiscountPercent.Value;
                pricing.MaxPrintDiscountPercent = maxPrintDiscountPercent.Value;

                try
                {
                    PricingStore.Save(pricing);
                    applyPricing(pricing);
                    LogAdminAction("تعديل", "الأسعار", "تم تحديث أسعار الطباعة والتصوير والمبيعات وحدود الخصم.");
                    MessageBox.Show(this, "تم حفظ الأسعار وحدود الخصم بنجاح.", "تم الحفظ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "تعذر حفظ الأسعار:\n\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            body.Controls.Add(note);
            body.Controls.Add(printBox);
            body.Controls.Add(copyBox);
            body.Controls.Add(salesBox);
            body.Controls.Add(save);
            page.Controls.Add(body);
            WheelScrollFix.Apply(body);
        }

        private GroupBox CreatePriceGroup(
            string title,
            int left,
            int top,
            int width,
            decimal black,
            decimal color,
            decimal duplexBlack,
            decimal duplexColor,
            out NumericUpDown blackControl,
            out NumericUpDown colorControl,
            out NumericUpDown duplexBlackControl,
            out NumericUpDown duplexColorControl)
        {
            GroupBox box = new GroupBox
            {
                Text = title,
                Left = left,
                Top = top,
                Width = width,
                Height = 182,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
            };

            blackControl = CreatePriceEditor(black);
            colorControl = CreatePriceEditor(color);
            duplexBlackControl = CreatePriceEditor(duplexBlack);
            duplexColorControl = CreatePriceEditor(duplexColor);

            AddPriceRow(box, "أسود - وش واحد", 24, blackControl);
            AddPriceRow(box, "ألوان - وش واحد", 62, colorControl);
            AddPriceRow(box, "أسود - وش وظهر", 100, duplexBlackControl);
            AddPriceRow(box, "ألوان - وش وظهر", 138, duplexColorControl);

            return box;
        }

        private static NumericUpDown CreatePriceEditor(decimal value)
        {
            return new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100000,
                DecimalPlaces = 2,
                Increment = 0.25m,
                Value = Math.Min(100000m, Math.Max(0m, value)),
                Width = 130,
                Height = 32,
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 11F)
            };
        }

        private static void AddPriceRow(Control parent, string labelText, int top, NumericUpDown control)
        {
            Label label = new Label
            {
                Text = labelText,
                Left = 18,
                Top = top + 5,
                Width = 260,
                Height = 26,
                AutoSize = false,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            control.Left = 300;
            control.Top = top;
            parent.Controls.Add(label);
            parent.Controls.Add(control);
        }

        private void BuildAccountsTab(TabPage page)
        {
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20),
                BackColor = Color.FromArgb(244, 246, 249),
                ColumnCount = 1,
                RowCount = 4,
                RightToLeft = RightToLeft.Yes
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 250F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 250F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));

            var note = new Label
            {
                Text = "من هنا تتحكم في الرصيد الافتتاحي والرصيد الحالي لماكينتي OPay وباب رزق، وكذلك حسابات Vodafone Cash وFawry وInstaPay. أي عملية جديدة تعدّل الرصيد الحالي تلقائيًا.",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                Margin = new Padding(0, 0, 0, 8)
            };
            body.Controls.Add(note, 0, 0);

            using var c = AppDatabase.OpenConnection();
            var op = AppDatabase.GetMachineAccount(c, "OPay");
            var bee = AppDatabase.GetMachineAccount(c, "BEE");

            var machines = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 8),
                RightToLeft = RightToLeft.Yes
            };
            machines.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            machines.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var opBox = CreateAccountBox("📲 ماكينة OPay", op.opening, op.current, out opayOpening, out opayCurrent);
            var beeBox = CreateAccountBox("📟 ماكينة باب رزق (BEE)", bee.opening, bee.current, out beeOpening, out beeCurrent);
            machines.Controls.Add(opBox, 0, 0);
            machines.Controls.Add(beeBox, 1, 0);
            body.Controls.Add(machines, 0, 1);

            string[] providers = { "Vodafone Cash", "Fawry", "InstaPay" };
            var wallets = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 8),
                RightToLeft = RightToLeft.Yes
            };
            for (int i = 0; i < 3; i++)
                wallets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));

            for (int i = 0; i < providers.Length; i++)
            {
                string provider = providers[i];
                var account = AppDatabase.GetWalletAccount(c, provider);
                var box = CreateWalletBox(provider, account.opening, account.current, out var opening, out var current);
                walletOpening[provider] = opening;
                walletCurrent[provider] = current;
                wallets.Controls.Add(box, i, 0);
            }
            body.Controls.Add(wallets, 0, 2);

            var savePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            var save = MakeButton("💾 حفظ أرصدة الماكينات والحسابات", Color.FromArgb(40, 167, 69));
            save.Dock = DockStyle.Left;
            save.Width = 360;
            save.Height = 48;

            save.Click += (s, e) =>
            {
                try
                {
                    using var db = AppDatabase.OpenConnection();
                    using var tx = db.BeginTransaction();
                    AppDatabase.SaveMachineAccount(db, "OPay", "OPay", opayOpening.Value, opayCurrent.Value, tx);
                    AppDatabase.SaveMachineAccount(db, "BEE", "باب رزق", beeOpening.Value, beeCurrent.Value, tx);

                    foreach (string provider in providers)
                        AppDatabase.SaveWalletAccount(
                            db,
                            provider,
                            walletOpening[provider].Value,
                            walletCurrent[provider].Value,
                            tx);

                    string balanceDetails = $"OPay افتتاحي={opayOpening.Value:0.00} حالي={opayCurrent.Value:0.00}; باب رزق افتتاحي={beeOpening.Value:0.00} حالي={beeCurrent.Value:0.00}; " +
                        string.Join("; ", providers.Select(provider => $"{provider} افتتاحي={walletOpening[provider].Value:0.00} حالي={walletCurrent[provider].Value:0.00}"));
                    AppDatabase.AddAuditLog(db, tx, currentUser, "تعديل", "أرصدة الماكينات والمحافظ", null, balanceDetails);
                    tx.Commit();

                    MessageBox.Show(
                        this,
                        "تم حفظ أرصدة الماكينات وحسابات السحب والإيداع بنجاح.",
                        "تم الحفظ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "تعذر حفظ الأرصدة:\n\n" + ex.Message,
                        "خطأ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

            savePanel.Controls.Add(save);
            body.Controls.Add(savePanel, 0, 3);

            page.Controls.Add(body);
            WheelScrollFix.Apply(body);
        }

        private static GroupBox CreateAccountBox(
            string title,
            decimal opening,
            decimal current,
            out NumericUpDown openingControl,
            out NumericUpDown currentControl)
        {
            var box = new GroupBox
            {
                Dock = DockStyle.Fill,
                MinimumSize = new Size(0, 220),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                BackColor = Color.White,
                Padding = new Padding(18, 28, 18, 14),
                Margin = new Padding(6)
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0),
                RightToLeft = RightToLeft.Yes
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 24F));

            openingControl = CreatePriceEditor(opening);
            openingControl.Dock = DockStyle.Fill;
            openingControl.Margin = new Padding(6);

            currentControl = CreatePriceEditor(current);
            currentControl.Dock = DockStyle.Fill;
            currentControl.Margin = new Padding(6);

            grid.Controls.Add(AccountLabel("الرصيد الافتتاحي"), 0, 0);
            grid.Controls.Add(openingControl, 1, 0);
            grid.Controls.Add(AccountLabel("الرصيد الحالي"), 0, 1);
            grid.Controls.Add(currentControl, 1, 1);

            var hint = new Label
            {
                Text = "يمكن تعديل الحالي يدويًا للتسوية أو التصحيح.",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(90, 98, 107),
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(6, 4, 6, 0)
            };
            grid.Controls.Add(hint, 0, 2);
            grid.SetColumnSpan(hint, 2);

            box.Controls.Add(grid);
            box.Text = title;
            return box;
        }

        private static Label AccountLabel(string text) => new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Margin = new Padding(6)
        };

        private static GroupBox CreateWalletBox(
            string provider,
            decimal opening,
            decimal current,
            out NumericUpDown openingControl,
            out NumericUpDown currentControl)
        {
            var box = CreateAccountBox(
                "💳 " + provider,
                opening,
                current,
                out openingControl,
                out currentControl);
            box.Tag = "walletBox";
            return box;
        }

        // ============================================================
        // تبويب كلمات المرور: تغيير كلمة مرور أي مستخدم (عمر / أحمد) + الأدمن
        // ============================================================
        private void BuildUsersTab(TabPage page)
        {
            var body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(24),
                BackColor = Color.FromArgb(244, 246, 249)
            };

            // ---------- كارت المستخدمين ----------
            var usersCard = MakeCard("🔑 كلمات مرور المستخدمين",
                "اختر المستخدم واكتب كلمة المرور الجديدة مرتين. لو عايز مستخدم يدخل بدون كلمة مرور استخدم زر «إزالة».");

            var userCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 11F),
                Width = 300
            };
            foreach (string user in AdminSecurity.Users) userCombo.Items.Add(user);
            userCombo.SelectedIndex = Math.Max(0, userCombo.Items.IndexOf(currentUser));

            var statusLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            void RefreshStatus()
            {
                string user = userCombo.SelectedItem?.ToString() ?? "";
                bool has = AdminSecurity.UserRequiresPassword(user);
                statusLabel.Text = has ? "الحالة: له كلمة مرور" : "الحالة: بدون كلمة مرور";
                statusLabel.ForeColor = has ? Color.FromArgb(39, 120, 70) : Color.FromArgb(192, 57, 43);
            }
            userCombo.SelectedIndexChanged += (_, _) => RefreshStatus();
            RefreshStatus();

            var newUserPass = MakePasswordBox();
            var confirmUserPass = MakePasswordBox();
            txtNewUserPassword = newUserPass;

            AddCardRow(usersCard, "المستخدم", userCombo);
            AddCardRow(usersCard, "", statusLabel);
            AddCardRow(usersCard, "كلمة المرور الجديدة", newUserPass);
            AddCardRow(usersCard, "تأكيد كلمة المرور", confirmUserPass);

            var saveUser = MakeButton("🔑 حفظ كلمة المرور", Color.FromArgb(0, 122, 204));
            saveUser.Size = new Size(220, 42);
            saveUser.Click += (_, _) =>
            {
                string user = userCombo.SelectedItem?.ToString() ?? "";
                if (!ValidateNewPassword(newUserPass.Text, confirmUserPass.Text, minimumLength: 4)) return;
                try
                {
                    AdminSecurity.SetUserPassword(user, newUserPass.Text);
                    LogAdminAction("تغيير كلمة مرور", "المستخدمون", $"تم تغيير كلمة مرور المستخدم: {user}. لم يتم تسجيل كلمة المرور نفسها.");
                    newUserPass.Clear();
                    confirmUserPass.Clear();
                    RefreshStatus();
                    MessageBox.Show(this, $"تم تحديث كلمة مرور «{user}» بنجاح.", "تم التحديث",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppLog.Error("فشل تغيير كلمة مرور مستخدم", ex);
                    MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            var removeUser = MakeButton("🚫 إزالة كلمة المرور", Color.FromArgb(108, 117, 125));
            removeUser.Size = new Size(220, 42);
            removeUser.Click += (_, _) =>
            {
                string user = userCombo.SelectedItem?.ToString() ?? "";
                if (!AdminSecurity.UserRequiresPassword(user))
                {
                    MessageBox.Show(this, $"«{user}» أصلًا بدون كلمة مرور.", "تنبيه",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show(this, $"هل تريد إزالة كلمة مرور «{user}»؟ هيدخل بدون كلمة مرور.",
                        "تأكيد", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                try
                {
                    AdminSecurity.SetUserPassword(user, "");
                    LogAdminAction("إزالة كلمة مرور", "المستخدمون", $"تمت إزالة كلمة مرور المستخدم: {user}.");
                    RefreshStatus();
                }
                catch (Exception ex)
                {
                    AppLog.Error("فشل إزالة كلمة مرور مستخدم", ex);
                    MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            AddCardButtons(usersCard, saveUser, removeUser);

            // ---------- كارت الأدمن ----------
            var adminCard = MakeCard("🛡️ كلمة مرور الأدمن",
                "كلمة مرور الأدمن بتفتح إعدادات الإدارة. غيّرها من هنا إلى كلمة قوية لا تستخدمها في حسابات أخرى.");

            var newAdminPass = MakePasswordBox();
            var confirmAdminPass = MakePasswordBox();
            txtNewAdminPassword = newAdminPass;
            AddCardRow(adminCard, "كلمة المرور الجديدة", newAdminPass);
            AddCardRow(adminCard, "تأكيد كلمة المرور", confirmAdminPass);

            var saveAdmin = MakeButton("🛡️ حفظ كلمة مرور الأدمن", Color.FromArgb(111, 66, 193));
            saveAdmin.Size = new Size(240, 42);
            saveAdmin.Click += (_, _) =>
            {
                if (!ValidateNewPassword(newAdminPass.Text, confirmAdminPass.Text, minimumLength: 8)) return;
                try
                {
                    AdminSecurity.SetAdminPassword(newAdminPass.Text.Trim());
                    LogAdminAction("تغيير كلمة مرور", "الأدمن", "تم تغيير كلمة مرور الأدمن. لم يتم تسجيل كلمة المرور نفسها.");
                    newAdminPass.Clear();
                    confirmAdminPass.Clear();
                    MessageBox.Show(this, "تم تحديث كلمة مرور الأدمن بنجاح.", "تم التحديث",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppLog.Error("فشل تغيير كلمة مرور الأدمن", ex);
                    MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            AddCardButtons(adminCard, saveAdmin);

            body.Controls.Add(usersCard);
            body.Controls.Add(adminCard);
            page.Controls.Add(body);
        }

        private bool ValidateNewPassword(string password, string confirm, int minimumLength)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show(this, "اكتب كلمة المرور الجديدة.", "تنبيه",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (password.Trim().Length < minimumLength)
            {
                MessageBox.Show(this, $"كلمة المرور لازم تكون {minimumLength} خانات على الأقل.", "تنبيه",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "كلمتا المرور غير متطابقتين.", "تنبيه",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        // ============================================================
        // تبويب النظام: نسخ احتياطي + سجل الأخطاء
        // ============================================================
        private void BuildSystemTab(TabPage page)
        {
            var body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(24),
                BackColor = Color.FromArgb(244, 246, 249)
            };

            // ---------- النسخ الاحتياطي ----------
            var backupCard = MakeCard("💾 النسخ الاحتياطي",
                "النسخة الاحتياطية ملف ZIP شامل: قاعدة البيانات بكل جداولها (العملاء والديون والمبيعات والمخزن والخزينة والماكينات واليوميات والفواتير وسجل التدقيق)، والأسعار وكلمات المرور والإعدادات والقوائم وسجل الطباعة وسجل الأخطاء وباقي ملفات بيانات التطبيق في LocalAppData وRoaming. " +
                "كل نسخة جديدة يتم فحصها قبل اعتمادها، والاستعادة تعمل نسخة أمان أولًا. يُحتفظ بآخر 30 نسخة؛ مجلد أرشيف النسخ نفسه لا يدخل داخل النسخة لتجنب النسخ المتداخلة. " +
                "الأفضل تختار مجلد إضافي على فلاشة أو OneDrive عشان لو الجهاز اتعطل الداتا تفضل موجودة. " +
                "لو حدّثت البرنامج أو اتمسح حاجة بالغلط: اضغط «استعادة من نسخة» واختار الملف.");

            var extraLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            void RefreshExtra()
            {
                string extra = DatabaseBackup.ExtraFolder;
                extraLabel.Text = string.IsNullOrWhiteSpace(extra)
                    ? "المجلد الإضافي: غير محدد"
                    : "المجلد الإضافي: " + extra;
                extraLabel.ForeColor = string.IsNullOrWhiteSpace(extra)
                    ? Color.FromArgb(192, 57, 43)
                    : Color.FromArgb(39, 120, 70);
            }
            RefreshExtra();
            AddCardRow(backupCard, "", extraLabel);
            AddCardRow(backupCard, "مكان النسخ", new Label
            {
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Text = DatabaseBackup.BackupDirectory
            });

            var backupNow = MakeButton("💾 نسخة احتياطية الآن", Color.FromArgb(0, 122, 204));
            backupNow.Size = new Size(230, 42);
            backupNow.Click += (_, _) =>
            {
                string? path = DatabaseBackup.BackupNow("manual");
                if (path == null)
                    MessageBox.Show(this, "فشل عمل النسخة الاحتياطية. راجع سجل الأخطاء.", "خطأ",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                else if (!string.IsNullOrWhiteSpace(DatabaseBackup.LastBackupWarning))
                    MessageBox.Show(this, "تم إنشاء النسخة المحلية:\n" + path + "\n\n" + DatabaseBackup.LastBackupWarning,
                        "تم مع تحذير النسخ الخارجي", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    MessageBox.Show(this, "تم عمل نسخة احتياطية:\n" + path, "تم",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var pickExtra = MakeButton("📁 اختيار مجلد إضافي", Color.FromArgb(40, 167, 69));
            pickExtra.Size = new Size(230, 42);
            pickExtra.Click += (_, _) =>
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "اختر مجلد (فلاشة أو OneDrive) لنسخ النسخ الاحتياطية فيه",
                    UseDescriptionForTitle = true
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    DatabaseBackup.ExtraFolder = dialog.SelectedPath;
                    RefreshExtra();
                }
                catch (Exception ex)
                {
                    AppLog.Error("تعذر حفظ المجلد الإضافي للنسخ الاحتياطي", ex);
                    MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            var clearExtra = MakeButton("✖ إلغاء المجلد الإضافي", Color.FromArgb(108, 117, 125));
            clearExtra.Size = new Size(230, 42);
            clearExtra.Click += (_, _) =>
            {
                DatabaseBackup.ExtraFolder = "";
                RefreshExtra();
            };

            var openBackups = MakeButton("📂 فتح مجلد النسخ", Color.FromArgb(52, 73, 94));
            openBackups.Size = new Size(230, 42);
            openBackups.Click += (_, _) => OpenFolder(DatabaseBackup.BackupDirectory);

            var exportBackup = MakeButton("📤 حفظ نسخة في مكان أختاره", Color.FromArgb(0, 150, 136));
            exportBackup.Size = new Size(230, 42);
            exportBackup.Click += (_, _) => BackupRestoreUi.RunExport(this);

            var restoreBackup = MakeButton("♻ استعادة من نسخة (رفع ملف)", Color.FromArgb(192, 57, 43));
            restoreBackup.Size = new Size(230, 42);
            restoreBackup.Click += (_, _) =>
            {
                if (!BackupRestoreUi.RunRestore(this)) return;
                // البيانات اتغيّرت بالكامل: نرجع لصفحة تسجيل الدخول.
                AppSession.RequestLogout();
                Close();
            };

            AddCardButtons(backupCard, backupNow, exportBackup, restoreBackup, pickExtra, clearExtra, openBackups);

            // ---------- العمليات المعلقة للطباعة ----------
            var pendingCard = MakeCard("🖨️ عمليات الطباعة المعلقة",
                "لو قاعدة البيانات أو ملف سجل الطباعة تعذّر حفظه أثناء الطباعة، يحتفظ البرنامج بمحاولة معلقة. زر إعادة المحاولة يعالج عمليات تاريخ اليوم فقط حتى لا ينقل عملية قديمة إلى يومية أو شفت جديد بالخطأ.");
            var pendingStatus = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            void RefreshPendingStatus()
            {
                int count = PrintLogger.GetPendingLogCount();
                pendingStatus.Text = count == 0
                    ? "الحالة: لا توجد عمليات طباعة معلقة."
                    : count > 0
                        ? $"الحالة: يوجد {count} سجل معلّق يحتاج مزامنة أو مراجعة."
                        : "الحالة: تعذرت قراءة ملف العمليات المعلقة؛ لا تمسحه ولا تصفّر البيانات حتى تُحل المشكلة.";
                pendingStatus.ForeColor = count == 0
                    ? Color.FromArgb(39, 120, 70)
                    : Color.FromArgb(192, 57, 43);
            }
            RefreshPendingStatus();
            AddCardRow(pendingCard, "الحالة", pendingStatus);
            var retryPending = MakeButton("🔄 إعادة محاولة تسجيل عمليات اليوم", Color.FromArgb(0, 122, 204));
            retryPending.Size = new Size(280, 42);
            retryPending.Click += (_, _) =>
            {
                int before = PrintLogger.GetPendingLogCount();
                int remaining = PrintLogger.FlushPendingLogsForToday();
                RefreshPendingStatus();
                string message = remaining == 0
                    ? $"انتهت المزامنة. السجلات المعلقة قبل المحاولة: {before}."
                    : $"تمت محاولة مزامنة عمليات اليوم. ما زال {remaining} سجل معلّق.\n\nالعمليات من تواريخ سابقة لا تُسجل تلقائيًا في يومية اليوم. راجع سجل الأخطاء وتأكد من التاريخ والشفت قبل تصحيح أي حركة مالية.";
                MessageBox.Show(this, message,
                    remaining == 0 ? "مزامنة الطباعة" : "عمليات تحتاج مراجعة",
                    MessageBoxButtons.OK,
                    remaining == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            };
            AddCardButtons(pendingCard, retryPending);

            // ---------- سجل الأخطاء ----------
            var logCard = MakeCard("🧾 سجل الأخطاء",
                "أي خطأ غير متوقع بيتسجل تلقائيًا في ملف يومي. لو البرنامج عمل مشكلة ابعت ملف اليوم للمطوّر.");
            AddCardRow(logCard, "مكان السجل", new Label
            {
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Text = AppLog.LogDirectory
            });
            var openLogs = MakeButton("📂 فتح مجلد السجل", Color.FromArgb(52, 73, 94));
            openLogs.Size = new Size(230, 42);
            openLogs.Click += (_, _) => OpenFolder(AppLog.LogDirectory);
            AddCardButtons(logCard, openLogs);

            // ---------- سجل تدقيق العمليات ----------
            var auditCard = MakeCard("🛡️ سجل تدقيق العمليات",
                "سجل إداري لعمليات البيع والتحصيل والخدمات وتعديلات المخزون وحذف بعض الحركات المالية، مع اسم المستخدم والتوقيت والتفاصيل. يحتفظ البرنامج بآخر 1000 حركة عند العرض ويمكن تصديرها إلى CSV.");
            var openAudit = MakeButton("📋 فتح سجل التدقيق", Color.FromArgb(111, 66, 193));
            openAudit.Size = new Size(230, 42);
            openAudit.Click += (_, _) =>
            {
                using var form = new AuditLogForm();
                form.ShowDialog(this);
            };
            AddCardButtons(auditCard, openAudit);

            body.Controls.Add(backupCard);
            body.Controls.Add(pendingCard);
            body.Controls.Add(logCard);
            body.Controls.Add(auditCard);
            page.Controls.Add(body);
        }

        private void LogAdminAction(string action, string entityType, string details)
        {
            try
            {
                using var connection = AppDatabase.OpenConnection();
                AppDatabase.AddAuditLog(connection, null, currentUser, action, entityType, null, details);
            }
            catch (Exception ex)
            {
                // فشل كتابة السجل لا يلغي تغيير إعداد تم حفظه بالفعل.
                AppLog.Error("تعذر تسجيل إجراء إداري في سجل التدقيق", ex);
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                System.IO.Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر فتح المجلد: " + path, ex);
                MessageBox.Show(this, ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------- عناصر مساعدة لشكل الكروت ----------
        private static TableLayoutPanel MakeCard(string title, string description)
        {
            var card = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                BackColor = Color.White,
                Padding = new Padding(22, 16, 22, 18),
                Margin = new Padding(0, 0, 0, 18),
                Width = 720,
                RightToLeft = RightToLeft.Yes
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var heading = new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                Margin = new Padding(0, 0, 0, 6)
            };
            card.Controls.Add(heading, 0, 0);
            card.SetColumnSpan(heading, 2);

            var desc = new Label
            {
                Text = description,
                AutoSize = true,
                MaximumSize = new Size(670, 0),
                ForeColor = Color.FromArgb(108, 117, 125),
                Margin = new Padding(0, 0, 0, 12)
            };
            card.Controls.Add(desc, 0, 1);
            card.SetColumnSpan(desc, 2);
            return card;
        }

        private static void AddCardRow(TableLayoutPanel card, string label, Control input)
        {
            int row = card.RowCount;
            card.RowCount = row + 1;
            card.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Margin = new Padding(3, 8, 3, 8)
            }, 0, row);
            input.Margin = new Padding(3, 5, 3, 5);
            input.Anchor = AnchorStyles.Right;
            card.Controls.Add(input, 1, row);
        }

        private static void AddCardButtons(TableLayoutPanel card, params Button[] buttons)
        {
            int row = card.RowCount;
            card.RowCount = row + 1;
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = true,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 10, 0, 0),
                MaximumSize = new Size(670, 0)
            };
            foreach (var button in buttons)
            {
                button.Margin = new Padding(4);
                flow.Controls.Add(button);
            }
            card.Controls.Add(flow, 0, row);
            card.SetColumnSpan(flow, 2);
        }

        private static TextBox MakePasswordBox() => new TextBox
        {
            Width = 300,
            PasswordChar = '●',
            Font = new Font("Segoe UI", 11F),
            TextAlign = HorizontalAlignment.Center
        };

        private void BuildResetTab(TabPage page)
        {
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24) };

            var title = new Label
            {
                Text = "🧹 إعادة البرنامج لحالة جديدة",
                Dock = DockStyle.Top,
                Height = 48,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(192, 57, 43),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var warning = new Label
            {
                Text =
                    "هذا الإجراء يمسح بيانات النشاط التجاري بالكامل: المنتجات، العملاء، المبيعات، الديون، خدمات الكاش، حركات الخزينة وسجلات الطباعة والتقارير.\n" +
                    "سيتم الإبقاء على حسابات الدخول وكلمات المرور الخاصة بعمر وأحمد. لا يمكن التراجع عن العملية.",
                Dock = DockStyle.Top,
                Height = 95,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var reset = MakeButton("🧹 تصفير كل بيانات البرنامج", Color.FromArgb(192,57,43));
            reset.Width = 300;
            reset.Height = 52;
            reset.Left = 24;
            reset.Top = 170;
            reset.Click += (s,e) =>
            {
                var first = MessageBox.Show(
                    this,
                    "سيتم حذف كل بيانات المنتجات والعملاء والمبيعات والديون والخزينة وسجلات الطباعة.\n\nهل تريد المتابعة؟",
                    "تحذير شديد",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (first != DialogResult.Yes) return;

                using var confirm = new Form
                {
                    Text = "تأكيد التهيئة",
                    Size = new Size(460, 230),
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    RightToLeft = RightToLeft.Yes,
                    RightToLeftLayout = true,
                    BackColor = Color.FromArgb(244,246,249),
                    Font = new Font("Segoe UI",10.5f)
                };
                UiKit.EnableEscape(confirm);

                var prompt = new Label
                {
                    Text = "اكتب كلمة تأكيد:  RESET",
                    Dock = DockStyle.Top,
                    Height = 55,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI",11F,FontStyle.Bold)
                };
                var text = new TextBox
                {
                    Width = 260,
                    Left = 90,
                    Top = 65,
                    Font = new Font("Segoe UI",13F),
                    TextAlign = HorizontalAlignment.Center
                };
                var ok = MakeButton("تأكيد التصفير", Color.FromArgb(192,57,43));
                ok.Width = 150; ok.Height = 40; ok.Left = 75; ok.Top = 120;
                ok.Click += (ss,ee) =>
                {
                    if (!string.Equals(text.Text.Trim(), "RESET", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(confirm, "كلمة التأكيد غير صحيحة.", "رفض", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        AppDatabase.ResetBusinessData();
                        PrintLogger.ResetFileOnly();
                        PricingStore.Save(new PricingSettings());
                        confirm.DialogResult = DialogResult.OK;
                        confirm.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(confirm, "تعذر تصفير البيانات:\n\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };
                var cancel = MakeButton("إلغاء", Color.FromArgb(108,117,125));
                cancel.Width = 150; cancel.Height = 40; cancel.Left = 235; cancel.Top = 120;
                cancel.DialogResult = DialogResult.Cancel;

                confirm.Controls.Add(prompt);
                confirm.Controls.Add(text);
                confirm.Controls.Add(ok);
                confirm.Controls.Add(cancel);
                text.Focus();

                if (confirm.ShowDialog(this) == DialogResult.OK)
                {
                    MessageBox.Show(
                        this,
                        "تم تصفير بيانات النشاط التجاري بنجاح.\n\nرصيد الخزينة أصبح 0.00 ج.م.",
                        "تمت التهيئة",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            };

            body.Controls.Add(reset);
            body.Controls.Add(warning);
            body.Controls.Add(title);
            page.Controls.Add(body);
        }

        private void BuildHistoryTab(TabPage page)
        {
            Panel body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16)
            };

            Label info = new Label
            {
                Text = "اختر عملية أو أكثر من السجل ثم احذفها. ويمكن مسح السجل بالكامل. كل عمليات الحذف متاحة من داخل لوحة الإدارة المحمية بكلمة مرور الأدمن.",
                Dock = DockStyle.Top,
                Height = 52,
                AutoSize = false,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94)
            };

            Panel buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 62
            };

            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                BackgroundColor = Color.FromArgb(244, 246, 249),
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 40,
                Font = new Font("Segoe UI", 9.5F)
            };

            UiKit.EnableColumnSorting(grid);
            // كل حقول السجل الأساسية ظاهرة داخل جدول الإدارة.
            AddHistoryColumn(grid, "Timestamp", "التاريخ والوقت", 140);
            AddHistoryColumn(grid, "UserName", "المستخدم", 85);
            AddHistoryColumn(grid, "FileName", "اسم الملف", 230);
            AddHistoryColumn(grid, "FileType", "النوع", 80);
            AddHistoryColumn(grid, "Pages", "الصفحات", 65);
            AddHistoryColumn(grid, "Copies", "النسخ", 65);
            AddHistoryColumn(grid, "ColorType", "اللون", 75);
            AddHistoryColumn(grid, "DuplexMode", "الطباعة", 90);
            AddHistoryColumn(grid, "PhysicalSheets", "الورق", 65);
            AddHistoryColumn(grid, "Price", "السعر", 85);

            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            };

            grid.RowsDefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Padding = new Padding(3)
            };

            void LoadHistory()
            {
                grid.Rows.Clear();

                foreach (PrintLogger.LogEntry entry in
                         PrintLogger.LoadAll().OrderByDescending(x => x.Timestamp))
                {
                    int rowIndex = grid.Rows.Add(
                        entry.Timestamp.ToString("yyyy-MM-dd HH:mm"),
                        entry.UserName,
                        entry.FileName,
                        entry.FileType,
                        entry.Pages,
                        entry.Copies,
                        entry.ColorType,
                        entry.DuplexMode,
                        entry.PhysicalSheets,
                        $"{entry.Price:F2} ج.م");

                    grid.Rows[rowIndex].Tag = entry;
                }
            }

            Button deleteSelected = MakeButton(
                "🗑️ حذف العناصر المحددة",
                Color.FromArgb(255, 140, 0));
            deleteSelected.Left = 0;
            deleteSelected.Top = 8;
            deleteSelected.Width = 250;
            deleteSelected.Height = 44;
            deleteSelected.Anchor = AnchorStyles.Left | AnchorStyles.Top;

            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Delete && e.KeyCode != Keys.Back) return;
                e.SuppressKeyPress = true;
                e.Handled = true;
                deleteSelected.PerformClick();
            };

            deleteSelected.Click += (s, e) =>
            {
                List<PrintLogger.LogEntry> selected =
                    grid.SelectedRows
                        .Cast<DataGridViewRow>()
                        .Select(r => r.Tag as PrintLogger.LogEntry)
                        .Where(x => x != null)
                        .Cast<PrintLogger.LogEntry>()
                        .ToList();

                if (selected.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "برجاء تحديد سجل واحد أو أكثر أولاً.",
                        "السجل",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                DialogResult confirm = MessageBox.Show(
                    this,
                    $"سيتم حذف {selected.Count} عملية من السجل نهائيًا.\n\nهل تريد المتابعة؟",
                    "تأكيد الحذف",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                    return;

                try
                {
                    int deleted = PrintLogger.DeleteEntries(selected, currentUser);
                    LoadHistory();

                    MessageBox.Show(
                        this,
                        $"تم حذف {deleted} عملية من السجل.",
                        "السجل",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "تعذر حذف السجلات:\n\n" + ex.Message,
                        "خطأ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

            Button clearAll = MakeButton(
                "🗑️ مسح السجل بالكامل",
                Color.FromArgb(220, 53, 69));
            clearAll.Left = 265;
            clearAll.Top = 8;
            clearAll.Width = 250;
            clearAll.Height = 44;
            clearAll.Anchor = AnchorStyles.Left | AnchorStyles.Top;

            clearAll.Click += (s, e) =>
            {
                if (grid.Rows.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "السجل فارغ بالفعل.",
                        "السجل",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                DialogResult confirm = MessageBox.Show(
                    this,
                    "سيتم مسح جميع سجلات الطباعة نهائيًا.\n\nهل أنت متأكد؟",
                    "تأكيد مسح السجل",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                    return;

                try
                {
                    PrintLogger.ClearAll();
                    LoadHistory();

                    MessageBox.Show(
                        this,
                        "تم مسح السجل بالكامل.",
                        "السجل",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "تعذر مسح السجل:\n\n" + ex.Message,
                        "خطأ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

            body.Controls.Add(grid);
            body.Controls.Add(buttonPanel);
            body.Controls.Add(info);
            buttonPanel.Controls.Add(deleteSelected);
            buttonPanel.Controls.Add(clearAll);
            page.Controls.Add(body);
            LoadHistory();
        }

        private static void AddHistoryColumn(
            DataGridView grid,
            string name,
            string header,
            int width)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width
            });
        }

        private static Button MakeButton(string text, Color color)
        {
            Button button = new Button
            {
                Text = text,
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor =
                ControlPaint.Light(color, 0.15f);
            button.FlatAppearance.MouseDownBackColor =
                ControlPaint.Dark(color, 0.05f);

            return button;
        }
    }

    internal sealed class PasswordPromptForm : Form
    {
        private readonly TextBox textBox;
        public string Password => textBox.Text;

        public PasswordPromptForm(string title)
        {
            Text = "مصادقة الأدمن";
            Size = new Size(460, 210);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = Color.FromArgb(244, 246, 249);
            Font = new Font("Segoe UI", 10.5F);

            Label prompt = new Label
            {
                Text = title,
                Left = 25,
                Top = 22,
                Width = 390,
                Height = 45,
                AutoSize = false,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            textBox = new TextBox
            {
                Left = 65,
                Top = 75,
                Width = 320,
                Height = 36,
                PasswordChar = '●',
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 14F)
            };

            Button ok = new Button
            {
                Text = "تأكيد",
                Left = 65,
                Top = 125,
                Width = 150,
                Height = 38,
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            Button cancel = new Button
            {
                Text = "إلغاء",
                Left = 235,
                Top = 125,
                Width = 150,
                Height = 38,
                DialogResult = DialogResult.Cancel,
                BackColor = Color.FromArgb(108, 117, 125),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            ok.FlatAppearance.BorderSize = 0;
            cancel.FlatAppearance.BorderSize = 0;

            Controls.Add(prompt);
            Controls.Add(textBox);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            Shown += (s, e) => textBox.Focus();
        }
    }


    /// <summary>
    /// يمنع فتح إعدادات الأدمن باستخدام كلمة المرور المدمجة الافتراضية قبل تعيين كلمة مرور جديدة.
    /// </summary>
    internal static class AdminPasswordRotation
    {
        public static bool EnsureRotatedIfDefault(IWin32Window owner)
        {
            if (!AdminSecurity.IsUsingDefaultAdminPassword) return true;

            using var form = new AdminPasswordChangeForm();
            if (form.ShowDialog(owner) != DialogResult.OK) return false;

            try
            {
                AdminSecurity.SetAdminPassword(form.NewPassword);
                MessageBox.Show(owner,
                    "تم تغيير كلمة مرور الأدمن بنجاح. استخدم كلمة المرور الجديدة في المرات القادمة.",
                    "تم تأمين حساب الأدمن", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر تغيير كلمة مرور الأدمن الافتراضية", ex);
                MessageBox.Show(owner,
                    "تعذر حفظ كلمة المرور الجديدة، لذلك لن يتم فتح العملية الحساسة.\n\n" + ex.Message,
                    "تعذر تأمين الحساب", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }

    internal sealed class AdminPasswordChangeForm : Form
    {
        private readonly TextBox txtNewPassword;
        private readonly TextBox txtConfirmPassword;
        public string NewPassword { get; private set; } = string.Empty;

        public AdminPasswordChangeForm(string accountLabel = "الأدمن")
        {
            Text = $"تغيير إلزامي لكلمة مرور {accountLabel}";
            Size = new Size(500, 315);
            MinimumSize = new Size(500, 315);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = Color.FromArgb(244, 246, 249);
            Font = new Font("Segoe UI", 10.5F);

            var title = new Label
            {
                Text = $"كلمة مرور {accountLabel} ما زالت الافتراضية.\nعيّن كلمة مرور جديدة قبل المتابعة.",
                Left = 24, Top = 18, Width = 435, Height = 54,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            var newLabel = new Label { Text = "كلمة المرور الجديدة (8 أحرف/أرقام على الأقل)", Left = 30, Top = 82, Width = 420, Height = 25 };
            txtNewPassword = new TextBox
            {
                Left = 30, Top = 108, Width = 420, Height = 34,
                PasswordChar = '●', TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 13F)
            };
            var confirmLabel = new Label { Text = "تأكيد كلمة المرور الجديدة", Left = 30, Top = 147, Width = 420, Height = 25 };
            txtConfirmPassword = new TextBox
            {
                Left = 30, Top = 173, Width = 420, Height = 34,
                PasswordChar = '●', TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 13F)
            };
            var ok = new Button
            {
                Text = "حفظ وتأمين الحساب", Left = 30, Top = 220, Width = 245, Height = 42,
                BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            var cancel = new Button
            {
                Text = "إلغاء", Left = 290, Top = 220, Width = 160, Height = 42,
                BackColor = Color.FromArgb(108, 117, 125), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            ok.FlatAppearance.BorderSize = 0;
            cancel.FlatAppearance.BorderSize = 0;
            ok.Click += (_, _) =>
            {
                string password = txtNewPassword.Text.Trim();
                string confirm = txtConfirmPassword.Text.Trim();
                if (password.Length < 8)
                {
                    MessageBox.Show(this, "كلمة المرور يجب أن تكون 8 أحرف أو أرقام على الأقل.", "كلمة مرور ضعيفة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtNewPassword.Focus();
                    return;
                }
                if (!string.Equals(password, confirm, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "كلمتا المرور غير متطابقتين.", "تأكيد كلمة المرور", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtConfirmPassword.Clear();
                    txtConfirmPassword.Focus();
                    return;
                }
                NewPassword = password;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.AddRange(new Control[] { title, newLabel, txtNewPassword, confirmLabel, txtConfirmPassword, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (_, _) => txtNewPassword.Focus();
        }
    }


    internal static class UserPasswordRotation
    {
        public static bool EnsureRotatedIfDefault(IWin32Window owner, string userName)
        {
            if (!AdminSecurity.IsUsingDefaultUserPassword(userName)) return true;
            using var form = new AdminPasswordChangeForm(userName);
            if (form.ShowDialog(owner) != DialogResult.OK) return false;
            try
            {
                AdminSecurity.SetUserPassword(userName, form.NewPassword);
                MessageBox.Show(owner,
                    $"تم تغيير كلمة مرور {userName} الافتراضية بنجاح.",
                    "تم تأمين الحساب", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error($"تعذر تغيير كلمة مرور {userName} الافتراضية", ex);
                MessageBox.Show(owner,
                    "تعذر حفظ كلمة المرور الجديدة؛ لن يتم تسجيل الدخول حتى لا يظل الحساب على كلمة المرور الافتراضية.\n\n" + ex.Message,
                    "تعذر تأمين الحساب", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }

}
