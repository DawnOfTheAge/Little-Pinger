using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>A name/value pair representing one field in an X.509 certificate.</summary>
public record CertProperty(string Name, string Value);

/// <summary>Summary row for one certificate in the trust chain.</summary>
public class CertChainEntry
{
    public int      Index          { get; init; }
    public string   SubjectCN      { get; init; } = "";
    public string   IssuerCN       { get; init; } = "";
    public DateTime ValidTo        { get; init; }
    public string   ValidToDisplay => ValidTo.ToString("yyyy-MM-dd");
    public int      DaysLeft       => (int)(ValidTo - DateTime.Now).TotalDays;
}

/// <summary>
/// View-model for the SSL/TLS Certificate Inspector.
/// Connects with an <see cref="SslStream"/>, reads the server certificate,
/// builds the chain, and surfaces all relevant fields.
/// </summary>
public class SslInspectorViewModel : ViewModelBase
{
    private static readonly string DataDir     = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string HistoryFile = Path.Combine(DataDir, "ssl_history.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ObservableCollection<PingEntryViewModel> _pingEntries;
    private CancellationTokenSource? _cts;

    private string _domain          = "";
    private int    _port            = 443;
    private bool   _isRunning;
    private bool   _hasResult;
    private string _errorMessage    = "";
    private string _expiryStatus    = "None"; // Good | Warning | Critical | Expired | None
    private string _expiryBanner    = "";

    public ObservableCollection<string>       History        { get; } = new();
    public ObservableCollection<string>       AllDomains     { get; } = new();
    public ObservableCollection<CertProperty> CertProperties { get; } = new();
    public ObservableCollection<CertChainEntry> CertChain    { get; } = new();

    public string Domain
    {
        get => _domain;
        set { SetField(ref _domain, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public int Port
    {
        get => _port;
        set => SetField(ref _port, value);
    }

    public bool   IsRunning      { get => _isRunning;    set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }
    public bool   HasResult      { get => _hasResult;    set => SetField(ref _hasResult, value); }
    public string ErrorMessage   { get => _errorMessage; set => SetField(ref _errorMessage, value); }
    public string ExpiryStatus   { get => _expiryStatus; set => SetField(ref _expiryStatus, value); }
    public string ExpiryBanner   { get => _expiryBanner; set => SetField(ref _expiryBanner, value); }

    public RelayCommand InspectCommand { get; }
    public RelayCommand CancelCommand  { get; }
    public RelayCommand ClearCommand   { get; }

    public SslInspectorViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries   = pingEntries;
        InspectCommand = new RelayCommand(OnInspect, () => !string.IsNullOrWhiteSpace(Domain) && !IsRunning);
        CancelCommand  = new RelayCommand(OnCancel,  () => IsRunning);
        ClearCommand   = new RelayCommand(OnClear,   () => !IsRunning);

        pingEntries.CollectionChanged += (_, _) => RebuildAllDomains();
        LoadHistory();
        RebuildAllDomains();
    }

    // ── Address list ─────────────────────────────────────────────────────────

    private void RebuildAllDomains()
    {
        AllDomains.Clear();
        foreach (var h in History) AllDomains.Add(h);
        foreach (var e in _pingEntries)
            if (!string.IsNullOrWhiteSpace(e.IpAddress) && !AllDomains.Contains(e.IpAddress))
                AllDomains.Add(e.IpAddress);
    }

    // ── Command handlers ─────────────────────────────────────────────────────

    private void OnInspect()
    {
        var domain = Domain.Trim();
        if (string.IsNullOrEmpty(domain)) return;

        _cts         = new CancellationTokenSource();
        IsRunning    = true;
        HasResult    = false;
        ErrorMessage = "";
        ExpiryStatus = "None";
        ExpiryBanner = "";
        CertProperties.Clear();
        CertChain.Clear();

        _ = Task.Run(() => RunInspectAsync(domain, Port, _cts.Token));
    }

    private async Task RunInspectAsync(string domain, int port, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(domain, port, linked.Token);

            // Accept any cert so we can inspect even self-signed/expired ones
            using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(domain);

            if (ssl.RemoteCertificate is null)
                throw new InvalidOperationException("Server presented no certificate.");

            using var leaf = new X509Certificate2(ssl.RemoteCertificate);

            // Build trust chain (revocation disabled so self-signed certs still produce a chain)
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode     = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags  = X509VerificationFlags.AllFlags;
            chain.Build(leaf);

            int    daysLeft    = (int)(leaf.NotAfter - DateTime.Now).TotalDays;
            string expStatus   = daysLeft switch { > 30 => "Good", > 10 => "Warning", >= 0 => "Critical", _ => "Expired" };
            string expBanner   = daysLeft >= 0
                ? $"{expStatus}:  {daysLeft} days until expiry  ·  Expires {leaf.NotAfter:yyyy-MM-dd HH:mm} UTC"
                : $"EXPIRED on {leaf.NotAfter:yyyy-MM-dd}";

            // Subject Alternative Names via OID 2.5.29.17
            var sanExt    = leaf.Extensions["2.5.29.17"];
            var sanText   = sanExt is not null ? sanExt.Format(true).Trim() : "(none)";

            var props = new List<CertProperty>
            {
                new("Subject",            leaf.Subject),
                new("Issuer",             leaf.Issuer),
                new("Valid From",         leaf.NotBefore.ToString("yyyy-MM-dd HH:mm:ss")),
                new("Valid To",           leaf.NotAfter .ToString("yyyy-MM-dd HH:mm:ss")),
                new("Days Until Expiry",  daysLeft >= 0 ? $"{daysLeft} days" : $"Expired {-daysLeft} days ago"),
                new("Serial Number",      leaf.SerialNumber),
                new("Thumbprint (SHA-1)", leaf.Thumbprint),
                new("Signature Algorithm",leaf.SignatureAlgorithm.FriendlyName
                                          ?? leaf.SignatureAlgorithm.Value ?? ""),
                new("Subject Alt Names",  sanText),
                new("TLS Version",        ssl.SslProtocol.ToString()),
                new("Cipher Suite",       ssl.CipherAlgorithm.ToString()),
            };

            var chainEntries = chain.ChainElements
                .Select((el, i) => new CertChainEntry
                {
                    Index     = i,
                    SubjectCN = ExtractCN(el.Certificate.Subject),
                    IssuerCN  = ExtractCN(el.Certificate.Issuer),
                    ValidTo   = el.Certificate.NotAfter,
                })
                .ToList();

            Dispatch(() =>
            {
                ExpiryStatus = expStatus;
                ExpiryBanner = expBanner;
                foreach (var p in props)        CertProperties.Add(p);
                foreach (var e in chainEntries) CertChain.Add(e);
                ErrorMessage = "";
                HasResult    = true;

                if (!History.Contains(domain))
                {
                    History.Insert(0, domain);
                    if (History.Count > 50) History.RemoveAt(History.Count - 1);
                    RebuildAllDomains();
                    SaveHistory();
                }
            });
        }
        catch (OperationCanceledException)
        {
            Dispatch(() => { ErrorMessage = "Cancelled"; HasResult = true; });
        }
        catch (Exception ex)
        {
            Dispatch(() => { ErrorMessage = ex.Message; HasResult = true; });
        }
        finally
        {
            Dispatch(() => IsRunning = false);
        }
    }

    private void OnCancel() => _cts?.Cancel();

    private void OnClear()
    {
        HasResult    = false;
        ErrorMessage = "";
        ExpiryStatus = "None";
        ExpiryBanner = "";
        CertProperties.Clear();
        CertChain.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ExtractCN(string dn)
    {
        var m = Regex.Match(dn, @"CN=([^,]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : dn;
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private void LoadHistory()
    {
        if (!File.Exists(HistoryFile)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryFile));
            if (list is null) return;
            foreach (var h in list) History.Add(h);
        }
        catch (Exception ex) { Debug.WriteLine($"[SslVM] Load: {ex.Message}"); }
    }

    private void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(History.ToList(), JsonOpts));
        }
        catch (Exception ex) { Debug.WriteLine($"[SslVM] Save: {ex.Message}"); }
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
