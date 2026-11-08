using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LibraryPrintApp
{
    // ============================================================
    // جسر الإضافة (WhatsApp → شاشة الطباعة)
    //
    // سيرفر محلي صغير على 127.0.0.1:48715 بيستقبل الملفات من إضافة المتصفح:
    //   GET  /ping
    //   POST /add-path            {"path":"C:\\...\\file.pdf"}
    //   POST /add-file?name=x.jpg  (الجسم = محتوى الملف)
    // الملفات بتتضاف لشاشة الطباعة؛ لو مش مفتوحة بتتفتح، ولو فيه شاشة تانية
    // مفتوحة الملفات بتستنى في طابور وتتضاف أول ما تفتح الطباعة.
    // ============================================================
    internal static class PrintBridge
    {
        public const int Port = 48715;
        private const long MaxBodyBytes = 200L * 1024 * 1024;

        private static readonly object sync = new();
        private static readonly Queue<string> pending = new();
        private static TcpListener? listener;
        private static Form1? current;

        // بتتظبط من شاشة الرئيسية: بتفتح شاشة الطباعة لو مفيش شاشة تانية مفتوحة.
        public static Action? OpenPrintingHandler { get; set; }

        private static readonly string[] SupportedExtensions =
        {
            ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".pps", ".ppsx",
            ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"
        };

        // ------------------------------------------------------------
        public static void Start()
        {
            try
            {
                TempFiles.CleanAll();   // أي ملفات مؤقتة فضلت من المرة اللي فاتت
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                _ = Task.Run(AcceptLoopAsync);
                AppLog.Info($"جسر الإضافة شغال على 127.0.0.1:{Port}");
            }
            catch (Exception ex)
            {
                listener = null;
                AppLog.Error($"تعذر تشغيل جسر الإضافة على المنفذ {Port} (ممكن نسخة تانية من البرنامج شغالة)", ex);
            }
        }

        public static void Stop()
        {
            try { listener?.Stop(); } catch { }
            listener = null;
        }

        // ------------------------------------------------------------
        // ربط شاشة الطباعة
        // ------------------------------------------------------------
        public static void Attach(Form1 form)
        {
            lock (sync) current = form;
            Pump();
        }

        public static void Detach(Form1 form)
        {
            lock (sync)
            {
                if (ReferenceEquals(current, form))
                    current = null;
            }
        }

        /// <summary> ملفات جاية من سطر الأوامر وقت التشغيل: بتستنى في الطابور لحد ما شاشة الطباعة تتفتح. </summary>
        public static void EnqueueInitial(IEnumerable<string> paths)
        {
            lock (sync)
            {
                foreach (string p in paths)
                    if (IsSupported(p))
                        pending.Enqueue(p);
            }
        }

        /// <summary> بيتندّه عليها لما اللوحة تجهز (بعد تسجيل الدخول) عشان الملفات المنتظرة تتفتح لها الطباعة. </summary>
        public static void Resume() => Pump();

        private static void Enqueue(string path)
        {
            lock (sync) pending.Enqueue(path);
            Pump();
        }

        // بتتنفذ على أي thread؛ التسليم الفعلي بيحصل على thread الواجهة.
        private static void Pump()
        {
            Form1? form;
            List<string>? batch = null;

            lock (sync)
            {
                if (pending.Count == 0)
                    return;

                form = current;
                if (form != null && !form.IsDisposed)
                {
                    batch = new List<string>(pending);
                    pending.Clear();
                }
            }

            if (batch != null && form != null)
            {
                try
                {
                    form.BeginInvoke(new Action(() =>
                    {
                        try { form.AddFilesFromBridge(batch); }
                        catch (Exception ex) { AppLog.Error("فشل إضافة ملفات الإضافة لشاشة الطباعة", ex); }
                    }));
                }
                catch (Exception ex)
                {
                    // الشاشة اتقفلت لحظة التسليم: نرجّع الملفات للطابور.
                    AppLog.Error("تعذر تسليم الملفات لشاشة الطباعة", ex);
                    lock (sync)
                    {
                        foreach (string p in batch) pending.Enqueue(p);
                    }
                }
                return;
            }

            // مفيش شاشة طباعة: نطلب فتحها من الشاشة الرئيسية (لو متاحة).
            Action? open = OpenPrintingHandler;
            if (open != null)
            {
                try { open(); } catch (Exception ex) { AppLog.Error("تعذر فتح شاشة الطباعة من الإضافة", ex); }
            }
        }

        // ------------------------------------------------------------
        // السيرفر
        // ------------------------------------------------------------
        private static async Task AcceptLoopAsync()
        {
            while (listener != null)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    return; // اتقفل
                }

                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        private sealed class Request
        {
            public string Method = "";
            public string Target = "";
            public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
            public byte[] Body = Array.Empty<byte>();
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 60000;
                    client.SendTimeout = 60000;
                    NetworkStream stream = client.GetStream();

                    Request? req = await ReadRequestAsync(stream).ConfigureAwait(false);
                    if (req == null)
                        return;

                    string origin = req.Headers.TryGetValue("origin", out string? o) ? (o ?? "") : "";
                    bool originOk = origin.Length == 0 ||
                                    origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                                    origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase);

                    if (req.Method == "OPTIONS")
                    {
                        await WriteAsync(stream, originOk ? 204 : 403, "", origin).ConfigureAwait(false);
                        return;
                    }

                    // حماية: صفحات الويب العادية مش مسموح لها تضيف ملفات.
                    if (!originOk || !req.Headers.ContainsKey("x-libraryprint"))
                    {
                        await WriteAsync(stream, 403, Json(false, "غير مسموح"), origin).ConfigureAwait(false);
                        return;
                    }

                    string path = req.Target;
                    string query = "";
                    int q = path.IndexOf('?');
                    if (q >= 0) { query = path.Substring(q + 1); path = path.Substring(0, q); }

                    if (req.Method == "GET" && path == "/ping")
                    {
                        await WriteAsync(stream, 200, Json(true, null), origin).ConfigureAwait(false);
                        return;
                    }

                    if (req.Method == "POST" && path == "/add-path")
                    {
                        string? filePath = null;
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(req.Body);
                            if (doc.RootElement.TryGetProperty("path", out JsonElement p))
                                filePath = p.GetString();
                        }
                        catch { }

                        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                        {
                            await WriteAsync(stream, 400, Json(false, "الملف مش موجود"), origin).ConfigureAwait(false);
                            return;
                        }

                        if (!IsSupported(filePath))
                        {
                            await WriteAsync(stream, 400, Json(false, "نوع الملف مش مدعوم في الطباعة"), origin).ConfigureAwait(false);
                            return;
                        }

                        Enqueue(filePath);
                        await WriteAsync(stream, 200, Json(true, null, Path.GetFileName(filePath)), origin).ConfigureAwait(false);
                        return;
                    }

                    if (req.Method == "POST" && path == "/add-file")
                    {
                        string name = "file";
                        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                        {
                            int eq = part.IndexOf('=');
                            if (eq > 0 && part.Substring(0, eq) == "name")
                                name = Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
                        }

                        name = SanitizeFileName(name);
                        if (!IsSupported(name))
                        {
                            await WriteAsync(stream, 400, Json(false, "نوع الملف مش مدعوم في الطباعة"), origin).ConfigureAwait(false);
                            return;
                        }

                        if (req.Body.Length == 0)
                        {
                            await WriteAsync(stream, 400, Json(false, "الملف فاضي"), origin).ConfigureAwait(false);
                            return;
                        }

                        string dir = Path.Combine(TempRoot(), Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        string saved = Path.Combine(dir, name);
                        await File.WriteAllBytesAsync(saved, req.Body).ConfigureAwait(false);

                        Enqueue(saved);
                        await WriteAsync(stream, 200, Json(true, null, name), origin).ConfigureAwait(false);
                        return;
                    }

                    await WriteAsync(stream, 404, Json(false, "غير معروف"), origin).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLog.Error("خطأ في جسر الإضافة", ex);
                }
            }
        }

        private static async Task<Request?> ReadRequestAsync(NetworkStream stream)
        {
            var data = new MemoryStream();
            byte[] buf = new byte[16384];
            int headerEnd = -1;

            while (headerEnd < 0)
            {
                int n = await stream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                if (n <= 0) return null;
                data.Write(buf, 0, n);
                if (data.Length > 65536 && headerEnd < 0 && IndexOfHeaderEnd(data) < 0) return null;
                headerEnd = IndexOfHeaderEnd(data);
            }

            byte[] all = data.GetBuffer();
            string head = Encoding.ASCII.GetString(all, 0, headerEnd);
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return null;

            var req = new Request { Method = first[0].ToUpperInvariant(), Target = first[1] };
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0)
                    req.Headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
            }

            long contentLength = 0;
            if (req.Headers.TryGetValue("content-length", out string? cl))
                long.TryParse(cl, out contentLength);
            if (contentLength < 0 || contentLength > MaxBodyBytes) return null;

            int bodyStart = headerEnd + 4;
            var body = new MemoryStream();
            int already = (int)Math.Min(data.Length - bodyStart, contentLength);
            if (already > 0) body.Write(all, bodyStart, already);

            while (body.Length < contentLength)
            {
                int n = await stream.ReadAsync(buf, 0, (int)Math.Min(buf.Length, contentLength - body.Length)).ConfigureAwait(false);
                if (n <= 0) return null;
                body.Write(buf, 0, n);
            }

            req.Body = body.ToArray();
            return req;
        }

        private static int IndexOfHeaderEnd(MemoryStream ms)
        {
            byte[] b = ms.GetBuffer();
            int len = (int)ms.Length;
            for (int i = 0; i <= len - 4; i++)
            {
                if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10)
                    return i;
            }
            return -1;
        }

        private static async Task WriteAsync(NetworkStream stream, int status, string body, string origin)
        {
            string reason = status switch
            {
                200 => "OK", 204 => "No Content", 400 => "Bad Request",
                403 => "Forbidden", 404 => "Not Found", _ => "Error"
            };

            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            var sb = new StringBuilder();
            sb.Append($"HTTP/1.1 {status} {reason}\r\n");
            if (origin.Length > 0 && status != 403)
            {
                sb.Append($"Access-Control-Allow-Origin: {origin}\r\n");
                sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
                sb.Append("Access-Control-Allow-Headers: Content-Type, X-LibraryPrint\r\n");
                sb.Append("Access-Control-Allow-Private-Network: true\r\n");
                sb.Append("Vary: Origin\r\n");
            }
            sb.Append("Content-Type: application/json; charset=utf-8\r\n");
            sb.Append($"Content-Length: {bodyBytes.Length}\r\n");
            sb.Append("Connection: close\r\n\r\n");

            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
            if (bodyBytes.Length > 0)
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static string Json(bool ok, string? error, string? name = null)
        {
            var dict = new Dictionary<string, object?> { ["ok"] = ok };
            if (error != null) dict["error"] = error;
            if (name != null) dict["name"] = name;
            return JsonSerializer.Serialize(dict);
        }

        // ------------------------------------------------------------
        // مساعدات
        // ------------------------------------------------------------
        private static bool IsSupported(string pathOrName)
        {
            string ext = Path.GetExtension(pathOrName).ToLowerInvariant();
            return Array.IndexOf(SupportedExtensions, ext) >= 0;
        }

        private static string SanitizeFileName(string name)
        {
            name = Path.GetFileName(name.Replace('/', '\\'));
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "file" : name;
        }

        private static string TempRoot() =>
            Path.Combine(Path.GetTempPath(), "LibraryPrintApp", "bridge");

        private static void CleanupOldTempFiles()
        {
            try
            {
                string root = TempRoot();
                if (!Directory.Exists(root)) return;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) > TimeSpan.FromDays(2))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
