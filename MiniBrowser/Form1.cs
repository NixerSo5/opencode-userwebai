using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;

namespace MiniBrowser;

public partial class Form1 : Form
{
    private const string HomePage = "https://chat.deepseek.com/";
    private const int MaxTextBodyChars = 64 * 1024;

    private static readonly (string Name, string Url)[] Sites =
    {
        ("DeepSeek", "https://chat.deepseek.com/"),
        ("ChatGPT", "https://chatgpt.com/"),
        ("Gemini", "https://gemini.google.com/"),
    };
    private const int MaxBase64Chars = 8 * 1024;

    private static readonly string[] CdpEvents =
    {
        "Network.requestWillBeSent",
        "Network.responseReceived",
        "Network.loadingFinished",
        "Network.loadingFailed",
    };

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MiniBrowser");
    private static readonly string LogFile = Path.Combine(LogDir, "request-log.jsonl");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<string, RequestRecord> _pending = new();
    private readonly Dictionary<string, CoreWebView2DevToolsProtocolEventReceiver> _receivers = new();

    private bool _ready;
    private bool _recording;
    private int _recordCount;

    private sealed class RequestRecord
    {
        public string? Time { get; set; }
        public string? Method { get; set; }
        public string? Url { get; set; }
        public string? Query { get; set; }
        public string? PostData { get; set; }
        public int? Status { get; set; }
        public string? MimeType { get; set; }
        public string? ResponseBody { get; set; }
        public bool? ResponseBodyBase64 { get; set; }
        public string? Error { get; set; }
    }

    public Form1()
    {
        InitializeComponent();
        lblCount.ToolTipText = LogFile;
        foreach (var site in Sites) siteCombo.Items.Add(site.Name);
        siteCombo.SelectedIndex = 0;
        siteCombo.SelectedIndexChanged += (_, _) => UpdateSiteHint();
        addressBox.TextBox.KeyDown += AddressBox_KeyDown;
        btnBack.Click += (_, _) =>
        {
            if (_ready && webView.CoreWebView2.CanGoBack) webView.CoreWebView2.GoBack();
        };
        btnForward.Click += (_, _) =>
        {
            if (_ready && webView.CoreWebView2.CanGoForward) webView.CoreWebView2.GoForward();
        };
        btnRefresh.Click += (_, _) =>
        {
            if (_ready) webView.CoreWebView2.Reload();
        };
        btnGo.Click += (_, _) => NavigateFromAddressBar();
        btnJump.Click += (_, _) => JumpToSelectedSite();
        btnRecord.Click += (_, _) => ToggleRecording();
        btnClear.Click += (_, _) => ClearRecords();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiniBrowser", "WebView2");

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "未找到 Microsoft Edge WebView2 运行时。\n\n" +
                "请从 Microsoft 官网下载安装后重试。",
                "MiniBrowser",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "WebView2 初始化失败：\n" + ex.Message,
                "MiniBrowser",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = webView.CoreWebView2;
        core.SourceChanged += (_, _) => SyncAddress();
        core.HistoryChanged += (_, _) => UpdateNavState();
        core.DocumentTitleChanged += (_, _) => Text =
            string.IsNullOrEmpty(core.DocumentTitle) ? "MiniBrowser"
            : core.DocumentTitle + " - MiniBrowser";
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            core.Navigate(args.Uri);
        };

        _ready = true;
        btnRecord.Enabled = true;
        btnClear.Enabled = true;
        if (File.Exists(LogFile))
        {
            _recordCount = File.ReadAllLines(LogFile).Length;
            UpdateCountLabel();
        }
        await InitChatServiceAsync();
        UpdateNavState();
        core.Navigate(HomePage);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeChatService();
        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (addressBox is null || toolStrip is null) return;

