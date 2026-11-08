using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LibraryPrintApp
{
    // ============================================================
    // شاشة تسجيل الدخول: تظهر أول ما البرنامج يفتح. التحقق من كلمات المرور يتم من ملف الأمان المحفوظ كـ Hash.
    // ============================================================
    public class LoginForm : Form
    {
        public string LoggedInUser { get; private set; } = "";

        private Panel selectPanel = null!;
        private Panel passwordPanel = null!;
        private TextBox txtPassword = null!;
        private Label lblError = null!;
        private string selectedUser = "";

        public LoginForm()
        {
            BuildUi();
        }

        // تأكيد قبل قفل البرنامج من شاشة الدخول (دخول ناجح = DialogResult.OK بدون سؤال).
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK && e.CloseReason == CloseReason.UserClosing)
            {
                var answer = MessageBox.Show(
                    this,
                    "متأكد إنك عايز تقفل البرنامج؟",
                    "تأكيد قفل البرنامج",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            base.OnFormClosing(e);
        }

        private void BuildUi()
        {
            Text = "تسجيل الدخول - مكتبة الشريف";
            Size = new Size(440, 470);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = Color.FromArgb(244, 246, 249);
            Font = new Font("Segoe UI", 10.5F);
            UiKit.EnableEscape(this);

            // ========================================================
            // رأس الشاشة (عنوان + شعار نصي بسيط)
            // ========================================================
            Panel headerPanel =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 110,
                    BackColor = Color.White
                };

            Label lblBrand =
                new Label
                {
                    Text = "مكتبة الشريف",
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(0, 122, 204),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Top,
                    Height = 34,
                    Padding = new Padding(0, 12, 0, 0)
                };

            Label title =
                new Label
                {
                    Text = "من فضلك اختر المستخدم",
                    Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(52, 73, 94),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill
                };

            headerPanel.Controls.Add(title);
            headerPanel.Controls.Add(lblBrand);

            Panel headerSeparator =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 2,
                    BackColor = Color.FromArgb(224, 228, 233)
                };

            // ========================================================
            // شاشة اختيار المستخدم
            // ========================================================
            selectPanel =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(244, 246, 249)
                };

            Button btnOmar =
                MakeUserButton(
                    "عمر",
                    "👤",
                    Color.FromArgb(0, 122, 204));

            btnOmar.Top = 50;
            btnOmar.Click +=
                (s, e) =>
                {
                    selectedUser = "عمر";
                    ShowPasswordStep();
                };

            Button btnAhmed =
                MakeUserButton(
                    "أحمد",
                    "👤",
                    Color.FromArgb(40, 167, 69));

            btnAhmed.Top = 150;
            btnAhmed.Click +=
                (s, e) =>
                {
                    selectedUser = "أحمد";

                    if (AdminSecurity.UserRequiresPassword(selectedUser))
                    {
                        ShowPasswordStep();
                    }
                    else
                    {
                        LoginAs(selectedUser);
                    }
                };

            selectPanel.Controls.Add(btnOmar);
            selectPanel.Controls.Add(btnAhmed);

            // استعادة نسخة احتياطية من شاشة الدخول (مفيدة بعد تحديث/تثبيت البرنامج من جديد).
            // بتطلب كلمة مرور الأدمن لأنها بتستبدل كل البيانات.
            Button btnRestore =
                new Button
                {
                    Text = "♻ استعادة نسخة احتياطية",
                    Width = 300,
                    Height = 38,
                    Top = 245,
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(108, 117, 125),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
            btnRestore.FlatAppearance.BorderColor = Color.FromArgb(200, 205, 211);
            btnRestore.Click += (s, e) => RestoreFromLogin();
            selectPanel.Controls.Add(btnRestore);

            selectPanel.Resize +=
                (s, e) =>
                {
                    CenterHorizontally(btnOmar, selectPanel);
                    CenterHorizontally(btnAhmed, selectPanel);
                    CenterHorizontally(btnRestore, selectPanel);
                };

            // ========================================================
            // شاشة كلمة مرور عمر
            // ========================================================
            passwordPanel =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(244, 246, 249),
                    Visible = false
                };

            Label lblPromptOmar =
                new Label
                {
                    Text = $"كلمة مرور {selectedUser}",
                    Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(52, 73, 94),
                    AutoSize = true,
                    Top = 45
                };

            txtPassword =
                new TextBox
                {
                    Top = 78,
                    Width = 300,
                    Height = 36,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 14F),
                    PasswordChar = '●',
                    TextAlign = HorizontalAlignment.Center
                };

            txtPassword.KeyDown +=
                (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        TryLoginOmar();
                    }
                };

            lblError =
                new Label
                {
                    Text = "",
                    ForeColor = Color.FromArgb(192, 57, 43),
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    AutoSize = true,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Top = 122,
                    Width = 300
                };

            Button btnConfirm =
                new Button
                {
                    Text = "دخول",
                    Top = 156,
                    Width = 145,
                    Height = 44,
                    BackColor = Color.FromArgb(0, 122, 204),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };

            btnConfirm.FlatAppearance.BorderSize = 0;
            btnConfirm.Click += (s, e) => TryLoginOmar();
            ApplyRoundedCorners(btnConfirm, 10);

            Button btnBack =
                new Button
                {
                    Text = "رجوع",
                    Top = 156,
                    Width = 145,
                    Height = 44,
                    BackColor = Color.FromArgb(108, 117, 125),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };

            btnBack.FlatAppearance.BorderSize = 0;
            btnBack.Click +=
                (s, e) =>
                {
                    lblError.Text = "";
                    txtPassword.Text = "";
                    passwordPanel.Visible = false;
                    selectPanel.Visible = true;
                };
            ApplyRoundedCorners(btnBack, 10);

            passwordPanel.Controls.AddRange(
                new Control[]
                {
                    lblPromptOmar,
                    txtPassword,
                    lblError,
                    btnConfirm,
                    btnBack
                });

            passwordPanel.Resize +=
                (s, e) =>
                {
                    CenterHorizontally(lblPromptOmar, passwordPanel);
                    CenterHorizontally(txtPassword, passwordPanel);
                    CenterHorizontally(lblError, passwordPanel);

                    int pairWidth =
                        btnConfirm.Width + 15 + btnBack.Width;

                    int pairLeft =
                        Math.Max(
                            0,
                            (passwordPanel.ClientSize.Width - pairWidth) / 2);

                    btnConfirm.Left = pairLeft;
                    btnBack.Left = pairLeft + btnConfirm.Width + 15;
                };

            Controls.Add(passwordPanel);
            Controls.Add(selectPanel);
            Controls.Add(headerSeparator);
            Controls.Add(headerPanel);

            CenterHorizontally(btnOmar, selectPanel);
            CenterHorizontally(btnAhmed, selectPanel);
            CenterHorizontally(btnRestore, selectPanel);
        }

        private void RestoreFromLogin()
        {
            using var prompt = new PasswordPromptForm("أدخل كلمة مرور الأدمن لاستعادة نسخة احتياطية:");
            if (prompt.ShowDialog(this) != DialogResult.OK) return;
            if (!AdminSecurity.ValidateAdminPassword(prompt.Password))
            {
                MessageBox.Show(this, "كلمة مرور الأدمن غير صحيحة.", "رفض الدخول",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!AdminPasswordRotation.EnsureRotatedIfDefault(this)) return;

            // الاستعادة قد تسترجع ملف security.json قديمًا؛ لذلك نفحص الإعدادات بعد الاستعادة أيضًا.
            BackupRestoreUi.RunRestore(this);
            AdminSecurity.Reload();
            if (!AdminPasswordRotation.EnsureRotatedIfDefault(this)) return;
        }

        private Button MakeUserButton(
            string text,
            string icon,
            Color color)
        {
            Button button =
                new Button
                {
                    Text = $"{icon}   {text}",
                    Width = 300,
                    Height = 70,
                    BackColor = color,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    TextAlign = ContentAlignment.MiddleCenter,
                    UseCompatibleTextRendering = false
                };

            button.FlatAppearance.BorderSize = 0;

            button.FlatAppearance.MouseOverBackColor =
                ControlPaint.Light(color, 0.15f);

            button.FlatAppearance.MouseDownBackColor =
                ControlPaint.Dark(color, 0.05f);

            ApplyRoundedCorners(button, 14);

            return button;
        }

        // ============================================================
        // زوايا دائرية للأزرار (شكل حديث بدل المستطيل الحاد)
        // ============================================================
        private static void ApplyRoundedCorners(
            Control control,
            int radius)
        {
            void SetRegion(object? sender, EventArgs e)
            {
                GraphicsPath path =
                    new GraphicsPath();

                Rectangle bounds =
                    new Rectangle(
                        0,
                        0,
                        control.Width,
                        control.Height);

                int d = radius * 2;

                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();

                control.Region =
                    new Region(path);
            }

            control.SizeChanged += SetRegion;
            SetRegion(control, EventArgs.Empty);
        }

        private static void CenterHorizontally(
            Control control,
            Control parent)
        {
            control.Left =
                Math.Max(
                    0,
                    (parent.ClientSize.Width - control.Width) / 2);
        }

        private void ShowPasswordStep()
        {
            if (string.IsNullOrWhiteSpace(selectedUser))
                selectedUser = "عمر";

            lblError.Text = "";
            txtPassword.Text = "";
            selectPanel.Visible = false;
            passwordPanel.Visible = true;
            txtPassword.Focus();

            // تحديث عنوان كلمة المرور حسب المستخدم الذي تم اختياره.
            foreach (Control control in passwordPanel.Controls)
            {
                if (control is Label label &&
                    label != lblError)
                {
                    label.Text = $"كلمة مرور {selectedUser}";
                    break;
                }
            }
        }

        private void TryLoginOmar()
        {
            if (AdminSecurity.ValidateUserPassword(
                    selectedUser,
                    txtPassword.Text))
            {
                if (!UserPasswordRotation.EnsureRotatedIfDefault(this, selectedUser)) return;
                LoginAs(selectedUser);
            }
            else
            {
                lblError.Text =
                    "كلمة المرور غير صحيحة، حاول مرة أخرى.";

                txtPassword.Text = "";
                txtPassword.Focus();
            }
        }

        private void LoginAs(string user)
        {
            LoggedInUser = user;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
