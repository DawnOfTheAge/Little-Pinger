using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

// ── Port-picker view-models ───────────────────────────────────────────────────

/// <summary>One selectable port row inside a <see cref="PortCategoryViewModel"/>.</summary>
public class PortItemViewModel : ViewModelBase
{
    private int    _port;
    private string _serviceName = "";
    private bool   _isChecked;
    private bool   _isEditing;
    private string _editPort    = "";
    private string _editService = "";

    public int    Port
    {
        get => _port;
        set { SetField(ref _port, value); OnPropertyChanged(nameof(DisplayText)); }
    }

    public string ServiceName
    {
        get => _serviceName;
        set { SetField(ref _serviceName, value); OnPropertyChanged(nameof(DisplayText)); }
    }

    public string DisplayText => $"{Port,-6}  {ServiceName}";

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetField(ref _isChecked, value)) return;
            Parent?.NotifyChildChanged();
        }
    }

    public bool   IsEditing   { get => _isEditing;   set => SetField(ref _isEditing, value); }
    public string EditPort    { get => _editPort;    set => SetField(ref _editPort, value); }
    public string EditService { get => _editService; set => SetField(ref _editService, value); }

    public RelayCommand  BeginEditCommand  { get; }
    public RelayCommand  CommitEditCommand { get; }
    public RelayCommand  CancelEditCommand { get; }
    /// <summary>Set by the parent category when the item is created.</summary>
    public RelayCommand? DeleteCommand { get; set; }

    internal PortCategoryViewModel? Parent { get; set; }

    public PortItemViewModel(int port, string serviceName, bool isChecked = false)
    {
        _port        = port;
        _serviceName = serviceName;
        _isChecked   = isChecked;

        BeginEditCommand  = new RelayCommand(() => { EditPort = Port.ToString(); EditService = ServiceName; IsEditing = true; });
        CommitEditCommand = new RelayCommand(OnCommitEdit,
            () => int.TryParse(EditPort, out var p) && p >= 1 && p <= 65535);
        CancelEditCommand = new RelayCommand(() => IsEditing = false);
    }

    private void OnCommitEdit()
    {
        if (!int.TryParse(EditPort, out var p) || p < 1 || p > 65535) return;
        Port        = p;
        ServiceName = EditService.Trim();
        IsEditing   = false;
    }
}

/// <summary>
/// A group of ports with a tri-state checkbox that controls all its children.
/// </summary>
public class PortCategoryViewModel : ViewModelBase
{
    private readonly Action? _notifyChanged;
    private bool  _updatingChildren;
    private bool? _isChecked = false;
    private string _name;
    private bool   _isAddingPort;
    private string _newPortNumber  = "";
    private string _newPortService = "";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public ObservableCollection<PortItemViewModel> Ports { get; } = new();

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            // WPF tri-state: true→click→null, so null here means "was fully checked, now uncheck all"
            // indeterminate→click arrives as false, false→click arrives as true — both handled directly
            var v = value ?? false;
            _isChecked = v;
            OnPropertyChanged(nameof(IsChecked));
            _updatingChildren = true;
            foreach (var p in Ports) p.IsChecked = v;
            _updatingChildren = false;
            _notifyChanged?.Invoke();
        }
    }

    public bool   IsAddingPort   { get => _isAddingPort;   set => SetField(ref _isAddingPort, value); }
    public string NewPortNumber  { get => _newPortNumber;  set => SetField(ref _newPortNumber, value); }
    public string NewPortService { get => _newPortService; set => SetField(ref _newPortService, value); }

    public RelayCommand  ShowAddPortCommand   { get; }
    public RelayCommand  CommitAddPortCommand { get; }
    public RelayCommand  CancelAddPortCommand { get; }
    /// <summary>Set by the container (PortScannerViewModel) when the category is created.</summary>
    public RelayCommand? DeleteCommand { get; set; }

    public PortCategoryViewModel(string name, Action? notifyChanged = null)
    {
        _name          = name;
        _notifyChanged = notifyChanged;

        ShowAddPortCommand   = new RelayCommand(() => IsAddingPort = true);
        CommitAddPortCommand = new RelayCommand(OnCommitAdd,
            () => int.TryParse(NewPortNumber, out var p) && p >= 1 && p <= 65535
                  && !Ports.Any(x => x.Port == p));
        CancelAddPortCommand = new RelayCommand(() =>
        {
            IsAddingPort  = false;
            NewPortNumber = NewPortService = "";
        });
    }

    public void AddPort(int port, string service, bool isChecked = false)
    {
        var item = new PortItemViewModel(port, service, isChecked) { Parent = this };
        item.DeleteCommand = new RelayCommand(() => { Ports.Remove(item); _notifyChanged?.Invoke(); });
        Ports.Add(item);
    }

    internal void NotifyChildChanged()
    {
        if (_updatingChildren) return;
        int n = Ports.Count(p => p.IsChecked);
        bool? state = n == 0 ? false : n == Ports.Count ? (bool?)true : null;
        if (_isChecked != state)
        {
            _isChecked = state;
            OnPropertyChanged(nameof(IsChecked));
        }
        _notifyChanged?.Invoke();
    }

    private void OnCommitAdd()
    {
        if (!int.TryParse(NewPortNumber, out var p) || p < 1 || p > 65535) return;
        if (Ports.Any(x => x.Port == p)) return;
        AddPort(p, NewPortService.Trim(), isChecked: true);
        _notifyChanged?.Invoke();
        NewPortNumber = NewPortService = "";
        IsAddingPort  = false;
    }
}

