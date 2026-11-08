using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryPrintApp;

/// <summary>
/// بيراقب طابور الطباعة في ويندوز (Spooler) عشان نعرف لو حد لغى ملف يدويًا من الطابور
/// بعد ما البرنامج بعته للطابعة. كل ملف بنبعته بيتحط في اسمه علامة مميزة مثل [LP7F3A]
/// وبنتابع الوظايف اللي فيها العلامة دي: لو ظهرت بحالة «جاري الحذف/محذوفة» من غير ما تكون اتطبعت
/// بالكامل، يبقى اتلغت.
/// الخطأ في المراقبة مش بيوقف الطباعة أبدًا: لو الطابور مش متاح ببساطة مش هنعرف نكشف الإلغاء.
/// </summary>
internal sealed class SpoolWatcher : IDisposable
{
    private const int JOB_STATUS_DELETING = 0x00000004;
    private const int JOB_STATUS_PRINTED = 0x00000080;
    private const int JOB_STATUS_DELETED = 0x00000100;
    private const int JOB_STATUS_COMPLETE = 0x00001000;
    private const int JOB_CONTROL_DELETE = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JOB_INFO_1
    {
        public int JobId;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pPrinterName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pMachineName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pUserName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pDocument;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pDatatype;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pStatus;
        public int Status;
        public int Priority;
        public int Position;
        public int TotalPages;
        public int PagesPrinted;
        public SYSTEMTIME Submitted;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EnumJobsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumJobs(
        IntPtr hPrinter, int firstJob, int noJobs, int level,
        IntPtr pJob, int cbBuf, out int pcbNeeded, out int pcReturned);

    [DllImport("winspool.drv", EntryPoint = "SetJobW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetJob(
        IntPtr hPrinter, int jobId, int level, IntPtr pJob, int command);

    private sealed class Ticket
    {
        public string Tag = "";
        public string Queue = "";
        public bool Sent;       // الإرسال للـSpooler خلص (Print() رجعت)
        public bool Seen;       // شفناه في الطابور
        public bool Cancelled;  // اتلغى يدويًا
        public bool Gone;       // مبقاش يستنى حاجة
        public int MissingPolls;
    }

    private readonly object sync = new();
    private readonly List<Ticket> tickets = new();
    private readonly CancellationTokenSource stop = new();
    private Task? loop;
    private volatile bool sendFinished;

    /// <summary> علامة قصيرة فريدة تتحط في اسم المستند. </summary>
    public static string NewTag() => "[LP" + Guid.NewGuid().ToString("N").Substring(0, 5).ToUpperInvariant() + "]";

    public void Start()
    {
        loop = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { PollOnce(); } catch { /* المراقبة اختيارية */ }
                try { await Task.Delay(250, stop.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }

    public void Register(string tag, string queue)
    {
        lock (sync) tickets.Add(new Ticket { Tag = tag, Queue = queue });
    }

    public void MarkSent(string tag)
    {
        lock (sync)
        {
            var t = tickets.FirstOrDefault(x => x.Tag == tag);
            if (t != null) t.Sent = true;
        }
    }

    public void SendFinished() => sendFinished = true;

    public bool IsCancelled(string tag)
    {
        lock (sync) return tickets.Any(t => t.Tag == tag && t.Cancelled);
    }

    /// <summary>
    /// يطلب حذف مهام هذا التطبيق فقط من طابور ويندوز. كل مهمة تُطابق بعلامة فريدة،
    /// حتى لا يتم حذف مهام طباعة المستخدم أو البرامج الأخرى. تُعلّم كملغاة فور قبول ويندوز للطلب.
    /// </summary>
    public int CancelPendingJobs()
    {
        List<Ticket> active;
        lock (sync) active = tickets.Where(t => !t.Gone).ToList();

        int cancelled = 0;
        foreach (var group in active.GroupBy(t => t.Queue))
        {
            List<JOB_INFO_1>? jobs = ReadJobs(group.Key);
            if (jobs == null || jobs.Count == 0) continue;

            foreach (Ticket ticket in group)
            {
                foreach (JOB_INFO_1 job in jobs.Where(j => (j.pDocument ?? "").Contains(ticket.Tag)))
                {
                    if (!TryDeleteJob(group.Key, job.JobId)) continue;

                    lock (sync) ticket.Cancelled = true;
                    cancelled++;
                    // قد تنشأ عدة مهام طباعة بالعلامة نفسها عند طباعة نسخ متعددة؛ نحذفها كلها.
                }
            }
        }
        return cancelled;
    }

    private static bool TryDeleteJob(string printerName, int jobId)
    {
        if (!OpenPrinter(printerName, out IntPtr handle, IntPtr.Zero) || handle == IntPtr.Zero)
            return false;
        try
        {
            return SetJob(handle, jobId, 0, IntPtr.Zero, JOB_CONTROL_DELETE);
        }
        catch
        {
            return false;
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    /// <summary> عدد الملفات اللي لسه في الطابور (مستنية تخلص). </summary>
    public int PendingCount
    {
        get { lock (sync) return tickets.Count(t => !t.Gone); }
    }

    public bool AllDone
    {
        get { lock (sync) return sendFinished && tickets.All(t => t.Gone); }
    }

    private void PollOnce()
    {
        List<Ticket> active;
        lock (sync) active = tickets.Where(t => !t.Gone).ToList();
        if (active.Count == 0) return;

        foreach (var group in active.GroupBy(t => t.Queue))
        {
            List<JOB_INFO_1>? jobs = ReadJobs(group.Key);
            lock (sync)
            {
                foreach (var ticket in group)
                {
                    if (jobs == null)
                    {
                        // الطابور مش متاح للمراقبة: منستناش حاجة.
                        ticket.Gone = true;
                        continue;
                    }

                    var mine = jobs.Where(j => (j.pDocument ?? "").Contains(ticket.Tag)).ToList();
                    if (mine.Count > 0)
                    {
                        ticket.Seen = true;
                        ticket.MissingPolls = 0;
                        if (mine.Any(IsUserCancelled)) ticket.Cancelled = true;
                    }
                    else if (ticket.Seen)
                    {
                        ticket.Gone = true;
                    }
                    else if (ticket.Sent && ++ticket.MissingPolls >= 5)
                    {
                        // اتطبع بسرعة قبل ما نلحق نشوفه.
                        ticket.Gone = true;
                    }
                }
            }
        }
    }

    private static bool IsUserCancelled(JOB_INFO_1 job)
    {
        bool deleting = (job.Status & (JOB_STATUS_DELETING | JOB_STATUS_DELETED)) != 0;
        if (!deleting) return false;
        bool finished = (job.Status & (JOB_STATUS_PRINTED | JOB_STATUS_COMPLETE)) != 0
                        || (job.TotalPages > 0 && job.PagesPrinted >= job.TotalPages);
        return !finished;
    }

    private static List<JOB_INFO_1>? ReadJobs(string printerName)
    {
        if (!OpenPrinter(printerName, out IntPtr handle, IntPtr.Zero) || handle == IntPtr.Zero)
            return null;
        try
        {
            EnumJobs(handle, 0, 200, 1, IntPtr.Zero, 0, out int needed, out _);
            if (needed <= 0) return new List<JOB_INFO_1>();

            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!EnumJobs(handle, 0, 200, 1, buffer, needed, out _, out int returned))
                    return null;

                var list = new List<JOB_INFO_1>(returned);
                int size = Marshal.SizeOf<JOB_INFO_1>();
                for (int i = 0; i < returned; i++)
                    list.Add(Marshal.PtrToStructure<JOB_INFO_1>(buffer + i * size));
                return list;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    public void Dispose()
    {
        try { stop.Cancel(); } catch { }
        try { loop?.Wait(1500); } catch { }
        stop.Dispose();
    }
}
