using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LibraryPrintApp
{
    internal sealed class AppSecurityState
    {
        // 2 = كلمات المرور مخزنة كـ Hash (PBKDF2) بدل النص الصريح.
        public int Version { get; set; } = 2;

        public string AdminHash { get; set; } = "";
        public string OmarHash { get; set; } = "";
        public string AhmedHash { get; set; } = "";

        // خانات قديمة (نص صريح) تُقرأ مرة واحدة فقط لترحيل الملفات القديمة ثم تُمسح.
        public string? AdminPassword { get; set; }
        public string? OmarPassword { get; set; }
        public string? AhmedPassword { get; set; }
    }

    internal static class AdminSecurity
    {
        private const string DefaultAdminPassword = "091076";
        private const string DefaultOmarPassword = "9909";
        private const int Iterations = 100_000;

        /// <summary> المستخدمون الذين يمكن تغيير كلمة مرورهم من الإدارة. </summary>
        public static readonly string[] Users = { "عمر", "أحمد" };

        private static readonly object Sync = new object();
        private static AppSecurityState state = Load();

        private static string StateDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LibraryPrintApp");

        private static string StateFile =>
            Path.Combine(StateDirectory, "security.json");

        // ---------------- التحقق ----------------

        public static bool ValidateAdminPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password)) return false;
            lock (Sync) return Verify(password, state.AdminHash);
        }

        /// <summary>True only when the currently stored admin credential is still the built-in default.</summary>
        public static bool IsUsingDefaultAdminPassword
        {
            get { lock (Sync) return Verify(DefaultAdminPassword, state.AdminHash); }
        }

        public static bool IsUsingDefaultUserPassword(string userName)
        {
            lock (Sync)
            {
                return userName == "عمر" && Verify(DefaultOmarPassword, state.OmarHash);
            }
        }

        public static bool UserRequiresPassword(string userName)
        {
            lock (Sync) return !string.IsNullOrEmpty(GetHashLocked(userName));
        }

        public static bool ValidateUserPassword(string userName, string password)
        {
            lock (Sync)
            {
                string hash = GetHashLocked(userName);
                if (string.IsNullOrEmpty(hash))
                    return string.IsNullOrEmpty(password); // مستخدم بلا كلمة مرور
                return Verify(password ?? "", hash);
            }
        }

        // ---------------- التغيير ----------------

        public static void SetAdminPassword(string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("كلمة مرور الأدمن لا يمكن أن تكون فارغة.");

            lock (Sync)
            {
                state.AdminHash = Hash(newPassword.Trim());
                SaveLocked();
            }
        }

        /// <summary> كلمة مرور فارغة = المستخدم يدخل بدون كلمة مرور. </summary>
        public static void SetUserPassword(string userName, string newPassword)
        {
            if (Array.IndexOf(Users, userName) < 0)
                throw new ArgumentException("المستخدم غير معروف.");

            newPassword ??= "";

            lock (Sync)
            {
                SetHashLocked(userName, newPassword.Length == 0 ? "" : Hash(newPassword));
                SaveLocked();
            }
        }

        // ---------------- التشفير ----------------

        private static string Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        private static bool Verify(string password, string stored)
        {
            if (string.IsNullOrEmpty(stored)) return false;
            try
            {
                string[] parts = stored.Split('.');
                if (parts.Length != 3) return false;
                int iterations = int.Parse(parts[0]);
                byte[] salt = Convert.FromBase64String(parts[1]);
                byte[] expected = Convert.FromBase64String(parts[2]);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        private static string GetHashLocked(string userName) =>
            userName == "عمر" ? state.OmarHash
            : userName == "أحمد" ? state.AhmedHash
            : "";

        private static void SetHashLocked(string userName, string hash)
        {
            if (userName == "عمر") state.OmarHash = hash;
            else if (userName == "أحمد") state.AhmedHash = hash;
        }

        // ---------------- التحميل والحفظ ----------------

        /// <summary> إعادة قراءة security.json من القرص (بعد استعادة نسخة احتياطية). </summary>
        public static void Reload()
        {
            lock (Sync) state = Load();
        }

        private static AppSecurityState Load()
        {
            try
            {
                if (!File.Exists(StateFile))
                    return CreateDefaults(save: true);

                string json = File.ReadAllText(StateFile);
                AppSecurityState? loaded = JsonSerializer.Deserialize<AppSecurityState>(json);
                if (loaded == null)
                    throw new InvalidDataException("ملف الأمان فارغ أو غير صالح.");

                // ملف قديم بنص صريح: نرحّله لـ Hash ونمسح النص الصريح من الملف.
                bool legacy = !json.Contains("\"Version\"") || loaded.Version < 2;
                if (legacy)
                {
                    loaded.AdminHash = Hash(
                        string.IsNullOrWhiteSpace(loaded.AdminPassword)
                            ? DefaultAdminPassword
                            : loaded.AdminPassword!);

                    // Omar: null = الافتراضي، فارغ = بدون كلمة مرور.
                    loaded.OmarHash = loaded.OmarPassword == null
                        ? Hash(DefaultOmarPassword)
                        : (loaded.OmarPassword.Length == 0 ? "" : Hash(loaded.OmarPassword));

                    loaded.AhmedHash = string.IsNullOrEmpty(loaded.AhmedPassword)
                        ? ""
                        : Hash(loaded.AhmedPassword!);

                    loaded.AdminPassword = null;
                    loaded.OmarPassword = null;
                    loaded.AhmedPassword = null;
                    loaded.Version = 2;
                    Persist(loaded);
                }
                else if (string.IsNullOrEmpty(loaded.AdminHash))
                {
                    throw new InvalidDataException("ملف الأمان لا يحتوي على كلمة مرور أدمن صالحة.");
                }

                return loaded;
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر قراءة ملف الأمان security.json؛ ستتم محاولة الاستعادة من النسخة الاحتياطية", ex);
                try
                {
                    string backupFile = StateFile + ".bak";
                    if (File.Exists(backupFile))
                    {
                        AppSecurityState? backup = JsonSerializer.Deserialize<AppSecurityState>(File.ReadAllText(backupFile));
                        if (backup != null && backup.Version >= 2 && !string.IsNullOrWhiteSpace(backup.AdminHash))
                        {
                            File.Copy(backupFile, StateFile, true);
                            AppLog.Info("تمت استعادة إعدادات الأمان من security.json.bak.");
                            return backup;
                        }
                    }
                }
                catch (Exception restoreEx)
                {
                    AppLog.Error("تعذرت استعادة ملف الأمان من النسخة الاحتياطية", restoreEx);
                }
                // احتياط أخير للتوافق مع التثبيتات القديمة؛ نسجل الخطأ بوضوح كي تُراجع كلمات المرور فورًا.
                AppLog.Error("لم توجد نسخة أمان صالحة؛ استُخدمت القيم الافتراضية. يجب تغيير كلمات المرور فور الدخول.");
                return CreateDefaults(save: false);
            }
        }

        private static AppSecurityState CreateDefaults(bool save)
        {
            var created = new AppSecurityState
            {
                AdminHash = Hash(DefaultAdminPassword),
                OmarHash = Hash(DefaultOmarPassword),
                AhmedHash = ""
            };
            if (save) Persist(created);
            return created;
        }

        private static void SaveLocked() => Persist(state);

        private static void Persist(AppSecurityState value)
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                string json = JsonSerializer.Serialize(
                    value,
                    new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                string tempFile = StateFile + ".tmp";
                File.WriteAllText(tempFile, json);
                if (File.Exists(StateFile))
                {
                    // لا نستبدل النسخة الاحتياطية بملف أمان تالف.
                    try
                    {
                        AppSecurityState? current = JsonSerializer.Deserialize<AppSecurityState>(File.ReadAllText(StateFile));
                        if (current != null && current.Version >= 2 && !string.IsNullOrWhiteSpace(current.AdminHash))
                            File.Copy(StateFile, StateFile + ".bak", true);
                    }
                    catch (Exception backupEx)
                    {
                        AppLog.Error("تعذر إنشاء نسخة أمان احتياطية قبل التحديث", backupEx);
                    }
                }
                File.Move(tempFile, StateFile, true);
            }
            catch (Exception ex)
            {
                AppLog.Error("تعذر حفظ ملف الأمان security.json", ex);
                throw;
            }
        }
    }
}