/// <summary>
/// View-model for the Port Scanner tab. Performs a parallel TCP-connect scan and
/// reports only open ports to <see cref="Results"/>.
/// </summary>
public class PortScannerViewModel : ViewModelBase
{
    private static readonly Dictionary<int, string> WellKnown = new()
    {
        {   20, "FTP Data"      }, {   21, "FTP"           }, {   22, "SSH"           },
        {   23, "Telnet"        }, {   25, "SMTP"          }, {   53, "DNS"           },
        {   67, "DHCP"          }, {   80, "HTTP"          }, {  110, "POP3"          },
        {  119, "NNTP"          }, {  123, "NTP"           }, {  143, "IMAP"          },
        {  161, "SNMP"          }, {  179, "BGP"           }, {  389, "LDAP"          },
        {  443, "HTTPS"         }, {  445, "SMB"           }, {  465, "SMTPS"         },
        {  587, "SMTP Submit"   }, {  636, "LDAPS"         }, {  993, "IMAPS"         },
        {  995, "POP3S"         }, { 1194, "OpenVPN"       }, { 1433, "MSSQL"         },
        { 1723, "PPTP"          }, { 2049, "NFS"           }, { 2082, "cPanel"        },
        { 2083, "cPanel SSL"    }, { 3306, "MySQL"         }, { 3389, "RDP"           },
        { 5432, "PostgreSQL"    }, { 5900, "VNC"           }, { 6379, "Redis"         },
        { 6443, "K8s API"       }, { 7001, "WebLogic"      }, { 8080, "HTTP Alt"      },
        { 8443, "HTTPS Alt"     }, { 8888, "HTTP Alt 2"    }, { 9200, "Elasticsearch" },
        { 9300, "ES Transport"  }, { 9418, "Git"           }, {27017, "MongoDB"       },
    };

    private readonly ObservableCollection<PingEntryViewModel> _pingEntries;
    private CancellationTokenSource? _cts;

    private string _host              = "";
    private int    _timeoutMs         = 1000;
    private int    _maxConcurrent     = 200;
    private bool   _isRunning;
    private int    _progressPercent;
    private string _statusText        = "Ready";
    private string _selectedPortsSummary = "20 ports selected";

    public ObservableCollection<string>              AllHosts       { get; } = new();
    public ObservableCollection<PortResult>          Results        { get; } = new();
    public ObservableCollection<PortCategoryViewModel> PortCategories { get; } = new();