        var used = toolStrip.Items.Cast<ToolStripItem>()
            .Where(i => i != addressBox)
            .Sum(i => i.Width + i.Margin.Horizontal);
        used += toolStrip.Padding.Horizontal + 8;
        addressBox.Width = Math.Max(200, toolStrip.Width - used);
    }

    private void AddressBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            NavigateFromAddressBar();
        }
    }

    private void NavigateFromAddressBar()
    {
        if (!_ready) return;

        var input = addressBox.Text.Trim();
        if (input.Length == 0) return;

        webView.CoreWebView2.Navigate(ResolveInput(input));
        addressBox.TextBox.Focus();
    }

    private void UpdateSiteHint()
    {
        if (siteCombo.SelectedIndex < 0) return;
        siteCombo.ToolTipText = Sites[siteCombo.SelectedIndex].Url;
    }

    private void JumpToSelectedSite()
    {
        if (!_ready || siteCombo.SelectedIndex < 0) return;
        var url = Sites[siteCombo.SelectedIndex].Url;
        addressBox.Text = url;
        webView.CoreWebView2.Navigate(url);
    }

    private static string ResolveInput(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return input;
        }

        if (!input.Contains(' ') &&
            (input.Contains('.') || input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return "https://" + input;
        }

        return "https://www.bing.com/search?q=" + Uri.EscapeDataString(input);
    }

    private void SyncAddress()
    {
        if (!_ready) return;
        var source = webView.CoreWebView2.Source;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            !string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
        {
            addressBox.Text = uri.AbsoluteUri;
            _currentUrl = uri.AbsoluteUri;
        }
    }

    private void UpdateNavState()
    {
        if (!_ready) return;
        btnBack.Enabled = webView.CoreWebView2.CanGoBack;
        btnForward.Enabled = webView.CoreWebView2.CanGoForward;
        btnRefresh.Enabled = true;
    }

    private void ToggleRecording() => SetRecording(!_recording);

    private void SetRecording(bool on)
    {
        if (!_ready || on == _recording) return;
        var core = webView.CoreWebView2;

        if (on)
        {
            Directory.CreateDirectory(LogDir);
            foreach (var name in CdpEvents)
            {
                var receiver2 = core.GetDevToolsProtocolEventReceiver(name);
                receiver2.DevToolsProtocolEventReceived += OnCdpEvent;
                _receivers[name] = receiver2;
            }
            _ = core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
            _recording = true;
            btnRecord.Text = "停止记录";
            btnRecord.ToolTipText = "正在记录，日志写入：" + LogFile;
        }
        else
        {
            foreach (var receiver in _receivers.Values)
            {
                receiver.DevToolsProtocolEventReceived -= OnCdpEvent;
            }
            _receivers.Clear();
            _ = core.CallDevToolsProtocolMethodAsync("Network.disable", "{}");
            _pending.Clear();
            _recording = false;
            btnRecord.Text = "开始记录";
        }
    }

    private void ClearRecords()
    {
        _pending.Clear();
        _recordCount = 0;
        UpdateCountLabel();
        try
        {
            if (File.Exists(LogFile)) File.WriteAllText(LogFile, string.Empty);
        }
        catch (IOException)
        {
        }
    }

    private void UpdateCountLabel()
    {
        lblCount.Text = _recordCount > 0 ? $"已记录 {_recordCount} 条" : string.Empty;
    }

    private void OnCdpEvent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (!_recording || sender is not CoreWebView2DevToolsProtocolEventReceiver receiver) return;
        var name = _receivers.FirstOrDefault(kv => ReferenceEquals(kv.Value, receiver)).Key;
        if (name is null) return;
        try
        {
            HandleCdpEvent(name, e.ParameterObjectAsJson);
        }
        catch (JsonException)
        {
        }
    }

    private void HandleCdpEvent(string name, string paramsJson)
    {
        using var doc = JsonDocument.Parse(paramsJson);
        var p = doc.RootElement;

        switch (name)
        {
            case "Network.requestWillBeSent":
            {
                var id = p.GetProperty("requestId").GetString();
                var request = p.GetProperty("request");
                var url = request.GetProperty("url").GetString();
                if (id is null || url is null ||
                    url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

                _pending[id] = new RequestRecord
                {
                    Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    Method = request.TryGetProperty("method", out var m) ? m.GetString() : null,
                    Url = url,
                    Query = QueryOf(url),
                    PostData = request.TryGetProperty("postData", out var pd) ? pd.GetString() : null,
                };
                break;
            }
            case "Network.responseReceived":
            {
                var id = p.GetProperty("requestId").GetString();
                if (id is null || !_pending.TryGetValue(id, out var record)) return;
                var response = p.GetProperty("response");
                if (response.TryGetProperty("status", out var st))
                {
                    record.Status = (int)st.GetDouble();
                }
                if (response.TryGetProperty("mimeType", out var mt))
                {
                    record.MimeType = mt.GetString();
                }
                break;
            }
            case "Network.loadingFinished":
            {
                var id = p.GetProperty("requestId").GetString();
                if (id is not null) _ = FinishRecordAsync(id);
                break;
            }
            case "Network.loadingFailed":
            {
                var id = p.GetProperty("requestId").GetString();
                if (id is null || !_pending.TryGetValue(id, out var record)) return;
                record.Error = p.TryGetProperty("errorText", out var et) ? et.GetString() : "failed";
                _pending.Remove(id);
                WriteRecord(record);
                break;
            }
        }
    }

    private async Task FinishRecordAsync(string requestId)
    {
        if (!_pending.TryGetValue(requestId, out var record)) return;
        _pending.Remove(requestId);

        try
        {
            var json = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Network.getResponseBody",
                $"{{\"requestId\":\"{requestId}\"}}");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
            if (!string.IsNullOrEmpty(body))
            {
                var isBase64 = root.TryGetProperty("base64Encoded", out var b) && b.GetBoolean();
                if (isBase64)
                {
                    record.ResponseBodyBase64 = true;
                    record.ResponseBody = body.Length > MaxBase64Chars ? body[..MaxBase64Chars] : body;
                }
                else
                {
                    record.ResponseBody = body.Length > MaxTextBodyChars ? body[..MaxTextBodyChars] : body;
                }
            }
        }
        catch
        {
            // 响应体不可用（缓存、重定向、页面已跳转等）
        }

        WriteRecord(record);
    }

    private void WriteRecord(RequestRecord record)
    {
        try
        {
            File.AppendAllText(LogFile, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
            _recordCount++;
            UpdateCountLabel();
        }
        catch (IOException)
        {
        }
    }

    private static string QueryOf(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Query.TrimStart('?') : string.Empty;
    }
}
