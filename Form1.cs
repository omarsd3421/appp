using System;
using System.Drawing;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing.Printing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PdfiumViewer;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharpPdfDocument = PdfSharp.Pdf.PdfDocument;
using PdfiumViewerPdfDocument = PdfiumViewer.PdfDocument;

// ============================================================
// OpenCV alias
// مهم: لا نستخدم using OpenCvSharp;
// حتى لا يحدث تعارض مع System.Drawing.Point / Size.
// ============================================================
using Cv = OpenCvSharp;

namespace LibraryPrintApp
{
    public partial class Form1 : Form
    {
        private DataGridView gridFiles = null!;

        private NumericUpDown numBlackPrice = null!;
        private NumericUpDown numColorPrice = null!;
        private NumericUpDown numDuplexPrice = null!;
        private NumericUpDown numDuplexColorPrice = null!;
        private ComboBox cmbPricingPreset = null!;
        private ComboBox cmbSelectedFileCategory = null!;
        private Label lblSelectedFileCategory = null!;

        private Label lblTotalPages = null!;
        private Label lblTotalPapers = null!;
        private Label lblTotalPrice = null!;

        private Button btnPrintAll = null!;
        private Button btnPrintSelected = null!;
        private Button btnPrintSettings = null!;
        private Button btnClearList = null!;
        private Button btnDeleteSelected = null!;
        private Button btnAddFiles = null!;
        private Button btnAddCard = null!;
        private Button btnMergeFiles = null!;
        private Button btnEditImage = null!;
        private Button btnReports = null!;
        private Button btnLogout = null!;
        private Button btnAdminSettings = null!;
        private Button btnCopyService = null!;

        private PricingSettings pricingSettings = null!;

        private Panel? previewPanel;
        private PictureBox? previewPanelPicture;
        private Label? previewPanelTitle;
        private Image? currentPreviewImage;
        private SheetNavigator? previewNavigator;
        private int previewSheetIndex;
        private int previewSheetCount;

        private ComboBox cmbPrinters = null!;
        private ComboBox cmbGlobalColor = null!;
        private ComboBox cmbGlobalDuplex = null!;
        private NumericUpDown numGlobalCopies = null!;

        private int totalPagesCount;
        private int totalPhysicalPapers;
        private decimal grandTotalPrice;

        private PrinterSettings currentPrinterSettings =
            new PrinterSettings();

        private bool isGlobalUpdating;
        private bool isRecalculating;

        // اسم المستخدم الحالي (يتم تحديده من شاشة تسجيل الدخول).
        public string CurrentUserName { get; }

        private sealed class FileEntry
        {
            public string OriginalPath { get; }
            public string PrintPath { get; }
            public PrintOptions Options { get; }
            public string? PricingCategory { get; }

            public FileEntry(
                string originalPath,
                string printPath,
                PrintOptions? options = null,
                string? pricingCategory = null)
            {
                OriginalPath = originalPath;
                PrintPath = printPath;
                Options = options?.Clone() ?? new PrintOptions();
                PricingCategory = string.IsNullOrWhiteSpace(pricingCategory) ? null : pricingCategory.Trim();
            }
        }

