using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>Key/value pair for displaying an HTTP response header.</summary>
public record HttpHeaderEntry(string Name, string Value);

/// <summary>
/// View-model for the HTTP/HTTPS Check tool.
/// Performs a GET request, optionally follows redirects manually so each hop is visible,
/// and surfaces the status code, response time, final URL, and all response headers.
/// </summary>
public class HttpCheckViewModel : ViewModelBase
{
    private static readonly string DataDir     = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string HistoryFile = Path.Combine(DataDir, "http_history.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ObservableCollection<PingEntryViewModel> _pingEntries;
    private CancellationTokenSource? _cts;

    private string _url             = "";
    private bool   _followRedirects = true;
    private bool   _isRunning;
    private bool   _hasResult;
    private string _statusCode      = "";
    private string _statusDesc      = "";
    private string _statusGroup     = "None"; // Success | Redirect | ClientError | ServerError | Error | None
    private long   _responseTimeMs;
    private string _finalUrl        = "";
    private string _errorMessage    = "";

    public ObservableCollection<string>         History      { get; } = new();
    public ObservableCollection<string>         AllUrls      { get; } = new();
    public ObservableCollection<string>         RedirectChain { get; } = new();
    public ObservableCollection<HttpHeaderEntry> Headers      { get; } = new();

    public string Url
    {
        get => _url;
        set { SetField(ref _url, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public bool FollowRedirects
    {
        get => _followRedirects;
        set => SetField(ref _followRedirects, value);
    }

    public bool   IsRunning      { get => _isRunning;     set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }
    public bool   HasResult      { get => _hasResult;     set => SetField(ref _hasResult, value); }
    public string StatusCode     { get => _statusCode;    set => SetField(ref _statusCode, value); }
    public string StatusDesc     { get => _statusDesc;    set => SetField(ref _statusDesc, value); }
    public string StatusGroup    { get => _statusGroup;   set => SetField(ref _statusGroup, value); }
    public long   ResponseTimeMs { get => _responseTimeMs; set => SetField(ref _responseTimeMs, value); }
    public string FinalUrl       { get => _finalUrl;      set => SetField(ref _finalUrl, value); }
    public string ErrorMessage   { get => _errorMessage;  set => SetField(ref _errorMessage, value); }

    public bool HasRedirects => RedirectChain.Count > 1;

    public RelayCommand CheckCommand  { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearCommand  { get; }

    public HttpCheckViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries  = pingEntries;
        CheckCommand  = new RelayCommand(OnCheck,  () => !string.IsNullOrWhiteSpace(Url) && !IsRunning);
        CancelCommand = new RelayCommand(OnCancel, () => IsRunning);
        ClearCommand  = new RelayCommand(OnClear,  () => !IsRunning);

        pingEntries.CollectionChanged += (_, _) => RebuildAllUrls();
        LoadHistory();
        RebuildAllUrls();
    }

    // ── Address list ─────────────────────────────────────────────────────────

    private void RebuildAllUrls()
    {
        AllUrls.Clear();
        foreach (var u in History) AllUrls.Add(u);
        foreach (var e in _pingEntries)
            if (!string.IsNullOrWhiteSpace(e.IpAddress))
            {
                var url = "https://" + e.IpAddress;
                if (!AllUrls.Contains(url)) AllUrls.Add(url);
            }
    }

    // ── Command handlers ─────────────────────────────────────────────────────

    private void OnCheck()
    {
        var url = NormalizeUrl(Url.Trim());
        if (string.IsNullOrEmpty(url)) return;

        _cts         = new CancellationTokenSource();
        IsRunning    = true;
        HasResult    = false;
        StatusCode   = "";
        StatusDesc   = "";
        StatusGroup  = "None";
        FinalUrl     = "";
        ErrorMessage = "";
        ResponseTimeMs = 0;
        RedirectChain.Clear();
        Headers.Clear();

        _ = Task.Run(() => RunCheckAsync(url, FollowRedirects, _cts.Token));
    }

    private async Task RunCheckAsync(string url, bool followRedirects, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client  = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 LittlePinger/1.0");

            string currentUrl = url;
            int    hops       = 0;
            const  int maxHops = 10;
            HttpResponseMessage? lastResponse = null;

            while (hops <= maxHops)
            {
                ct.ThrowIfCancellationRequested();
                var response = await client.GetAsync(currentUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                var hop      = $"  {(int)response.StatusCode}  {response.ReasonPhrase,-25}  {currentUrl}";
                Dispatch(() => RedirectChain.Add(hop));

                int code = (int)response.StatusCode;
                if (followRedirects && code is >= 300 and < 400 && response.Headers.Location != null)
                {
                    lastResponse?.Dispose();
                    lastResponse = response;
                    var loc = response.Headers.Location;
                    currentUrl  = loc.IsAbsoluteUri ? loc.ToString()
                                                    : new Uri(new Uri(currentUrl), loc).ToString();
                    hops++;
                }
                else
                {
                    lastResponse = response;
                    break;
                }
            }

            sw.Stop();
            var resp = lastResponse!;
            int statusCode = (int)resp.StatusCode;

            var headers = new List<HttpHeaderEntry>();
            foreach (var kvp in resp.Headers)
                headers.AddRange(kvp.Value.Select(v => new HttpHeaderEntry(kvp.Key, v)));
            foreach (var kvp in resp.Content.Headers)
                headers.AddRange(kvp.Value.Select(v => new HttpHeaderEntry(kvp.Key, v)));

            Dispatch(() =>
            {
                StatusCode     = statusCode.ToString();
                StatusDesc     = resp.ReasonPhrase ?? "";
                ResponseTimeMs = sw.ElapsedMilliseconds;
                FinalUrl       = currentUrl;
                StatusGroup    = statusCode switch
                {
                    >= 200 and < 300 => "Success",
                    >= 300 and < 400 => "Redirect",
                    >= 400 and < 500 => "ClientError",
                    >= 500           => "ServerError",
                    _                => "Other"
                };
                foreach (var h in headers) Headers.Add(h);
                ErrorMessage = "";
                HasResult    = true;

                if (!History.Contains(url))
                {
                    History.Insert(0, url);
                    if (History.Count > 50) History.RemoveAt(History.Count - 1);
                    RebuildAllUrls();
                    SaveHistory();
                }
            });

            resp.Dispose();
        }
        catch (OperationCanceledException)
        {
            Dispatch(() => { ErrorMessage = "Cancelled"; HasResult = true; });
        }
        catch (Exception ex)
        {
            Dispatch(() => { ErrorMessage = ex.Message; StatusGroup = "Error"; HasResult = true; });
        }
        finally
        {
            sw.Stop();
            Dispatch(() => IsRunning = false);
        }
    }

    private void OnCancel() => _cts?.Cancel();

    private void OnClear()
    {
        HasResult    = false;
        StatusCode   = "";
        StatusDesc   = "";
        StatusGroup  = "None";
        FinalUrl     = "";
        ErrorMessage = "";
        ResponseTimeMs = 0;
        RedirectChain.Clear();
        Headers.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NormalizeUrl(string url) =>
        url.Contains("://") ? url : "https://" + url;

    // ── Persistence ──────────────────────────────────────────────────────────

    private void LoadHistory()
    {
        if (!File.Exists(HistoryFile)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryFile));
            if (list is null) return;
            foreach (var u in list) History.Add(u);
        }
        catch (Exception ex) { Debug.WriteLine($"[HttpVM] Load: {ex.Message}"); }
    }

    private void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(History.ToList(), JsonOpts));
        }
        catch (Exception ex) { Debug.WriteLine($"[HttpVM] Save: {ex.Message}"); }
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
