using System.Collections.Generic;
using System.Windows.Forms;

namespace LibraryPrintApp
{
    public partial class Form1
    {
        // ملفات جاية من إضافة المتصفح (بتتنادى على thread الواجهة).
        public void AddFilesFromBridge(IEnumerable<string> paths)
        {
            foreach (string path in paths)
                ProcessFile(path);

            RecalculateTotals();

            // الملفات اتضافت من برا (كليك يمين / الإضافة): نرجّع الشاشة قدامك لو كانت مصغّرة.
            if (WindowState == FormWindowState.Minimized)
                ShowWindow(Handle, 9); // SW_RESTORE
            Activate();
        }
    }
}