        // ============================================================
        // Constructor
        // ============================================================
        public Form1(string userName)
        {
            CurrentUserName =
                string.IsNullOrWhiteSpace(userName)
                    ? "غير معروف"
                    : userName;

            pricingSettings = PricingStore.Load();

            InitializeCustomComponents();

            // حماية إضافية: الأسعار في الواجهة الرئيسية مقفولة دائمًا.
            // التعديل يتم فقط من لوحة الإدارة بعد المصادقة.
            LockMainPriceControls();

            LoadInstalledPrinters();

            RecalculateTotals();

            // إضافة المتصفح: الملفات اللي كانت مستنية تتضاف أول ما الشاشة تظهر.
            // نركّز الجدول بعد ظهور النافذة حتى تكون اختصارات لوحة المفاتيح جاهزة فورًا.
            Activated += (_, _) =>
            {
                // رجعنا من Picture Manager: نشوف لو الصورة اتحفظت.
                if (editWatch.Count > 0)
                    CheckEditedImages();
            };

            Shown += (_, _) =>
            {
                PrintBridge.Attach(this);
                BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                {
                    if (!IsDisposed && !Disposing && gridFiles != null && gridFiles.CanFocus)
                        gridFiles.Focus();
                }));
            };
        }

        // ============================================================
        // إنشاء الواجهة
        // ============================================================
        private void InitializeCustomComponents()
        {
            Text =
                "مكتبة الشريف - نظام إدارة طباعة المكتبة المتقدم - تصميم عمر الشريف" +
                $"    |    المستخدم الحالي: {CurrentUserName}";

            // نافذة أعرض شوية عشان جدول الطباعة مايبقاش ضيق، بحد أقصى مساحة الشاشة المتاحة.
            Rectangle workArea =
                Screen.PrimaryScreen?.WorkingArea
                ?? new Rectangle(0, 0, 1366, 768);

            Size =
                new Size(
                    Math.Min(1480, workArea.Width),
                    Math.Min(800, workArea.Height));

            MinimumSize =
                new Size(
                    Math.Min(1180, workArea.Width),
                    Math.Min(650, workArea.Height));

            StartPosition =
                FormStartPosition.CenterScreen;

            // نافذة مستقلة: ليها زر تصغير وزر في شريط المهام عشان تكمّل في باقي الخدمات.
            MinimizeBox = true;
            ShowInTaskbar = true;

            Font =
                new Font("Segoe UI", 10F);

            BackColor =
                Color.FromArgb(244, 246, 249);

            UiKit.EnableEscape(this);
            KeyPreview = true;
            KeyDown += Form1_KeyDown;

            AllowDrop = true;

            DragEnter +=
                Form1_DragEnter;

            DragDrop +=
                Form1_DragDrop;

            // ========================================================
            // اللوحة العلوية
            // ========================================================
            Panel topPanel =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 222,
                    BackColor = Color.White,
                    Padding = new Padding(18, 14, 18, 14)
                };

            Panel topPanelSeparator =
                new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 2,
                    BackColor = Color.FromArgb(224, 228, 233)
                };

            topPanel.Controls.Add(topPanelSeparator);

            Font labelFont =
                new Font("Segoe UI", 10F, FontStyle.Bold);

            Font controlFont =
                new Font("Segoe UI", 10.5F);

            // ========================================================
            // فئة السعر (بريست): الطلبة / عادي / مخصص
            // ========================================================
            Label lblPreset =
                new Label
                {
                    Text = "الفئة:",
                    Location = new Point(18, 22),
                    AutoSize = true,
                    Font = labelFont
                };

            cmbPricingPreset =
                new ComboBox
                {
                    Location = new Point(75, 18),
                    Width = 130,
                    Height = 32,
                    Font = controlFont,
                    FlatStyle = FlatStyle.Flat,
                    DropDownStyle =
                        ComboBoxStyle.DropDownList
                };

            cmbPricingPreset.Items.AddRange(
                new object[]
                {
                    "الطلبة",
                    "عادي",
                    "مخصص"
                });

            ComboBoxStyler.ApplyModernStyle(
                cmbPricingPreset,
                Color.FromArgb(52, 73, 94));

            // ========================================================
            // سعر الأسود (الوجه الواحد - وش واحد)
            // ========================================================
            Label lblBlack =
                new Label
                {
                    Text = "سعر الأسود:",
                    Location = new Point(220, 22),
                    AutoSize = true,
                    Font = labelFont
                };

            numBlackPrice =
                new NumericUpDown
                {
                    Location =
                        new Point(305, 18),
                    DecimalPlaces = 2,
                    Increment = 0.25m,
                    Minimum = 0,
                    Maximum = 100000,
                    Value = 1.00m,
                    Width = 78,
                    Font = controlFont,
                    TextAlign = HorizontalAlignment.Center
                };

            numBlackPrice.ValueChanged +=
                (s, e) => { SaveCustomPricingIfNeeded(); RecalculateTotals(); };

            // ========================================================
            // سعر الألوان (الوجه الواحد - وش واحد)
            // ========================================================
            Label lblColor =
                new Label
                {
                    Text = "سعر الألوان:",
                    Location = new Point(395, 22),
                    AutoSize = true,
                    Font = labelFont
                };

            numColorPrice =
                new NumericUpDown
                {
                    Location =
                        new Point(480, 18),
                    DecimalPlaces = 2,
                    Increment = 0.25m,
                    Minimum = 0,
                    Maximum = 100000,
                    Value = 3.00m,
                    Width = 78,
                    Font = controlFont,
                    TextAlign = HorizontalAlignment.Center
                };

            numColorPrice.ValueChanged +=
                (s, e) => { SaveCustomPricingIfNeeded(); RecalculateTotals(); };

            // ========================================================
            // سعر الوجه في حالة وش وظهر (سعر منفصل عن الأسود/الألوان)
            // ========================================================
            // ملحوظة: صف مستقل لأسعار "وش وظهر" لأن نصوصها طويلة.
            // بنستخدم إحداثيات ثابتة + عرض ثابت لكل Label (مش AutoSize
            // ومش حساب Right لعنصر سابق)، عشان النصوص الطويلة متتقفلش
            // ولا يحصل تداخل بين صندوق سعر وصندوق التاني.
            const int duplexRowY = 70;
            const int duplexLabelWidth = 270;
            const int duplexLabelHeight = 22;

            Label lblDuplexPrice =
                new Label
                {
                    Text = "سعر الأسود (وش وظهر) - للصفحة:",
                    Location = new Point(18, duplexRowY + 4),
                    AutoSize = false,
                    Width = duplexLabelWidth,
                    Height = duplexLabelHeight,
                    Font = labelFont
                };

            numDuplexPrice =
                new NumericUpDown
                {
                    Location =
                        new Point(296, duplexRowY),
                    DecimalPlaces = 2,
                    Increment = 0.25m,
                    Minimum = 0,
                    Maximum = 100000,
                    Value = 1.00m,
                    Width = 78,
                    Font = controlFont,
                    TextAlign = HorizontalAlignment.Center
                };

            numDuplexPrice.ValueChanged +=
                (s, e) => { SaveCustomPricingIfNeeded(); RecalculateTotals(); };

            // ========================================================
            // سعر الألوان في حالة وش وظهر - سعر منفصل عن سعر الأسود
            // وش وظهر، ويُحسب بالصفحة (الوجه المطبوع) زي باقي الأسعار.
            // ========================================================
            Label lblDuplexColorPrice =
                new Label
                {
                    Text = "سعر الألوان (وش وظهر) - للصفحة:",
                    Location = new Point(410, duplexRowY + 4),
                    AutoSize = false,
                    Width = duplexLabelWidth,
                    Height = duplexLabelHeight,
                    Font = labelFont
                };

            numDuplexColorPrice =
                new NumericUpDown
                {
                    Location =
                        new Point(688, duplexRowY),
                    DecimalPlaces = 2,
                    Increment = 0.25m,
                    Minimum = 0,
                    Maximum = 100000,
                    Value = 2.50m,
                    Width = 78,
                    Font = controlFont,
                    TextAlign = HorizontalAlignment.Center
                };

            numDuplexColorPrice.ValueChanged +=
                (s, e) => { SaveCustomPricingIfNeeded(); RecalculateTotals(); };

            // تطبيق البريست المختار (تحديث الأسعار وتفعيل/تعطيل التعديل اليدوي).
            cmbPricingPreset.SelectedIndexChanged +=
                (s, e) => ApplyPricingPreset();

            cmbPricingPreset.SelectedIndex = 1; // "عادي" افتراضيًا
            AttachLivePriceEvents();

            // ========================================================
            // اللون لجميع الملفات
            // ========================================================
            Label lblGlobalColor =
                new Label
                {
                    Text = "لون الكل:",
                    Location = new Point(18, 122),
                    AutoSize = true,
                    Font = labelFont
                };

            cmbGlobalColor =
                new ComboBox
                {
                    Location =
                        new Point(98, 118),
                    Width = 130,
                    Height = 32,
                    Font = controlFont,
                    FlatStyle = FlatStyle.Flat,
                    DropDownStyle =
                        ComboBoxStyle.DropDownList
                };

            cmbGlobalColor.Items.AddRange(
                new object[]
                {
                    "أسود",
                    "ألوان"
                });

            cmbGlobalColor.SelectedIndex = 0;

            cmbGlobalColor.SelectedIndexChanged +=
                CmbGlobalColor_SelectedIndexChanged;

            ComboBoxStyler.ApplyModernStyle(
                cmbGlobalColor,
                Color.FromArgb(0, 122, 204));

            // ========================================================
            // نوع الطباعة لجميع الملفات
            // ========================================================
            Label lblGlobalDuplex =
                new Label
                {
                    Text = "نوع الكل:",
                    Location = new Point(260, 122),
                    AutoSize = true,
                    Font = labelFont
                };

            cmbGlobalDuplex =
                new ComboBox
                {
                    Location =
                        new Point(340, 118),
                    Width = 150,
                    Height = 32,
                    Font = controlFont,
                    FlatStyle = FlatStyle.Flat,
                    DropDownStyle =
                        ComboBoxStyle.DropDownList
                };

            cmbGlobalDuplex.Items.AddRange(
                new object[]
                {
                    "وش واحد",
                    "وش وظهر"
                });

            cmbGlobalDuplex.SelectedIndex = 0;

            cmbGlobalDuplex.SelectedIndexChanged +=
                CmbGlobalDuplex_SelectedIndexChanged;

            ComboBoxStyler.ApplyModernStyle(
                cmbGlobalDuplex,
                Color.FromArgb(0, 122, 204));

            // ========================================================
            // الطابعة
            // ========================================================
            Label lblPrinter =
                new Label
                {
                    Text = "الطابعة:",
                    Location = new Point(520, 122),
                    AutoSize = true,
                    Font = labelFont
                };

            cmbPrinters =
                new ComboBox
                {
                    Location =
                        new Point(600, 118),
                    Width = 230,
                    Height = 32,
                    Font = controlFont,
                    FlatStyle = FlatStyle.Flat,
                    DropDownStyle =
                        ComboBoxStyle.DropDownList
                };

            cmbPrinters.SelectedIndexChanged +=
                CmbPrinters_SelectedIndexChanged;

            ComboBoxStyler.ApplyModernStyle(
                cmbPrinters,
                Color.FromArgb(0, 122, 204));

            // ========================================================
            // عدد النسخ لكل الملفات
            // ========================================================
            Label lblGlobalCopies =
                new Label
                {
                    Text = "نسخ الكل:",
                    Location = new Point(842, 122),
                    AutoSize = true,
                    Font = labelFont
                };

            numGlobalCopies =
                new NumericUpDown
                {
                    Location = new Point(918, 118),
                    Width = 60,
                    Minimum = 1,
                    Maximum = 999,
                    Value = 1,
                    Font = controlFont,
                    TextAlign = HorizontalAlignment.Center
                };

            numGlobalCopies.ValueChanged +=
                (s, e) => ApplyGlobalCopies();

            // Enter يعيد التطبيق على كل الملفات حتى لو الرقم ما اتغيرش
            // (مفيد بعد ما تعدّل نسخ ملفات يدويًا وتحب ترجّع الكل لنفس الرقم).
            numGlobalCopies.KeyDown +=
                (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        // نقل التركيز بيثبّت الرقم المكتوب (ويطبّقه)، وبعدها نطبّق صراحةً.
                        gridFiles.Focus();
                        ApplyGlobalCopies();
                    }
                };

            // إعداد موحّد لكل الأزرار: حجم أكبر، حواف مسطحة أنيقة،
            // ومؤشر يد عند المرور فوقها عشان تبقى واضح إنها قابلة للضغط.
            void StyleActionButton(
                Button button,
                Color backColor)
            {
                button.Height = 38;
                button.BackColor = backColor;
                button.ForeColor = Color.White;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor =
                    ControlPaint.Light(backColor, 0.15f);
                button.FlatAppearance.MouseDownBackColor =
                    ControlPaint.Dark(backColor, 0.05f);
                button.Font =
                    new Font("Segoe UI", 10F, FontStyle.Bold);
                button.Cursor = Cursors.Hand;
                button.TextAlign = ContentAlignment.MiddleCenter;
            }

            // ========================================================
            // إعدادات الطابعة
            // ========================================================
            btnPrintSettings =
                new Button
                {
                    Text = "⚙️ خيارات الطباعة",
                    Location = new Point(395, 68),
                    Width = 130
                };

            StyleActionButton(
                btnPrintSettings,
                Color.FromArgb(108, 117, 125));

            btnPrintSettings.Click +=
                BtnPrintSettings_Click;

            // ========================================================
            // مسح القائمة
            // ========================================================
            btnClearList =
                new Button
                {
                    Text = "🗑️ مسح القائمة",
                    Location =
                        new Point(535, 68),
                    Width = 135
                };

            StyleActionButton(
                btnClearList,
                Color.FromArgb(220, 53, 69));

            btnClearList.Click +=
                BtnClearList_Click;

            // ========================================================
            // حذف المختار
            // ========================================================
            btnDeleteSelected =
                new Button
                {
                    Text = "🗑️ حذف المختار",
                    Location =
                        new Point(680, 68),
                    Width = 135
                };

            StyleActionButton(
                btnDeleteSelected,
                Color.FromArgb(255, 140, 0));

            btnDeleteSelected.Click +=
                BtnDeleteSelected_Click;

            // ========================================================
            // إضافة ملفات يدويًا
            // ========================================================
            btnAddFiles =
                new Button
                {
                    Text = "➕ إضافة ملفات",
                    Location = new Point(825, 68),
                    Width = 150
                };

            StyleActionButton(
                btnAddFiles,
                Color.FromArgb(40, 167, 69));

            btnAddFiles.Click +=
                BtnAddFiles_Click;

            // ========================================================
            // إضافة بطاقة وش وظهر
            // ========================================================
            btnAddCard =
                new Button
                {
                    Text = "🪪 إضافة بطاقة",
                    Location = new Point(985, 68),
                    Width = 150
                };

            StyleActionButton(
                btnAddCard,
                Color.FromArgb(111, 66, 193));

            btnAddCard.Click +=
                BtnAddCard_Click;

            // ========================================================
            // دمج الملفات المحددة في ملف واحد (يظهر لما تحدد ملفين أو أكتر)
            // ========================================================
            btnMergeFiles =
                new Button
                {
                    Text = "🔗 دمج الملفات",
                    Location = new Point(864, 166),
                    Width = 126,
                    Visible = false
                };

            StyleActionButton(
                btnMergeFiles,
                Color.FromArgb(0, 128, 128));

            btnMergeFiles.Click +=
                BtnMergeFiles_Click;

            // ========================================================
            // تعديل الصورة المحددة في Microsoft Office Picture Manager
            // (مكانه على يمين زر تسجيل الخروج، واختصاره يتغير من إعدادات الإدارة).
            // ========================================================
            btnEditImage =
                new Button
                {
                    Text = "🖼️ تعديل الصورة",
                    Location = new Point(1240, 166),
                    Width = 140
                };

            StyleActionButton(
                btnEditImage,
                Color.FromArgb(40, 90, 180));

            btnEditImage.Click +=
                BtnEditImage_Click;

            // فئة مستقلة للملفات المحددة فقط، دون تغيير بقية الملفات.
            lblSelectedFileCategory = new Label
            {
                Text = "الفئة للملفات المحددة:",
                Location = new Point(1178, 108),
                Width = 205,
                Height = 24,
                TextAlign = ContentAlignment.MiddleRight,
                Font = labelFont
            };
            cmbSelectedFileCategory = new ComboBox
            {
                Location = new Point(1178, 132),
                Width = 205,
                Height = 30,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = controlFont
            };
            cmbSelectedFileCategory.Items.AddRange(new object[] { "حسب فئة الشاشة", "الطلبة", "عادي", "مخصص" });
            cmbSelectedFileCategory.SelectedIndex = 0;
            ComboBoxStyler.ApplyModernStyle(cmbSelectedFileCategory, Color.FromArgb(52, 73, 94));
            cmbSelectedFileCategory.SelectedIndexChanged += (_, _) => ApplySelectedFilePricingCategory();

            // ========================================================
            // تسجيل خروج من المستخدم الحالي
            // ========================================================
            btnLogout =
                new Button
                {
                    Text = "🚪 تسجيل خروج",
                    Location = new Point(1000, 68),
                    Width = 150
                };

            StyleActionButton(
                btnLogout,
                Color.FromArgb(178, 34, 34));

            btnLogout.Click +=
                BtnLogout_Click;

            // ========================================================
            // النسخ والتصوير اليدوي.
            // التصوير يتم خارج البرنامج، لذلك نفتح شاشة مستقلة لتسجيل
            // ما تم نسخه وحساب السعر وإضافته للخزينة والتقارير.
            // ========================================================
            btnCopyService = new Button
            {
                Text = "📄 تصوير",
                Location = new Point(975, 16),
                Width = 205,
                Height = 110
            };

            StyleActionButton(
                btnCopyService,
                Color.FromArgb(0, 102, 102));
            btnCopyService.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnCopyService.Click += (s, e) =>
            {
                using var copyForm = new CopyForm(CurrentUserName);
                copyForm.ShowDialog(this);
            };

            // ========================================================
            // إعدادات الإدارة - فوق زر تسجيل الخروج مباشرة.
            // ========================================================
            btnAdminSettings =
                new Button
                {
                    Text = "🔐 إعدادات الإدارة",
                    Location = new Point(1000, 126),
                    Width = 150
                };

            StyleActionButton(
                btnAdminSettings,
                Color.FromArgb(111, 66, 193));

            btnAdminSettings.Click +=
                BtnAdminSettings_Click;

            // ========================================================
            // السجل والملخصات (يومي / شهري / سنوي)
            // ========================================================
            btnReports =
                new Button
                {
                    Text = "📊 السجل والملخصات",
                    Location = new Point(18, 166),
                    Width = 180
                };

            StyleActionButton(
                btnReports,
                Color.FromArgb(52, 73, 94));

            btnReports.Click +=
                (s, e) =>
                {
                    using SummaryForm summaryForm =
                        new SummaryForm(CurrentUserName);

                    summaryForm.ShowDialog(this);
                };

            // صف الأزرار اتضغط شوية عشان يستوعب زر «دمج الملفات» قبل زر الخروج.
            btnReports.Width = 150;

            btnPrintSettings.Location =
                new Point(174, 166);

            btnClearList.Location =
                new Point(310, 166);
            btnClearList.Width = 125;

            btnDeleteSelected.Location =
                new Point(441, 166);
            btnDeleteSelected.Width = 125;

            btnAddFiles.Location =
                new Point(572, 166);
            btnAddFiles.Width = 140;

            btnAddCard.Location =
                new Point(718, 166);
            btnAddCard.Width = 140;

            btnMergeFiles.Location =
                new Point(864, 166);

            btnLogout.Location =
                new Point(1000, 166);

            topPanel.Controls.AddRange(
                new Control[]
                {
                    lblPreset,
                    cmbPricingPreset,
                    lblBlack,
                    numBlackPrice,
                    lblColor,
                    numColorPrice,
                    lblDuplexPrice,
                    numDuplexPrice,
                    lblDuplexColorPrice,
                    numDuplexColorPrice,
                    lblGlobalColor,
                    cmbGlobalColor,
                    lblGlobalDuplex,
                    cmbGlobalDuplex,
                    lblPrinter,
                    cmbPrinters,
                    lblGlobalCopies,
                    numGlobalCopies,
                    btnReports,
                    btnPrintSettings,
                    btnClearList,
                    btnDeleteSelected,
                    btnAddFiles,
                    btnAddCard,
                    btnMergeFiles,
                    btnCopyService,
                    btnAdminSettings,
                    btnLogout,
                    btnEditImage,
                    lblSelectedFileCategory,
                    cmbSelectedFileCategory
                });

            // ========================================================
            // جدول الملفات
            // ========================================================
            gridFiles =
                new DataGridView
                {
                    Dock = DockStyle.Fill,
                    RightToLeft = RightToLeft.No,
                    // الأعمدة بعروض مستقلة مع شريط تمرير أفقي عند ضيق الشاشة,
                    // بدل ضغط كل الأعمدة داخل المساحة المتاحة فتُقص العناوين.
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                    ScrollBars = ScrollBars.Both,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = true,
                    RowHeadersVisible = false,
                    SelectionMode =
                        DataGridViewSelectionMode.FullRowSelect,
                    MultiSelect = true,
                    EditMode =
                        DataGridViewEditMode.EditOnEnter,
                    BackgroundColor =
                        Color.FromArgb(244, 246, 249),
                    AutoGenerateColumns = false,
                    BorderStyle = BorderStyle.None,
                    CellBorderStyle =
                        DataGridViewCellBorderStyle.SingleHorizontal,
                    GridColor =
                        Color.FromArgb(224, 228, 233),
                    EnableHeadersVisualStyles = false,
                    ColumnHeadersHeightSizeMode =
                        DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                    ColumnHeadersHeight = 56,
                    Font = new Font("Segoe UI", 10.5F)
                };

            UiKit.EnableColumnSorting(gridFiles);
            gridFiles.ColumnHeadersDefaultCellStyle =
                new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(52, 73, 94),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Padding = new Padding(4)
                };

            gridFiles.RowsDefaultCellStyle =
                new DataGridViewCellStyle
                {
                    BackColor = Color.White,
                    SelectionBackColor = Color.FromArgb(214, 234, 248),
                    SelectionForeColor = Color.Black,
                    Padding = new Padding(4),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                };

            gridFiles.AlternatingRowsDefaultCellStyle =
                new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(247, 249, 251),
                    SelectionBackColor = Color.FromArgb(214, 234, 248),
                    SelectionForeColor = Color.Black
                };

            // ========================================================
            // المعاينة
            // ========================================================
            DataGridViewImageColumn colThumbnail =
                new DataGridViewImageColumn
                {
                    Name = "Thumbnail",
                    HeaderText = "المعاينة",
                    ImageLayout = DataGridViewImageCellLayout.Zoom,
                    ReadOnly = true,
                    Width = 84,
                    MinimumWidth = 72,
                    AutoSizeMode =
                        DataGridViewAutoSizeColumnMode.None,
                    DefaultCellStyle =
                        new DataGridViewCellStyle
                        {
                            Alignment =
                                DataGridViewContentAlignment.MiddleCenter
                        }
                };

            gridFiles.Columns.Add(colThumbnail);

            // ========================================================
            // اسم الملف
            // ========================================================
            gridFiles.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name = "FileName",
                    HeaderText = "اسم الملف",
                    ReadOnly = true,
                    Width = 280,
                    MinimumWidth = 170,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                    DefaultCellStyle =
                        new DataGridViewCellStyle
                        {
                            Alignment =
                                DataGridViewContentAlignment.MiddleRight,
                            Padding = new Padding(8, 0, 8, 0)
                        }
                });

            // ========================================================
            // النوع
            // ========================================================
            gridFiles.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name = "FileType",
                    HeaderText = "النوع",
                    ReadOnly = true,
                    Width = 80,
                    AutoSizeMode =
                        DataGridViewAutoSizeColumnMode.None
                });

            // ========================================================
            // الصفحات
            // ========================================================
            gridFiles.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name = "Pages",
                    HeaderText = "الصفحات/الوجه",
                    ReadOnly = true,
                    Width = 110,
                    AutoSizeMode =
                        DataGridViewAutoSizeColumnMode.None
                });

            // ========================================================
            // النسخ
            // ========================================================
            gridFiles.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name = "Copies",
                    HeaderText = "النسخ",
                    Width = 90,
                    AutoSizeMode =
                        DataGridViewAutoSizeColumnMode.None,
                    DefaultCellStyle =
                        new DataGridViewCellStyle
                        {
                            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                            Alignment = DataGridViewContentAlignment.MiddleCenter
                        }
                });

            // ========================================================
            // اللون
            // ========================================================
            FlatComboBoxColumn colColor =
                new FlatComboBoxColumn
                {
                    Name = "ColorType",
                    HeaderText = "اللون",
                    FlatStyle =
                        FlatStyle.Flat,
                    Width = 130,
                    AutoSizeMode =
                        DataGridViewAutoSizeColumnMode.None,
                    DefaultCellStyle =
                        new DataGridViewCellStyle
                        {
                            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                            Alignment = DataGridViewContentAlignment.MiddleCenter,
                            BackColor = Color.FromArgb(255, 249, 235)
                        }
                };

            colColor.Items.AddRange(
                new object[]
                {
                    "أسود",
                    "ألوان"
                });

            gridFiles.Columns.Add(
                colColor);

            // ========================================================
            // نوع الطباعة
            // ========================================================
            FlatComboBoxColumn colDuplex =
                new FlatComboBoxColumn
                {
                    Name = "DuplexMode",
                    HeaderText = "نوع الطباعة",
                    FlatStyle =
                        FlatStyle.Flat,
                    Width = 150,
                    AutoSizeMode =
                        DataGridViewAutoSizeColumnMode.None,
                    DefaultCellStyle =
                        new DataGridViewCellStyle
                        {
                            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                            Alignment = DataGridViewContentAlignment.MiddleCenter,
                            BackColor = Color.FromArgb(235, 245, 255)
                        }
                };

            colDuplex.Items.AddRange(
                new object[]
                {
                    "وش واحد",
                    "وش وظهر"
                });

            gridFiles.Columns.Add(
                colDuplex);

            // ========================================================
            // الورق الفعلي
            // ========================================================
            gridFiles.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name = "PhysicalSheets",
                    HeaderText =
                        "ورق الدرج المسحوب",
                    ReadOnly = true,
                    Width = 120,
                    MinimumWidth = 100,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None
                });

            // ========================================================
            // السعر
            // ========================================================
            gridFiles.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name = "Price",
                    HeaderText =
                        "السعر الإجمالي",
                    ReadOnly = true,
                    Width = 120,
                    MinimumWidth = 95,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                    DefaultCellStyle =
                        new DataGridViewCellStyle
                        {
                            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                            ForeColor = Color.FromArgb(30, 120, 60)
                        }
                });

            gridFiles.CurrentCellDirtyStateChanged +=
                GridFiles_CurrentCellDirtyStateChanged;

            gridFiles.CellValueChanged +=
                GridFiles_CellValueChanged;

            gridFiles.RowsRemoved +=
                (s, e) => RecalculateTotals();

            gridFiles.CellValidating +=
                GridFiles_CellValidating;

            gridFiles.SelectionChanged +=
                GridFiles_SelectionChanged;

            gridFiles.CellClick +=
                GridFiles_CellClick;

            gridFiles.CellDoubleClick +=
                GridFiles_CellDoubleClick;

            // منع نافذة DataGridView الافتراضية عند حدوث تحويل قيمة،
            // مع بقاء التحقق الفعلي للقيم داخل الأحداث الخاصة بنا.
            gridFiles.DataError +=
                (s, e) => e.ThrowException = false;

            gridFiles.KeyDown +=
                GridFiles_KeyDown;

            gridFiles.RowTemplate.Height = 96;

            // ========================================================
            // اللوحة السفلية
            // ========================================================
            Panel bottomPanel =
                new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 100,
                    BackColor = Color.White,
                    Padding = new Padding(20, 4, 20, 4)
                };

            Panel bottomPanelSeparator =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 2,
                    BackColor = Color.FromArgb(224, 228, 233)
                };

            bottomPanel.Controls.Add(bottomPanelSeparator);

            lblTotalPages =
                new Label
                {
                    Text =
                        "إجمالي الأوجه المطبوعة: 0",
                    Location =
                        new Point(20, 18),
                    Font =
                        new Font(
                            "Segoe UI",
                            11,
                            FontStyle.Bold),
                    ForeColor = Color.FromArgb(52, 73, 94),
                    AutoSize = true
                };

            lblTotalPapers =
                new Label
                {
                    Text =
                        "ورق الدرج الفعلي: 0 ورقة",
                    Location =
                        new Point(20, 55),
                    Font =
                        new Font(
                            "Segoe UI",
                            11,
                            FontStyle.Bold),
                    ForeColor =
                        Color.FromArgb(192, 57, 43),
                    AutoSize = true
                };

            lblTotalPrice =
                new Label
                {
                    Text =
                        "إجمالي الحساب: 0.00 ج.م",
                    Location =
                        new Point(320, 33),
                    Font =
                        new Font(
                            "Segoe UI",
                            15,
                            FontStyle.Bold),
                    ForeColor =
                        Color.FromArgb(30, 132, 73),
                    AutoSize = true
                };

            // ========================================================
            // طباعة المحدد فقط: بيطبع الملفات المحددة (Selected) في
            // الجدول فقط، عشان يسهل طباعة جزء من القائمة بدل الكل.
            // ========================================================
            btnPrintSelected =
                new Button
                {
                    Text =
                        "🖨️ طباعة المحدد فقط",
                    Location =
                        new Point(730, 20),
                    Size =
                        new Size(220, 60),
                    BackColor =
                        Color.FromArgb(
                            230,
                            126,
                            34),
                    ForeColor = Color.White,
                    Font =
                        new Font(
                            "Segoe UI",
                            11,
                            FontStyle.Bold),
                    FlatStyle =
                        FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };

            btnPrintSelected.FlatAppearance.BorderSize = 0;

            btnPrintSelected.FlatAppearance.MouseOverBackColor =
                ControlPaint.Light(
                    Color.FromArgb(230, 126, 34),
                    0.15f);

            btnPrintSelected.Click +=
                BtnPrintSelected_Click;

            // ========================================================
            // طباعة الكل: بيطبع كل الملفات الموجودة في الجدول.
            // ========================================================
            btnPrintAll =
                new Button
                {
                    Text =
                        "🖨️ طباعة الكل",
                    Location =
                        new Point(960, 20),
                    Size =
                        new Size(200, 60),
                    BackColor =
                        Color.FromArgb(
                            0,
                            122,
                            204),
                    ForeColor = Color.White,
                    Font =
                        new Font(
                            "Segoe UI",
                            12,
                            FontStyle.Bold),
                    FlatStyle =
                        FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };

            btnPrintAll.FlatAppearance.BorderSize = 0;

            btnPrintAll.FlatAppearance.MouseOverBackColor =
                ControlPaint.Light(
                    Color.FromArgb(0, 122, 204),
                    0.15f);

            btnPrintAll.Click +=
                BtnPrintAll_Click;

            bottomPanel.Controls.AddRange(
                new Control[]
                {
                    lblTotalPages,
                    lblTotalPapers,
                    lblTotalPrice,
                    btnPrintSelected,
                    btnPrintAll
                });

            // ========================================================
            // لوحة معاينة ثابتة - تعرض نتيجة الطباعة النهائية
            // (حدود الطابعة + التحجيم + N-up + نطاق الصفحات).
            // ========================================================
            previewPanel =
                new Panel
                {
                    Dock = DockStyle.Right,
                    Width = 520,
                    MinimumSize = new Size(460, 0),
                    BackColor = Color.FromArgb(248, 249, 250),
                    Padding = new Padding(12)
                };

            Panel previewPanelSeparator =
                new Panel
                {
                    Dock = DockStyle.Left,
                    Width = 2,
                    BackColor = Color.FromArgb(224, 228, 233)
                };

            previewPanelPicture =
                new PictureBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.White,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.FixedSingle
                };

            previewPanelTitle =
                new Label
                {
                    Dock = DockStyle.Bottom,
                    Height = 58,
                    Text = "اختر ملفًا من القائمة لعرض نتيجة الطباعة هنا",
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(90, 98, 107),
                    BackColor = Color.FromArgb(248, 249, 250),
                    Padding = new Padding(8)
                };

            // شريط التنقل: سلايدر + مربع رقم الورقة (بنكتب فيه الرقم ونضغط Enter) + سابق/تالي.
            previewNavigator = new SheetNavigator(large: false)
            {
                Dock = DockStyle.Bottom
            };

            previewNavigator.IndexRequested += PreviewNavigator_IndexRequested;
            previewNavigator.EscapePressed += () => gridFiles.Focus();

            // دبل كليك على المعاينة = فتحها بالحجم الكامل (سلايدر + تنقل + أنيميشن).
            previewPanelPicture.Cursor = Cursors.Hand;
            previewPanelPicture.DoubleClick += (s, e) => OpenPreviewViewer();
            new ToolTip().SetToolTip(
                previewPanelPicture,
                "دبل كليك لعرض الورقة بالحجم الكامل");

            previewPanel.Controls.Add(previewPanelPicture);
            previewPanel.Controls.Add(previewNavigator);
            previewPanel.Controls.Add(previewPanelTitle);
            previewPanel.Controls.Add(previewPanelSeparator);

            // تقسيم النافذة إلى مناطق مستقلة بدل وضع الجدول والمعاينة فوق بعضهما.
            // هذا يمنع المعاينة من تغطية شريط الأدوات، ويجعل الجدول يأخذ المساحة
            // المتبقية فقط، وبذلك يظهر شريط التمرير الأفقي عند تجاوز عرض الأعمدة.
            var workspacePanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.FromArgb(244, 246, 249)
            };

            gridFiles.Dock = DockStyle.Fill;
            gridFiles.ScrollBars = ScrollBars.Both;
            previewPanel.Dock = DockStyle.Right;
            previewPanel.Width = 320;
            previewPanel.MinimumSize = new Size(280, 0);

            // الأعمدة تتوزع على عرض الجدول تلقائيًا (من غير سحب أفقي)،
            // والمعاينة تاخد حوالي ربع العرض بس عشان الجدول ياخد المساحة الأكبر.
            FitFilesGridToWidth();
            workspacePanel.Resize +=
                (s2, e2) =>
                {
                    previewPanel.Width =
                        Math.Clamp((int)(workspacePanel.Width * 0.24), 280, 380);
                };

            // يضاف الجدول أولًا ثم المعاينة؛ المعاينة تحجز عرضها داخل مساحة العمل
            // والجدول يملأ الجزء المتبقي دون أن يتداخل معها أو مع الشرائط العلوية والسفلية.
            workspacePanel.Controls.Add(gridFiles);
            workspacePanel.Controls.Add(previewPanel);

            // شريط تقدم الطباعة (مخفي لحد ما نبدأ إرسال ملفات للطابعة).
            BuildPrintStatusPanel();
            workspacePanel.Controls.Add(printStatusPanel!);

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.FromArgb(244, 246, 249)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 222));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

            topPanel.Dock = DockStyle.Fill;
            bottomPanel.Dock = DockStyle.Fill;
            workspacePanel.Dock = DockStyle.Fill;
            mainLayout.Controls.Add(topPanel, 0, 0);
            mainLayout.Controls.Add(workspacePanel, 0, 1);
            mainLayout.Controls.Add(bottomPanel, 0, 2);
            Controls.Add(mainLayout);
        }

        // ============================================================
        // أحداث الجدول
        // ============================================================
        private void GridFiles_CurrentCellDirtyStateChanged(
            object? sender,
            EventArgs e)
        {
            if (gridFiles.IsCurrentCellDirty)
            {
                gridFiles.CommitEdit(
                    DataGridViewDataErrorContexts.Commit);
            }
        }

        private void GridFiles_CellValueChanged(
            object? sender,
            DataGridViewCellEventArgs e)
        {
            if (!isGlobalUpdating &&
                !isRecalculating &&
                e.RowIndex >= 0)
            {
                RecalculateTotals();
            }
        }

        // ============================================================
        // التحقق من النسخ
        // ============================================================
        private void GridFiles_CellValidating(
            object? sender,
            DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            if (gridFiles.Columns[e.ColumnIndex].Name !=
                "Copies")
            {
                return;
            }

            string value =
                e.FormattedValue?.ToString()
                ?? "";

            if (!int.TryParse(
                    value,
                    out int copies) ||
                copies < 1)
            {
                MessageBox.Show(
                    "عدد النسخ يجب أن يكون رقمًا صحيحًا أكبر من أو يساوي 1.",
                    "قيمة غير صحيحة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                e.Cancel = true;
            }
        }

        // ============================================================
        // اللون لجميع الملفات
        // ============================================================
        private void CmbGlobalColor_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            if (isGlobalUpdating ||
                cmbGlobalColor.SelectedItem == null)
            {
                return;
            }

            string selectedColor =
                cmbGlobalColor.SelectedItem
                    .ToString()!;

            isGlobalUpdating = true;

            try
            {
                foreach (
                    DataGridViewRow row
                    in gridFiles.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    row.Cells["ColorType"].Value =
                        selectedColor;
                }

                gridFiles.EndEdit();
            }
            finally
            {
                isGlobalUpdating = false;
            }

            UpdateSelectedPrinterCapabilities();
            RecalculateTotals();
            previewSheetIndex = 0;
            UpdateRightPreview();
        }

        // ============================================================
        // عدد النسخ لكل الملفات
        // بيتطبق على كل الملفات الحالية، وأي ملف يتضاف بعد كده بياخد نفس الرقم.
        // تعديل نسخ ملف يدويًا بعد كده بيتغير عادي ومش بيتأثر بالباقي.
        // ============================================================
        private void ApplyGlobalCopies()
        {
            if (isGlobalUpdating)
                return;

            int copies = (int)numGlobalCopies.Value;

            isGlobalUpdating = true;

            try
            {
                gridFiles.EndEdit();

                foreach (
                    DataGridViewRow row
                    in gridFiles.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    row.Cells["Copies"].Value =
                        copies;
                }
            }
            finally
            {
                isGlobalUpdating = false;
            }

            RecalculateTotals();
        }

        // ============================================================
        // وش واحد / وش وظهر
        // ============================================================
        private void CmbGlobalDuplex_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            if (isGlobalUpdating ||
                cmbGlobalDuplex.SelectedItem == null)
            {
                return;
            }

            string selectedDuplex =
                cmbGlobalDuplex.SelectedItem
                    .ToString()!;

            isGlobalUpdating = true;

            try
            {
                foreach (
                    DataGridViewRow row
                    in gridFiles.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    row.Cells["DuplexMode"].Value =
                        selectedDuplex;
                }

                gridFiles.EndEdit();
            }
            finally
            {
                isGlobalUpdating = false;
            }

            UpdateSelectedPrinterCapabilities();
            RecalculateTotals();
            previewSheetIndex = 0;
            UpdateRightPreview();
        }

        // ============================================================
        // تغيير الطابعة
        // ============================================================
        private void CmbPrinters_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            string? printer =
                cmbPrinters.SelectedItem?
                    .ToString();

            if (string.IsNullOrWhiteSpace(printer))
                return;

            try
            {
                currentPrinterSettings =
                    new PrinterSettings
                    {
                        PrinterName =
                            printer
                    };
            }
            catch
            {
            }

            UpdateSelectedPrinterCapabilities();
            previewSheetIndex = 0;
            UpdateRightPreview();
        }

        // ============================================================
        // حذف المختار
        // ============================================================
        private void BtnDeleteSelected_Click(
            object? sender,
            EventArgs e)
        {
            List<DataGridViewRow> selectedRows =
                gridFiles.SelectedRows
                    .Cast<DataGridViewRow>()
                    .Where(row => !row.IsNewRow)
                    .OrderBy(row => row.Index)
                    .ToList();

            if (selectedRows.Count == 0)
            {
                MessageBox.Show(
                    "برجاء اختيار ملف واحد أو أكثر من القائمة أولاً.",
                    "حذف الملفات",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            foreach (DataGridViewRow selectedRow in selectedRows)
            {
                DisposeRowThumbnail(selectedRow);

                gridFiles.Rows.Remove(
                    selectedRow);
            }

            RecalculateTotals();
        }

        // ============================================================
        // تسجيل خروج من المستخدم الحالي
        // (بيقفل الجلسة الحالية ويعيد تشغيل البرنامج عشان يرجع
        // لشاشة تسجيل الدخول من جديد).
        // ============================================================
        private void BtnLogout_Click(
            object? sender,
            EventArgs e)
        {
            if (isPrintingInBackground || activeBatches.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "فيه طباعة لسه شغالة. استنى لحد ما تخلص (أو اضغط «إيقاف») قبل تسجيل الخروج.",
                    "الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult result =
                MessageBox.Show(
                    $"هل أنت متأكد من تسجيل الخروج من المستخدم " +
                    $"\"{CurrentUserName}\"؟",
                    "تأكيد تسجيل الخروج",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            try
            {
                DisposeAllThumbnails();

                currentPreviewImage?.Dispose();
                currentPreviewImage = null;
            }
            catch
            {
            }

            // نرجع لشاشة تسجيل الدخول (الـDashboard بيقفل كل الشاشات، وProgram يفتح الدخول من جديد).
            AppSession.RequestLogout();
        }

        // ============================================================
        // مسح القائمة
        // ============================================================
        private void BtnClearList_Click(
            object? sender,
            EventArgs e)
        {
            if (gridFiles.Rows.Count == 0)
                return;

            DisposeAllThumbnails();
            gridFiles.Rows.Clear();

            RecalculateTotals();
        }

        // ============================================================
        // مسح النسخ المؤقتة بتاعة الصف (صور واتساب، ملفات مدمجة، نسخ تعديل...).
        // بنمسح بس اللي جوه %TEMP%\LibraryPrintApp، والملفات الأصلية مبتتلمسش.
        // ============================================================
        private void ReleaseRowTempFiles(DataGridViewRow row)
        {
            try
            {
                if (row.Tag is FileEntry entry)
                {
                    editWatch.Remove(entry.PrintPath);
                    TempFiles.TryDelete(entry.PrintPath);
                    TempFiles.TryDelete(entry.OriginalPath);
                }
            }
            catch
            {
            }
        }

        // ============================================================
        // تعديل الصورة في Microsoft Office Picture Manager
        // الصورة بتتفتح كنسخة شغل مؤقتة (الأصلية متتغيرش)، ولما تحفظ في Picture Manager
        // البرنامج بيلقط التغيير ويحدّث المعاينة والصورة المصغرة، والطباعة بتطلع من النسخة المعدّلة.
        // ============================================================
        private readonly Dictionary<string, (long Length, long Ticks)> editWatch =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, (long Length, long Ticks)> editSeenChange =
            new(StringComparer.OrdinalIgnoreCase);

        private System.Windows.Forms.Timer? editWatchTimer;

        private static readonly string PictureEditorSettingPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LibraryPrintApp",
                "picture-editor.txt");

        private static string? FindPictureManager()
        {
            try
            {
                if (File.Exists(PictureEditorSettingPath))
                {
                    string saved = File.ReadAllText(PictureEditorSettingPath).Trim();
                    if (File.Exists(saved))
                        return saved;
                }
            }
            catch
            {
            }

            try
            {
                object? reg = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\OIS.EXE",
                    "",
                    null);
                if (reg is string regPath && File.Exists(regPath))
                    return regPath;
            }
            catch
            {
            }

            var roots = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"C:\Program Files",
                @"C:\Program Files (x86)"
            };

            foreach (string root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string office = Path.Combine(root, "Microsoft Office");
                    if (!Directory.Exists(office)) continue;

                    var searchDirs = new List<string>(Directory.GetDirectories(office, "Office*"));
                    string clickToRun = Path.Combine(office, "root");
                    if (Directory.Exists(clickToRun))
                        searchDirs.AddRange(Directory.GetDirectories(clickToRun, "Office*"));

                    foreach (string dir in searchDirs)
                    {
                        string candidate = Path.Combine(dir, "OIS.EXE");
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private string? AskForPictureManager()
        {
            if (MessageBox.Show(
                    this,
                    "مش لاقي Microsoft Office Picture Manager على الجهاز.\n\n" +
                    "تحب تحدد مكان الملف OIS.EXE بنفسك؟ (هيتحفظ ومش هيسألك تاني)",
                    "تعديل الصورة",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return null;

            using var dialog = new OpenFileDialog
            {
                Title = "اختر OIS.EXE (Microsoft Office Picture Manager)",
                Filter = "Picture Manager (OIS.EXE)|OIS.EXE|كل البرامج (*.exe)|*.exe",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PictureEditorSettingPath)!);
                File.WriteAllText(PictureEditorSettingPath, dialog.FileName);
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر حفظ مسار برنامج تعديل الصور", ex);
            }

            return dialog.FileName;
        }

        private void BtnEditImage_Click(
            object? sender,
            EventArgs e)
        {
            List<DataGridViewRow> rows =
                gridFiles.SelectedRows
                    .Cast<DataGridViewRow>()
                    .Where(r => !r.IsNewRow)
                    .ToList();

            if (rows.Count != 1)
            {
                MessageBox.Show(
                    this,
                    "حدد صورة واحدة بس من القائمة عشان تتعدّل.",
                    "تعديل الصورة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DataGridViewRow row = rows[0];
            if (row.Tag is not FileEntry entry)
                return;

            string ext = Path.GetExtension(entry.PrintPath).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" &&
                ext != ".bmp" && ext != ".tif" && ext != ".tiff")
            {
                MessageBox.Show(
                    this,
                    "الملف المحدد مش صورة. التعديل بيشتغل على الصور بس (JPG / PNG / BMP / TIFF).",
                    "تعديل الصورة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (isPrintingInBackground)
            {
                MessageBox.Show(
                    this,
                    "استنى لحد ما إرسال الملفات للطابعة يخلص (ثواني) وبعدين عدّل الصورة.",
                    "تعديل الصورة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(entry.PrintPath))
            {
                MessageBox.Show(
                    this,
                    "ملف الصورة مش موجود.",
                    "تعديل الصورة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string? editor = FindPictureManager() ?? AskForPictureManager();
            if (editor == null)
                return;

            try
            {
                string workPath = entry.PrintPath;

                // الصورة الأصلية (من فلاشة/تنزيلات مثلًا) منعدّلهاش: بنشتغل على نسخة مؤقتة،
                // وبتتمسح لما تشيل الصورة من القائمة أو تقفل الشاشة.
                if (!TempFiles.IsAppOwned(workPath))
                {
                    string dir = Path.Combine(TempFiles.Root, "Edited", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    workPath = Path.Combine(dir, Path.GetFileName(entry.PrintPath));
                    File.Copy(entry.PrintPath, workPath, true);
                    row.Tag = new FileEntry(entry.OriginalPath, workPath, entry.Options, entry.PricingCategory);
                }

                var info = new FileInfo(workPath);
                editWatch[workPath] = (info.Length, info.LastWriteTimeUtc.Ticks);
                editSeenChange.Remove(workPath);
                StartEditWatchTimer();

                Process.Start(new ProcessStartInfo
                {
                    FileName = editor,
                    Arguments = "\"" + workPath + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر فتح الصورة في Picture Manager", ex);
                MessageBox.Show(
                    this,
                    "تعذر فتح الصورة في Picture Manager:\n\n" + ex.Message,
                    "تعديل الصورة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void StartEditWatchTimer()
        {
            if (editWatchTimer == null)
            {
                editWatchTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                editWatchTimer.Tick += (_, _) => CheckEditedImages();
            }

            editWatchTimer.Start();
        }

        /// <summary>
        /// بيقارن حجم/وقت تعديل الصور اللي مفتوحة للتعديل. لو اتغيرت وثبتت (الحفظ خلص)
        /// بنحدّث الصورة المصغرة والمعاينة.
        /// </summary>
        private void CheckEditedImages()
        {
            if (editWatch.Count == 0)
            {
                editWatchTimer?.Stop();
                return;
            }

            foreach (string path in editWatch.Keys.ToList())
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists)
                    {
                        editWatch.Remove(path);
                        editSeenChange.Remove(path);
                        continue;
                    }

                    var stamp = (info.Length, info.LastWriteTimeUtc.Ticks);
                    if (stamp == editWatch[path])
                        continue;

                    // لازم الحجم/الوقت يثبتوا لفحصين ورا بعض عشان نتأكد إن الحفظ خلص.
                    if (!editSeenChange.TryGetValue(path, out var seen) || seen != stamp)
                    {
                        editSeenChange[path] = stamp;
                        continue;
                    }

                    RefreshEditedImage(path);
                    editWatch[path] = stamp;
                    editSeenChange.Remove(path);
                }
                catch (Exception ex)
                {
                    // الملف لسه مقفول أو بيتكتب: هنحاول تاني في الفحص الجاي.
                    AppLog.Info("تحديث الصورة المعدّلة هيتعاد: " + ex.Message);
                }
            }
        }

        private void RefreshEditedImage(string path)
        {
            foreach (DataGridViewRow row in gridFiles.Rows)
            {
                if (row.IsNewRow || row.Tag is not FileEntry entry)
                    continue;

                if (!string.Equals(entry.PrintPath, path, StringComparison.OrdinalIgnoreCase))
                    continue;

                Image newThumbnail = CreateImageThumbnail(path);
                Image? old = row.Cells["Thumbnail"].Value as Image;
                row.Cells["Thumbnail"].Value = newThumbnail;
                old?.Dispose();
            }

            // مفتاح المعاينة فيه حجم ووقت الملف، فهتتحمّل الصورة الجديدة تلقائيًا.
            lastPreviewKey = null;
            UpdateRightPreview();
        }

        // ============================================================
        // تنظيف الصور المصغرة
        // ============================================================
        private void DisposeRowThumbnail(
            DataGridViewRow row)
        {
            // الصف بيتشال أو الشاشة بتتقفل: نمسح النسخ المؤقتة بتاعته (مش الملفات الأصلية).
            ReleaseRowTempFiles(row);

            try
            {
                if (row.Cells["Thumbnail"].Value is Image image)
                {
                    row.Cells["Thumbnail"].Value = null;
                    image.Dispose();
                }
            }
            catch
            {
            }
        }

        private void DisposeAllThumbnails()
        {
            try
            {
                foreach (DataGridViewRow row in gridFiles.Rows)
                {
                    if (!row.IsNewRow)
                        DisposeRowThumbnail(row);
                }
            }
            catch
            {
            }
        }

        // ============================================================
        // تحديث صلاحيات الطابعة بناءً على الإعدادات المختارة
        // ============================================================
        private void UpdateSelectedPrinterCapabilities()
        {
            string printerName =
                cmbPrinters.SelectedItem?.ToString()
                ?? currentPrinterSettings.PrinterName;

            if (string.IsNullOrWhiteSpace(printerName))
                return;

            try
            {
                PrinterSettings settings =
                    new PrinterSettings
                    {
                        PrinterName = printerName
                    };

                if (!settings.IsValid)
                    return;

                string duplex =
                    cmbGlobalDuplex.SelectedItem?.ToString()
                    ?? "وش واحد";

                if (duplex == "وش وظهر" &&
                    !settings.CanDuplex)
                {
                    isGlobalUpdating = true;

                    try
                    {
                        cmbGlobalDuplex.SelectedItem =
                            "وش واحد";

                        foreach (DataGridViewRow row in gridFiles.Rows)
                        {
                            if (row.IsNewRow)
                                continue;

                            row.Cells["DuplexMode"].Value =
                                "وش واحد";
                        }
                    }
                    finally
                    {
                        isGlobalUpdating = false;
                    }

                    MessageBox.Show(
                        $"الطابعة الحالية لا تدعم الطباعة وش وظهر:\n\n" +
                        $"{printerName}\n\n" +
                        "تم الرجوع تلقائيًا إلى وش واحد.",
                        "إعداد الطباعة",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch
            {
            }
        }

        // ============================================================
        // تغيير الطابعة
        // ============================================================
        private void LoadInstalledPrinters()
        {
            cmbPrinters.Items.Clear();

            foreach (
                string printer
                in PrinterSettings.InstalledPrinters)
            {
                cmbPrinters.Items.Add(
                    printer);
            }

            if (cmbPrinters.Items.Count == 0)
                return;

            try
            {
                using PrintDocument pd =
                    new PrintDocument();

                string defaultPrinter =
                    pd.PrinterSettings.PrinterName;

                int index =
                    cmbPrinters.Items.IndexOf(
                        defaultPrinter);

                cmbPrinters.SelectedIndex =
                    index >= 0
                    ? index
                    : 0;

                currentPrinterSettings =
                    new PrinterSettings
                    {
                        PrinterName =
                            cmbPrinters.SelectedItem?
                                .ToString()
                            ?? defaultPrinter
                    };
            }
            catch
            {
                cmbPrinters.SelectedIndex =
                    0;
            }
        }

        // ============================================================
        // فتح إعدادات الطابعة
        // ============================================================
        private void BtnPrintSettings_Click(
            object? sender,
            EventArgs e)
        {
            OpenPrintOptionsForSelectedRows();
        }

        private void OpenPrintOptionsForSelectedRows()
        {
            List<DataGridViewRow> rows = gridFiles.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .OrderBy(r => r.Index)
                .ToList();

            if (rows.Count == 0 && gridFiles.CurrentRow != null && !gridFiles.CurrentRow.IsNewRow)
            {
                rows.Add(gridFiles.CurrentRow);
            }

            if (rows.Count == 0)
            {
                MessageBox.Show(
                    "حدد ملفًا من القائمة أولاً لفتح خيارات الطباعة الخاصة به.",
                    "خيارات الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DataGridViewRow row = rows[0];
            FileEntry? entry = row.Tag as FileEntry;

            if (entry == null)
            {
                MessageBox.Show(
                    "تعذر قراءة إعدادات الملف المحدد.",
                    "خيارات الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            int pageCount = GetPositiveInt(row.Cells["Pages"].Value, 1);
            string fileName = row.Cells["FileName"].Value?.ToString() ?? "الملف";
            string printerName = cmbPrinters.SelectedItem?.ToString() ?? currentPrinterSettings.PrinterName;

            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show(
                    "اختر الطابعة أولاً حتى يتم تحميل مقاسات الورق المتاحة.",
                    "خيارات الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            PrinterSettings settings = new PrinterSettings { PrinterName = printerName };
            List<PaperSize> paperSizes = settings.IsValid
                ? settings.PaperSizes.Cast<PaperSize>().ToList()
                : new List<PaperSize>();

            using PrintOptionsForm optionsForm = new PrintOptionsForm(
                fileName,
                pageCount,
                entry.Options,
                printerName,
                paperSizes,
                () => OpenSystemPrinterProperties(printerName));

            if (optionsForm.ShowDialog(this) != DialogResult.OK)
                return;

            PrintOptions selectedOptions = optionsForm.Options.Clone();
            selectedOptions.ClampToPageCount(pageCount);
            // إعدادات النافذة تطبق على الملفات المحددة فقط؛ لا نكتبها كإعداد افتراضي دائم.
            // إعادة التعيين بعد إتمام طباعة الكل تظل مسؤولة عن تجهيز العملية التالية بالقيم الافتراضية.

            foreach (DataGridViewRow selectedRow in rows)
            {
                if (selectedRow.Tag is not FileEntry selectedEntry)
                    continue;

                PrintOptions applied = selectedOptions.Clone();
                applied.ClampToPageCount(GetPositiveInt(selectedRow.Cells["Pages"].Value, 1));
                selectedRow.Tag = new FileEntry(
                    selectedEntry.OriginalPath,
                    selectedEntry.PrintPath,
                    applied,
                    selectedEntry.PricingCategory);
            }

            if (optionsForm.ApplyToAll)
            {
                foreach (DataGridViewRow allRow in gridFiles.Rows)
                {
                    if (allRow.IsNewRow || allRow.Tag is not FileEntry allEntry)
                        continue;

                    PrintOptions applied = selectedOptions.Clone();
                    applied.ClampToPageCount(GetPositiveInt(allRow.Cells["Pages"].Value, 1));
                    allRow.Tag = new FileEntry(
                        allEntry.OriginalPath,
                        allEntry.PrintPath,
                        applied,
                        allEntry.PricingCategory);
                }
            }

            UpdatePrintOptionTooltips();
            RecalculateTotals();
            previewSheetIndex = 0;
            UpdateRightPreview();
        }

        private void UpdatePrintOptionTooltips()
        {
            foreach (DataGridViewRow r in gridFiles.Rows)
            {
                if (r.IsNewRow || r.Tag is not FileEntry e)
                    continue;

                int total = GetPositiveInt(r.Cells["Pages"].Value, 1);
                PrintOptions o = e.Options.Clone();
                o.ClampToPageCount(total);

                string range = o.PageRangeMode == PrintPageRangeMode.AllPages
                    ? "كل الصفحات"
                    : (string.IsNullOrWhiteSpace(o.PageRangesText)
                        ? $"من {o.FromPage} إلى {o.ToPage}"
                        : o.PageRangesText);

                string scaling = o.Scaling switch
                {
                    PrintScalingMode.FitToPrintableArea => "ملء المنطقة القابلة للطباعة",
                    PrintScalingMode.ActualSize => "الحجم الفعلي",
                    _ => $"مخصص {o.CustomScalePercent:0}%"
                };

                r.Cells["Pages"].ToolTipText =
                    $"إجمالي صفحات الملف: {total}\n" +
                    $"النطاق الحالي: {range}\n" +
                    $"صفحات في الورقة: {o.PagesPerSheet}\n" +
                    $"المسافة بين الصفحات: {o.NUpGapInches:0.000} بوصة\n" +
                    $"التحجيم: {scaling}";
            }
        }

        private void OpenSystemPrinterProperties(string printerName)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = "printui.dll,PrintUIEntry /e /n \"" + printerName.Replace("\"", "") + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر فتح خصائص الطابعة:\n\n" + ex.Message,
                    "خصائص الطابعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // Drag Enter
        // ============================================================
        private void Form1_DragEnter(
            object? sender,
            DragEventArgs e)
        {
            if (e.Data != null &&
                e.Data.GetDataPresent(
                    DataFormats.FileDrop))
            {
                e.Effect =
                    DragDropEffects.Copy;
            }
        }

        // ============================================================
        // Drag Drop
        // ============================================================
        private void Form1_DragDrop(
            object? sender,
            DragEventArgs e)
        {
            if (e.Data == null ||
                !e.Data.GetDataPresent(
                    DataFormats.FileDrop))
            {
                return;
            }

            string[]? files =
                (string[]?)
                e.Data.GetData(
                    DataFormats.FileDrop);

            if (files == null)
                return;

            foreach (
                string filePath
                in files)
            {
                ProcessFile(
                    filePath);
            }

            RecalculateTotals();
        }

        // ============================================================
        // إضافة ملفات يدويًا
        // ============================================================
        private void BtnAddFiles_Click(
            object? sender,
            EventArgs e)
        {
            using OpenFileDialog dialog =
                new OpenFileDialog
                {
                    Title = "اختيار ملفات للطباعة",
                    Multiselect = true,
                    Filter =
                        "ملفات مدعومة (*.pdf;*.doc;*.docx;*.ppt;*.pptx;*.pps;*.ppsx;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|" +
                        "*.pdf;*.doc;*.docx;*.ppt;*.pptx;*.pps;*.ppsx;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|" +
                        "ملفات PDF (*.pdf)|*.pdf|" +
                        "ملفات وورد (*.doc;*.docx)|*.doc;*.docx|" +
                        "ملفات PowerPoint (*.ppt;*.pptx;*.pps;*.ppsx)|*.ppt;*.pptx;*.pps;*.ppsx|" +
                        "الصور (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|" +
                        "*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|" +
                        "كل الملفات (*.*)|*.*"
                };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            foreach (string filePath in dialog.FileNames)
                ProcessFile(filePath);

            RecalculateTotals();
        }

        // ============================================================
        // إضافة بطاقة وش وظهر / تحويل الملفين المحددين لبطاقة
        //
        // - لو المستخدم محدد في القائمة صورتين بالظبط: الزر بيتحول لـ «تحويل لبطاقة»،
        //   والصورة اللي حددها أولًا = الوش، والتانية = الظهر. الصورتين بيتشالوا من القائمة
        //   ويتحل محلهم صف واحد للبطاقة.
        // - غير كده: الزر «إضافة بطاقة» بيفتح اختيار الوش ثم الظهر من الجهاز.
        //
        // في الحالتين بتتجهز البطاقة كـ PDF من صفحتين A4 (البطاقة في منتصف النصف العلوي،
        // بهامش أبيض) ويتضبط الطباعة وش وظهر تلقائياً.
        // ============================================================
        private const string AddCardText = "🪪 إضافة بطاقة";
        private const string ConvertToCardText = "🪪 تحويل لبطاقة";

        // ترتيب تحديد الصفوف (الأقدم أولًا) عشان نعرف أي ملف اتحدد الأول.
        private readonly List<DataGridViewRow> selectionOrder = new();

        private void TrackSelectionOrder()
        {
            List<DataGridViewRow> selected =
                gridFiles.SelectedRows
                    .Cast<DataGridViewRow>()
                    .Where(row => !row.IsNewRow && row.DataGridView != null)
                    .ToList();

            selectionOrder.RemoveAll(
                row => !selected.Contains(row));

            // الصفوف الجديدة في نفس الضغطة (مثلاً تحديد بـ Shift) بترتّب حسب مكانها في القائمة.
            foreach (DataGridViewRow row in selected.OrderBy(r => r.Index))
            {
                if (!selectionOrder.Contains(row))
                    selectionOrder.Add(row);
            }

            UpdateCardButton();
        }

        private static bool IsCardImageRow(
            DataGridViewRow row)
        {
            if (row.IsNewRow || row.Tag is not FileEntry entry)
                return false;

            string ext =
                Path.GetExtension(entry.OriginalPath)
                    .ToLowerInvariant();

            return ext == ".jpg" ||
                   ext == ".jpeg" ||
                   ext == ".png" ||
                   ext == ".bmp" ||
                   ext == ".tif" ||
                   ext == ".tiff";
        }

        // صورتين محددتين بالظبط (الأولى = الوش، التانية = الظهر).
        private bool TryGetSelectedCardPair(
            out DataGridViewRow frontRow,
            out DataGridViewRow backRow)
        {
            frontRow = null!;
            backRow = null!;

            // نتجاهل أي صف اتشال من الجدول أو اتلغى تحديده من غير ما يوصلنا إشعار.
            selectionOrder.RemoveAll(
                row => row.DataGridView == null || !row.Selected);

            if (selectionOrder.Count != 2 ||
                !IsCardImageRow(selectionOrder[0]) ||
                !IsCardImageRow(selectionOrder[1]))
            {
                return false;
            }

            frontRow = selectionOrder[0];
            backRow = selectionOrder[1];
            return true;
        }

        private void UpdateCardButton()
        {
            if (btnAddCard == null)
                return;

            bool convertMode =
                TryGetSelectedCardPair(
                    out _,
                    out _);

            btnAddCard.Text =
                convertMode
                    ? ConvertToCardText
                    : AddCardText;

            // زر الدمج يظهر لما يتحدد ملفين أو أكتر.
            if (btnMergeFiles != null)
            {
                btnMergeFiles.Visible =
                    selectionOrder.Count(
                        row => row.DataGridView != null &&
                               !row.IsNewRow &&
                               row.Selected) >= 2;
            }
        }

        // ============================================================
        // دمج الملفات المحددة في ملف PDF واحد
        //
        // الترتيب = ترتيب التحديد (أول ملف حددته يبقى الأول).
        // PDF/Word/PowerPoint بتتضاف صفحاتها كما هي، والصور بتتحط كل صورة
        // في صفحة A4. بعد الدمج الملفات الأصلية بتتشال من القائمة ويتضاف
        // صف واحد، فالطباعة وش وظهر والحساب يتعاملوا معاه كملف واحد.
        // ============================================================
        private void BtnMergeFiles_Click(
            object? sender,
            EventArgs e)
        {
            selectionOrder.RemoveAll(
                row => row.DataGridView == null ||
                       row.IsNewRow ||
                       !row.Selected);

            List<DataGridViewRow> rows =
                selectionOrder.ToList();

            if (rows.Count < 2)
                return;

            try
            {
                Cursor = Cursors.WaitCursor;
                btnMergeFiles.Enabled = false;

                string directory =
                    Path.Combine(
                        Path.GetTempPath(),
                        "LibraryPrintApp",
                        "Merged");

                Directory.CreateDirectory(directory);

                string firstName =
                    Path.GetFileNameWithoutExtension(
                        ((FileEntry)rows[0].Tag!).OriginalPath);

                string outputPath =
                    Path.Combine(
                        directory,
                        "مدمج_" + firstName + "_" +
                        DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                        ".pdf");

                string firstColor =
                    rows[0].Cells["ColorType"].Value?.ToString() ?? "أسود";

                string firstDuplex =
                    rows[0].Cells["DuplexMode"].Value?.ToString() ?? "وش واحد";

                int firstCopies =
                    GetPositiveInt(rows[0].Cells["Copies"].Value, 1);

                using (PdfSharpPdfDocument merged =
                       new PdfSharpPdfDocument())
                {
                    foreach (DataGridViewRow row in rows)
                    {
                        FileEntry entry = (FileEntry)row.Tag!;
                        string source = entry.PrintPath;

                        if (string.Equals(
                                Path.GetExtension(source),
                                ".pdf",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            using PdfSharpPdfDocument input =
                                PdfSharp.Pdf.IO.PdfReader.Open(
                                    source,
                                    PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

                            for (int i = 0; i < input.PageCount; i++)
                                merged.AddPage(input.Pages[i]);
                        }
                        else
                        {
                            AddImagePageForMerge(merged, source);
                        }
                    }

                    if (merged.PageCount == 0)
                        throw new InvalidOperationException(
                            "الملفات المحددة مفيهاش صفحات تتدمج.");

                    merged.Save(outputPath);
                }

                int before = gridFiles.Rows.Count;

                ProcessFile(outputPath);

                if (gridFiles.Rows.Count <= before)
                {
                    MessageBox.Show(
                        "تم دمج الملفات لكن تعذر إضافة الملف الناتج للقائمة.",
                        "دمج الملفات",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                DataGridViewRow newRow =
                    gridFiles.Rows[gridFiles.Rows.Count - 1];

                // الملف المدموج يمشي بنفس إعدادات أول ملف (اللون / وش وظهر / النسخ).
                newRow.Cells["ColorType"].Value = firstColor;
                newRow.Cells["DuplexMode"].Value = firstDuplex;
                newRow.Cells["Copies"].Value = firstCopies;

                foreach (DataGridViewRow oldRow in rows)
                {
                    if (oldRow.DataGridView == null)
                        continue;

                    DisposeRowThumbnail(oldRow);
                    gridFiles.Rows.Remove(oldRow);
                }

                selectionOrder.Clear();

                RecalculateTotals();

                gridFiles.ClearSelection();
                newRow.Selected = true;

                if (gridFiles.Columns.Count > 0)
                    gridFiles.CurrentCell =
                        newRow.Cells["FileName"];
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر دمج الملفات:\n\n" + ex.Message,
                    "خطأ في دمج الملفات",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                TempFiles.CleanSubfolder("Converted");
                btnMergeFiles.Enabled = true;
                Cursor = Cursors.Default;
                UpdateCardButton();
            }
        }

        /// <summary>
        /// بيفتح الصورة لإدخالها في PDF. لو PDFsharp رفضت الصيغة (Unsupported image format — زي صور
        /// واتساب WebP أو JPEG بصيغة غريبة)، بنحوّلها لنسخة PNG/JPEG عادية في مجلد مؤقت ونكمّل.
        /// الصورة الأصلية مبتتغيرش.
        /// </summary>
        private static XImage LoadImageForPdf(string imagePath)
        {
            try
            {
                return XImage.FromFile(imagePath);
            }
            catch (Exception firstError)
            {
                string dir = Path.Combine(Path.GetTempPath(), "LibraryPrintApp", "Converted");
                Directory.CreateDirectory(dir);
                string baseName = Guid.NewGuid().ToString("N");

                // 1) GDI+ (بيفتح معظم الصيغ اللي ويندوز يعرفها)
                try
                {
                    using System.Drawing.Image img = System.Drawing.Image.FromFile(imagePath);
                    string png = Path.Combine(dir, baseName + ".png");
                    img.Save(png, ImageFormat.Png);
                    return XImage.FromFile(png);
                }
                catch (Exception ex)
                {
                    AppLog.Info("تحويل الصورة عبر GDI+ فشل، هجرب OpenCV: " + ex.Message);
                }

                // 2) OpenCV (بيفتح WebP وصيغ تانية)
                try
                {
                    using Cv.Mat mat = Cv.Cv2.ImRead(imagePath, Cv.ImreadModes.Color);
                    if (mat.Empty())
                    {
                        byte[] bytes = File.ReadAllBytes(imagePath);
                        using Cv.Mat decoded = Cv.Cv2.ImDecode(bytes, Cv.ImreadModes.Color);
                        if (decoded.Empty())
                            throw new InvalidOperationException("OpenCV مقدرش يقرا الصورة.");
                        string jpg2 = Path.Combine(dir, baseName + ".jpg");
                        Cv.Cv2.ImWrite(jpg2, decoded, new Cv.ImageEncodingParam(Cv.ImwriteFlags.JpegQuality, 95));
                        return XImage.FromFile(jpg2);
                    }

                    string jpg = Path.Combine(dir, baseName + ".jpg");
                    Cv.Cv2.ImWrite(jpg, mat, new Cv.ImageEncodingParam(Cv.ImwriteFlags.JpegQuality, 95));
                    return XImage.FromFile(jpg);
                }
                catch (Exception ex)
                {
                    AppLog.Error("تعذر تحويل الصورة لدمجها في PDF: " + imagePath, ex);
                }

                throw new InvalidOperationException(
                    "صيغة الصورة غير مدعومة: " + Path.GetFileName(imagePath), firstError);
            }
        }

        // صورة واحدة = صفحة A4 واحدة (بهامش 10 مم، بنفس نسبة أبعاد الصورة).
        private void AddImagePageForMerge(
            PdfSharpPdfDocument document,
            string imagePath)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException(
                    "ملف الصورة غير موجود.",
                    imagePath);

            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(210);
            page.Height = XUnit.FromMillimeter(297);

            using XGraphics gfx =
                XGraphics.FromPdfPage(page);

            using XImage image = LoadImageForPdf(imagePath);

            const double marginMm = 10.0;
            double boxW = 210.0 - marginMm * 2;
            double boxH = 297.0 - marginMm * 2;

            double aspect =
                image.PixelWidth /
                (double)Math.Max(1, image.PixelHeight);

            double w = boxW;
            double h = w / aspect;

            if (h > boxH)
            {
                h = boxH;
                w = h * aspect;
            }

            gfx.DrawRectangle(
                XBrushes.White,
                0,
                0,
                page.Width.Point,
                page.Height.Point);

            gfx.DrawImage(
                image,
                MmToPoint((210.0 - w) / 2.0),
                MmToPoint((297.0 - h) / 2.0),
                MmToPoint(w),
                MmToPoint(h));
        }

        private void BtnAddCard_Click(
            object? sender,
            EventArgs e)
        {
            if (TryGetSelectedCardPair(
                    out DataGridViewRow frontRow,
                    out DataGridViewRow backRow))
            {
                string frontFile =
                    ((FileEntry)frontRow.Tag!).OriginalPath;

                string backFile =
                    ((FileEntry)backRow.Tag!).OriginalPath;

                AddCardFromImages(
                    frontFile,
                    backFile,
                    new[] { frontRow, backRow },
                    showSuccessMessage: false);

                return;
            }

            string? frontPath = SelectCardSide(
                "اختر صورة وش البطاقة أولاً");

            if (string.IsNullOrWhiteSpace(frontPath))
                return;

            string? backPath = SelectCardSide(
                "اختر صورة ظهر البطاقة الآن");

            if (string.IsNullOrWhiteSpace(backPath))
                return;

            AddCardFromImages(
                frontPath,
                backPath,
                null,
                showSuccessMessage: true);
        }

        private void AddCardFromImages(
            string frontPath,
            string backPath,
            IEnumerable<DataGridViewRow>? rowsToReplace,
            bool showSuccessMessage)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                btnAddCard.Enabled = false;

                string pdfPath = CreateCardDuplexPdf(
                    frontPath,
                    backPath);

                TempFiles.CleanSubfolder("Converted");

                int before = gridFiles.Rows.Count;

                ProcessFile(pdfPath);

                if (gridFiles.Rows.Count <= before)
                {
                    MessageBox.Show(
                        "تم إنشاء ملف البطاقة لكن تعذر إضافته إلى القائمة.",
                        "إضافة البطاقة",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                DataGridViewRow row =
                    gridFiles.Rows[gridFiles.Rows.Count - 1];

                // البطاقة دائماً صفحتي PDF للطباعة على وش وظهر.
                row.Cells["FileType"].Value = "بطاقة وش وظهر";
                row.Cells["DuplexMode"].Value = "وش وظهر";
                row.Cells["Copies"].Value = 1;

                // تحويل: نشيل صورتي الوش والظهر من القائمة بعد ما البطاقة اتضافت بنجاح.
                if (rowsToReplace != null)
                {
                    foreach (DataGridViewRow oldRow in rowsToReplace.ToList())
                    {
                        if (oldRow.DataGridView == null)
                            continue;

                        DisposeRowThumbnail(oldRow);
                        gridFiles.Rows.Remove(oldRow);
                    }
                }

                // إعادة الحساب بعد ضبط خصائص البطاقة.
                RecalculateTotals();

                // تحديث المعاينة اليمنى للصف الجديد.
                gridFiles.ClearSelection();
                row.Selected = true;
                if (gridFiles.Columns.Count > 0)
                    gridFiles.CurrentCell =
                        row.Cells["FileName"];

                if (showSuccessMessage)
                {
                    MessageBox.Show(
                        "تمت إضافة البطاقة بنجاح.\n\n" +
                        "تم إنشاء PDF من صفحتين (وش + ظهر).\n" +
                        "وسيتم طباعتهما على نفس الورقة بنظام وش وظهر.",
                        "إضافة بطاقة",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تجهيز البطاقة للطباعة:\n\n" +
                    ex.Message,
                    "خطأ في إضافة البطاقة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnAddCard.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private string? SelectCardSide(
            string prompt)
        {
            using OpenFileDialog dialog =
                new OpenFileDialog
                {
                    Title = prompt,
                    Multiselect = false,
                    Filter =
                        "صور البطاقة (*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff)|" +
                        "*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff"
                };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return null;

            return dialog.FileName;
        }

        // ============================================================
        // إنشاء PDF بطاقة وش وظهر
        // ============================================================
        private string CreateCardDuplexPdf(
            string frontPath,
            string backPath)
        {
            // ملاحظة: تم إيقاف AutoCropImage على البطاقات نهائياً
            // لأنه كان أحياناً يقص جزء من البطاقة (نص البطاقة يضيع).
            // نستخدم صورة البطاقة الأصلية كما هي بدون أي قص أو تعديل تلقائي.
            string frontProcessed = frontPath;

            string backProcessed = backPath;

            // نضيف هامش أبيض واضح حول البطاقة نفسها،
            // مع الحفاظ على تفاصيل الصورة الأصلية.
            frontProcessed =
                AddWhitePaddingToImage(
                    frontProcessed,
                    45);

            backProcessed =
                AddWhitePaddingToImage(
                    backProcessed,
                    45);

            string directory =
                Path.Combine(
                    Path.GetTempPath(),
                    "LibraryPrintApp",
                    "Cards");

            Directory.CreateDirectory(directory);

            string outputPath =
                Path.Combine(
                    directory,
                    "بطاقة_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                    "_" +
                    Guid.NewGuid().ToString("N") +
                    ".pdf");

            using PdfSharpPdfDocument document =
                new PdfSharpPdfDocument();

            AddCardPdfPage(
                document,
                frontProcessed,
                "وش البطاقة");

            AddCardPdfPage(
                document,
                backProcessed,
                "ظهر البطاقة");

            document.Save(outputPath);

            return outputPath;
        }

        private void AddCardPdfPage(
            PdfSharpPdfDocument document,
            string imagePath,
            string pageLabel)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException(
                    "صورة البطاقة غير موجودة.",
                    imagePath);

            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(210);
            page.Height = XUnit.FromMillimeter(297);

            using XGraphics gfx =
                XGraphics.FromPdfPage(page);

            using XImage image = LoadImageForPdf(imagePath);

            // حجم منطقة البطاقة على الورقة: حوالي 94 × 62 مم.
            // موضوعة في النصف العلوي، بالمنتصف أفقياً.
            const double boxWidthMm = 94.0;
            const double boxHeightMm = 62.0;
            const double topMm = 42.0;

            // نستخدم Aspect Ratio الحقيقي للصورة بدلاً من تمديدها.
            double imageAspect =
                image.PixelWidth /
                (double)Math.Max(1, image.PixelHeight);

            double drawWidthMm = boxWidthMm;
            double drawHeightMm = drawWidthMm / imageAspect;

            if (drawHeightMm > boxHeightMm)
            {
                drawHeightMm = boxHeightMm;
                drawWidthMm = drawHeightMm * imageAspect;
            }

            // في حالة الصور شديدة الاختلاف في الأبعاد، نضمن عدم خروجها.
            drawWidthMm = Math.Min(drawWidthMm, boxWidthMm);
            drawHeightMm = Math.Min(drawHeightMm, boxHeightMm);

            double xMm =
                (210.0 - drawWidthMm) / 2.0;

            double yMm =
                topMm +
                (boxHeightMm - drawHeightMm) / 2.0;

            // خلفية بيضاء صريحة.
            gfx.DrawRectangle(
                XBrushes.White,
                0,
                0,
                page.Width.Point,
                page.Height.Point);

            gfx.DrawImage(
                image,
                MmToPoint(xMm),
                MmToPoint(yMm),
                MmToPoint(drawWidthMm),
                MmToPoint(drawHeightMm));
        }

        private double MmToPoint(double millimeter)
        {
            return millimeter * 72.0 / 25.4;
        }

        // ============================================================
        // إضافة حواف بيضاء للصورة بدون تغيير محتواها.
        // ============================================================
        private string AddWhitePaddingToImage(
            string imagePath,
            int paddingPixels)
        {
            if (!File.Exists(imagePath))
                return imagePath;

            using Image source =
                Image.FromFile(imagePath);

            int width =
                checked(source.Width + (paddingPixels * 2));

            int height =
                checked(source.Height + (paddingPixels * 2));

            Bitmap result =
                new Bitmap(
                    width,
                    height,
                    PixelFormat.Format24bppRgb);

            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.White);
                g.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;
                g.SmoothingMode =
                    SmoothingMode.HighQuality;
                g.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;
                g.CompositingQuality =
                    CompositingQuality.HighQuality;

                g.DrawImage(
                    source,
                    new Rectangle(
                        paddingPixels,
                        paddingPixels,
                        source.Width,
                        source.Height));
            }

            string directory =
                Path.Combine(
                    Path.GetTempPath(),
                    "LibraryPrintApp",
                    "Cards");

            Directory.CreateDirectory(directory);

            string outputPath =
                Path.Combine(
                    directory,
                    Path.GetFileNameWithoutExtension(imagePath) +
                    "_padded_" +
                    Guid.NewGuid().ToString("N") +
                    ".png");

            result.Save(
                outputPath,
                ImageFormat.Png);

            result.Dispose();

            return outputPath;
        }

        // ============================================================
        // اختصارات شاشة الطباعة — قابلة للتغيير من «إعدادات الإدارة ← الاختصارات».
        // الافتراضي: أزرار الأرقام الجانبية (NumLock شغال). أثناء الكتابة داخل خانة نص
        // أو تحرير خلية بنسمح بس بالاختصارات اللي فيها Ctrl/Alt أو مفاتيح F.
        // F1 (قائمة الاختصارات) بيتعامل معاه UiKit.EnableHelpKey.
        // ============================================================
        private void Form1_KeyDown(
            object? sender,
            KeyEventArgs e)
        {
            bool editingText = IsTextInputFocused() || gridFiles.IsCurrentCellInEditMode;

            if (editingText && !Shortcuts.SafeWhileTyping(e.KeyData))
                return;

            // Ctrl+V الخاص بملفات الطباعة يعمل أيضًا لو التركيز على قائمة اختيار غير نصية،
            // لكنه يظل لصق نص عادي داخل حقول الكتابة.
            if (!editingText && Shortcuts.Is("print.gridPaste", e))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                PasteFilesFromClipboard();
                return;
            }

            (string Id, Action Run)[] actions =
            {
                ("print.addFiles", () => BtnAddFiles_Click(this, EventArgs.Empty)),
                ("print.paste", PasteFilesFromClipboard),
                ("print.selectAll", () => gridFiles.SelectAll()),
                ("print.printSelected", () => BtnPrintSelected_Click(this, EventArgs.Empty)),
                ("print.printAll", () => BtnPrintAll_Click(this, EventArgs.Empty)),
                ("print.options", () => BtnPrintSettings_Click(this, EventArgs.Empty)),
                ("print.delete", () => BtnDeleteSelected_Click(this, EventArgs.Empty)),
                ("print.clear", () => BtnClearList_Click(this, EventArgs.Empty)),
                ("print.recalc", RecalculateTotals),
                ("print.recalcAlt", RecalculateTotals),
                ("print.addCard", () => BtnAddCard_Click(this, EventArgs.Empty)),
                ("print.editImage", () => BtnEditImage_Click(this, EventArgs.Empty)),
                ("print.merge", () =>
                {
                    if (gridFiles.SelectedRows.Count < 2)
                    {
                        MessageBox.Show(
                            this,
                            "حدد ملفين أو أكتر الأول عشان تدمجهم في ملف واحد.",
                            "دمج الملفات",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }
                    BtnMergeFiles_Click(this, EventArgs.Empty);
                }),
                ("print.copyService", () => btnCopyService.PerformClick()),
                ("print.reports", () => btnReports.PerformClick()),
                ("print.adminSettings", () => BtnAdminSettings_Click(this, EventArgs.Empty)),
                ("print.cancelPrint", () =>
                {
                    if (btnCancelPrint != null && btnCancelPrint.Visible && btnCancelPrint.Enabled)
                        btnCancelPrint.PerformClick();
                }),
                ("print.minimize", () => WindowState = FormWindowState.Minimized),
                ("print.logout", () => BtnLogout_Click(this, EventArgs.Empty)),
            };

            foreach (var (id, run) in actions)
            {
                if (!Shortcuts.Is(id, e)) continue;

                e.SuppressKeyPress = true;
                e.Handled = true;
                run();
                return;
            }
        }

        private bool IsTextInputFocused()
        {
            // ActiveControl قد يكون Panel/Container بدل العنصر الداخلي؛ ابحث عن أعمق عنصر عليه Focus.
            Control? control = FindDeepestFocusedControl(this);
            if (control is TextBoxBase || control is NumericUpDown)
                return true;

            // ComboBox من نوع DropDownList (مثل خانة الفئة) لا يستقبل نصًا،
            // فلا ينبغي أن يعطّل اختصارات NumPad بمجرد أن يكون محددًا.
            if (control is ComboBox combo)
                return combo.DropDownStyle != ComboBoxStyle.DropDownList || combo.DroppedDown;

            return false;
        }

        private static Control? FindDeepestFocusedControl(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (!child.ContainsFocus)
                    continue;

                return FindDeepestFocusedControl(child) ?? child;
            }

            return root.ContainsFocus ? root : null;
        }

        private void GridFiles_KeyDown(
            object? sender,
            KeyEventArgs e)
        {
            if (Shortcuts.Is("print.gridPaste", e))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                PasteFilesFromClipboard();
                return;
            }

            if ((Shortcuts.Is("global.gridDelete", e) || Shortcuts.Is("global.gridDelete2", e)) &&
                gridFiles.CurrentCell != null &&
                !gridFiles.IsCurrentCellInEditMode)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                BtnDeleteSelected_Click(this, EventArgs.Empty);
            }
        }

        private void PasteFilesFromClipboard()
        {
            try
            {
                int added = 0;

                if (Clipboard.ContainsFileDropList())
                {
                    StringCollection fileList =
                        Clipboard.GetFileDropList();

                    foreach (string? filePath in fileList)
                    {
                        if (string.IsNullOrWhiteSpace(filePath))
                            continue;
                        int before = gridFiles.Rows.Count;
                        ProcessFile(filePath);

                        if (gridFiles.Rows.Count > before)
                            added++;
                    }
                }
                else if (Clipboard.ContainsImage())
                {
                    Image? clipboardImage =
                        Clipboard.GetImage();

                    if (clipboardImage != null)
                    {
                        string directory =
                            Path.Combine(
                                Path.GetTempPath(),
                                "LibraryPrintApp",
                                "PastedImages");

                        Directory.CreateDirectory(directory);

                        string path =
                            Path.Combine(
                                directory,
                                "Pasted_" +
                                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                                "_" +
                                Guid.NewGuid().ToString("N") +
                                ".png");

                        using Bitmap copy =
                            new Bitmap(clipboardImage);

                        copy.Save(
                            path,
                            ImageFormat.Png);

                        int before = gridFiles.Rows.Count;
                        ProcessFile(path);

                        if (gridFiles.Rows.Count > before)
                            added++;
                    }
                }

                RecalculateTotals();

                if (added == 0 &&
                    !Clipboard.ContainsFileDropList() &&
                    !Clipboard.ContainsImage())
                {
                    MessageBox.Show(
                        "الحافظة لا تحتوي على ملفات أو صورة يمكن إضافتها.",
                        "لصق الملفات",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر إضافة المحتوى من الحافظة:\n\n" +
                    ex.Message,
                    "خطأ في اللصق",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // إضافة ملف
        // ============================================================
        private void ProcessFile(
            string filePath)
        {
            if (!File.Exists(filePath) ||
                IsFileAlreadyAdded(filePath))
            {
                return;
            }

            string ext =
                Path.GetExtension(filePath)
                    .ToLowerInvariant();

            string fileName =
                Path.GetFileName(filePath);

            int pages = 1;
            string fileType = "صورة";
            string fileToPrint = filePath;
            Image? thumbnail = null;

            if (ext == ".png" ||
                ext == ".jpg" ||
                ext == ".jpeg" ||
                ext == ".bmp" ||
                ext == ".tif" ||
                ext == ".tiff")
            {
                try
                {
                    Cursor = Cursors.WaitCursor;

                    // ملاحظة: تم إيقاف القص التلقائي (AutoCropImage) نهائياً
                    // لأنه غير مطلوب. تُستخدم الصورة الأصلية كما هي.
                    fileToPrint = filePath;

                    thumbnail =
                        CreateImageThumbnail(filePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Image processing error: " + ex);

                    thumbnail =
                        CreatePlaceholderThumbnail();
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
            else if (ext == ".doc" ||
                     ext == ".docx")
            {
                fileType = "مستند Word";

                try
                {
                    Cursor = Cursors.WaitCursor;

                    string convertedPdfPath =
                        ConvertWordToPdf(filePath);

                    fileToPrint = convertedPdfPath;

                    using PdfiumViewerPdfDocument pdf =
                        PdfiumViewerPdfDocument.Load(
                            convertedPdfPath);

                    if (pdf.PageCount <= 0)
                    {
                        MessageBox.Show(
                            $"ملف الوورد لا يحتوي على صفحات بعد التحويل:\n\n{fileName}",
                            "ملف وورد غير صالح",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);

                        return;
                    }

                    pages = pdf.PageCount;

                    thumbnail =
                        CreatePdfFirstPageThumbnail(pdf);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"تعذر تحويل ملف الوورد إلى PDF للطباعة:\n\n" +
                        $"{fileName}\n\n" +
                        $"الخطأ:\n{ex.Message}\n\n" +
                        "تأكد من أن برنامج Microsoft Word مثبت على هذا الجهاز.",
                        "خطأ في ملف Word",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
            else if (ext == ".ppt" ||
                     ext == ".pptx" ||
                     ext == ".pps" ||
                     ext == ".ppsx")
            {
                fileType = "عرض PowerPoint";

                try
                {
                    Cursor = Cursors.WaitCursor;

                    string convertedPdfPath =
                        ConvertPowerPointToPdf(filePath);

                    fileToPrint = convertedPdfPath;

                    using PdfiumViewerPdfDocument pdf =
                        PdfiumViewerPdfDocument.Load(
                            convertedPdfPath);

                    if (pdf.PageCount <= 0)
                    {
                        MessageBox.Show(
                            $"ملف PowerPoint لا يحتوي على شرائح بعد التحويل:\n\n{fileName}",
                            "ملف PowerPoint غير صالح",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);

                        return;
                    }

                    pages = pdf.PageCount;
                    thumbnail = CreatePdfFirstPageThumbnail(pdf);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"تعذر تحويل ملف PowerPoint إلى PDF للطباعة:\n\n" +
                        $"{fileName}\n\n" +
                        $"الخطأ:\n{ex.Message}\n\n" +
                        "تأكد من أن برنامج Microsoft PowerPoint مثبت على هذا الجهاز.",
                        "خطأ في ملف PowerPoint",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
            else if (ext == ".pdf")
            {
                fileType = "مستند PDF";

                try
                {
                    using PdfiumViewerPdfDocument pdf =
                        PdfiumViewerPdfDocument.Load(filePath);

                    if (pdf.PageCount <= 0)
                    {
                        MessageBox.Show(
                            $"ملف PDF لا يحتوي على صفحات:\n\n{fileName}",
                            "ملف PDF غير صالح",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);

                        return;
                    }

                    pages = pdf.PageCount;

                    thumbnail =
                        CreatePdfFirstPageThumbnail(pdf);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"تعذر فتح ملف PDF:\n\n" +
                        $"{fileName}\n\n" +
                        $"الخطأ:\n{ex.Message}",
                        "خطأ في PDF",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }
            }
            else
            {
                return;
            }

            string defaultColor =
                cmbGlobalColor.SelectedItem?.ToString()
                ?? "أسود";

            string defaultDuplex =
                cmbGlobalDuplex.SelectedItem?.ToString()
                ?? "وش واحد";

            int rowIndex =
                gridFiles.Rows.Add(
                    thumbnail,
                    fileName,
                    fileType,
                    pages,
                    (int)numGlobalCopies.Value,
                    defaultColor,
                    defaultDuplex,
                    pages,
                    "0.00 ج.م");

            DataGridViewRow row =
                gridFiles.Rows[rowIndex];

            // استخدم آخر إعدادات شكل الطباعة المحفوظة (هوامش/ورق/اتجاه/تحجيم)،
            // لكن التزم بطلب التشغيل: كل ملف جديد يبدأ دائمًا بصفحة واحدة في الورقة.
            PrintOptions defaultOptions = AppOptions.DefaultPrintOptions;
            defaultOptions.PageRangeMode = PrintPageRangeMode.AllPages;
            defaultOptions.FromPage = 1;
            defaultOptions.ToPage = pages;
            defaultOptions.PageRangesText = string.Empty;
            defaultOptions.PagesPerSheet = 1;
            defaultOptions.ClampToPageCount(pages);

            row.Tag =
                new FileEntry(
                    filePath,
                    fileToPrint,
                    defaultOptions);

            row.Cells["FileName"].ToolTipText =
                filePath;

            row.Cells["Pages"].ToolTipText =
                $"إجمالي صفحات الملف: {pages}\nالنطاق الحالي: كل الصفحات\nصفحات في الورقة: {defaultOptions.PagesPerSheet}";

            RecalculateTotals();
        }

        private bool IsFileAlreadyAdded(
            string filePath)
        {
            foreach (DataGridViewRow row in gridFiles.Rows)
            {
                if (row.IsNewRow)
                    continue;

                string? originalPath =
                    (row.Tag as FileEntry)?.OriginalPath;

                if (string.IsNullOrWhiteSpace(originalPath))
                    continue;

                try
                {
                    if (string.Equals(
                            Path.GetFullPath(filePath),
                            Path.GetFullPath(originalPath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    if (string.Equals(
                            filePath,
                            originalPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ============================================================
        // صورة مصغرة
        // ============================================================
        private Image CreateImageThumbnail(
            string imagePath)
        {
            using Image source =
                Image.FromFile(imagePath);

            return CreateFitThumbnail(
                source,
                86,
                68);
        }

        // ============================================================
        // أول صفحة من PDF كصورة مصغرة
        // ============================================================
        private Image CreatePdfFirstPageThumbnail(
            PdfiumViewerPdfDocument pdf)
        {
            SizeF pageSize = pdf.PageSizes[0];
            double aspect = pageSize.Width / Math.Max(1f, pageSize.Height);
            const int maxWidth = 240;
            const int maxHeight = 300;

            int width = maxWidth;
            int height = Math.Max(1, (int)Math.Round(width / Math.Max(0.1, aspect)));

            if (height > maxHeight)
            {
                height = maxHeight;
                width = Math.Max(1, (int)Math.Round(height * aspect));
            }

            using Image firstPage =
                pdf.Render(
                    0,
                    width,
                    height,
                    96f,
                    96f,
                    true);

            return CreateFitThumbnail(
                firstPage,
                86,
                68);
        }

        private Image CreateFitThumbnail(
            Image source,
            int maxWidth,
            int maxHeight)
        {
            Bitmap result =
                new Bitmap(
                    maxWidth,
                    maxHeight,
                    PixelFormat.Format24bppRgb);

            using Graphics g =
                Graphics.FromImage(result);

            g.Clear(Color.White);
            g.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            g.SmoothingMode =
                SmoothingMode.HighQuality;
            g.PixelOffsetMode =
                PixelOffsetMode.HighQuality;

            double scale =
                Math.Min(
                    (double)maxWidth / source.Width,
                    (double)maxHeight / source.Height);

            int width =
                Math.Max(
                    1,
                    (int)Math.Round(
                        source.Width * scale));

            int height =
                Math.Max(
                    1,
                    (int)Math.Round(
                        source.Height * scale));

            int x =
                (maxWidth - width) / 2;

            int y =
                (maxHeight - height) / 2;

            g.DrawImage(
                source,
                new Rectangle(
                    x,
                    y,
                    width,
                    height));

            return result;
        }

        private Image CreatePlaceholderThumbnail()
        {
            Bitmap bitmap =
                new Bitmap(
                    86,
                    68,
                    PixelFormat.Format24bppRgb);

            using Graphics g =
                Graphics.FromImage(bitmap);

            g.Clear(Color.White);

            using Font font =
                new Font(
                    "Segoe UI",
                    9,
                    FontStyle.Bold);

            using Brush brush =
                new SolidBrush(
                    Color.DimGray);

            using StringFormat format =
                new StringFormat
                {
                    Alignment =
                        StringAlignment.Center,
                    LineAlignment =
                        StringAlignment.Center
                };

            g.DrawString(
                "FILE",
                font,
                brush,
                new RectangleF(
                    0,
                    0,
                    bitmap.Width,
                    bitmap.Height),
                format);

            return bitmap;
        }

        // ============================================================
        // المعاينة الثابتة في الجانب الأيمن
        // يتم تحديثها عند اختيار صف فقط، وليس عند تحريك الماوس.
        // ============================================================
        private void GridFiles_SelectionChanged(
            object? sender,
            EventArgs e)
        {
            TrackSelectionOrder();

            previewSheetIndex = 0;
            UpdateRightPreview();
        }

        private void GridFiles_CellClick(
            object? sender,
            DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= gridFiles.Rows.Count)
                return;

            gridFiles.Rows[e.RowIndex].Selected = true;
            previewSheetIndex = 0;
            UpdateRightPreview();
        }

        private void GridFiles_CellDoubleClick(
            object? sender,
            DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= gridFiles.Rows.Count)
                return;

            gridFiles.Rows[e.RowIndex].Selected = true;
            OpenPrintOptionsForSelectedRows();
        }

        // ============================================================
        // المعاينة الجانبية — سريعة وغير حاجزة للواجهة:
        //  1) التحميل بيتم في خيط خلفي (مفيش تجميد حتى على جهاز ضعيف).
        //  2) تأخير بسيط (Debounce) فلو المستخدم بيتنقل بين الصفوف بسرعة
        //     بيتحمل آخر صف بس.
        //  3) تخزين مؤقت (Cache) لآخر 10 معاينات: الرجوع لنفس الملف/الورقة فوري.
        //  4) مقاسات الورق وهوامش الطابعة (اللي بتسأل عنها درايفر الطابعة وبتاخد
        //     ثواني) بتتحسب مرة واحدة وتتخزن.
        // ============================================================
        private sealed class PreviewRequest
        {
            public string Key { get; init; } = "";
            public string FileName { get; init; } = "";
            public string FilePath { get; init; } = "";
            public string ColorType { get; init; } = "";
            public string DuplexMode { get; init; } = "";
            public string PrinterName { get; init; } = "";
            public PrintOptions Options { get; init; } = new PrintOptions();
            public int SheetIndex { get; init; }
            public string FileStamp { get; init; } = "";

            public PreviewRequest ForSheet(int sheetIndex, string key) =>
                new PreviewRequest
                {
                    Key = key,
                    FileName = FileName,
                    FilePath = FilePath,
                    ColorType = ColorType,
                    DuplexMode = DuplexMode,
                    PrinterName = PrinterName,
                    Options = Options,
                    FileStamp = FileStamp,
                    SheetIndex = sheetIndex
                };
        }

        private sealed class PreviewRenderResult
        {
            public Image? Image { get; init; }
            public int SheetIndex { get; init; }
            public int SheetCount { get; init; }
            public bool Skipped { get; init; }
        }

        private sealed class PreviewCacheEntry
        {
            public Image Image { get; init; } = null!;
            public int SheetIndex { get; init; }
            public int SheetCount { get; init; }
        }

        private readonly struct PreviewPageMetrics
        {
            public float PaperWidth { get; init; }
            public float PaperHeight { get; init; }
            public float HardMarginX { get; init; }
            public float HardMarginY { get; init; }
            public DateTime CreatedAt { get; init; }
        }

        // كل صورة ~2MB؛ 10 أوراق = حوالي 20MB بالكتير. الكاش الصغير مع التحميل المسبق
        // (الورقة التالية والسابقة) كفاية للتنقل السريع وبيخفف الذاكرة.
        private const int PreviewCacheCapacity = 10;
        private const int PreviewDefaultDelayMs = 150;
        private int previewNextDelay = PreviewDefaultDelayMs;
        private PreviewRequest? lastShownPreviewRequest;

        // نسخة PDF مفتوحة ومتخزنة (بتتفتح مرة واحدة للملف بدل كل ضغطة تنقل).
        // بتتقرأ في الذاكرة فمبتقفلش الملف على الطباعة/الحذف. تُستخدم فقط داخل previewRenderLock.
        private PdfiumViewerPdfDocument? previewPdfDoc;
        private MemoryStream? previewPdfStream;
        private string? previewPdfStamp;

        private static readonly object PdfiumRenderLock = new object();

        private readonly object previewRenderLock = new object();
        private readonly Dictionary<string, PreviewCacheEntry> previewCache = new();
        private readonly LinkedList<string> previewCacheOrder = new();
        private readonly Dictionary<string, PreviewPageMetrics> previewMetricsCache = new();
        private System.Windows.Forms.Timer? previewDebounceTimer;
        private PreviewRequest? pendingPreviewRequest;
        private int previewRequestId;
        private string? lastPreviewKey;

        private void UpdateRightPreview()
        {
            if (previewPanelPicture == null || previewPanelTitle == null)
                return;

            // التأخير (Debounce) مهم بس عند التنقل بين صفوف الجدول؛ التنقل بين الأوراق فوري.
            int delay = previewNextDelay;
            previewNextDelay = PreviewDefaultDelayMs;

            DataGridViewRow? row =
                gridFiles.SelectedRows.Count > 0
                    ? gridFiles.SelectedRows[0]
                    : gridFiles.CurrentRow;

            if (row == null || row.IsNewRow || row.Tag is not FileEntry entry)
            {
                CancelPendingPreview();
                lastPreviewKey = null;
                previewSheetIndex = 0;
                previewSheetCount = 0;
                SetRightPreviewImage(null);
                previewPanelTitle.Text =
                    "اختر ملفًا من القائمة لعرض نتيجة الطباعة هنا";
                UpdatePreviewNavigation();
                return;
            }

            string printerName = cmbPrinters.SelectedItem?.ToString()
                ?? currentPrinterSettings.PrinterName;

            if (string.IsNullOrWhiteSpace(printerName))
            {
                CancelPendingPreview();
                lastPreviewKey = null;
                SetRightPreviewImage(null);
                previewPanelTitle.Text = "لم يتم اختيار طابعة";
                previewSheetCount = 0;
                UpdatePreviewNavigation();
                return;
            }

            int pages = GetPositiveInt(row.Cells["Pages"].Value, 1);
            PrintOptions options = entry.Options.Clone();
            options.ClampToPageCount(pages);

            string colorType = row.Cells["ColorType"].Value?.ToString() ?? "أسود";
            string duplexMode = row.Cells["DuplexMode"].Value?.ToString() ?? "وش واحد";
            string fileName = row.Cells["FileName"].Value?.ToString() ?? "الملف";

            string fileStamp = "";
            try
            {
                FileInfo info = new FileInfo(entry.PrintPath);
                if (info.Exists)
                    fileStamp = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            }
            catch
            {
            }

            // اللون مش بيأثر على شكل المعاينة (مجرد Queue مختلفة)، فمش في المفتاح.
            string key = MakePreviewKey(
                entry.PrintPath,
                fileStamp,
                printerName,
                duplexMode,
                previewSheetIndex,
                options);

            // نفس الطلب (مثلًا SelectionChanged + CellClick بيتنادوا مع بعض).
            if (key == lastPreviewKey)
                return;

            lastPreviewKey = key;

            var request = new PreviewRequest
            {
                Key = key,
                FileName = fileName,
                FilePath = entry.PrintPath,
                ColorType = colorType,
                DuplexMode = duplexMode,
                PrinterName = printerName,
                Options = options,
                FileStamp = fileStamp,
                SheetIndex = previewSheetIndex
            };

            if (previewCache.TryGetValue(key, out PreviewCacheEntry? cached))
            {
                CancelPendingPreview();
                TouchPreviewCache(key);
                ShowPreviewResult(
                    request,
                    new Bitmap(cached.Image),
                    cached.SheetIndex,
                    cached.SheetCount);
                return;
            }

            // مش في الكاش: نحمّلها في الخلفية بعد تأخير بسيط.
            previewPanelTitle.Text = $"{fileName}\nجاري تحميل المعاينة…";

            // السلايدر ومربع الرقم يتحركوا فورًا حتى لو الصورة لسه بتتحمل
            // (الصورة القديمة بتفضل ظاهرة بدل ما الشاشة تفضى).
            previewNavigator?.SetState(previewSheetIndex, previewSheetCount);

            pendingPreviewRequest = request;
            Interlocked.Increment(ref previewRequestId);

            if (previewDebounceTimer == null)
            {
                previewDebounceTimer = new System.Windows.Forms.Timer { Interval = PreviewDefaultDelayMs };
                previewDebounceTimer.Tick += PreviewDebounceTimer_Tick;
            }

            previewDebounceTimer.Stop();

            if (delay <= 0)
            {
                // تنقل بين أوراق نفس الملف: نرسم فورًا من غير انتظار.
                PreviewDebounceTimer_Tick(null, EventArgs.Empty);
                return;
            }

            previewDebounceTimer.Interval = delay;
            previewDebounceTimer.Start();
        }

        private static string MakePreviewKey(
            string filePath,
            string fileStamp,
            string printerName,
            string duplexMode,
            int sheetIndex,
            PrintOptions options)
        {
            return string.Join(
                "|",
                filePath,
                fileStamp,
                printerName,
                duplexMode,
                sheetIndex,
                options.CacheSignature());
        }

        private void PreviewNavigator_IndexRequested(int index, bool dragging)
        {
            if (previewSheetCount <= 0)
                return;

            index = Math.Clamp(index, 0, previewSheetCount - 1);
            if (index == previewSheetIndex && dragging)
                return;

            previewSheetIndex = index;
            // أثناء سحب السلايدر نستنى 35ms بس (بيجمّع الحركات السريعة)، غير كده فوري.
            previewNextDelay = dragging ? 35 : 0;
            UpdateRightPreview();
        }

        /// <summary> دبل كليك على المعاينة الجانبية → شاشة المعاينة الكبيرة. </summary>
        private void OpenPreviewViewer()
        {
            PreviewRequest? basis = lastShownPreviewRequest;
            if (basis == null || previewSheetCount <= 0 || currentPreviewImage == null)
                return;

            Image placeholder = new Bitmap(currentPreviewImage);

            using var viewer = new PreviewViewerForm(
                basis.FileName,
                previewSheetIndex,
                previewSheetCount,
                placeholder,
                (idx, longSide, shortSide, isStale) =>
                    RenderForViewerAsync(basis, idx, longSide, shortSide, isStale));

            viewer.ShowDialog(this);

            // نرجّع المعاينة الجانبية لنفس الورقة اللي المستخدم وقف عندها.
            int back = viewer.CurrentIndex;
            if (back != previewSheetIndex)
            {
                previewSheetIndex = back;
                previewNextDelay = 0;
                UpdateRightPreview();
            }
        }

        private Task<PreviewViewerForm.Rendered?> RenderForViewerAsync(
            PreviewRequest basis,
            int sheetIndex,
            int longSide,
            int shortSide,
            Func<bool> isStale)
        {
            return RunOnStaThread<PreviewViewerForm.Rendered?>(() =>
            {
                lock (previewRenderLock)
                {
                    if (isStale())
                        return null;

                    int s = sheetIndex;
                    Image? image = CreateFinalPrintPreview(
                        basis.FilePath,
                        basis.ColorType,
                        basis.DuplexMode,
                        basis.PrinterName,
                        basis.Options,
                        ref s,
                        out int count,
                        longSide,
                        shortSide);

                    return new PreviewViewerForm.Rendered(image, s, count);
                }
            });
        }

        /// <summary>
        /// تحميل مسبق للأوراق المجاورة للورقة الظاهرة (التالية ثم السابقة ثم اللي بعدها)،
        /// فلما المستخدم يضغط «التالي» تكون جاهزة في الكاش. بيتوقف لو المستخدم عمل طلب جديد.
        /// </summary>
        private void SchedulePreviewPrefetch(PreviewRequest basis, int center, int sheetCount)
        {
            if (sheetCount <= 1 || IsDisposed || Disposing)
                return;

            int id = Volatile.Read(ref previewRequestId);
            _ = PrefetchPreviewSheetsAsync(basis, center, sheetCount, id);
        }

        private async Task PrefetchPreviewSheetsAsync(
            PreviewRequest basis,
            int center,
            int sheetCount,
            int id)
        {
            foreach (int offset in new[] { 1, -1, 2 })
            {
                int idx = center + offset;
                if (idx < 0 || idx >= sheetCount)
                    continue;

                if (IsDisposed || Disposing || id != Volatile.Read(ref previewRequestId))
                    return;

                string key = MakePreviewKey(
                    basis.FilePath,
                    basis.FileStamp,
                    basis.PrinterName,
                    basis.DuplexMode,
                    idx,
                    basis.Options);

                if (previewCache.ContainsKey(key))
                    continue;

                PreviewRequest req = basis.ForSheet(idx, key);
                PreviewRenderResult? result;

                try
                {
                    result = await RunOnStaThread(() => RenderPreviewWorker(req, id));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Preview prefetch error: " + ex);
                    return;
                }

                if (result == null || result.Skipped)
                    return;

                if (IsDisposed || Disposing)
                {
                    result.Image?.Dispose();
                    return;
                }

                if (result.Image == null)
                    return;

                AddToPreviewCache(key, result);
            }
        }

        private void CancelPendingPreview()
        {
            previewDebounceTimer?.Stop();
            pendingPreviewRequest = null;
            Interlocked.Increment(ref previewRequestId);
        }

        private void PreviewDebounceTimer_Tick(object? sender, EventArgs e)
        {
            previewDebounceTimer?.Stop();

            PreviewRequest? request = pendingPreviewRequest;
            pendingPreviewRequest = null;
            if (request == null)
                return;

            int id = Volatile.Read(ref previewRequestId);
            _ = RenderPreviewAsync(request, id);
        }

        private async Task RenderPreviewAsync(PreviewRequest request, int id)
        {
            PreviewRenderResult? result = null;
            Exception? error = null;

            try
            {
                result = await RunOnStaThread(() => RenderPreviewWorker(request, id));
            }
            catch (Exception ex)
            {
                error = ex;
            }

            try
            {
                if (IsDisposed || Disposing)
                {
                    result?.Image?.Dispose();
                    return;
                }

                bool stillCurrent = id == Volatile.Read(ref previewRequestId);

                if (error != null)
                {
                    Debug.WriteLine("Final print preview error: " + error);
                    if (stillCurrent)
                    {
                        lastPreviewKey = null;
                        SetRightPreviewImage(null);
                        previewSheetCount = 0;
                        UpdatePreviewNavigation();
                        if (previewPanelTitle != null)
                            previewPanelTitle.Text =
                                "تعذر عرض المعاينة النهائية:\n" + error.Message;
                    }
                    return;
                }

                if (result == null || result.Skipped)
                    return;

                if (result.Image == null)
                {
                    if (stillCurrent)
                    {
                        lastPreviewKey = null;
                        SetRightPreviewImage(null);
                        previewSheetCount = 0;
                        if (previewPanelTitle != null)
                            previewPanelTitle.Text = "تعذر إنشاء معاينة لنتيجة الطباعة";
                        UpdatePreviewNavigation();
                    }
                    return;
                }

                // نحفظها في الكاش حتى لو المستخدم انتقل لصف تاني (الشغل اتعمل خلاص).
                AddToPreviewCache(request.Key, result);

                if (stillCurrent)
                {
                    ShowPreviewResult(
                        request,
                        new Bitmap(result.Image),
                        result.SheetIndex,
                        result.SheetCount);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Preview display error: " + ex);
            }
        }

        private PreviewRenderResult RenderPreviewWorker(PreviewRequest request, int id)
        {
            lock (previewRenderLock)
            {
                // اتبدّل بطلب أحدث وإحنا مستنيين الدور؟ نتخطاه من غير ما نرسم حاجة.
                if (id != Volatile.Read(ref previewRequestId))
                    return new PreviewRenderResult { Skipped = true };

                int sheetIndex = request.SheetIndex;

                Image? image = CreateFinalPrintPreview(
                    request.FilePath,
                    request.ColorType,
                    request.DuplexMode,
                    request.PrinterName,
                    request.Options,
                    ref sheetIndex,
                    out int sheetCount);

                return new PreviewRenderResult
                {
                    Image = image,
                    SheetIndex = sheetIndex,
                    SheetCount = sheetCount
                };
            }
        }

        private void ShowPreviewResult(
            PreviewRequest request,
            Image image,
            int sheetIndex,
            int sheetCount)
        {
            if (previewPanelTitle == null)
            {
                image.Dispose();
                return;
            }

            previewSheetIndex = sheetIndex;
            previewSheetCount = sheetCount;

            SetRightPreviewImage(image);

            previewPanelTitle.Text =
                $"{request.FileName}\nمعاينة نتيجة الطباعة — ورقة {sheetIndex + 1} من {Math.Max(1, sheetCount)}";

            lastShownPreviewRequest = request;
            UpdatePreviewNavigation();
            SchedulePreviewPrefetch(request, sheetIndex, sheetCount);
        }

        private void AddToPreviewCache(string key, PreviewRenderResult result)
        {
            if (result.Image == null)
                return;

            if (previewCache.TryGetValue(key, out PreviewCacheEntry? existing))
            {
                // نستبدل القديم (ونتخلص منه).
                previewCache.Remove(key);
                previewCacheOrder.Remove(key);
                existing.Image.Dispose();
            }

            previewCache[key] = new PreviewCacheEntry
            {
                Image = result.Image,
                SheetIndex = result.SheetIndex,
                SheetCount = result.SheetCount
            };
            previewCacheOrder.AddFirst(key);

            while (previewCacheOrder.Count > PreviewCacheCapacity)
            {
                string oldest = previewCacheOrder.Last!.Value;
                previewCacheOrder.RemoveLast();

                if (previewCache.TryGetValue(oldest, out PreviewCacheEntry? old))
                {
                    previewCache.Remove(oldest);
                    old.Image.Dispose();
                }
            }
        }

        private void TouchPreviewCache(string key)
        {
            if (previewCacheOrder.Remove(key))
                previewCacheOrder.AddFirst(key);
        }

        private void ClearPreviewCache()
        {
            foreach (PreviewCacheEntry entry in previewCache.Values)
            {
                try { entry.Image.Dispose(); } catch { }
            }

            previewCache.Clear();
            previewCacheOrder.Clear();
        }

        /// <summary>
        /// مقاسات الورقة وهوامش الطابعة الصلبة. أخذها من درايفر الطابعة (UTAX KX) بطيء
        /// جدًا على الأجهزة الضعيفة (ثواني)، فبنحسبها مرة وبنكرر استخدامها.
        /// بتتنادى من داخل previewRenderLock فقط.
        /// </summary>
        private PreviewPageMetrics GetPreviewPageMetrics(
            string colorType,
            string duplexMode,
            string printerName,
            PrintOptions effectiveOptions)
        {
            string key = string.Join(
                "|",
                printerName,
                effectiveOptions.PaperName,
                (int)effectiveOptions.Orientation,
                duplexMode);

            if (previewMetricsCache.TryGetValue(key, out PreviewPageMetrics cached) &&
                DateTime.UtcNow - cached.CreatedAt < TimeSpan.FromMinutes(10))
            {
                return cached;
            }

            using PrintDocument settingsDocument = new PrintDocument();
            ApplyPrintSettings(
                settingsDocument,
                colorType,
                duplexMode,
                printerName,
                effectiveOptions);

            PageSettings pageSettings = settingsDocument.DefaultPageSettings;

            var metrics = new PreviewPageMetrics
            {
                PaperWidth = Math.Max(1f, pageSettings.PaperSize.Width),
                PaperHeight = Math.Max(1f, pageSettings.PaperSize.Height),
                HardMarginX = pageSettings.HardMarginX,
                HardMarginY = pageSettings.HardMarginY,
                CreatedAt = DateTime.UtcNow
            };

            previewMetricsCache[key] = metrics;
            return metrics;
        }

        private void UpdatePreviewNavigation()
        {
            previewNavigator?.SetState(previewSheetIndex, previewSheetCount);
        }

        private void SetRightPreviewImage(Image? image)
        {
            if (previewPanelPicture == null)
            {
                image?.Dispose();
                return;
            }

            Image? old = currentPreviewImage;
            currentPreviewImage = image;
            previewPanelPicture.Image = image;
            old?.Dispose();
        }

        private Image? CreateFinalPrintPreview(
            string filePath,
            string colorType,
            string duplexMode,
            string printerName,
            PrintOptions options,
            ref int requestedSheetIndex,
            out int sheetCount,
            int canvasLongSide = 1000,
            int canvasShortSide = 710)
        {
            sheetCount = 0;

            if (!File.Exists(filePath))
                return null;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            PdfiumViewerPdfDocument? pdf = null;

            // ملاحظة: الـPDF بيتفتح مرة واحدة ويتخزن (AcquirePreviewPdf) فمبنقفلوش هنا.
            // الدالة دي بتتنادى دايمًا من داخل previewRenderLock.
            {
                int pageCount;
                List<int> pageIndexes;
                SizeF? firstPdfPageSize = null;

                if (ext == ".pdf")
                {
                    pdf = AcquirePreviewPdf(filePath);
                    if (pdf.PageCount <= 0)
                        return null;

                    pageCount = pdf.PageCount;
                    pageIndexes = BuildPageIndexList(options, pageCount);
                    if (pageIndexes.Count == 0)
                        return null;

                    firstPdfPageSize = pdf.PageSizes[pageIndexes[0]];
                }
                else if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff")
                {
                    pageCount = 1;
                    pageIndexes = new List<int> { 0 };
                }
                else
                {
                    return null;
                }

                PrintOptions effectiveOptions = options.Clone();
                effectiveOptions.ClampToPageCount(pageCount);

                // Determine automatic orientation from the first printable page.
                if (effectiveOptions.Orientation == PrintPageOrientation.Auto)
                {
                    if (firstPdfPageSize.HasValue)
                    {
                        SizeF size = firstPdfPageSize.Value;
                        effectiveOptions.Orientation = size.Width > size.Height
                            ? PrintPageOrientation.Landscape
                            : PrintPageOrientation.Portrait;
                    }
                    else
                    {
                        using Image probe = Image.FromFile(filePath);
                        effectiveOptions.Orientation = probe.Width > probe.Height
                            ? PrintPageOrientation.Landscape
                            : PrintPageOrientation.Portrait;
                    }
                }

                // The preview is generated manually from the exact page range and N-up layout.
                // This avoids PreviewPrintController/driver differences and guarantees that
                // the preview always contains all pages that will be placed on the sheet.
                // مقاسات الورق/الهوامش من درايفر الطابعة بطيئة، فبتتخزن (انظر GetPreviewPageMetrics).
                PreviewPageMetrics pageMetrics = GetPreviewPageMetrics(
                    colorType,
                    duplexMode,
                    printerName,
                    effectiveOptions);

                float paperWidthHundredths = pageMetrics.PaperWidth;
                float paperHeightHundredths = pageMetrics.PaperHeight;

                // PaperSize stores the paper's native orientation. The preview must
                // explicitly swap it when Landscape is selected so the canvas and slots
                // have exactly the same orientation as the real print job.
                if (effectiveOptions.Orientation == PrintPageOrientation.Landscape &&
                    paperWidthHundredths < paperHeightHundredths)
                {
                    (paperWidthHundredths, paperHeightHundredths) =
                        (paperHeightHundredths, paperWidthHundredths);
                }
                else if (effectiveOptions.Orientation == PrintPageOrientation.Portrait &&
                         paperWidthHundredths > paperHeightHundredths)
                {
                    (paperWidthHundredths, paperHeightHundredths) =
                        (paperHeightHundredths, paperWidthHundredths);
                }

                RectangleF printableHundredths = GetPreviewPrintableArea(pageMetrics.HardMarginX, pageMetrics.HardMarginY, paperWidthHundredths, paperHeightHundredths, effectiveOptions);

                // اللوحة الجانبية عرضها 380 بيكسل بالكتير، فمفيش داعي لصورة 1500×1050
                // (كانت بتبطّأ الجهاز الضعيف). 1000×710 كفاية وواضحة.
                // الشاشة الكبيرة بتطلب مقاس أكبر عن طريق canvasLongSide/canvasShortSide.
                int PreviewLongSide = Math.Max(400, canvasLongSide);
                int PreviewShortSide = Math.Max(280, canvasShortSide);
                float sizeFactor = PreviewLongSide / 1000f;
                bool landscape = paperWidthHundredths >= paperHeightHundredths;
                int canvasWidth = landscape ? PreviewLongSide : PreviewShortSide;
                int canvasHeight = landscape ? PreviewShortSide : PreviewLongSide;

                Bitmap sheet = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format24bppRgb);
                sheet.SetResolution(96f, 96f);

                using (Graphics g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.FromArgb(235, 237, 240));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;

                    float scale = Math.Min(
                        (canvasWidth - 40f * sizeFactor) / paperWidthHundredths,
                        (canvasHeight - 40f * sizeFactor) / paperHeightHundredths);

                    int paperWidthPx = Math.Max(1, (int)Math.Round(paperWidthHundredths * scale));
                    int paperHeightPx = Math.Max(1, (int)Math.Round(paperHeightHundredths * scale));
                    int paperX = (canvasWidth - paperWidthPx) / 2;
                    int paperY = (canvasHeight - paperHeightPx) / 2;
                    Rectangle paperRect = new Rectangle(paperX, paperY, paperWidthPx, paperHeightPx);

                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
                    {
                        g.FillRectangle(shadow, new Rectangle(paperRect.X + (int)(8 * sizeFactor), paperRect.Y + (int)(8 * sizeFactor), paperRect.Width, paperRect.Height));
                    }

                    using (SolidBrush paperBrush = new SolidBrush(Color.White))
                        g.FillRectangle(paperBrush, paperRect);

                    Rectangle printablePx = Rectangle.Round(new RectangleF(
                        paperX + printableHundredths.X * scale,
                        paperY + printableHundredths.Y * scale,
                        printableHundredths.Width * scale,
                        printableHundredths.Height * scale));

                    printablePx.Intersect(paperRect);
                    if (printablePx.Width <= 0 || printablePx.Height <= 0)
                        printablePx = paperRect;

                    int nUp = Math.Max(1, effectiveOptions.PagesPerSheet);
                    int previewGapPx = (int)Math.Round(effectiveOptions.NUpGapInches * 100f * scale);
                    Rectangle[] slots = BuildNUpSlots(printablePx, nUp, previewGapPx, previewGapPx);
                    sheetCount = (int)Math.Ceiling(pageIndexes.Count / (double)nUp);
                    requestedSheetIndex = Math.Clamp(requestedSheetIndex, 0, Math.Max(0, sheetCount - 1));

                    int startIndex = requestedSheetIndex * nUp;
                    int endIndex = Math.Min(pageIndexes.Count, startIndex + nUp);

                    // Draw a very light printable-area guide without changing the actual print.
                    using (Pen printablePen = new Pen(Color.FromArgb(215, 220, 225), 1f))
                        g.DrawRectangle(printablePen, printablePx);

                    for (int i = startIndex; i < endIndex; i++)
                    {
                        int slotIndex = i - startIndex;
                        if (slotIndex >= slots.Length)
                            break;

                        Rectangle slot = slots[slotIndex];

                        using (Pen slotPen = new Pen(Color.FromArgb(225, 228, 232), 1f))
                            g.DrawRectangle(slotPen, slot);

                        if (pdf != null)
                        {
                            RenderPdfPageIntoPreviewSlot(
                                pdf,
                                pageIndexes[i],
                                g,
                                slot,
                                effectiveOptions);

                            // تشخيص مؤقت: نطبع المقاس الأصلي الحقيقي لكل صفحة (كما
                            // يقرأه PdfiumViewer) أسفل خانتها عندما تكون الطباعة
                            // بعدة صفحات في الورقة، حتى نتأكد هل اختلاف الحجم بين
                            // الصفحات سببه أن الملف نفسه فيه صفحات بمقاسات مختلفة،
                            // أم أنه خلل في الحساب. يمكن حذف هذا الجزء لاحقًا.
                            if (nUp > 1)
                            {
                                SizeF ptSize = pdf.PageSizes[pageIndexes[i]];
                                double mmW = ptSize.Width / 72.0 * 25.4;
                                double mmH = ptSize.Height / 72.0 * 25.4;
                                string sizeLabel = $"{mmW:0}×{mmH:0}mm";
                                using Font dbgFont = new Font("Segoe UI", 7f);
                                using SolidBrush dbgBrush = new SolidBrush(Color.FromArgb(180, 200, 60, 60));
                                g.DrawString(sizeLabel, dbgFont, dbgBrush, slot.Left + 2, slot.Bottom - 14);
                            }
                        }
                        else if (i == 0)
                        {
                            using Image img = Image.FromFile(filePath);
                            DrawImageFit(g, img, slot, effectiveOptions);
                        }
                    }

                    string sheetText = $"ورقة {requestedSheetIndex + 1} / {Math.Max(1, sheetCount)}    •    {nUp} صفحة/ورقة";
                    using Font infoFont = new Font("Segoe UI", 9f * Math.Max(1f, sizeFactor), FontStyle.Bold);
                    SizeF infoSize = g.MeasureString(sheetText, infoFont);
                    using SolidBrush infoBrush = new SolidBrush(Color.FromArgb(70, 78, 86));
                    g.DrawString(
                        sheetText,
                        infoFont,
                        infoBrush,
                        Math.Max(10, canvasWidth - infoSize.Width - 12),
                        Math.Max(8, paperY + paperHeightPx + 8));
                }

                return sheet;
            }
        }

        /// <summary>
        /// بيفتح الـPDF مرة واحدة ويحتفظ بيه طول ما المستخدم بيتنقل بين أوراقه
        /// (قبل كده كان بيتفتح ويتقفل مع كل ضغطة «التالي»، وده أهم سبب للبطء).
        /// بيتقرأ في الذاكرة فمبيقفلش الملف على باقي البرنامج. لازم يتنادى من داخل previewRenderLock.
        /// </summary>
        private PdfiumViewerPdfDocument AcquirePreviewPdf(string filePath)
        {
            FileInfo info = new FileInfo(filePath);
            string stamp = filePath + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;

            if (previewPdfDoc != null && previewPdfStamp == stamp)
                return previewPdfDoc;

            ReleasePreviewPdfUnlocked();

            PdfiumViewerPdfDocument doc;
            MemoryStream? stream = null;

            if (info.Length <= 200L * 1024 * 1024)
            {
                stream = new MemoryStream(File.ReadAllBytes(filePath), writable: false);
                try
                {
                    doc = PdfiumViewerPdfDocument.Load(stream);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            else
            {
                // ملفات ضخمة جدًا: نفتحها من القرص بدل ما ناكل الذاكرة.
                doc = PdfiumViewerPdfDocument.Load(filePath);
            }

            previewPdfDoc = doc;
            previewPdfStream = stream;
            previewPdfStamp = stamp;
            return doc;
        }

        private void ReleasePreviewPdfUnlocked()
        {
            try { previewPdfDoc?.Dispose(); } catch { }
            try { previewPdfStream?.Dispose(); } catch { }
            previewPdfDoc = null;
            previewPdfStream = null;
            previewPdfStamp = null;
        }

        private void ReleasePreviewPdf()
        {
            lock (previewRenderLock)
            {
                ReleasePreviewPdfUnlocked();
            }
        }

        private static RectangleF GetPreviewPrintableArea(
            float hardMarginX,
            float hardMarginY,
            float paperWidthHundredths,
            float paperHeightHundredths,
            PrintOptions options)
        {
            // For a reliable preview, derive the printable rectangle from the
            // printer's hard margins instead of trusting PrintableArea coordinates,
            // which vary between printer drivers and orientations.
            float left = Math.Max(0f, hardMarginX);
            float top = Math.Max(0f, hardMarginY);
            float right = Math.Max(left, paperWidthHundredths - left);
            float bottom = Math.Max(top, paperHeightHundredths - top);

            float x = left;
            float y = top;
            float width = Math.Max(1f, right - left);
            float height = Math.Max(1f, bottom - top);

            float leftMm = MmToHundredthsInch(options.MarginLeftMm);
            float topMm = MmToHundredthsInch(options.MarginTopMm);
            float rightMm = MmToHundredthsInch(options.MarginRightMm);
            float bottomMm = MmToHundredthsInch(options.MarginBottomMm);

            return new RectangleF(
                x + leftMm,
                y + topMm,
                Math.Max(1f, width - leftMm - rightMm),
                Math.Max(1f, height - topMm - bottomMm));
        }

        private static void RenderPdfPageIntoPreviewSlot(
            PdfiumViewerPdfDocument pdf,
            int pageIndex,
            Graphics graphics,
            Rectangle target,
            PrintOptions options)
        {
            SizeF pageSize = pdf.PageSizes[pageIndex];
            double sourceWidthPoints = Math.Max(1d, pageSize.Width);
            double sourceHeightPoints = Math.Max(1d, pageSize.Height);

            // PdfiumViewer applies a world-transform correction when the DPI passed
            // here differs from graphics.DpiX/DpiY. The preview sheet bitmap is 96 DPI;
            // passing 144 DPI made the PDF render at 96/144 (about 67%) of its slot.
            // Use the actual destination Graphics DPI for both sizing and rendering so
            // the preview page occupies the same Fit rectangle calculated below.
            float previewDpiX = Math.Max(1f, graphics.DpiX);
            float previewDpiY = Math.Max(1f, graphics.DpiY);
            double sourceWidthPixelsAtDpi = sourceWidthPoints / 72.0 * previewDpiX;
            double sourceHeightPixelsAtDpi = sourceHeightPoints / 72.0 * previewDpiY;

            double width;
            double height;

            switch (options.Scaling)
            {
                case PrintScalingMode.ActualSize:
                    width = sourceWidthPixelsAtDpi;
                    height = sourceHeightPixelsAtDpi;
                    break;

                case PrintScalingMode.Custom:
                    double custom = Math.Max(0.10, options.CustomScalePercent / 100.0);
                    width = sourceWidthPixelsAtDpi * custom;
                    height = sourceHeightPixelsAtDpi * custom;
                    break;

                default:
                    double fit = Math.Min(
                        target.Width / sourceWidthPixelsAtDpi,
                        target.Height / sourceHeightPixelsAtDpi);
                    width = sourceWidthPixelsAtDpi * fit;
                    height = sourceHeightPixelsAtDpi * fit;
                    break;
            }

            // Never allow an individual page to escape its slot.
            if (width > target.Width || height > target.Height)
            {
                double down = Math.Min(
                    target.Width / Math.Max(1d, width),
                    target.Height / Math.Max(1d, height));
                width *= down;
                height *= down;
            }

            int w = Math.Max(1, Math.Min(target.Width, (int)Math.Round(width)));
            int h = Math.Max(1, Math.Min(target.Height, (int)Math.Round(height)));
            // نوسّط دائمًا داخل الخانة (سواء صفحة واحدة أو N-up)، بغض النظر عن
            // خياري "توسيط أفقي/رأسي"؛ فبدون هذا كانت الصفحات الأصغر من مساحتها
            // المخصصة (خصوصًا في وضع عدة صفحات بالورقة) تُرسم ملتصقة بالركن
            // العلوي الأيسر للخانة بدلاً من أن تملأها بشكل متوازن ومتوسط.
            int x = target.Left + (target.Width - w) / 2;
            int y = target.Top + (target.Height - h) / 2;

            // الطباعة والمعاينة بيشتغلوا في خيوط مختلفة دلوقتي؛ نسيب Pdfium يرسم صفحة
            // واحدة في كل مرة عشان مايحصلش تداخل بين الخيطين.
            lock (PdfiumRenderLock)
            {
                pdf.Render(
                    pageIndex,
                    graphics,
                    previewDpiX,
                    previewDpiY,
                    new Rectangle(x, y, w, h),
                    PdfiumViewer.PdfRenderFlags.ForPrinting | PdfiumViewer.PdfRenderFlags.Annotations);
            }
        }

        private Image CreatePreviewBitmap(
            Image source,
            int maxWidth,
            int maxHeight)
        {
            double scale =
                Math.Min(
                    1.0,
                    Math.Min(
                        (double)maxWidth / source.Width,
                        (double)maxHeight / source.Height));

            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            Bitmap result =
                new Bitmap(width, height, PixelFormat.Format24bppRgb);

            using Graphics g = Graphics.FromImage(result);
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            g.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel);

            return result;
        }

        // ============================================================
        // المعالجة الكاملة للصور
        // ============================================================
        private string AutoCropImage(
            string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return imagePath;

                using Cv.Mat source =
                    Cv.Cv2.ImRead(
                        imagePath,
                        Cv.ImreadModes.Color);

                if (source.Empty())
                    return imagePath;

                if (source.Width < 200 ||
                    source.Height < 200)
                {
                    return imagePath;
                }

                using Cv.Mat analysis =
                    ResizeForAnalysis(
                        source);

                using Cv.Mat gray =
                    PrepareGrayForDocument(
                        analysis);

                using Cv.Mat edges =
                    DetectDocumentEdges(
                        gray);

                Cv.Point[]? bestQuad =
                    FindBestDocumentQuadrilateral(
                        edges,
                        analysis.Width,
                        analysis.Height);

                // ----------------------------------------------------
                // وجدنا الورقة
                // ----------------------------------------------------
                if (bestQuad != null)
                {
                    Cv.Point[] corners =
                        OrderCorners(
                            bestQuad);

                    double scaleX =
                        (double)
                            source.Width /
                            analysis.Width;

                    double scaleY =
                        (double)
                            source.Height /
                            analysis.Height;

                    for (
                        int i = 0;
                        i < 4;
                        i++)
                    {
                        corners[i] =
                            new Cv.Point(
                                Clamp(
                                    (int)
                                        Math.Round(
                                            corners[i].X *
                                            scaleX),
                                    0,
                                    source.Width - 1),

                                Clamp(
                                    (int)
                                        Math.Round(
                                            corners[i].Y *
                                            scaleY),
                                    0,
                                    source.Height - 1));
                    }

                    using Cv.Mat corrected =
                        PerspectiveCorrect(
                            source,
                            corners);

                    using Cv.Mat enhanced =
                        ImproveDocumentImage(
                            corrected);

                    using Cv.Mat finalImage =
                        AddDocumentBorder(
                            enhanced);

                    return SaveProcessedImage(
                        finalImage,
                        imagePath,
                        "document");
                }

                // ----------------------------------------------------
                // لم نجد الورقة:
                // قص احتياطي
                // ----------------------------------------------------
                using Cv.Mat fallback =
                    FallbackCropByForeground(
                        source);

                if (!fallback.Empty() &&
                    fallback.Width > 100 &&
                    fallback.Height > 100)
                {
                    using Cv.Mat enhanced =
                        ImproveDocumentImage(
                            fallback);

                    using Cv.Mat finalImage =
                        AddDocumentBorder(
                            enhanced);

                    return SaveProcessedImage(
                        finalImage,
                        imagePath,
                        "fallback");
                }

                // ----------------------------------------------------
                // آخر حل:
                // تحسين بدون قص
                // ----------------------------------------------------
                using Cv.Mat enhancedOnly =
                    ImproveDocumentImage(
                        source);

                using Cv.Mat finalEnhanced =
                    AddDocumentBorder(
                        enhancedOnly);

                return SaveProcessedImage(
                    finalEnhanced,
                    imagePath,
                    "enhanced");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "AutoCropImage Error:");

                System.Diagnostics.Debug.WriteLine(
                    ex);

                return imagePath;
            }
        }

        // ============================================================
        // تصغير الصورة للتحليل
        // ============================================================
        private Cv.Mat ResizeForAnalysis(
            Cv.Mat source)
        {
            const int maxSize = 1600;

            if (source.Width <= maxSize &&
                source.Height <= maxSize)
            {
                return source.Clone();
            }

            double scale =
                Math.Min(
                    (double)
                        maxSize /
                        source.Width,

                    (double)
                        maxSize /
                        source.Height);

            int width =
                Math.Max(
                    1,
                    (int)
                        Math.Round(
                            source.Width *
                            scale));

            int height =
                Math.Max(
                    1,
                    (int)
                        Math.Round(
                            source.Height *
                            scale));

            Cv.Mat resized =
                new Cv.Mat();

            Cv.Cv2.Resize(
                source,
                resized,
                new Cv.Size(
                    width,
                    height),
                0,
                0,
                Cv.InterpolationFlags.Area);

            return resized;
        }

        // ============================================================
        // تحويل الصورة إلى grayscale مع معالجة الإضاءة
        // ============================================================
        private Cv.Mat PrepareGrayForDocument(
            Cv.Mat source)
        {
            Cv.Mat gray =
                new Cv.Mat();

            Cv.Cv2.CvtColor(
                source,
                gray,
                Cv.ColorConversionCodes.BGR2GRAY);

            Cv.Mat blurred =
                new Cv.Mat();

            Cv.Cv2.GaussianBlur(
                gray,
                blurred,
                new Cv.Size(
                    5,
                    5),
                0);

            gray.Dispose();

            // --------------------------------------------------------
            // تقدير الإضاءة الخلفية
            // --------------------------------------------------------
            Cv.Mat background =
                new Cv.Mat();

            Cv.Cv2.GaussianBlur(
                blurred,
                background,
                new Cv.Size(
                    31,
                    31),
                0);

            Cv.Mat normalized =
                new Cv.Mat();

            Cv.Cv2.Divide(
                blurred,
                background,
                normalized,
                255.0);

            blurred.Dispose();
            background.Dispose();

            Cv.Mat result =
                new Cv.Mat();

            Cv.Cv2.Normalize(
                normalized,
                result,
                0,
                255,
                Cv.NormTypes.MinMax);

            normalized.Dispose();

            return result;
        }

        // ============================================================
        // اكتشاف حواف الورقة
        // ============================================================
        private Cv.Mat DetectDocumentEdges(
            Cv.Mat gray)
        {
            Cv.Mat edges =
                new Cv.Mat();

            Cv.Cv2.Canny(
                gray,
                edges,
                35,
                120);

            using Cv.Mat kernel =
                Cv.Cv2.GetStructuringElement(
                    Cv.MorphShapes.Rect,
                    new Cv.Size(
                        7,
                        7));

            Cv.Cv2.MorphologyEx(
                edges,
                edges,
                Cv.MorphTypes.Close,
                kernel,
                iterations: 2);

            Cv.Cv2.Dilate(
                edges,
                edges,
                kernel,
                iterations: 1);

            return edges;
        }

        // ============================================================
        // العثور على الورقة
        // ============================================================
        private Cv.Point[]?
            FindBestDocumentQuadrilateral(
                Cv.Mat edges,
                int imageWidth,
                int imageHeight)
        {
            Cv.Cv2.FindContours(
                edges,
                out Cv.Point[][] contours,
                out _,
                Cv.RetrievalModes.External,
                Cv.ContourApproximationModes.ApproxSimple);

            double imageArea =
                imageWidth *
                (double)
                imageHeight;

            Cv.Point[]? bestQuad = null;

            double bestScore = 0;

            foreach (
                Cv.Point[] contour
                in contours)
            {
                double contourArea =
                    Math.Abs(
                        Cv.Cv2.ContourArea(
                            contour));

                // تجاهل الأشياء الصغيرة
                if (contourArea <
                    imageArea * 0.12)
                {
                    continue;
                }

                double perimeter =
                    Cv.Cv2.ArcLength(
                        contour,
                        true);

                if (perimeter <= 0)
                    continue;

                Cv.Point[] approx =
                    Cv.Cv2.ApproxPolyDP(
                        contour,
                        0.025 *
                        perimeter,
                        true);

                if (approx.Length != 4)
                    continue;

                if (!Cv.Cv2.IsContourConvex(
                        approx))
                {
                    continue;
                }

                double areaRatio =
                    contourArea /
                    imageArea;

                // إذا كانت الصورة كلها contour
                // فغالبًا لم نجد حدود الورقة الحقيقية.
                if (areaRatio > 0.985)
                    continue;

                double rectangularity =
                    CalculateRectangularity(
                        approx,
                        contourArea);

                if (rectangularity < 0.55)
                    continue;

                double centerScore =
                    CalculateCenterScore(
                        approx,
                        imageWidth,
                        imageHeight);

                double score =
                    contourArea *
                    (
                        0.70 +
                        0.30 *
                        rectangularity
                    ) *
                    (
                        0.75 +
                        0.25 *
                        centerScore
                    );

                if (score > bestScore)
                {
                    bestScore = score;

                    bestQuad =
                        approx;
                }
            }

            return bestQuad;
        }

        // ============================================================
        // قياس شكل المستطيل
        // ============================================================
        private double CalculateRectangularity(
            Cv.Point[] quad,
            double contourArea)
        {
            double a =
                Distance(
                    quad[0],
                    quad[1]);

            double b =
                Distance(
                    quad[1],
                    quad[2]);

            double c =
                Distance(
                    quad[2],
                    quad[3]);

            double d =
                Distance(
                    quad[3],
                    quad[0]);

            double rectangleArea =
                Math.Max(a, c) *
                Math.Max(b, d);

            if (rectangleArea <= 0)
                return 0;

            return Math.Min(
                1.0,
                contourArea /
                rectangleArea);
        }

        // ============================================================
        // تقييم مكان الورقة
        // ============================================================
        private double CalculateCenterScore(
            Cv.Point[] quad,
            int imageWidth,
            int imageHeight)
        {
            double centerX =
                quad.Average(
                    p => p.X);

            double centerY =
                quad.Average(
                    p => p.Y);

            double targetX =
                imageWidth /
                2.0;

            double targetY =
                imageHeight /
                2.0;

            double distance =
                Math.Sqrt(
                    Math.Pow(
                        centerX -
                        targetX,
                        2)
                    +
                    Math.Pow(
                        centerY -
                        targetY,
                        2));

            double maxDistance =
                Math.Sqrt(
                    targetX *
                    targetX
                    +
                    targetY *
                    targetY);

            if (maxDistance <= 0)
                return 1.0;

            double score =
                1.0 -
                (
                    distance /
                    maxDistance
                );

            return Math.Max(
                0,
                Math.Min(
                    1,
                    score));
        }

        // ============================================================
        // ترتيب الزوايا
        // ============================================================
        private Cv.Point[] OrderCorners(
            Cv.Point[] points)
        {
            if (points.Length != 4)
            {
                throw new ArgumentException(
                    "يجب أن تكون هناك 4 نقاط.");
            }

            Cv.Point[] result =
                new Cv.Point[4];

            // أعلى يسار
            result[0] =
                points
                    .OrderBy(
                        p => p.X + p.Y)
                    .First();

            // أسفل يمين
            result[2] =
                points
                    .OrderByDescending(
                        p => p.X + p.Y)
                    .First();

            // أعلى يمين
            result[1] =
                points
                    .OrderBy(
                        p => p.Y - p.X)
                    .First();

            // أسفل يسار
            result[3] =
                points
                    .OrderByDescending(
                        p => p.Y - p.X)
                    .First();

            return result;
        }

        // ============================================================
        // تصحيح المنظور
        // ============================================================
        private Cv.Mat PerspectiveCorrect(
            Cv.Mat source,
            Cv.Point[] corners)
        {
            double topWidth =
                Distance(
                    corners[0],
                    corners[1]);

            double bottomWidth =
                Distance(
                    corners[3],
                    corners[2]);

            double leftHeight =
                Distance(
                    corners[0],
                    corners[3]);

            double rightHeight =
                Distance(
                    corners[1],
                    corners[2]);

            int outputWidth =
                Clamp(
                    (int)
                        Math.Round(
                            Math.Max(
                                topWidth,
                                bottomWidth)),
                    300,
                    5000);

            int outputHeight =
                Clamp(
                    (int)
                        Math.Round(
                            Math.Max(
                                leftHeight,
                                rightHeight)),
                    300,
                    7000);

            Cv.Point2f[] src =
            {
                new Cv.Point2f(
                    corners[0].X,
                    corners[0].Y),

                new Cv.Point2f(
                    corners[1].X,
                    corners[1].Y),

                new Cv.Point2f(
                    corners[2].X,
                    corners[2].Y),

                new Cv.Point2f(
                    corners[3].X,
                    corners[3].Y)
            };

            Cv.Point2f[] dst =
            {
                new Cv.Point2f(
                    0,
                    0),

                new Cv.Point2f(
                    outputWidth - 1,
                    0),

                new Cv.Point2f(
                    outputWidth - 1,
                    outputHeight - 1),

                new Cv.Point2f(
                    0,
                    outputHeight - 1)
            };

            using Cv.Mat transform =
                Cv.Cv2.GetPerspectiveTransform(
                    src,
                    dst);

            Cv.Mat corrected =
                new Cv.Mat();

            Cv.Cv2.WarpPerspective(
                source,
                corrected,
                transform,
                new Cv.Size(
                    outputWidth,
                    outputHeight),
                Cv.InterpolationFlags.Cubic,
                Cv.BorderTypes.Replicate);

            return corrected;
        }

        // ============================================================
        // تحسين الصورة
        // ============================================================
        private Cv.Mat ImproveDocumentImage(
            Cv.Mat source)
        {
            Cv.Mat work =
                source.Clone();

            // --------------------------------------------------------
            // إزالة noise
            // --------------------------------------------------------
            Cv.Mat denoised =
                new Cv.Mat();

            Cv.Cv2.FastNlMeansDenoisingColored(
                work,
                denoised,
                3,
                3,
                7,
                21);

            work.Dispose();

            // --------------------------------------------------------
            // LAB
            // --------------------------------------------------------
            Cv.Mat lab =
                new Cv.Mat();

            Cv.Cv2.CvtColor(
                denoised,
                lab,
                Cv.ColorConversionCodes.BGR2Lab);

            denoised.Dispose();

            Cv.Mat[] channels =
                Cv.Cv2.Split(
                    lab);

            try
            {
                using Cv.CLAHE clahe =
                    Cv.Cv2.CreateCLAHE(
                        2.2,
                        new Cv.Size(
                            8,
                            8));

                // ----------------------------------------------------
                // رفع وضوح التفاصيل
                // ----------------------------------------------------
                clahe.Apply(
                    channels[0],
                    channels[0]);

                Cv.Cv2.Merge(
                    channels,
                    lab);

                Cv.Mat result =
                    new Cv.Mat();

                Cv.Cv2.CvtColor(
                    lab,
                    result,
                    Cv.ColorConversionCodes.Lab2BGR);

                return ApplyLightingCorrection(
                    result);
            }
            finally
            {
                foreach (
                    Cv.Mat channel
                    in channels)
                {
                    channel.Dispose();
                }

                lab.Dispose();
            }
        }

        // ============================================================
        // تصحيح الإضاءة والظلال
        // ============================================================
        private Cv.Mat ApplyLightingCorrection(
            Cv.Mat source)
        {
            Cv.Mat lab =
                new Cv.Mat();

            Cv.Cv2.CvtColor(
                source,
                lab,
                Cv.ColorConversionCodes.BGR2Lab);

            source.Dispose();

            Cv.Mat[] channels =
                Cv.Cv2.Split(
                    lab);

            try
            {
                // ----------------------------------------------------
                // تقدير الخلفية والإضاءة
                // ----------------------------------------------------
                Cv.Mat background =
                    new Cv.Mat();

                Cv.Cv2.GaussianBlur(
                    channels[0],
                    background,
                    new Cv.Size(
                        51,
                        51),
                    0);

                Cv.Mat correctedL =
                    new Cv.Mat();

                Cv.Cv2.Divide(
                    channels[0],
                    background,
                    correctedL,
                    110.0);

                Cv.Cv2.Normalize(
                    correctedL,
                    correctedL,
                    0,
                    255,
                    Cv.NormTypes.MinMax);

                // ----------------------------------------------------
                // دمج التصحيح مع الصورة الأصلية
                // ----------------------------------------------------
                Cv.Cv2.AddWeighted(
                    channels[0],
                    0.35,
                    correctedL,
                    0.65,
                    0,
                    channels[0]);

                correctedL.Dispose();
                background.Dispose();

                Cv.Cv2.Merge(
                    channels,
                    lab);

                Cv.Mat result =
                    new Cv.Mat();

                Cv.Cv2.CvtColor(
                    lab,
                    result,
                    Cv.ColorConversionCodes.Lab2BGR);

                // ----------------------------------------------------
                // Sharpen خفيف
                // ----------------------------------------------------
                using Cv.Mat blurred =
                    new Cv.Mat();

                Cv.Cv2.GaussianBlur(
                    result,
                    blurred,
                    new Cv.Size(
                        0,
                        0),
                    1.0);

                Cv.Mat sharpened =
                    new Cv.Mat();

                Cv.Cv2.AddWeighted(
                    result,
                    1.25,
                    blurred,
                    -0.25,
                    0,
                    sharpened);

                result.Dispose();

                return sharpened;
            }
            finally
            {
                foreach (
                    Cv.Mat channel
                    in channels)
                {
                    channel.Dispose();
                }

                lab.Dispose();
            }
        }

        // ============================================================
        // قص احتياطي
        // ============================================================
        private Cv.Mat FallbackCropByForeground(
            Cv.Mat source)
        {
            using Cv.Mat gray =
                new Cv.Mat();

            Cv.Cv2.CvtColor(
                source,
                gray,
                Cv.ColorConversionCodes.BGR2GRAY);

            using Cv.Mat blurred =
                new Cv.Mat();

            Cv.Cv2.GaussianBlur(
                gray,
                blurred,
                new Cv.Size(
                    9,
                    9),
                0);

            using Cv.Mat threshold =
                new Cv.Mat();

            Cv.Cv2.AdaptiveThreshold(
                blurred,
                threshold,
                255,
                Cv.AdaptiveThresholdTypes.GaussianC,
                Cv.ThresholdTypes.BinaryInv,
                41,
                15);

            using Cv.Mat kernel =
                Cv.Cv2.GetStructuringElement(
                    Cv.MorphShapes.Rect,
                    new Cv.Size(
                        21,
                        21));

            Cv.Cv2.MorphologyEx(
                threshold,
                threshold,
                Cv.MorphTypes.Close,
                kernel,
                iterations: 2);

            Cv.Cv2.FindContours(
                threshold,
                out Cv.Point[][] contours,
                out _,
                Cv.RetrievalModes.External,
                Cv.ContourApproximationModes.ApproxSimple);

            double bestArea = 0;

            Cv.Rect bestRect =
                new Cv.Rect();

            foreach (
                Cv.Point[] contour
                in contours)
            {
                Cv.Rect rect =
                    Cv.Cv2.BoundingRect(
                        contour);

                double area =
                    rect.Width *
                    (double)
                    rect.Height;

                double ratio =
                    area /
                    (
                        source.Width *
                        (double)
                        source.Height
                    );

                if (ratio < 0.20)
                    continue;

                if (area > bestArea)
                {
                    bestArea = area;
                    bestRect = rect;
                }
            }

            if (bestArea <= 0)
                return new Cv.Mat();

            int padX =
                Math.Max(
                    8,
                    (int)
                        Math.Round(
                            source.Width *
                            0.012));

            int padY =
                Math.Max(
                    8,
                    (int)
                        Math.Round(
                            source.Height *
                            0.012));

            int x =
                Math.Max(
                    0,
                    bestRect.X -
                    padX);

            int y =
                Math.Max(
                    0,
                    bestRect.Y -
                    padY);

            int right =
                Math.Min(
                    source.Width,
                    bestRect.X +
                    bestRect.Width +
                    padX);

            int bottom =
                Math.Min(
                    source.Height,
                    bestRect.Y +
                    bestRect.Height +
                    padY);

            int width =
                right - x;

            int height =
                bottom - y;

            if (width < 100 ||
                height < 100)
            {
                return new Cv.Mat();
            }

            using Cv.Mat roi =
                new Cv.Mat(
                    source,
                    new Cv.Rect(
                        x,
                        y,
                        width,
                        height));

            return roi.Clone();
        }

        // ============================================================
        // إضافة إطار أبيض
        // ============================================================
        private Cv.Mat AddDocumentBorder(
            Cv.Mat source)
        {
            Cv.Mat result =
                new Cv.Mat();

            Cv.Cv2.CopyMakeBorder(
                source,
                result,
                8,
                8,
                8,
                8,
                Cv.BorderTypes.Constant,
                new Cv.Scalar(
                    255,
                    255,
                    255));

            return result;
        }

        // ============================================================
        // حفظ الصورة المعالجة
        // ============================================================
        private string SaveProcessedImage(
            Cv.Mat image,
            string originalPath,
            string prefix)
        {
            string directory =
                Path.Combine(
                    Path.GetTempPath(),
                    "LibraryPrintApp",
                    "ProcessedImages");

            Directory.CreateDirectory(
                directory);

            string outputPath =
                Path.Combine(
                    directory,

                    Path.GetFileNameWithoutExtension(
                        originalPath)

                    + "_"
                    + prefix
                    + "_"
                    + Guid.NewGuid()
                        .ToString("N")
                    + ".jpg");

            Cv.Cv2.ImWrite(
                outputPath,
                image,
                new Cv.ImageEncodingParam(
                    Cv.ImwriteFlags.JpegQuality,
                    95));

            return outputPath;
        }

        // ============================================================
        // Distance
        // ============================================================
        private static double Distance(
            Cv.Point p1,
            Cv.Point p2)
        {
            double dx =
                p1.X -
                p2.X;

            double dy =
                p1.Y -
                p2.Y;

            return Math.Sqrt(
                dx * dx +
                dy * dy);
        }

        // ============================================================
        // Clamp
        // ============================================================
        private static int Clamp(
            int value,
            int min,
            int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        // ============================================================
        // سعر الوجه/الصفحة الواحدة: يعتمد على وضع الطباعة واللون.
        // وش وظهر له سعرين منفصلين (أسود / ألوان) عن السعر العادي،
        // والاحتساب دايمًا بعدد الصفحات المطبوعة (وش) وليس بالورقة.
        // ============================================================
        private decimal GetPricePerFace(
            string colorType,
            string duplexMode,
            string? pricingCategory = null)
        {
            bool isDuplex = duplexMode == "وش وظهر";
            bool isColor = colorType == "ألوان";

            if (!string.IsNullOrWhiteSpace(pricingCategory))
            {
                PricingSettings saved = pricingSettings;
                return pricingCategory switch
                {
                    "الطلبة" => isDuplex
                        ? (isColor ? saved.StudentDuplexColor : saved.StudentDuplexBlack)
                        : (isColor ? saved.StudentColor : saved.StudentBlack),
                    "عادي" => isDuplex
                        ? (isColor ? saved.NormalDuplexColor : saved.NormalDuplexBlack)
                        : (isColor ? saved.NormalColor : saved.NormalBlack),
                    "مخصص" => isDuplex
                        ? (isColor ? saved.CustomDuplexColor : saved.CustomDuplexBlack)
                        : (isColor ? saved.CustomColor : saved.CustomBlack),
                    _ => GetPricePerFace(colorType, duplexMode, null)
                };
            }

            if (isDuplex && isColor) return LiveNumericValue(numDuplexColorPrice);
            if (isDuplex) return LiveNumericValue(numDuplexPrice);
            return isColor ? LiveNumericValue(numColorPrice) : LiveNumericValue(numBlackPrice);
        }

        private string ResolveRowPricingCategory(DataGridViewRow row)
        {
            if (row.Tag is FileEntry entry && !string.IsNullOrWhiteSpace(entry.PricingCategory))
                return entry.PricingCategory;
            return cmbPricingPreset.SelectedItem?.ToString() ?? "عادي";
        }

        private void ApplySelectedFilePricingCategory()
        {
            if (gridFiles == null || cmbSelectedFileCategory == null || gridFiles.Rows.Count == 0)
                return;

            var rows = gridFiles.SelectedRows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow && r.Tag is FileEntry)
                .ToList();
            if (rows.Count == 0)
            {
                // لا نظهر رسالة عند تهيئة الواجهة؛ الرسالة فقط عند اختيار قيمة يدوية.
                if (cmbSelectedFileCategory.SelectedIndex > 0)
                    MessageBox.Show(this, "حدد ملفًا واحدًا أو أكثر من الجدول أولًا، ثم اختر الفئة.", "فئة الملفات", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string? category = cmbSelectedFileCategory.SelectedIndex <= 0
                ? null
                : cmbSelectedFileCategory.SelectedItem?.ToString();

            foreach (DataGridViewRow row in rows)
            {
                var entry = (FileEntry)row.Tag!;
                row.Tag = new FileEntry(entry.OriginalPath, entry.PrintPath, entry.Options, category);
                row.Cells["FileName"].ToolTipText =
                    entry.OriginalPath + (category == null ? "\nالفئة: حسب فئة الشاشة" : "\nالفئة: " + category);
            }

            RecalculateTotals();
        }

        // ============================================================
        // نتيجة تسعير صف واحد
        // ============================================================
        private readonly struct RowPricing
        {
            /// <summary>إجمالي الصفحات المطبوعة (الصفحات المحددة × النسخ).</summary>
            public int Faces { get; init; }

            /// <summary>أوراق الدرج الفعلية المسحوبة.</summary>
            public int PhysicalSheets { get; init; }

            /// <summary>عدد الأوجه المطبوعة على الورق فعليًا (بعد N-up) — أساس الحساب.</summary>
            public int BillableSides { get; init; }

            public decimal PricePerSide { get; init; }

            public decimal Total { get; init; }
        }

        /// <summary>
        /// القاعدة الوحيدة لحساب سعر الطباعة، ومستخدمة في كل مكان:
        /// السعر = الأوجه المطبوعة × سعر الصفحة حسب (اللون + وش واحد/وش وظهر)،
        /// مع احتساب آخر ورقة فردية في وش وظهر بسعر وش واحد.
        /// الحساب بيتم لكل نسخة على حدة لأن كل نسخة بتتطبع كمهمة مستقلة
        /// (ملف 5 صفحات وش وظهر × نسختين = 3 + 3 أوراق).
        /// </summary>
        private RowPricing ComputeRowPricing(
            int selectedPages,
            int copies,
            int pagesPerSheet,
            string colorType,
            string duplexMode,
            string? pricingCategory = null)
        {
            int pages = Math.Max(1, selectedPages);
            int copyCount = Math.Max(1, copies);
            int nUp = Math.Max(1, pagesPerSheet);
            bool duplex = duplexMode == "وش وظهر";

            int sidesPerCopy = (int)Math.Ceiling(pages / (double)nUp);
            int sheetsPerCopy = duplex
                ? (int)Math.Ceiling(sidesPerCopy / 2.0)
                : sidesPerCopy;

            int billableSides = sidesPerCopy * copyCount;
            decimal pricePerSide = GetPricePerFace(colorType, duplexMode, pricingCategory);

            // التسعير الدقيق:
            // - وش واحد: كل وجه × سعر الوش.
            // - وش وظهر: الأزواج الكاملة (وجهين على ورقة) بسعر "وش وظهر"،
            //   ولو عدد الأوجه فردي فآخر ورقة بتتطبع وش واحد فبتتحسب بسعر "وش واحد".
            //   مثال: 11 صفحة وش وظهر = 5 أوراق وش وظهر + ورقة وش واحد.
            decimal totalPerCopy;

            if (duplex)
            {
                int fullPairs = sidesPerCopy / 2;
                int lastSingleSide = sidesPerCopy % 2;

                decimal singleFacePrice =
                    GetPricePerFace(colorType, "وش واحد", pricingCategory);

                totalPerCopy =
                    (fullPairs * 2 * pricePerSide) +
                    (lastSingleSide * singleFacePrice);
            }
            else
            {
                totalPerCopy = sidesPerCopy * pricePerSide;
            }

            return new RowPricing
            {
                Faces = pages * copyCount,
                PhysicalSheets = sheetsPerCopy * copyCount,
                BillableSides = billableSides,
                PricePerSide = pricePerSide,
                Total = totalPerCopy * copyCount
            };
        }

        // ============================================================
        // تطبيق فئة السعر المختارة (الطلبة / عادي / مخصص)
        // ============================================================
        private void AttachLivePriceEvents()
        {
            foreach (NumericUpDown control in new[]
            {
                numBlackPrice, numColorPrice, numDuplexPrice, numDuplexColorPrice
            })
            {
                control.KeyUp += (s, e) => RecalculateTotals();
                control.MouseUp += (s, e) => RecalculateTotals();

                TextBox? editor = control.Controls.OfType<TextBox>().FirstOrDefault();
                if (editor != null)
                {
                    editor.TextChanged += (s, e) => RecalculateTotals();
                    editor.KeyUp += (s, e) => RecalculateTotals();
                    editor.MouseUp += (s, e) => RecalculateTotals();
                }
            }
        }

        private static decimal LiveNumericValue(NumericUpDown control)
        {
            string text = control.Text?.Trim() ?? "";
            if (decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out decimal value) ||
                decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return Math.Min(control.Maximum, Math.Max(control.Minimum, value));
            }
            return control.Value;
        }

        private void ApplyPricingPreset()
        {
            string preset =
                cmbPricingPreset.SelectedItem?.ToString()
                ?? "عادي";

            pricingSettings = PricingStore.Load();

            bool oldGlobalUpdating = isGlobalUpdating;
            isGlobalUpdating = true;

            try
            {
                if (preset == "الطلبة")
                {
                    numBlackPrice.Value = pricingSettings.StudentBlack;
                    numColorPrice.Value = pricingSettings.StudentColor;
                    numDuplexPrice.Value = pricingSettings.StudentDuplexBlack;
                    numDuplexColorPrice.Value = pricingSettings.StudentDuplexColor;
                    LockMainPriceControls();
                }
                else if (preset == "مخصص")
                {
                    numBlackPrice.Value = ClampPrice(pricingSettings.CustomBlack);
                    numColorPrice.Value = ClampPrice(pricingSettings.CustomColor);
                    numDuplexPrice.Value = ClampPrice(pricingSettings.CustomDuplexBlack);
                    numDuplexColorPrice.Value = ClampPrice(pricingSettings.CustomDuplexColor);
                    UnlockMainPriceControls();
                }
                else
                {
                    numBlackPrice.Value = pricingSettings.NormalBlack;
                    numColorPrice.Value = pricingSettings.NormalColor;
                    numDuplexPrice.Value = pricingSettings.NormalDuplexBlack;
                    numDuplexColorPrice.Value = pricingSettings.NormalDuplexColor;
                    LockMainPriceControls();
                }
            }
            finally
            {
                isGlobalUpdating = oldGlobalUpdating;
            }

            RecalculateTotals();
        }

        private decimal ClampPrice(decimal value) =>
            Math.Min(100000m, Math.Max(0m, value));

        private void UnlockMainPriceControls()
        {
            NumericUpDown[] controls =
            {
                numBlackPrice,
                numColorPrice,
                numDuplexPrice,
                numDuplexColorPrice
            };

            foreach (NumericUpDown control in controls)
            {
                control.ReadOnly = false;
                control.Enabled = true;
                control.TabStop = true;
                control.ContextMenuStrip = null;
            }
        }

        private void SaveCustomPricingIfNeeded()
        {
            if (cmbPricingPreset == null ||
                cmbPricingPreset.SelectedItem?.ToString() != "مخصص" ||
                isGlobalUpdating)
                return;

            try
            {
                pricingSettings.CustomBlack = LiveNumericValue(numBlackPrice);
                pricingSettings.CustomColor = LiveNumericValue(numColorPrice);
                pricingSettings.CustomDuplexBlack = LiveNumericValue(numDuplexPrice);
                pricingSettings.CustomDuplexColor = LiveNumericValue(numDuplexColorPrice);
                PricingStore.Save(pricingSettings);
            }
            catch
            {
                // عدم حفظ السعر لا يمنع المستخدم من متابعة الحساب.
            }
        }

        private void LockMainPriceControls()
        {
            // لا نعتمد على ReadOnly وحدها؛ NumericUpDown قد يتغير بأسهم التحكم.
            // Enabled=false يقفل الأسهم والكتابة ولوحة المفاتيح والـ mouse wheel معًا.
            NumericUpDown[] controls =
            {
                numBlackPrice,
                numColorPrice,
                numDuplexPrice,
                numDuplexColorPrice
            };

            foreach (NumericUpDown control in controls)
            {
                control.ReadOnly = true;
                control.Enabled = false;
                control.TabStop = false;

                // تعطيل أي قائمة سياقية قد تسمح بالنسخ/اللصق أو الإدخال غير المتوقع.
                control.ContextMenuStrip = new ContextMenuStrip();
            }
        }

        private void ApplyPricingSettings(PricingSettings newSettings)
        {
            pricingSettings = newSettings ?? PricingStore.Load();
            PricingStore.Save(pricingSettings);

            ApplyPricingPreset();
        }

        private void BtnAdminSettings_Click(
            object? sender,
            EventArgs e)
        {
            using PasswordPromptForm prompt =
                new PasswordPromptForm(
                    "أدخل كلمة مرور الأدمن لفتح إعدادات الإدارة:");

            if (prompt.ShowDialog(this) != DialogResult.OK)
                return;

            if (!AdminSecurity.ValidateAdminPassword(prompt.Password))
            {
                MessageBox.Show(
                    this,
                    "كلمة مرور الأدمن غير صحيحة.",
                    "رفض الدخول",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (!AdminPasswordRotation.EnsureRotatedIfDefault(this))
                return;

            using AdminPanelForm adminForm =
                new AdminPanelForm(
                    CurrentUserName,
                    PricingStore.Load(),
                    ApplyPricingSettings);

            adminForm.ShowDialog(this);
        }

        private int GetSelectedPageCount(DataGridViewRow row, int pageCount)
        {
            int safePageCount = Math.Max(1, pageCount);
            if (row.Tag is not FileEntry entry)
                return safePageCount;

            PrintOptions options = entry.Options.Clone();
            options.ClampToPageCount(safePageCount);
            return options.GetSelectedPageCount(safePageCount);
        }

        private static int GetPagesPerSheet(DataGridViewRow row)
        {
            if (row.Tag is not FileEntry entry)
                return 1;

            return entry.Options.PagesPerSheet switch
            {
                2 or 4 or 6 or 9 or 16 => entry.Options.PagesPerSheet,
                _ => 1
            };
        }

        private static List<int> BuildPageIndexList(PrintOptions options, int pageCount)
        {
            int safePageCount = Math.Max(1, pageCount);
            return options.GetSelectedPageIndexes(safePageCount);
        }

        // ============================================================
        // إعادة حساب الإجماليات
        // ============================================================
        private void RecalculateTotals()
        {
            if (gridFiles == null)
                return;

            if (isRecalculating)
                return;

            isRecalculating = true;

            try
            {
                totalPagesCount = 0;

                totalPhysicalPapers = 0;

                grandTotalPrice = 0;

                foreach (
                    DataGridViewRow row
                    in gridFiles.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    int pages =
                        GetPositiveInt(
                            row.Cells["Pages"]
                                .Value,
                            1);

                    int copies =
                        GetPositiveInt(
                            row.Cells["Copies"]
                                .Value,
                            1);

                    string colorType =
                        row.Cells["ColorType"]
                            .Value?.ToString()
                        ?? "أسود";

                    string duplexMode =
                        row.Cells["DuplexMode"]
                            .Value?.ToString()
                        ?? "وش واحد";

                    int selectedPages = GetSelectedPageCount(row, pages);
                    int pagesPerSheet = GetPagesPerSheet(row);

                    // قاعدة التسعير الموحدة (نفسها في الجدول والسجل وإجمالي الطباعة):
                    // السعر = عدد الأوجه المطبوعة فعليًا × سعر الصفحة (الوجه).
                    // وش وظهر: الورقة = وجهين، فسعر 68 صفحة وش وظهر = 68 × سعر الصفحة
                    // (مش 34 ورقة × سعر الصفحة زي ما كان بيحصل).
                    RowPricing pricing = ComputeRowPricing(
                        selectedPages,
                        copies,
                        pagesPerSheet,
                        colorType,
                        duplexMode,
                        row.Tag is FileEntry rowEntry ? rowEntry.PricingCategory : null);

                    int printedFaces = pricing.Faces;
                    int physicalSheets = pricing.PhysicalSheets;
                    decimal rowTotal = pricing.Total;

                    row.Cells[
                        "PhysicalSheets"].Value =
                        physicalSheets;

                    row.Cells[
                        "Price"].Value =
                        $"{rowTotal:F2} ج.م";

                    totalPagesCount +=
                        printedFaces;

                    totalPhysicalPapers +=
                        physicalSheets;

                    grandTotalPrice +=
                        rowTotal;
                }

                lblTotalPages.Text =
                    $"إجمالي الأوجه المطبوعة: " +
                    $"{totalPagesCount}";

                lblTotalPapers.Text =
                    $"ورق الدرج الفعلي: " +
                    $"{totalPhysicalPapers} ورقة";

                lblTotalPrice.Text =
                    $"إجمالي الحساب: " +
                    $"{grandTotalPrice:F2} ج.م";
            }
            finally
            {
                isRecalculating = false;
            }
        }

        // ============================================================
        // الحصول على رقم موجب
        // ============================================================
        private static int GetPositiveInt(
            object? value,
            int defaultValue)
        {
            if (value == null)
                return defaultValue;

            if (!int.TryParse(
                    value.ToString(),
                    out int result))
            {
                return defaultValue;
            }

            return Math.Max(
                1,
                result);
        }

        // ============================================================
        // حساب إجمالي (الأوجه / الورق / السعر) لمجموعة صفوف معينة
        // (تُستخدم لعرض الإجمالي الخاص بالملفات المحددة فقط عند
        // الطباعة الجزئية، بنفس معادلات RecalculateTotals).
        // ============================================================
        /// <summary>
        /// يوزّع أعمدة جدول الملفات على عرض الجدول المتاح (Fill) بدل عروض ثابتة
        /// كانت بتجبر على السحب للجنب. العناوين الطويلة بتنزل على سطرين.
        /// </summary>
        private void FitFilesGridToWidth()
        {
            // الاسم: (وزن النسبة, أقل عرض)
            var layout = new Dictionary<string, (float Weight, int Min)>
            {
                ["Thumbnail"] = (8, 68),
                ["FileName"] = (24, 150),
                ["FileType"] = (8, 64),
                ["Pages"] = (12, 112),
                ["Copies"] = (8, 72),
                ["ColorType"] = (11, 96),
                ["DuplexMode"] = (14, 116),
                ["PhysicalSheets"] = (11, 104),
                ["Price"] = (13, 118)
            };

            gridFiles.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridFiles.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;

            foreach (DataGridViewColumn column in gridFiles.Columns)
            {
                (float weight, int min) =
                    layout.TryGetValue(column.Name, out var item) ? item : (10f, 70);

                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                column.FillWeight = weight;
                column.MinimumWidth = min;
            }
        }

        private (int Pages, int Papers, decimal Price) ComputeRowsTotals(
            IEnumerable<DataGridViewRow> rows)
        {
            int pages = 0;
            int papers = 0;
            decimal price = 0;

            foreach (DataGridViewRow row in rows)
            {
                if (row.IsNewRow)
                    continue;

                int rowPages =
                    GetPositiveInt(
                        row.Cells["Pages"].Value,
                        1);

                int copies =
                    GetPositiveInt(
                        row.Cells["Copies"].Value,
                        1);

                string colorType =
                    row.Cells["ColorType"].Value?.ToString()
                    ?? "أسود";

                string duplexMode =
                    row.Cells["DuplexMode"].Value?.ToString()
                    ?? "وش واحد";

                int selectedPages = GetSelectedPageCount(row, rowPages);

                // نفس قاعدة التسعير المستخدمة في الجدول بالظبط.
                RowPricing pricing = ComputeRowPricing(
                    selectedPages,
                    copies,
                    GetPagesPerSheet(row),
                    colorType,
                    duplexMode,
                    row.Tag is FileEntry rowEntry ? rowEntry.PricingCategory : null);

                pages += pricing.Faces;
                papers += pricing.PhysicalSheets;
                price += pricing.Total;
            }

            return (pages, papers, price);
        }

        // ============================================================
        // طباعة كل الملفات الموجودة في القائمة
        // ============================================================
        private void BtnPrintAll_Click(
            object? sender,
            EventArgs e)
        {
            if (gridFiles.Rows.Count == 0)
            {
                MessageBox.Show(
                    "برجاء إضافة ملفات أولاً!",
                    "تنبيه",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            List<DataGridViewRow> rowsToPrint =
                gridFiles.Rows
                    .Cast<DataGridViewRow>()
                    .Where(row => !row.IsNewRow)
                    .ToList();

            ExecutePrintJob(
                rowsToPrint,
                "كل الملفات",
                resetAfterSuccessfulPrintAll: true);
        }

        // ============================================================
        // طباعة الملفات المحددة فقط من القائمة (Selection)
        // ============================================================
        private void BtnPrintSelected_Click(
            object? sender,
            EventArgs e)
        {
            List<DataGridViewRow> rowsToPrint =
                gridFiles.SelectedRows
                    .Cast<DataGridViewRow>()
                    .Where(row => !row.IsNewRow)
                    .OrderBy(row => row.Index)
                    .ToList();

            if (rowsToPrint.Count == 0)
            {
                MessageBox.Show(
                    "برجاء تحديد ملف واحد على الأقل من القائمة أولاً.",
                    "طباعة المحدد فقط",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            ExecutePrintJob(
                rowsToPrint,
                $"الملفات المحددة ({rowsToPrint.Count})");
        }

        // ============================================================
        // تنفيذ عملية الطباعة الفعلية على مجموعة صفوف معينة
        // (تُستخدم من "طباعة الكل" و"طباعة المحدد فقط" معًا).
        //
        // مهم: الإرسال للطابعة بيتم في خيط خلفي (STA) — فالبرنامج مبيعملش
        // Not Responding حتى لو الطلب 15 ملف. الواجهة بتفضل شغالة، وبيظهر
        // شريط تقدم باسم الملف الحالي وزر إلغاء يحذف المهام التابعة للعملية من طابور ويندوز.
        // ============================================================

        /// <summary>لقطة ثابتة من صف الجدول وقت الطباعة (مستقلة عن الجدول).</summary>
        private sealed class PrintJobItem
        {
            public string FilePath { get; init; } = "";
            public string FileName { get; init; } = "";
            public string FileType { get; init; } = "";
            public string PricingCategory { get; init; } = "عادي";
            public int Copies { get; init; }
            public string ColorType { get; init; } = "أسود";
            public string DuplexMode { get; init; } = "وش واحد";
            public int SelectedPages { get; init; }
            public PrintOptions Options { get; init; } = new PrintOptions();
            public RowPricing Pricing { get; set; }

            /// <summary>لو الطباعة اتلغت: عدد الأوجه اللي طلعت مظبوطة فعلًا (null = الملف كامل).</summary>
            public int? PrintedFaces { get; set; }

            /// <summary>علامة بتتحط في اسم المستند عشان نتابعه في طابور ويندوز.</summary>
            public string SpoolTag { get; } = SpoolWatcher.NewTag();
        }

        private sealed class PrintBatchResult
        {
            public List<PrintJobItem> Printed { get; } = new();
            public Exception? Error { get; set; }
            public bool Cancelled { get; set; }
            /// <summary>الملف اللي حصل الخطأ أثناء إرساله (ممكن يكون طبع جزء منه).</summary>
            public PrintJobItem? FailedJob { get; set; }
        }

        /// <summary>
        /// دفعة طباعة واحدة (زر «طباعة الكل» أو «طباعة المحدد» مرة واحدة).
        /// كل دفعة ليها مراقب طابور وأرقام سجل خاصة بيها، فتقدر تبعت دفعة عميل جديد
        /// وانت لسه دفعة العميل اللي قبله بتتطبع في الماكينة.
        /// </summary>
        private sealed class PrintBatch
        {
            public List<PrintJobItem> Jobs { get; init; } = new();
            public string PrinterName { get; init; } = "";
            public int SkippedFilesCount { get; init; }
            public bool ResetAfterSuccessfulPrintAll { get; init; }
            public SpoolWatcher Watcher { get; init; } = null!;
            public CancellationTokenSource SendCts { get; } = new();
            public CancellationTokenSource WaitCts { get; } = new();
            public PrintBatchResult Result { get; set; } = new();
            public List<long> LogIds { get; } = new();
            public bool UserCancelled { get; set; }
            public bool LogFailureWarningShown { get; set; }
            public bool Sent { get; set; }
        }

        // true فقط أثناء إرسال الملفات للـSpooler (ثواني). بعدها الأزرار بترجع تشتغل فورًا
        // والدفعة بتكمّل متابعة الطابور في الخلفية.
        private bool isPrintingInBackground;
        private bool printCancelInProgress;
        private readonly List<PrintBatch> activeBatches = new();
        private readonly SemaphoreSlim batchFinishGate = new(1, 1);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private Panel? printStatusPanel;
        private Label? lblPrintStatus;
        private ProgressBar? printProgressBar;
        private Button? btnCancelPrint;

        private async void ExecutePrintJob(
            List<DataGridViewRow> rowsToPrint,
            string scopeDescription,
            bool resetAfterSuccessfulPrintAll = false)
        {
            if (isPrintingInBackground)
            {
                MessageBox.Show(
                    this,
                    "فيه طباعة شغالة دلوقتي. استنى لحد ما تخلص، أو اضغط «إيقاف» من الشريط اللي تحت الجدول.",
                    "الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                await ExecutePrintJobAsync(rowsToPrint, scopeDescription, resetAfterSuccessfulPrintAll);
            }
            catch (Exception ex)
            {
                SetPrintingState(false);

                MessageBox.Show(
                    this,
                    "حدث خطأ أثناء الطباعة:\n\n" + ex.Message,
                    "خطأ في الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async Task ExecutePrintJobAsync(
            List<DataGridViewRow> rowsToPrint,
            string scopeDescription,
            bool resetAfterSuccessfulPrintAll)
        {
            if (gridFiles.IsCurrentCellDirty)
            {
                gridFiles.CommitEdit(
                    DataGridViewDataErrorContexts.Commit);
            }

            gridFiles.EndEdit();

            RecalculateTotals();

            string printerName =
                cmbPrinters.SelectedItem?
                    .ToString()
                ?? "";

            if (string.IsNullOrWhiteSpace(
                    printerName))
            {
                MessageBox.Show(
                    "برجاء اختيار الطابعة أولاً!",
                    "تنبيه",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            PrinterSettings testSettings =
                new PrinterSettings
                {
                    PrinterName =
                        printerName
                };

            if (!testSettings.IsValid)
            {
                MessageBox.Show(
                    "الطابعة المحددة غير صالحة أو غير متاحة.",
                    "خطأ في الطابعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            // تحقّق من دعم عدد النسخ قبل التأكيد والطباعة، ولكل Queue فعلية (أسود/ألوان)،
            // حتى لا نرسل طلبًا لا تستطيع الطابعة تنفيذه أو نطلب من المستخدم تأكيد ملف لم يُرسل.
            try
            {
                var printerSettingsByQueue = new Dictionary<string, PrinterSettings>(StringComparer.OrdinalIgnoreCase);
                var resolvedQueuesByColor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataGridViewRow row in rowsToPrint)
                {
                    if (row.IsNewRow)
                        continue;

                    FileEntry? entry = row.Tag as FileEntry;
                    string? sourcePath = entry?.PrintPath;
                    if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                        continue;

                    int copies = GetPositiveInt(row.Cells["Copies"].Value, 1);
                    string colorType = row.Cells["ColorType"].Value?.ToString() ?? "أسود";
                    if (!resolvedQueuesByColor.TryGetValue(colorType, out string? targetQueue) || string.IsNullOrWhiteSpace(targetQueue))
                    {
                        targetQueue = ResolvePrinterForColor(printerName, colorType);
                        resolvedQueuesByColor[colorType] = targetQueue;
                    }

                    if (!printerSettingsByQueue.TryGetValue(targetQueue, out PrinterSettings? queueSettings) || queueSettings == null)
                    {
                        queueSettings = new PrinterSettings { PrinterName = targetQueue };
                        if (!queueSettings.IsValid)
                            throw new InvalidOperationException($"الطابعة غير متاحة: {targetQueue}");
                        printerSettingsByQueue[targetQueue] = queueSettings;
                    }

                    ValidatePrinterCopyCount(queueSettings, copies);
                }
            }
            catch (Exception copySettingsEx)
            {
                MessageBox.Show(
                    this,
                    "تعذر إرسال عدد النسخ المطلوب في مهمة واحدة للطابعة المختارة.\n\n" + copySettingsEx.Message +
                    "\n\nلم يتم إرسال أي ملف. راجع تعريف الطابعة أو حدّثه إذا لم يكن يدعم النسخ المتعددة.",
                    "عدد النسخ غير مدعوم",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            (int scopePages, int scopePapers, decimal scopePrice) =
                ComputeRowsTotals(rowsToPrint);

            var confirmRows = new List<(string Label, string Value)>
            {
                ("سيتم إرسال", scopeDescription),
                ("الطابعة الأساسية", printerName),
                ("إجمالي الأوجه", scopePages.ToString()),
                ("إجمالي ورق الدرج", scopePapers.ToString()),
                ("إجمالي الحساب", $"{scopePrice:F2} ج.م")
            };

            using (var confirmDialog = new PrintConfirmForm(
                "🖨️ تأكيد الطباعة",
                confirmRows,
                "يتم اختيار Queue الأسود/الألوان تلقائيًا لكل ملف حسب الإعداد المختار.   Enter = بدء الطباعة"))
            {
                if (confirmDialog.ShowDialog(this) != DialogResult.Yes)
                    return;
            }

            // --------------------------------------------------------
            // لقطة ثابتة من كل الصفوف على خيط الواجهة (قبل بدء الخيط الخلفي)،
            // عشان الخيط الخلفي مايلمسش الجدول أبدًا، والمستخدم يقدر يكمل شغل
            // أو يمسح القائمة وإحنا بنطبع.
            // --------------------------------------------------------
            var jobs = new List<PrintJobItem>();
            int skippedFilesCount = 0;

            foreach (DataGridViewRow row in rowsToPrint)
            {
                if (row.IsNewRow)
                    continue;

                FileEntry? entry = row.Tag as FileEntry;
                string? filePath = entry?.PrintPath;

                if (string.IsNullOrWhiteSpace(filePath) ||
                    !File.Exists(filePath))
                {
                    skippedFilesCount++;
                    continue;
                }

                int copies =
                    GetPositiveInt(row.Cells["Copies"].Value, 1);

                string colorType =
                    row.Cells["ColorType"].Value?.ToString()
                    ?? "أسود";

                string duplexMode =
                    row.Cells["DuplexMode"].Value?.ToString()
                    ?? "وش واحد";

                int totalFilePages =
                    GetPositiveInt(row.Cells["Pages"].Value, 1);

                if (entry?.Options != null)
                    entry.Options.ClampToPageCount(totalFilePages);

                int selectedPages =
                    GetSelectedPageCount(row, totalFilePages);

                string pricingCategory = ResolveRowPricingCategory(row);
                var jobOptions = (entry?.Options ?? new PrintOptions()).Clone();
                jobOptions.PriceBlackSimpleSnapshot = GetPricePerFace("أسود", "وش واحد", pricingCategory);
                jobOptions.PriceBlackDuplexSnapshot = GetPricePerFace("أسود", "وش وظهر", pricingCategory);
                jobOptions.PriceColorSimpleSnapshot = GetPricePerFace("ألوان", "وش واحد", pricingCategory);
                jobOptions.PriceColorDuplexSnapshot = GetPricePerFace("ألوان", "وش وظهر", pricingCategory);

                jobs.Add(new PrintJobItem
                {
                    FilePath = filePath,
                    FileName = row.Cells["FileName"].Value?.ToString() ?? "",
                    FileType = row.Cells["FileType"].Value?.ToString() ?? "",
                    PricingCategory = pricingCategory,
                    Copies = copies,
                    ColorType = colorType,
                    DuplexMode = duplexMode,
                    SelectedPages = selectedPages,
                    Options = jobOptions,
                    Pricing = ComputeRowPricing(
                        selectedPages,
                        copies,
                        GetPagesPerSheet(row),
                        colorType,
                        duplexMode,
                        pricingCategory)
                });
            }

            if (jobs.Count == 0)
            {
                MessageBox.Show(
                    this,
                    skippedFilesCount > 0
                        ? $"مفيش ملفات صالحة للطباعة ({skippedFilesCount} ملف غير موجود على الجهاز)."
                        : "مفيش ملفات للطباعة.",
                    "الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // --------------------------------------------------------
            // الإرسال الفعلي في خيط خلفي. بمجرد ما آخر ملف يوصل لـSpooler ويندوز
            // الأزرار بترجع تشتغل فورًا، وانتظار الطابور وتسجيل النتيجة بيكملوا في الخلفية.
            // --------------------------------------------------------
            var batch = new PrintBatch
            {
                Jobs = jobs,
                PrinterName = printerName,
                SkippedFilesCount = skippedFilesCount,
                ResetAfterSuccessfulPrintAll = resetAfterSuccessfulPrintAll,
                Watcher = new SpoolWatcher()
            };
            CancellationToken token = batch.SendCts.Token;
            int total = jobs.Count;

            activeBatches.Add(batch);
            SetPrintingState(true, total);
            batch.Watcher.Start();

            // Progress<T> بتتصنع هنا على خيط الواجهة، فالـcallback بيتنفذ عليه تلقائيًا.
            var progress = new Progress<(int Index, string Name)>(
                p => UpdatePrintProgress(p.Index, total, p.Name));

            try
            {
                batch.Result = await RunOnStaThread(
                    () => RunPrintBatch(jobs, printerName, progress, token, batch.Watcher));
            }
            catch (Exception sendEx)
            {
                batch.Result.Error = sendEx;
            }

            batch.Watcher.SendFinished();
            batch.Sent = true;
            SetPrintingState(false);   // الأزرار اتفتحت: تقدر تبعت العميل اللي بعده

            // «طباعة الكل» اتبعتت كلها بدون أي مشكلة: نفضّي القائمة فورًا عشان العميل الجديد.
            bool sentCleanly =
                batch.ResetAfterSuccessfulPrintAll &&
                !batch.UserCancelled &&
                batch.Result.Error == null &&
                !batch.Result.Cancelled &&
                batch.SkippedFilesCount == 0 &&
                batch.Result.Printed.Count == jobs.Count;

            if (sentCleanly)
                ResetPrintScreenAfterSuccessfulPrintAll();

            // متابعة الطابور وتسجيل النتيجة في الخلفية (من غير await عن قصد).
            _ = FinishPrintBatchAsync(batch);
        }

        /// <summary>
        /// بتستنى الدفعة تخلص من طابور ويندوز (من غير ما تقفل الشاشة)، وبعدين بتعرض
        /// نوافذ النتيجة/الدفع واحدة ورا التانية (دفعة واحدة في المرة) من غير ما تتراكب فوق نافذة مفتوحة.
        /// </summary>
        private async Task FinishPrintBatchAsync(PrintBatch batch)
        {
            try
            {
                SpoolWatcher watcher = batch.Watcher;

                if (batch.UserCancelled)
                {
                    // لو اتضغط الإلغاء أثناء الإرسال، نعيد محاولات الحذف بعد رجوع Print().
                    for (int attempt = 0; attempt < 8 && !watcher.AllDone; attempt++)
                    {
                        try { watcher.CancelPendingJobs(); }
                        catch (Exception ex) { AppLog.Error("تعذر حذف بعض مهام الطباعة من طابور ويندوز", ex); }
                        if (attempt < 7) await Task.Delay(200);
                    }
                }
                else
                {
                    while (!watcher.AllDone && !batch.WaitCts.IsCancellationRequested && !IsDisposed)
                    {
                        RefreshPrintStatusPanel();
                        try { await Task.Delay(300, batch.WaitCts.Token); }
                        catch (OperationCanceledException) { break; }
                    }
                }

                await batchFinishGate.WaitAsync();
                try
                {
                    // لو فيه نافذة مفتوحة فوق الشاشة (مثلاً تأكيد طباعة عميل تاني) نستناها تتقفل.
                    while (!IsDisposed && IsHandleCreated && !IsWindowEnabled(Handle))
                        await Task.Delay(300);

                    if (!IsDisposed)
                    {
                        // لو الشاشة مصغّرة نرجّعها عشان نافذة الدفع/النتيجة تبان قدامك.
                        if (WindowState == FormWindowState.Minimized)
                            ShowWindow(Handle, 9); // SW_RESTORE
                        Activate();
                        CompletePrintBatch(batch);
                    }
                }
                finally
                {
                    batchFinishGate.Release();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر إنهاء تسجيل دفعة الطباعة", ex);
                try
                {
                    MessageBox.Show(this,
                        "حصل خطأ أثناء تسجيل نتيجة الطباعة:\n\n" + ex.Message +
                        "\n\nراجع سجل الطباعة قبل ما تعيد الإرسال.",
                        "الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch { }
            }
            finally
            {
                activeBatches.Remove(batch);
                SpoolWatcher toDispose = batch.Watcher;
                _ = Task.Run(() => { try { toDispose.Dispose(); } catch { } });
                try { batch.SendCts.Dispose(); } catch { }
                try { batch.WaitCts.Dispose(); } catch { }
                if (!IsDisposed) RefreshPrintStatusPanel();
            }
        }

        /// <summary>نتيجة الدفعة بعد ما خلصت من الطابور: سجل + نوافذ النتيجة والدفع (على خيط الواجهة).</summary>
        private void CompletePrintBatch(PrintBatch batch)
        {
            PrintBatchResult result = batch.Result;
            List<PrintJobItem> jobs = batch.Jobs;
            string printerName = batch.PrinterName;
            int skippedFilesCount = batch.SkippedFilesCount;
            int total = jobs.Count;
            SpoolWatcher spoolWatcher = batch.Watcher;

            // --------------------------------------------------------
            // من هنا رجعنا على خيط الواجهة: تسجيل السجل ثم النتيجة.
            // --------------------------------------------------------
            // لو الطباعة اتوقفت (أو حصل خطأ في النص): الورق اللي راح للطابعة ممكن يكون باظ،
            // فنسأل المستخدم الطباعة تمت صح ولا لا — وميتحسبش غير اللي تمت صح
            // (الزبون مش هيدفع تمن الورق الباظ).
            // الملفات اللي اتلغت يدويًا من طابور ويندوز (أو اتوقفت/حصل فيها خطأ) محتاجة سؤال.
            // الزر الأحمر لو اتداس وقت إرسال آخر ملف مابيبقاش result.Cancelled=true، فبنعتمد على batch.UserCancelled كمان.
            bool stoppedOrError = result.Cancelled || result.Error != null || batch.UserCancelled;
            var allSent = new List<PrintJobItem>(result.Printed);
            if (result.FailedJob != null)
                allSent.Add(result.FailedJob);

            var candidates = new List<PrintJobItem>();
            var defaults = new List<bool>();
            var notes = new List<string>();
            int spoolCancelledCount = 0;

            for (int i = 0; i < allSent.Count; i++)
            {
                PrintJobItem job = allSent[i];
                bool isFailed = ReferenceEquals(job, result.FailedJob);
                bool cancelledInQueue = spoolWatcher.IsCancelled(job.SpoolTag);
                if (cancelledInQueue) spoolCancelledCount++;

                if (!(stoppedOrError || cancelledInQueue))
                    continue;

                candidates.Add(job);
                defaults.Add(!isFailed && !cancelledInQueue);
                notes.Add(cancelledInQueue
                    ? "   (اتلغت من طابور الطابعة)"
                    : isFailed ? "   (حصل خطأ أثناءها — ممكن تكون طبعت جزء)" : "");
            }

            List<PrintJobItem> accepted = result.Printed
                .Where(j => !candidates.Contains(j))
                .ToList();

            if (candidates.Count > 0)
            {
                var items = candidates
                    .Select((job, index) => new PrintOutcomeForm.Item(
                        $"{job.FileName}  —  {job.Pricing.PhysicalSheets} ورقة  —  {job.Pricing.Total:F2} ج.م" + notes[index],
                        defaults[index],
                        job.Pricing.Total))
                    .ToList();

                var headlineParts = new List<string>();
                if (spoolCancelledCount > 0)
                    headlineParts.Add("فيه ملفات اتلغت من طابور الطباعة قبل ما تخلص.");
                if (result.Error != null)
                    headlineParts.Add("حصل خطأ أثناء الطباعة: " + result.Error.Message);
                else if (result.Cancelled)
                    headlineParts.Add("تم إيقاف الطباعة قبل ما تخلص كل الملفات.");
                else if (batch.UserCancelled)
                    headlineParts.Add("أنت ألغيت الطباعة يدويًا.");

                using var outcome = new PrintOutcomeForm(string.Join("\n", headlineParts), items);
                outcome.ShowDialog(this);

                // الملفات اللي المستخدم قال إنها «تمت صح»: نسأله كام وجه طلع مظبوط منها،
                // لأن مش كل الأوراق اللي الكمبيوتر بعتها بتتطبع لما العملية تتلغي.
                var okCandidates = new List<PrintJobItem>();
                for (int c = 0; c < candidates.Count; c++)
                    if (outcome.AcceptedFlags[c])
                        okCandidates.Add(candidates[c]);

                int countedFromCandidates = 0;
                if (okCandidates.Count > 0)
                {
                    using var pagesDialog = new PrintPagesForm(
                        okCandidates
                            .Select(j => new PrintPagesForm.Item(j.FileName, j.Pricing.Faces, j.Pricing.Total))
                            .ToList());
                    pagesDialog.ShowDialog(this);

                    for (int k = 0; k < okCandidates.Count; k++)
                    {
                        PrintJobItem job = okCandidates[k];
                        int okFaces = Math.Min(job.Pricing.Faces, Math.Max(0, pagesDialog.PrintedFaces[k]));
                        if (okFaces <= 0)
                            continue;

                        if (okFaces < job.Pricing.Faces)
                        {
                            job.Pricing = PartialPricing(job.Pricing, okFaces);
                            job.PrintedFaces = okFaces;
                        }

                        accepted.Add(job);
                        countedFromCandidates++;
                    }
                }

                int rejected = candidates.Count - countedFromCandidates;
                if (rejected > 0)
                    AppLog.Info($"طباعة لم تُحسب (ورق باظ/لم تتم صح): {rejected} ملف — المستخدم {CurrentUserName}");

                // نحافظ على ترتيب الطباعة الأصلي في السجل.
                accepted = allSent.Where(j => accepted.Contains(j)).ToList();
            }

            foreach (PrintJobItem printedJob in accepted)
                LogPrintedJob(printedJob, batch);

            if (result.Error != null)
            {
                MessageBox.Show(
                    this,
                    $"حدث خطأ أثناء الطباعة:\n\n{result.Error.Message}\n\n" +
                    $"تم احتساب {accepted.Count} ملف من {total}.",
                    "خطأ في الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (result.Printed.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "تم إيقاف الطباعة قبل إرسال أي ملف.",
                    "الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (accepted.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "تم تسجيل الطباعة على إنها لم تتم بشكل صحيح، فلم يتم احتساب أي مبلغ.",
                    "الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            int donePages = accepted.Sum(j => j.Pricing.Faces);
            int donePapers = accepted.Sum(j => j.Pricing.PhysicalSheets);
            decimal donePrice = accepted.Sum(j => j.Pricing.Total);

            // نافذة النجاح الحديثة (Enter = حسنًا، مسطرة = إضافة شيء آخر)
            var doneRows = new List<(string Label, string Value)>
            {
                ("الطابعة", printerName),
                ("عدد الملفات", accepted.Count.ToString()),
                ("عدد الأوجه المطبوعة", donePages.ToString()),
                ("عدد الأوراق", donePapers.ToString()),
                ("السعر", $"{donePrice:F2} ج.م")
            };

            if (skippedFilesCount > 0)
                doneRows.Add(("ملفات تم تخطيها", $"{skippedFilesCount} (الملف غير موجود)"));

            if (result.Cancelled)
                doneRows.Add(("ملفات لم تُطبع (تم الإيقاف)", (total - result.Printed.Count).ToString()));

            int partialJobs = accepted.Count(j => j.PrintedFaces.HasValue);
            if (partialJobs > 0)
                doneRows.Add(("ملفات اتحسبت جزئيًا (أوجه مظبوطة بس)", partialJobs.ToString()));

            if (result.Printed.Count > accepted.Count)
                doneRows.Add(("ملفات لم تُحسب (الطباعة لم تتم صح)", (result.Printed.Count - accepted.Count).ToString()));

            using (var done = new OperationDoneForm(
                "تمت الطباعة",
                result.Cancelled ? "⚠️ تم إيقاف الطباعة — الملفات التالية تمت طباعتها فقط" : "✅ تمت الطباعة بنجاح",
                doneRows,
                donePrice,
                donePrice,
                null,
                allowBaseDebt: true,
                allowDiscount: true))
            {
                done.ShowDialog(this);

                try
                {
                    OperationDoneForm.ApplyResult(CurrentUserName, "طباعة", done, batch.LogIds);
                }
                catch (Exception extraEx)
                {
                    MessageBox.Show(
                        "تمت الطباعة، لكن تعذر تسجيل الدين/الشيء الإضافي:\n\n" + extraEx.Message,
                        "تنبيه",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private void ResetPrintScreenAfterSuccessfulPrintAll()
        {
            // نفكّر في القائمة أولًا حتى لا يمكن إعادة إرسال نفس الملفات بالخطأ.
            try
            {
                DisposeAllThumbnails();
                gridFiles.ClearSelection();
                gridFiles.Rows.Clear();
                previewSheetIndex = 0;
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر تفريغ قائمة الطباعة بعد إتمام طباعة الكل", ex);
            }

            bool previousGlobalUpdate = isGlobalUpdating;
            isGlobalUpdating = true;
            try
            {
                // الفئة الافتراضية هي «عادي»؛ أعد تحميل أسعارها المحفوظة أيضًا.
                int normalPresetIndex = cmbPricingPreset.Items.IndexOf("عادي");
                if (normalPresetIndex >= 0)
                    cmbPricingPreset.SelectedIndex = normalPresetIndex;
                ApplyPricingPreset();

                if (cmbGlobalColor.Items.Count > 0)
                    cmbGlobalColor.SelectedIndex = 0; // أسود
                if (cmbGlobalDuplex.Items.Count > 0)
                    cmbGlobalDuplex.SelectedIndex = 0; // وش واحد
                numGlobalCopies.Value = Math.Max(numGlobalCopies.Minimum, 1m);
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر إعادة بعض اختيارات الطباعة إلى الوضع الافتراضي", ex);
            }
            finally
            {
                isGlobalUpdating = previousGlobalUpdate;
            }

            try
            {
                // أي خيارات مخصصة محفوظة من ملف سابق (2/4 صفحات، نطاق صفحات، اتجاه، ورق...)
                // لا تنتقل للملفات الجديدة بعد طباعة الكل.
                AppOptions.ResetDefaultPrintOptions();
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر إعادة خيارات الطباعة المحفوظة إلى الوضع الافتراضي", ex);
                MessageBox.Show(this,
                    "تمت الطباعة وتفريغ القائمة، لكن تعذر حفظ إعادة تعيين خيارات الطباعة الافتراضية:\n" + ex.Message,
                    "تنبيه",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            try
            {
                RecalculateTotals();
                UpdateRightPreview();
                if (gridFiles.CanFocus)
                    gridFiles.Focus();
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر تحديث شاشة الطباعة بعد إعادة التعيين", ex);
            }
        }

        /// <summary>
        /// بتتنفذ على خيط خلفي: بتطبع الملفات واحد واحد بالترتيب.
        /// مفيهاش أي لمس لعناصر الواجهة.
        /// </summary>
        private PrintBatchResult RunPrintBatch(
            List<PrintJobItem> jobs,
            string printerName,
            IProgress<(int Index, string Name)> progress,
            CancellationToken token,
            SpoolWatcher spoolWatcher)
        {
            var result = new PrintBatchResult();
            PrintJobItem? currentJob = null;
            var queueByColor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                for (int i = 0; i < jobs.Count; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        break;
                    }

                    PrintJobItem job = jobs[i];
                    currentJob = job;
                    progress.Report((i, job.FileName));

                    // مهم: كل ملف يستخدم Queue مستقلة حسب اللون.
                    // لا نعتمد على DefaultPageSettings.Color لأن تعريف KX
                    // الخاص بـ UTAX قد يحتفظ بوضع اللون من الـ Queue نفسها.
                    // الحل بيتعمل مرة واحدة لكل لون في الدفعة (تعداد الطابعات المثبتة بطيء نسبيًا).
                    string colorKey = job.ColorType ?? "";
                    if (!queueByColor.TryGetValue(colorKey, out string? jobPrinterName))
                    {
                        jobPrinterName = ResolvePrinterForColor(printerName, job.ColorType ?? "");
                        queueByColor[colorKey] = jobPrinterName;
                    }

                    spoolWatcher.Register(job.SpoolTag, jobPrinterName);
                    try
                    {
                        PrintSingleFile(
                            job.FilePath,
                            job.Copies,
                            job.ColorType,
                            job.DuplexMode,
                            jobPrinterName,
                            job.Options,
                            job.SpoolTag);
                    }
                    finally
                    {
                        spoolWatcher.MarkSent(job.SpoolTag);
                    }

                    result.Printed.Add(job);
                    currentJob = null;
                }

                progress.Report((jobs.Count, ""));
            }
            catch (Exception ex)
            {
                result.Error = ex;
                result.FailedJob = currentJob;
            }

            return result;
        }

        /// <summary>تسعير جزئي: الأوجه اللي طلعت مظبوطة بس (السعر بالتناسب مع عدد الأوجه).</summary>
        private static RowPricing PartialPricing(RowPricing p, int okFaces)
        {
            if (p.Faces <= 0 || okFaces >= p.Faces)
                return p;

            double ratio = (double)okFaces / p.Faces;
            return new RowPricing
            {
                Faces = okFaces,
                PhysicalSheets = Math.Max(1, (int)Math.Ceiling(p.PhysicalSheets * ratio)),
                BillableSides = Math.Max(1, (int)Math.Ceiling(p.BillableSides * ratio)),
                PricePerSide = p.PricePerSide,
                Total = Math.Round(p.Total * okFaces / p.Faces, 2)
            };
        }

        /// <summary>تسجيل ملف مطبوع في السجل (على خيط الواجهة، بعد انتهاء الإرسال).</summary>
        private void LogPrintedJob(PrintJobItem job, PrintBatch batch)
        {
            try
            {
                long loggedPrintId = PrintLogger.Log(
                    new PrintLogger.LogEntry
                    {
                        Timestamp = DateTime.Now,
                        UserName = CurrentUserName,
                        FileName = job.FileName,
                        FileType = job.FileType,
                        // لو الطباعة اتلغت: بنسجل الأوجه اللي طلعت فعلًا (نسخة واحدة بعدد الأوجه المظبوطة).
                        Pages = job.PrintedFaces ?? job.SelectedPages,
                        Copies = job.PrintedFaces.HasValue ? 1 : job.Copies,
                        ColorType = job.ColorType,
                        DuplexMode = job.DuplexMode,
                        PhysicalSheets = job.Pricing.PhysicalSheets,
                        Price = job.Pricing.Total,
                        PricingCategory = job.PricingCategory,
                        PrintOptionsJson = System.Text.Json.JsonSerializer.Serialize(job.Options)
                    });

                if (loggedPrintId > 0)
                    batch.LogIds.Add(loggedPrintId);
            }
            catch (Exception ex)
            {
                // الطباعة تمت بالفعل؛ لا نعيد إرسالها للطابعة تلقائيًا. نوضح للمستخدم أن التسجيل المالي لم يكتمل.
                AppLog.Error("تمت الطباعة لكن تعذر استكمال تسجيل العملية", ex);
                if (!batch.LogFailureWarningShown)
                {
                    batch.LogFailureWarningShown = true;
                    MessageBox.Show(this,
                        ex.Message + "\n\nلا تعِد الطباعة لمجرد ظهور هذه الرسالة؛ راجع قائمة العمليات المعلقة وسجل الخزينة أولًا.",
                        "تنبيه: الطباعة تمت والتسجيل يحتاج مراجعة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>يشغّل عمل على خيط STA مستقل ويرجّع Task (بدون حجز الواجهة).</summary>
        private static Task<T> RunOnStaThread<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                try
                {
                    tcs.SetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "LibraryPrintApp.Worker";
            thread.Start();

            return tcs.Task;
        }

        private void BuildPrintStatusPanel()
        {
            printStatusPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = Color.FromArgb(255, 248, 225),
                Padding = new Padding(10, 6, 10, 6),
                Visible = false
            };

            lblPrintStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 80, 0),
                AutoEllipsis = true,
                Text = ""
            };

            printProgressBar = new ProgressBar
            {
                Dock = DockStyle.Right,
                Width = 220,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };

            btnCancelPrint = new Button
            {
                Dock = DockStyle.Right,
                Width = 200,
                Text = "❌ إلغاء الطباعة وحذف المهام",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(192, 57, 43),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCancelPrint.FlatAppearance.BorderSize = 0;
            btnCancelPrint.Click += async (s, e) => await CancelCurrentPrintAsync();

            printStatusPanel.Controls.Add(lblPrintStatus);
            printStatusPanel.Controls.Add(printProgressBar);
            printStatusPanel.Controls.Add(btnCancelPrint);
        }

        /// <summary>إلغاء مباشر: يوقف إرسال الملفات التالية ويحذف مهام البرنامج المعلقة فقط من طابور ويندوز.</summary>
        private async Task CancelCurrentPrintAsync()
        {
            var batches = activeBatches.Where(b => !b.UserCancelled).ToList();
            if (batches.Count == 0 || printCancelInProgress) return;

            int pendingFiles = batches.Sum(b => b.Watcher.PendingCount);
            if (batches.Count > 1 &&
                MessageBox.Show(this,
                    $"فيه {batches.Count} عمليات طباعة شغالة ({pendingFiles} ملف في الطابور).\n\n" +
                    "الإلغاء هيحذف كل المهام المعلقة بتاعة البرنامج لكل العملاء. متأكد؟",
                    "إلغاء الطباعة", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            printCancelInProgress = true;
            foreach (var b in batches)
            {
                b.UserCancelled = true;
                try { b.SendCts.Cancel(); } catch { }
            }

            if (btnCancelPrint != null)
            {
                btnCancelPrint.Enabled = false;
                btnCancelPrint.Text = "جاري حذف مهام الطباعة…";
            }
            if (lblPrintStatus != null)
                lblPrintStatus.Text = "جاري إلغاء الملفات المعلقة من طابور ويندوز…";

            // نعيد المحاولة لفترة قصيرة لأن بعض تعريفات الطابعات تُظهر المهمة متأخرة.
            // لا نلمس مهام الطباعة التي لا تحمل العلامة الفريدة الخاصة بالبرنامج.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                foreach (var b in batches)
                {
                    try { b.Watcher.CancelPendingJobs(); }
                    catch (Exception ex) { AppLog.Error("تعذر حذف بعض مهام الطباعة من طابور ويندوز", ex); }
                }

                if (batches.All(b => b.Watcher.AllDone)) break;
                await Task.Delay(200);
            }

            // ننهي الانتظار تلقائيًا بعد طلب الحذف، ثم تعرض شاشة النتيجة الملفات
            // التي يُحتمل أنها طبعت وتسأل عن الأوجه السليمة.
            foreach (var b in batches)
            {
                try { b.WaitCts.Cancel(); } catch { }
            }

            printCancelInProgress = false;
            RefreshPrintStatusPanel();
        }

        private void SetPrintingState(bool sending, int total = 0)
        {
            // sending = مرحلة إرسال الملفات للـSpooler بس. بعدها الأزرار بترجع تشتغل
            // حتى لو الملفات لسه بتتطبع في الماكينة.
            isPrintingInBackground = sending;

            if (btnPrintAll != null)
                btnPrintAll.Enabled = !sending;

            if (btnPrintSelected != null)
                btnPrintSelected.Enabled = !sending;

            if (sending && lblPrintStatus != null)
                lblPrintStatus.Text = $"جاري إرسال الملفات للطابعة… (0 من {total})";

            RefreshPrintStatusPanel();
        }

        /// <summary>يحدّث شريط الحالة: ظاهر طول ما فيه إرسال شغال أو ملفات لسه في طابور الطابعة.</summary>
        private void RefreshPrintStatusPanel()
        {
            bool anyBatch = activeBatches.Count > 0;
            AppSession.PrintingInProgress = isPrintingInBackground || anyBatch;

            if (printStatusPanel == null)
                return;

            printStatusPanel.Visible = isPrintingInBackground || anyBatch;

            if (btnCancelPrint != null && !printCancelInProgress)
            {
                btnCancelPrint.Enabled = isPrintingInBackground || anyBatch;
                btnCancelPrint.Text = "❌ إلغاء الطباعة وحذف المهام";
            }

            if (!isPrintingInBackground && anyBatch && lblPrintStatus != null && !printCancelInProgress)
            {
                int pending = activeBatches.Where(b => b.Sent).Sum(b => b.Watcher.PendingCount);
                lblPrintStatus.Text = pending > 0
                    ? $"تم الإرسال للطابعة — {pending} ملف في الطابور. كمّل شغلك وابعت العميل اللي بعده."
                    : "الطابعة خلصت — جاري تسجيل النتيجة…";
            }
        }

        private void UpdatePrintProgress(int index, int total, string fileName)
        {
            if (lblPrintStatus == null || !isPrintingInBackground)
                return;

            lblPrintStatus.Text = index >= total
                ? $"تم إرسال {total} من {total} — في انتظار الطابعة…"
                : $"جاري الإرسال: ملف {index + 1} من {total} — {fileName}";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isPrintingInBackground || activeBatches.Count > 0)
            {
                e.Cancel = true;

                // Esc / X / Backspace أثناء الطباعة: بدل رسالة المنع نصغّر الشاشة
                // عشان تكمّل شغلك والطباعة شغالة في الخلفية.
                if (e.CloseReason == CloseReason.UserClosing && !AppSession.LogoutRequested)
                {
                    WindowState = FormWindowState.Minimized;
                    return;
                }

                MessageBox.Show(
                    this,
                    "فيه طباعة لسه شغالة. استنى لحد ما تخلص (أو اضغط «إيقاف») قبل ما تقفل الشاشة.",
                    "الطباعة",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            base.OnFormClosing(e);
        }

        // ============================================================
        // اختيار Queue الطباعة حسب لون الملف
        // ============================================================
        private string ResolvePrinterForColor(
            string selectedPrinterName,
            string colorType)
        {
            bool wantColor =
                string.Equals(
                    colorType?.Trim(),
                    "ألوان",
                    StringComparison.OrdinalIgnoreCase);

            // أسماء الـQueues التي أنشأناها:
            // UTAX TA 5006ci BLACK
            // UTAX TA 5006ci COLOR
            //
            // البحث يتم داخل الطابعات المثبتة، لذلك لا نحتاج
            // تغيير اسم الطابعة في الكود كل مرة.
            string[] installed =
                PrinterSettings.InstalledPrinters
                    .Cast<string>()
                    .ToArray();

            string selected =
                selectedPrinterName?.Trim() ?? "";

            string colorPrinter =
                installed.FirstOrDefault(
                    p =>
                        p.IndexOf(
                            "COLOR",
                            StringComparison.OrdinalIgnoreCase) >= 0 &&
                        IsSamePrinterFamily(
                            selected,
                            p))
                ?? installed.FirstOrDefault(
                    p =>
                        p.IndexOf(
                            "COLOUR",
                            StringComparison.OrdinalIgnoreCase) >= 0 &&
                        IsSamePrinterFamily(
                            selected,
                            p))
                ?? "";

            string blackPrinter =
                installed.FirstOrDefault(
                    p =>
                        p.IndexOf(
                            "BLACK",
                            StringComparison.OrdinalIgnoreCase) >= 0 &&
                        IsSamePrinterFamily(
                            selected,
                            p))
                ?? "";

            // لو المستخدم اختار Queue COLOR بالفعل، استخدمها مباشرة
            // عند طلب الألوان.
            if (wantColor &&
                selected.IndexOf(
                    "COLOR",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return selected;
            }

            if (!wantColor &&
                selected.IndexOf(
                    "BLACK",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return selected;
            }

            string resolved =
                wantColor
                    ? colorPrinter
                    : blackPrinter;

            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }

            // لا نخاطر بإرسال الملف إلى Queue غير موجودة.
            throw new InvalidOperationException(
                wantColor
                    ? "لم يتم العثور على طابعة الألوان.\\n" +
                      "تأكد من وجود Queue باسم يحتوي على COLOR " +
                      "لنفس UTAX TA 5006ci."
                    : "لم يتم العثور على طابعة الأسود.\\n" +
                      "تأكد من وجود Queue باسم يحتوي على BLACK " +
                      "لنفس UTAX TA 5006ci.");
        }

        private static bool IsSamePrinterFamily(
            string first,
            string second)
        {
            if (string.IsNullOrWhiteSpace(first) ||
                string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            string Normalize(string value)
            {
                string s =
                    value.Trim().ToUpperInvariant();

                s = s.Replace("COLOR", "");
                s = s.Replace("COLOUR", "");
                s = s.Replace("BLACK", "");
                s = s.Replace("أحمر", "");
                s = s.Replace("أسود", "");

                s = s.Replace("-", " ");
                s = s.Replace("_", " ");

                while (s.Contains("  "))
                    s = s.Replace("  ", " ");

                return s.Trim();
            }

            string a = Normalize(first);
            string b = Normalize(second);

            // في حالة تسمية الـQueues بشكل مختلف قليلًا،
            // نسمح أيضًا بمطابقة اسم الجهاز الأساسي.
            return a == b ||
                   a.Contains(b) ||
                   b.Contains(a);
        }

        private static void ConfigurePdfPrintDocument(
            PrintDocument printDocument,
            PdfiumViewerPdfDocument pdf,
            List<int> pageIndexes,
            PrintOptions options,
            bool autoOrientation)
        {
            int groupIndex = 0;
            int nUp = Math.Max(1, options.PagesPerSheet);

            printDocument.BeginPrint += (sender, e) => groupIndex = 0;
            printDocument.QueryPageSettings += (sender, e) =>
            {
                if (!autoOrientation)
                    return;

                int firstPageInSheet = groupIndex * nUp;
                if (firstPageInSheet < 0 || firstPageInSheet >= pageIndexes.Count)
                    return;

                SizeF pageSize = pdf.PageSizes[pageIndexes[firstPageInSheet]];
                // Let every sheet follow the orientation of its first page. This also
                // handles PDFs containing both portrait and landscape pages.
                e.PageSettings.Landscape = pageSize.Width > pageSize.Height;
            };

            printDocument.PrintPage += (sender, e) =>
            {
                // PrintableArea/PageBounds are expressed in hundredths of an inch,
                // while PdfiumViewer.Render sends its bounds directly to the printer
                // device context (device pixels). Convert before computing the PDF slots;
                // otherwise only a tiny fraction of the physical sheet is used.
                Rectangle printableDisplay = GetPrintableDisplayRectangle(e, options);
                if (printableDisplay.Width <= 0 || printableDisplay.Height <= 0)
                {
                    printableDisplay = new Rectangle(
                        0,
                        0,
                        Math.Max(1, e.PageBounds.Width),
                        Math.Max(1, e.PageBounds.Height));
                }

                Graphics pageGraphics = e.Graphics ?? throw new InvalidOperationException("تعذر الحصول على سطح الرسم الخاص بالطابعة.");
                Rectangle printable = DisplayRectangleToDevicePixels(pageGraphics, printableDisplay);
                int gapX = (int)Math.Round(options.NUpGapInches * pageGraphics.DpiX);
                int gapY = (int)Math.Round(options.NUpGapInches * pageGraphics.DpiY);
                Rectangle[] slots = BuildNUpSlots(printable, nUp, gapX, gapY);

                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    int listIndex = groupIndex * nUp + slotIndex;
                    if (listIndex >= pageIndexes.Count)
                        break;

                    int pdfPageIndex = pageIndexes[listIndex];
                    RenderPdfPageIntoSlot(
                        pdf,
                        pdfPageIndex,
                        e.Graphics,
                        slots[slotIndex],
                        options,
                        e);
                }

                groupIndex++;
                int totalPrinterFaces =
                    (int)Math.Ceiling(pageIndexes.Count / (double)nUp);
                e.HasMorePages = groupIndex < totalPrinterFaces;
            };
        }

        // ============================================================
        // اختيار طريقة الطباعة
        // ============================================================
        private void PrintSingleFile(
            string filePath,
            int copies,
            string colorType,
            string duplexMode,
            string printerName,
            PrintOptions? options = null,
            string? spoolTag = null)
        {
            PrintOptions effectiveOptions = options?.Clone() ?? new PrintOptions();

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".pdf")
            {
                PrintPdfFile(filePath, copies, colorType, duplexMode, printerName, effectiveOptions, spoolTag);
            }
            else
            {
                PrintImageFile(filePath, copies, colorType, duplexMode, printerName, effectiveOptions, spoolTag);
            }
        }

        private void ApplyPrintSettings(
            PrintDocument printDocument,
            string colorType,
            string duplexMode,
            string printerName,
            PrintOptions? options = null)
        {
            printDocument.PrinterSettings = new PrinterSettings
            {
                PrinterName = printerName,
                Copies = 1
            };

            if (!printDocument.PrinterSettings.IsValid)
                throw new InvalidOperationException($"الطابعة غير متاحة: {printerName}");

            printDocument.DefaultPageSettings.Color = colorType == "ألوان";

            if (duplexMode == "وش وظهر")
            {
                if (!printDocument.PrinterSettings.CanDuplex)
                    throw new InvalidOperationException($"الطابعة \"{printerName}\" لا تدعم الطباعة وش وظهر.");

                // Long-edge binding is the default. For landscape sheets, the driver's
                // duplex axis must be switched so the back side opens from the side rather
                // than appearing upside down. The user can explicitly choose top-edge binding.
                bool landscapeBinding = options?.Orientation == PrintPageOrientation.Landscape;
                bool shortEdge = options?.DuplexBinding == DuplexBindingEdge.ShortEdge;
                bool useHorizontalDuplex = landscapeBinding ? !shortEdge : shortEdge;
                printDocument.PrinterSettings.Duplex = useHorizontalDuplex
                    ? Duplex.Horizontal
                    : Duplex.Vertical;
            }
            else
            {
                printDocument.PrinterSettings.Duplex = Duplex.Simplex;
            }

            ApplyPaperAndOrientation(printDocument, options);
            printDocument.PrintController = new StandardPrintController();
        }

        private void ApplyPaperAndOrientation(PrintDocument printDocument, PrintOptions? options)
        {
            if (options == null)
                return;

            PrinterSettings ps = printDocument.PrinterSettings;

            if (!string.IsNullOrWhiteSpace(options.PaperName) && options.PaperName != "افتراضي")
            {
                PaperSize? paper = ps.PaperSizes
                    .Cast<PaperSize>()
                    .FirstOrDefault(p => string.Equals(p.PaperName, options.PaperName, StringComparison.OrdinalIgnoreCase));

                if (paper != null)
                {
                    try { printDocument.DefaultPageSettings.PaperSize = paper; } catch { }
                }
            }

            switch (options.Orientation)
            {
                case PrintPageOrientation.Portrait:
                    printDocument.DefaultPageSettings.Landscape = false;
                    break;
                case PrintPageOrientation.Landscape:
                    printDocument.DefaultPageSettings.Landscape = true;
                    break;
            }

            // نترك Margins = صفر لأننا نحسب حدود الطباعة الحقيقية من HardMargin/PrintableArea.
            printDocument.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        }

        private PrintDocument CreatePrinterDocument(
            string printerName,
            string colorType,
            string duplexMode,
            PrintOptions? options = null)
        {
            PrintDocument pd = new PrintDocument();
            pd.DocumentName = "مكتبة الشريف";
            ApplyPrintSettings(pd, colorType, duplexMode, printerName, options);
            return pd;
        }

        private static void ValidatePrinterCopyCount(PrinterSettings settings, int copies)
        {
            int requestedCopies = Math.Max(1, copies);
            if (requestedCopies > short.MaxValue)
                throw new InvalidOperationException("عدد النسخ أكبر من الحد الذي تدعمه واجهة الطباعة في Windows (32767 نسخة).");

            // MaximumCopies هي القدرة التي يعلنها تعريف الطابعة؛ لو العدد أعلى منها نرفض الطلب
            // بدل أن تطبع الطابعة عددًا أقل بصمت أو نرسل 50 مهمة منفصلة.
            int maximumCopies = settings.MaximumCopies;
            if (requestedCopies > 1 && maximumCopies <= 1)
                throw new InvalidOperationException(
                    $"تعريف الطابعة «{settings.PrinterName}» لا يعلن دعم النسخ المتعددة في مهمة واحدة.");

            if (maximumCopies > 0 && requestedCopies > maximumCopies)
                throw new InvalidOperationException(
                    $"تعريف الطابعة «{settings.PrinterName}» يدعم حتى {maximumCopies} نسخة في المهمة الواحدة، بينما المطلوب {requestedCopies}.");
        }

        private static void ApplyPrinterCopyCount(PrintDocument printDocument, int copies)
        {
            ValidatePrinterCopyCount(printDocument.PrinterSettings, copies);
            printDocument.PrinterSettings.Copies = (short)Math.Max(1, copies);
            printDocument.PrinterSettings.Collate = true;
        }

        private void PrintImageFile(
            string filePath,
            int copies,
            string colorType,
            string duplexMode,
            string printerName,
            PrintOptions options,
            string? spoolTag = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("ملف الصورة غير موجود.", filePath);

            PrintOptions effectiveOptions = options.Clone();
            if (effectiveOptions.Orientation == PrintPageOrientation.Auto)
            {
                using Image probe = Image.FromFile(filePath);
                effectiveOptions.Orientation = probe.Width > probe.Height
                    ? PrintPageOrientation.Landscape
                    : PrintPageOrientation.Portrait;
            }

            using PrintDocument pd = CreatePrinterDocument(
                printerName,
                colorType,
                duplexMode,
                effectiveOptions);
            ApplyPrinterCopyCount(pd, copies);
            if (!string.IsNullOrEmpty(spoolTag))
                pd.DocumentName = Path.GetFileName(filePath) + " " + spoolTag;

            // Enforce the chosen orientation at the last point before each sheet is printed.
            // Some printer drivers silently revert DefaultPageSettings after Print() starts.
            pd.QueryPageSettings += (sender, e) =>
            {
                e.PageSettings.Landscape = effectiveOptions.Orientation == PrintPageOrientation.Landscape;
            };

            pd.PrintPage += (sender, e) =>
            {
                DrawImageOnPrintPage(filePath, e, effectiveOptions);
                e.HasMorePages = false;
            };

            // عدد النسخ يُرسل كإعداد للطابعة في مهمة واحدة، ولا نعيد Print() لكل نسخة.
            pd.Print();
        }

        private void DrawImageOnPrintPage(
            string filePath,
            PrintPageEventArgs e,
            PrintOptions options)
        {
            using Image img = Image.FromFile(filePath);

            Graphics graphics = e.Graphics ?? throw new InvalidOperationException("تعذر الحصول على سطح الرسم الخاص بالطابعة.");
            GraphicsState state = graphics.Save();
            try
            {
                // Use device pixels explicitly, just like the PDF renderer. This avoids
                // relying on GraphicsUnit.Display, which can be interpreted differently
                // by some printer drivers and can make output smaller than the preview.
                Rectangle printableDisplay = GetPrintableDisplayRectangle(e, options);
                graphics.PageUnit = GraphicsUnit.Pixel;
                graphics.PageScale = 1f;

                Rectangle printable = DisplayRectangleToDevicePixels(graphics, printableDisplay);
                if (printable.Width <= 0 || printable.Height <= 0)
                {
                    Rectangle pageDisplay = new Rectangle(
                        0, 0,
                        Math.Max(1, e.PageSettings.Bounds.Width),
                        Math.Max(1, e.PageSettings.Bounds.Height));
                    printable = DisplayRectangleToDevicePixels(graphics, pageDisplay);
                }

                int nUp = Math.Max(1, options.PagesPerSheet);
                int gapX = (int)Math.Round(options.NUpGapInches * graphics.DpiX);
                int gapY = (int)Math.Round(options.NUpGapInches * graphics.DpiY);
                Rectangle[] slots = BuildNUpSlots(printable, nUp, gapX, gapY);
                Rectangle slot = slots.Length > 0 ? slots[0] : printable;
                DrawImageFit(graphics, img, slot, options);
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private void PrintPdfFile(
            string filePath,
            int copies,
            string colorType,
            string duplexMode,
            string printerName,
            PrintOptions options,
            string? spoolTag = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("ملف PDF غير موجود.", filePath);

            using PdfiumViewerPdfDocument pdf = PdfiumViewerPdfDocument.Load(filePath);
            if (pdf.PageCount <= 0)
                throw new InvalidOperationException("ملف PDF لا يحتوي على صفحات.");

            List<int> pageIndexes = BuildPageIndexList(options, pdf.PageCount);
            if (pageIndexes.Count == 0)
                throw new InvalidOperationException("نطاق الصفحات المحدد لا يحتوي على صفحات للطباعة.");

            bool autoOrientation = options.Orientation == PrintPageOrientation.Auto;
            PrintOptions effectiveOptions = options.Clone();
            if (effectiveOptions.Orientation == PrintPageOrientation.Auto)
            {
                SizeF firstPageSize = pdf.PageSizes[pageIndexes[0]];
                effectiveOptions.Orientation = firstPageSize.Width > firstPageSize.Height
                    ? PrintPageOrientation.Landscape
                    : PrintPageOrientation.Portrait;
            }

            using PrintDocument printDocument = new PrintDocument();
            ApplyPrintSettings(printDocument, colorType, duplexMode, printerName, effectiveOptions);
            ApplyPrinterCopyCount(printDocument, copies);
            printDocument.DocumentName = Path.GetFileName(filePath) +
                (string.IsNullOrEmpty(spoolTag) ? "" : " " + spoolTag);

            ConfigurePdfPrintDocument(
                printDocument,
                pdf,
                pageIndexes,
                effectiveOptions,
                autoOrientation);

            // Copies/Collate يذهبان لتعريف الطابعة، وPrint() تُستدعى مرة واحدة للملف كله.
            printDocument.Print();
        }

        private static Rectangle GetPrintableDisplayRectangle(PrintPageEventArgs e, PrintOptions options)
        {
            // PrintableArea/PageBounds are measured in hundredths of an inch.
            // Callers convert this normalized rectangle to device pixels before drawing.
            RectangleF area = e.PageSettings.PrintableArea;
            Rectangle orientedPageBounds = e.PageSettings.Bounds;

            // A few printer drivers return PrintableArea in portrait coordinates even
            // after Landscape=true. The preview already handles oriented paper bounds;
            // normalize the real print rectangle the same way, or images will be fitted
            // into a portrait-sized rectangle on a landscape sheet.
            bool pageIsLandscape = orientedPageBounds.Width > orientedPageBounds.Height;
            bool areaIsLandscape = area.Width > area.Height;
            if (area.Width > 0 && area.Height > 0 && pageIsLandscape != areaIsLandscape)
            {
                area = new RectangleF(area.Y, area.X, area.Height, area.Width);
            }

            if (area.Width <= 0 || area.Height <= 0)
            {
                float left = Math.Max(0f, e.PageSettings.HardMarginX);
                float top = Math.Max(0f, e.PageSettings.HardMarginY);
                float width = Math.Max(1f, orientedPageBounds.Width - left * 2f);
                float height = Math.Max(1f, orientedPageBounds.Height - top * 2f);
                area = new RectangleF(left, top, width, height);
            }

            float leftInset = MmToHundredthsInch(options.MarginLeftMm);
            float topInset = MmToHundredthsInch(options.MarginTopMm);
            float rightInset = MmToHundredthsInch(options.MarginRightMm);
            float bottomInset = MmToHundredthsInch(options.MarginBottomMm);

            RectangleF adjusted = new RectangleF(
                area.X + leftInset,
                area.Y + topInset,
                Math.Max(1f, area.Width - leftInset - rightInset),
                Math.Max(1f, area.Height - topInset - bottomInset));

            // ارجع نفس وحدة الرسم التي يستخدمها PrintPageEventArgs.Graphics.
            return Rectangle.Round(adjusted);
        }

        private static Rectangle DisplayRectangleToDevicePixels(Graphics graphics, Rectangle displayRectangle)
        {
            // PrintPageEventArgs page rectangles use 1/100 inch. PdfiumViewer.Render,
            // however, passes its rectangle directly to SetViewportOrgEx/FPDF_RenderPageToDC,
            // which requires device pixels. Use the printer graphics DPI on each axis.
            double scaleX = Math.Max(1f, graphics.DpiX) / 100.0;
            double scaleY = Math.Max(1f, graphics.DpiY) / 100.0;

            int left = (int)Math.Round(displayRectangle.Left * scaleX);
            int top = (int)Math.Round(displayRectangle.Top * scaleY);
            int right = (int)Math.Round(displayRectangle.Right * scaleX);
            int bottom = (int)Math.Round(displayRectangle.Bottom * scaleY);

            return new Rectangle(
                left,
                top,
                Math.Max(1, right - left),
                Math.Max(1, bottom - top));
        }

        private static float MmToHundredthsInch(float mm)
        {
            return mm / 25.4f * 100f;
        }

        private static Rectangle[] BuildNUpSlots(Rectangle printable, int nUp, int requestedGapX = 0, int requestedGapY = 0)
        {
            int columns;
            int rows;

            if (nUp == 1)
            {
                columns = rows = 1;
            }
            else if (nUp == 2)
            {
                bool landscape = printable.Width >= printable.Height;
                columns = landscape ? 2 : 1;
                rows = landscape ? 1 : 2;
            }
            else if (nUp == 4)
            {
                columns = rows = 2;
            }
            else if (nUp == 6)
            {
                bool landscape = printable.Width >= printable.Height;
                columns = landscape ? 3 : 2;
                rows = landscape ? 2 : 3;
            }
            else if (nUp == 9)
            {
                columns = rows = 3;
            }
            else
            {
                columns = rows = 4;
            }

            int gapX = columns > 1
                ? Math.Clamp(requestedGapX, 0, Math.Max(0, (printable.Width - columns) / (columns - 1)))
                : 0;
            int gapY = rows > 1
                ? Math.Clamp(requestedGapY, 0, Math.Max(0, (printable.Height - rows) / (rows - 1)))
                : 0;
            int cellWidth = Math.Max(1, (printable.Width - gapX * (columns - 1)) / columns);
            int cellHeight = Math.Max(1, (printable.Height - gapY * (rows - 1)) / rows);

            List<Rectangle> result = new List<Rectangle>(nUp);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    result.Add(new Rectangle(
                        printable.Left + c * (cellWidth + gapX),
                        printable.Top + r * (cellHeight + gapY),
                        cellWidth,
                        cellHeight));
                }
            }

            return result.ToArray();
        }

        private static Rectangle FitRectangle(
            int sourceWidth,
            int sourceHeight,
            Rectangle target,
            PrintOptions options)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return target;

            double scale;
            switch (options.Scaling)
            {
                case PrintScalingMode.ActualSize:
                    // نفترض أن الصور الرقمية بوحدة البكسل؛ نطبعها داخل الورقة بأقرب
                    // حجم عملي يحافظ على الأبعاد دون تكبير قسري.
                    scale = 1.0;
                    break;
                case PrintScalingMode.Custom:
                    scale = Math.Max(0.10, options.CustomScalePercent / 100.0);
                    break;
                default:
                    scale = double.PositiveInfinity;
                    break;
            }

            double width = sourceWidth * scale;
            double height = sourceHeight * scale;

            if (options.Scaling == PrintScalingMode.FitToPrintableArea)
            {
                scale = Math.Min(
                    target.Width / (double)sourceWidth,
                    target.Height / (double)sourceHeight);
                width = sourceWidth * scale;
                height = sourceHeight * scale;
            }
            else if (width > target.Width || height > target.Height)
            {
                double downScale = Math.Min(target.Width / width, target.Height / height);
                width *= downScale;
                height *= downScale;
            }

            int w = Math.Max(1, Math.Min(target.Width, (int)Math.Round(width)));
            int h = Math.Max(1, Math.Min(target.Height, (int)Math.Round(height)));

            // نوسّط دائمًا (انظر نفس الشرح في RenderPdfPageIntoPreviewSlot).
            int x = target.Left + (target.Width - w) / 2;
            int y = target.Top + (target.Height - h) / 2;

            return new Rectangle(x, y, w, h);
        }

        private static void DrawImageFit(
            Graphics graphics,
            Image image,
            Rectangle target,
            PrintOptions options)
        {
            Rectangle destination = FitRectangle(image.Width, image.Height, target, options);

            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;

            graphics.DrawImage(
                image,
                destination,
                0,
                0,
                image.Width,
                image.Height,
                GraphicsUnit.Pixel);
        }

        private static void RenderPdfPageIntoSlot(
            PdfiumViewerPdfDocument pdf,
            int pageIndex,
            Graphics graphics,
            Rectangle target,
            PrintOptions options,
            PrintPageEventArgs e)
        {
            Graphics pageGraphics = e.Graphics ?? throw new InvalidOperationException("تعذر الحصول على سطح الرسم الخاص بالطابعة.");
            SizeF sourceSize = pdf.PageSizes[pageIndex];
            double sourceWidth = Math.Max(1f, sourceSize.Width);
            double sourceHeight = Math.Max(1f, sourceSize.Height);

            double scale;
            switch (options.Scaling)
            {
                case PrintScalingMode.ActualSize:
                    scale = pageGraphics.DpiX / 72.0;
                    break;
                case PrintScalingMode.Custom:
                    scale = (pageGraphics.DpiX / 72.0) * Math.Max(0.10, options.CustomScalePercent / 100.0);
                    break;
                default:
                    scale = Math.Min(
                        target.Width / sourceWidth,
                        target.Height / sourceHeight);
                    break;
            }

            double width = sourceWidth * scale;
            double height = sourceHeight * scale;

            if (options.Scaling != PrintScalingMode.FitToPrintableArea &&
                (width > target.Width || height > target.Height))
            {
                double down = Math.Min(target.Width / width, target.Height / height);
                width *= down;
                height *= down;
            }

            int w = Math.Max(1, Math.Min(target.Width, (int)Math.Round(width)));
            int h = Math.Max(1, Math.Min(target.Height, (int)Math.Round(height)));

            // نوسّط دائمًا (نفس السبب الموضح في RenderPdfPageIntoPreviewSlot) حتى
            // تتطابق الطباعة الفعلية مع المعاينة تمامًا.
            int x = target.Left + (target.Width - w) / 2;
            int y = target.Top + (target.Height - h) / 2;

            lock (PdfiumRenderLock)
            {
                pdf.Render(
                    pageIndex,
                    graphics,
                    graphics.DpiX,
                    graphics.DpiY,
                    new Rectangle(x, y, w, h),
                    PdfiumViewer.PdfRenderFlags.ForPrinting | PdfiumViewer.PdfRenderFlags.Annotations);
            }
        }

        // ============================================================
        // إغلاق البرنامج
        // ============================================================
        protected override void OnFormClosed(
            FormClosedEventArgs e)
        {
            PrintBridge.Detach(this);

            try
            {
                CancelPendingPreview();
                previewDebounceTimer?.Dispose();
                previewDebounceTimer = null;
                editWatchTimer?.Dispose();
                editWatchTimer = null;
                ClearPreviewCache();
                ReleasePreviewPdf();

                DisposeAllThumbnails();

                currentPreviewImage?.Dispose();
                currentPreviewImage = null;

                if (gridFiles != null)
                {
                    foreach (
                        DataGridViewRow row
                        in gridFiles.Rows)
                    {
                        row.Tag = null;
                    }
                }
            }
            catch
            {
            }

            base.OnFormClosed(e);
        }
    }
}
