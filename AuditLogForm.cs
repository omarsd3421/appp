using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace LibraryPrintApp;

/// <summary>سجل تدقيق إداري للعمليات المؤثرة على بيانات وحسابات البرنامج.</summary>
internal sealed class AuditLogForm : Form
{
    private readonly DataGridView grid = new();
    private readonly TextBox search = new();
    private readonly Label status = new();

    public AuditLogForm()
    {
        Text = "سجل تدقيق العمليات";
        Width = 1120;
        Height = 680;
        MinimumSize = new System.Drawing.Size(860, 480);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (Shortcuts.Is("audit.refresh", e)) { e.SuppressKeyPress = true; LoadRows(); } };
        UiKit.EnableHelpKey(this);

        var header = new Panel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(12) };
        var title = new Label { Text = "سجل تدقيق العمليات (آخر 1000 حركة)", AutoSize = true, Dock = DockStyle.Right,
            Font = new System.Drawing.Font("Segoe UI", 13, System.Drawing.FontStyle.Bold), Padding = new Padding(5, 7, 5, 0) };
        var searchLabel = new Label { Text = "بحث:", AutoSize = true, Dock = DockStyle.Right, Padding = new Padding(8, 9, 4, 0) };
        search.Width = 220;
        search.Dock = DockStyle.Right;
        search.TextChanged += (_, _) => ApplySearch();
        var refresh = new Button { Text = "تحديث (F5)", AutoSize = true, Dock = DockStyle.Left };
        refresh.Click += (_, _) => LoadRows();
        var export = new Button { Text = "تصدير CSV", AutoSize = true, Dock = DockStyle.Left };
        export.Click += (_, _) => ExportCsv();
        header.Controls.Add(title);
        header.Controls.Add(search);
        header.Controls.Add(searchLabel);
        header.Controls.Add(export);
        header.Controls.Add(refresh);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.Columns.Add(TextColumn("CreatedAt", "التاريخ والوقت", 155));
        grid.Columns.Add(TextColumn("UserName", "المستخدم", 90));
        grid.Columns.Add(TextColumn("Action", "العملية", 105));
        grid.Columns.Add(TextColumn("EntityType", "القسم", 110));
        grid.Columns.Add(TextColumn("EntityId", "رقم الحركة", 75));
        grid.Columns.Add(TextColumn("Details", "التفاصيل", 350));
        status.Dock = DockStyle.Bottom;
        status.Height = 28;
        status.Padding = new Padding(8, 5, 8, 0);
        status.Text = "اضغط F5 لتحديث السجل.";

        Controls.Add(grid);
        Controls.Add(status);
        Controls.Add(header);
        LoadRows();
    }

    private static DataGridViewTextBoxColumn TextColumn(string name, string header, int minWidth) => new()
    {
        Name = name, DataPropertyName = name, HeaderText = header,
        MinimumWidth = Math.Min(minWidth, 70), FillWeight = Math.Max(50, minWidth),
        SortMode = DataGridViewColumnSortMode.Automatic
    };

    private void LoadRows()
    {
        try
        {
            using var connection = AppDatabase.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT CreatedAt, UserName, Action, EntityType,
COALESCE(CAST(EntityId AS TEXT), ''), Details
FROM AuditLog ORDER BY Id DESC LIMIT 1000";
            using var reader = command.ExecuteReader();
            var table = new System.Data.DataTable();
            table.Columns.Add("CreatedAt");
            table.Columns.Add("UserName");
            table.Columns.Add("Action");
            table.Columns.Add("EntityType");
            table.Columns.Add("EntityId");
            table.Columns.Add("Details");
            while (reader.Read())
            {
                var row = table.NewRow();
                for (int i = 0; i < table.Columns.Count; i++)
                    row[i] = reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString() ?? string.Empty;
                table.Rows.Add(row);
            }
            grid.DataSource = table;
            ApplySearch();
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر تحميل سجل تدقيق العمليات", ex);
            MessageBox.Show(this, "تعذر تحميل سجل التدقيق:\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplySearch()
    {
        if (grid.DataSource is not System.Data.DataTable table) return;
        string term = search.Text.Trim().Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");
        if (string.IsNullOrEmpty(term)) table.DefaultView.RowFilter = string.Empty;
        else
        {
            string escaped = term.Replace("_", "[_]");
            table.DefaultView.RowFilter = string.Join(" OR ", table.Columns.Cast<System.Data.DataColumn>()
                .Select(c => $"Convert([{c.ColumnName}], 'System.String') LIKE '%{escaped}%'") );
        }
        status.Text = $"النتائج المعروضة: {table.DefaultView.Count} من أصل {table.Rows.Count} (الحد الأقصى 1000 سجل).";
    }

    private void ExportCsv()
    {
        if (grid.Rows.Count == 0)
        {
            MessageBox.Show(this, "لا توجد بيانات لتصديرها.", "تصدير", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "تصدير سجل التدقيق",
            Filter = "CSV متوافق مع Excel (*.csv)|*.csv",
            FileName = $"سجل_التدقيق_{DateTime.Now:yyyy-MM-dd_HHmm}.csv",
            DefaultExt = "csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", grid.Columns.Cast<DataGridViewColumn>().Select(c => Csv(c.HeaderText))));
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                csv.AppendLine(string.Join(",", grid.Columns.Cast<DataGridViewColumn>()
                    .Select(c => Csv(row.Cells[c.Index].Value?.ToString() ?? string.Empty))));
            }
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            MessageBox.Show(this, "تم تصدير سجل التدقيق.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر تصدير سجل التدقيق", ex);
            MessageBox.Show(this, "تعذر التصدير:\n" + ex.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
}
