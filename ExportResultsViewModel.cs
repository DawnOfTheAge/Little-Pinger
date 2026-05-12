using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for the Export Results sub-tab.
/// Exports the current ping-entries collection to JSON, CSV, or HTML,
/// opens the file afterwards using the system default application.
/// </summary>
public class ExportResultsViewModel : ViewModelBase
{
    private readonly ObservableCollection<PingEntryViewModel> _entries;
    private string _selectedFormat = "HTML";
    private string _statusMessage  = "";

    public static IReadOnlyList<string> Formats => new[] { "HTML", "JSON", "CSV" };

    public ObservableCollection<PingEntryViewModel> Entries => _entries;
    public string SelectedFormat { get => _selectedFormat; set => SetField(ref _selectedFormat, value); }
    public string StatusMessage  { get => _statusMessage;  set => SetField(ref _statusMessage, value); }

    public RelayCommand ExportCommand { get; }

    public ExportResultsViewModel(ObservableCollection<PingEntryViewModel> entries)
    {
        _entries      = entries;
        ExportCommand = new RelayCommand(OnExport);
    }

    private void OnExport()
    {
        var dlg = new SaveFileDialog
        {
            Title    = "Export Diagnostics",
            FileName = $"LittlePinger_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        dlg.Filter = SelectedFormat switch
        {
            "CSV"  => "CSV files (*.csv)|*.csv",
            "JSON" => "JSON files (*.json)|*.json",
            _      => "HTML files (*.html)|*.html"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            string content = SelectedFormat switch
            {
                "CSV"  => BuildCsv(),
                "JSON" => BuildJson(),
                _      => BuildHtml()
            };

            File.WriteAllText(dlg.FileName, content, Encoding.UTF8);
            StatusMessage = $"✔  Exported {_entries.Count} entries → {Path.GetFileName(dlg.FileName)}";
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘  Export failed: {ex.Message}";
        }
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    private string BuildJson()
    {
        var data = _entries.Select(e => new
        {
            e.Name,
            e.IpAddress,
            e.IsRunning,
            e.SentCount,
            SuccessCount = e.SuccessCount,
            FailCount    = e.FailCount,
            LossPercent  = e.SentCount > 0 ? (double)e.FailCount / e.SentCount * 100 : 0,
            LastRttMs    = e.LastRttDisplay,
            Status       = e.Status.ToString(),
            ExportedAt   = DateTime.Now
        }).ToList();
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    private string BuildCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,IP Address,Running,Sent,Success,Fail,Loss (%),Last RTT,Status,Exported At");
        foreach (var e in _entries)
        {
            double loss = e.SentCount > 0 ? (double)e.FailCount / e.SentCount * 100 : 0;
            sb.AppendLine($"\"{e.Name}\",\"{e.IpAddress}\",{e.IsRunning}," +
                          $"{e.SentCount},{e.SuccessCount},{e.FailCount},{loss:F1}," +
                          $"\"{e.LastRttDisplay}\",\"{e.Status}\",{DateTime.Now:O}");
        }
        return sb.ToString();
    }

    private string BuildHtml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
        sb.AppendLine("<title>Little Pinger Export</title><style>");
        sb.AppendLine("body{font-family:Segoe UI,sans-serif;background:#f5f5f5;padding:24px;color:#263238}");
        sb.AppendLine("h1{color:#37474F;font-weight:300}p{color:#607D8B}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.1)}");
        sb.AppendLine("th{background:#455A64;color:#fff;padding:10px 14px;text-align:left;font-weight:500}");
        sb.AppendLine("td{padding:9px 14px;border-bottom:1px solid #ECEFF1}");
        sb.AppendLine("tr:nth-child(even)td{background:#FAFAFA}");
        sb.AppendLine(".ok{color:#43A047;font-weight:600}.warn{color:#FB8C00;font-weight:600}.err{color:#E53935;font-weight:600}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>🏓 Little Pinger — Diagnostics Report</h1>");
        sb.AppendLine($"<p>Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ·  {_entries.Count} entries</p>");
        sb.AppendLine("<table><thead><tr>");
        foreach (var h in new[] { "Name", "IP Address", "Status", "Sent", "Success", "Fail", "Loss %", "Last RTT" })
            sb.Append($"<th>{h}</th>");
        sb.AppendLine("</tr></thead><tbody>");
        foreach (var e in _entries)
        {
            double loss = e.SentCount > 0 ? (double)e.FailCount / e.SentCount * 100 : 0;
            var cls     = loss == 0 ? "ok" : loss < 5 ? "warn" : "err";
            sb.AppendLine($"<tr><td>{e.Name}</td><td>{e.IpAddress}</td><td>{e.Status}</td>" +
                          $"<td>{e.SentCount}</td><td>{e.SuccessCount}</td><td>{e.FailCount}</td>" +
                          $"<td class='{cls}'>{loss:F1}</td><td>{e.LastRttDisplay}</td></tr>");
        }
        sb.AppendLine("</tbody></table></body></html>");
        return sb.ToString();
    }
}