    public string Host
    {
        get => _host;
        set { SetField(ref _host, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public int TimeoutMs
    {
        get => _timeoutMs;
        set => SetField(ref _timeoutMs, value);
    }

    public int MaxConcurrent
    {
        get => _maxConcurrent;
        set => SetField(ref _maxConcurrent, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set => SetField(ref _progressPercent, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    /// <summary>Summary label shown on the port-picker toggle button (e.g. "20 ports selected").</summary>
    public string SelectedPortsSummary
    {
        get => _selectedPortsSummary;
        private set => SetField(ref _selectedPortsSummary, value);
    }

    private bool   _isAddingCategory;
    private string _newCategoryName  = "";

    public bool   IsAddingCategory { get => _isAddingCategory; set => SetField(ref _isAddingCategory, value); }
    public string NewCategoryName  { get => _newCategoryName;  set => SetField(ref _newCategoryName, value); }

    public RelayCommand ScanCommand               { get; }
    public RelayCommand CancelCommand             { get; }
    public RelayCommand ClearCommand              { get; }
    public RelayCommand SelectAllCommand          { get; }
    public RelayCommand DeselectAllCommand        { get; }
    public RelayCommand ShowAddCategoryCommand    { get; }
    public RelayCommand CommitAddCategoryCommand  { get; }
    public RelayCommand CancelAddCategoryCommand  { get; }

    public PortScannerViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries  = pingEntries;
        ScanCommand   = new RelayCommand(OnScan,   () => !string.IsNullOrWhiteSpace(Host) && !IsRunning);
        CancelCommand = new RelayCommand(OnCancel, () => IsRunning);
        ClearCommand  = new RelayCommand(OnClear,  () => !IsRunning);

        SelectAllCommand   = new RelayCommand(OnSelectAll);
        DeselectAllCommand = new RelayCommand(OnDeselectAll);

        ShowAddCategoryCommand   = new RelayCommand(() => IsAddingCategory = true);
        CommitAddCategoryCommand = new RelayCommand(OnCommitAddCategory,
            () => !string.IsNullOrWhiteSpace(NewCategoryName));
        CancelAddCategoryCommand = new RelayCommand(() =>
        {
            IsAddingCategory = false;
            NewCategoryName  = "";
        });

        pingEntries.CollectionChanged += (_, _) => RebuildAllHosts();
        RebuildAllHosts();
        InitPortCategories();
    }

    private void RebuildAllHosts()
    {
        AllHosts.Clear();
        foreach (var e in _pingEntries)
            if (!string.IsNullOrWhiteSpace(e.IpAddress))
                AllHosts.Add(e.IpAddress);
    }

    // ── Command handlers ─────────────────────────────────────────────────────

    private void OnScan()
    {
        var host = Host.Trim();
        if (string.IsNullOrEmpty(host)) return;

        var ports = GetSelectedPorts();
        if (ports.Count == 0) { StatusText = "No ports selected — use the port picker to choose ports."; return; }

        Results.Clear();
        ProgressPercent = 0;
        _cts      = new CancellationTokenSource();
        IsRunning = true;
        StatusText = $"Scanning {ports.Count} ports on {host}...";

        _ = Task.Run(() => RunScan(host, ports, _cts.Token));
    }

    private async Task RunScan(string host, List<int> ports, CancellationToken ct)
    {
        // Verify host resolves before flooding with connect attempts
        try   { await System.Net.Dns.GetHostEntryAsync(host, ct); }
        catch { Dispatch(() => { StatusText = $"Cannot resolve '{host}'."; IsRunning = false; }); return; }

        int total   = ports.Count;
        int scanned = 0;
        int openCnt = 0;
        var sem     = new SemaphoreSlim(MaxConcurrent);
        var timeout = TimeoutMs;

        var tasks = ports.Select(async port =>
        {
            await sem.WaitAsync(ct);
            try
            {
                bool open = await CheckPort(host, port, timeout, ct);
                int  s    = Interlocked.Increment(ref scanned);

                Dispatch(() =>
                {
                    if (open)
                    {
                        WellKnown.TryGetValue(port, out var svc);
                        // Insert in ascending port order
                        int idx = 0;
                        while (idx < Results.Count && Results[idx].Port < port) idx++;
                        Results.Insert(idx, new PortResult { Port = port, ServiceName = svc ?? "" });
                        openCnt = Results.Count;
                    }
                    ProgressPercent = s * 100 / total;
                    StatusText      = $"Scanning {s}/{total}  |  Open: {Results.Count}";
                });
            }
            finally { sem.Release(); }
        }).ToList();

        try   { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }

        Dispatch(() =>
        {
            IsRunning       = false;
            ProgressPercent = ct.IsCancellationRequested ? ProgressPercent : 100;
            StatusText      = ct.IsCancellationRequested
                ? $"Cancelled — {scanned}/{total} scanned  |  Open: {Results.Count}"
                : $"Complete — {total} ports scanned  |  Open: {Results.Count}";
        });
    }

    private static async Task<bool> CheckPort(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeoutMs);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, linked.Token);
            return true;
        }
        catch { return false; }
    }

    private void OnCancel() => _cts?.Cancel();

    private void OnClear()
    {
        Results.Clear();
        StatusText      = "Ready";
        ProgressPercent = 0;
    }

    // ── Select all / deselect all ─────────────────────────────────────────────

    private void OnSelectAll()
    {
        foreach (var cat in PortCategories)
            cat.IsChecked = true;
    }

    private void OnDeselectAll()
    {
        foreach (var cat in PortCategories)
            cat.IsChecked = false;
    }

    // ── Add category ──────────────────────────────────────────────────────────

    private void OnCommitAddCategory()
    {
        var name = NewCategoryName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var cat = CreateCategory(name);
        PortCategories.Add(cat);
        IsAddingCategory = false;
        NewCategoryName  = "";
        UpdateSelectedPortsSummary();
    }

    // ── Port category initialisation ─────────────────────────────────────────

    private void InitPortCategories()
    {
        var defaults = new HashSet<int> { 21, 22, 23, 25, 53, 80, 110, 143, 443, 445, 587, 993, 995, 1433, 3306, 3389, 5432, 5900, 8080, 8443 };

        var defs = new (string Name, (int Port, string Svc)[] Ports)[]
        {
            ("🌐  Web",            new[] { (80,"HTTP"),    (443,"HTTPS"),      (8080,"HTTP Alt"),  (8443,"HTTPS Alt"), (8888,"HTTP Alt 2") }),
            ("📧  Mail",           new[] { (25,"SMTP"),    (110,"POP3"),        (143,"IMAP"),        (465,"SMTPS"),     (587,"SMTP Submit"), (993,"IMAPS"), (995,"POP3S") }),
            ("🗄  Database",       new[] { (1433,"MSSQL"), (3306,"MySQL"),     (5432,"PostgreSQL"), (6379,"Redis"),    (9200,"Elasticsearch"), (27017,"MongoDB") }),
            ("📁  File Transfer",  new[] { (20,"FTP Data"), (21,"FTP"),        (2049,"NFS") }),
            ("🖥  Remote Access",  new[] { (22,"SSH"),     (23,"Telnet"),       (3389,"RDP"),        (5900,"VNC") }),
            ("🔗  Network",        new[] { (53,"DNS"),     (67,"DHCP"),         (123,"NTP"),         (161,"SNMP"),      (179,"BGP"),  (389,"LDAP"), (636,"LDAPS") }),
            ("⚙  Other",           new[] { (119,"NNTP"),   (445,"SMB"),        (1194,"OpenVPN"),    (1723,"PPTP"),     (2082,"cPanel"), (2083,"cPanel SSL"), (6443,"K8s API"), (7001,"WebLogic"), (9300,"ES Transport"), (9418,"Git") }),
        };

        foreach (var (name, ports) in defs)
        {
            var cat = CreateCategory(name);
            foreach (var (port, svc) in ports)
                cat.AddPort(port, svc, defaults.Contains(port));
            PortCategories.Add(cat);
        }

        UpdateSelectedPortsSummary();
    }

    private PortCategoryViewModel CreateCategory(string name)
    {
        var cat = new PortCategoryViewModel(name, UpdateSelectedPortsSummary);
        cat.DeleteCommand = new RelayCommand(() => { PortCategories.Remove(cat); UpdateSelectedPortsSummary(); });
        return cat;
    }

    private void UpdateSelectedPortsSummary()
    {
        int n = GetSelectedPorts().Count;
        SelectedPortsSummary = n == 0 ? "No ports selected" : $"{n} port{(n == 1 ? "" : "s")} selected";
    }

    private List<int> GetSelectedPorts() =>
        PortCategories
            .SelectMany(c => c.Ports)
            .Where(p => p.IsChecked)
            .Select(p => p.Port)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
