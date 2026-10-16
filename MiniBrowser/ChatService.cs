using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace MiniBrowser;

public partial class Form1
{
    private const string ServicePrefix = "http://localhost:8787/";

    private const string EnterKeyDownJson =
        """{"type":"rawKeyDown","windowsVirtualKeyCode":13,"nativeVirtualKeyCode":13,"key":"Enter","code":"Enter","text":"\r"}""";

    private const string EnterKeyUpJson =
        """{"type":"keyUp","windowsVirtualKeyCode":13,"nativeVirtualKeyCode":13,"key":"Enter","code":"Enter"}""";

    private const string InjectedScript = """
    (function () {
      if (window.__minibrowser_hooked) return;
      window.__minibrowser_hooked = true;

      function post(obj) {
        try { window.chrome && window.chrome.webview && window.chrome.webview.postMessage(JSON.stringify(obj)); } catch (e) {}
      }

      function isCompletionUrl(u) {
        if (!u || typeof u !== 'string') return false;
        if (u.indexOf('/api/v0/chat/completion') !== -1) return true;
        if (u.indexOf('/backend-api/f/conversation') !== -1) return true;
        if (u.indexOf('/backend-api/conversation') !== -1) return true;
        if (u.indexOf('/conversation/updates') !== -1) return true;
        return false;
      }

      function gemRead(root) {
        var out = '';
        function walk(n) {
          if (n.nodeType === 3) { out += n.nodeValue; return; }
          if (n.nodeType !== 1) return;
          var tag = n.tagName;
          if (tag === 'CODE-BLOCK' || tag === 'PRE') {
            var codeEl = n.querySelector('code') || n;
            var code = (codeEl.innerText || '').replace(/[\s\u00a0]+$/, '');
            var isTool = /^\s*\{/.test(code) && /"name"\s*:/.test(code) && /"arguments"\s*:/.test(code);
            if (out && out.charAt(out.length - 1) !== '\n') out += '\n';
            out += '```' + (isTool ? 'tool_call' : '') + '\n' + code + '\n```\n';
            return;
          }
          if (tag === 'BR') { out += '\n'; return; }
          var kids = n.childNodes;
          for (var i = 0; i < kids.length; i++) walk(kids[i]);
          if (/^(P|DIV|LI|UL|OL|H[1-6]|TABLE|TR|BLOCKQUOTE|RESPONSE-ELEMENT|MESSAGE-CONTENT)$/.test(tag)) out += '\n';
        }
        var cs = root.childNodes;
        for (var i = 0; i < cs.length; i++) walk(cs[i]);
        return out.replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
      }

      function captureResponse(resp, url) {
        try {
          var clone = resp.clone();
          if (clone.body && clone.body.getReader) {
            var reader = clone.body.getReader();
            var dec = new TextDecoder();
            var acc = '';
            (function read() {
              reader.read().then(function (r) {
                if (r.done) {
                  window.__minibrowser_last = { url: url, body: acc };
                  post({ type: 'completion', format: 'sse', url: url, body: acc });
                }
                else { acc += dec.decode(r.value, { stream: true }); read(); }
              }).catch(function () { post({ type: 'completion', format: 'sse', url: url, body: acc, error: 'stream' }); });
            })();
          } else if (clone.text) {
            clone.text().then(function (t) {
              window.__minibrowser_last = { url: url, body: t };
              post({ type: 'completion', format: 'sse', url: url, body: t });
            });
          }
        } catch (e) { post({ type: 'completion', format: 'sse', url: url, body: '', error: String(e) }); }
      }

      var origFetch = window.fetch;
      window.fetch = function (input, init) {
        var url = '';
        try { url = (typeof input === 'string') ? input : (input && input.url) || ''; } catch (e) {}
        var p = origFetch.apply(this, arguments);
        try {
          if (isCompletionUrl(url)) {
            window.__minibrowser_seen = (window.__minibrowser_seen || 0) + 1;
            p.then(function (resp) { captureResponse(resp, url); });
          }
        } catch (e) {}
        return p;
      };

      var origOpen = XMLHttpRequest.prototype.open;
      var origSend = XMLHttpRequest.prototype.send;
      XMLHttpRequest.prototype.open = function (m, u) { try { this.__mb_url = u; } catch (e) {} return origOpen.apply(this, arguments); };
      XMLHttpRequest.prototype.send = function (body) {
        var xhr = this;
        try {
          if (isCompletionUrl(xhr.__mb_url)) {
            window.__minibrowser_seen = (window.__minibrowser_seen || 0) + 1;
            xhr.addEventListener('load', function () {
              window.__minibrowser_last = { url: xhr.__mb_url, body: xhr.responseText || '' };
              post({ type: 'completion', format: 'sse', url: xhr.__mb_url, body: xhr.responseText || '' });
            });
          }
        } catch (e) {}
        return origSend.apply(this, arguments);
      };

      window.__minibrowser_send = function (text) {
        try {
          var ed = document.querySelector('#mobile-composer-prompt') ||
                   document.querySelector('#prompt-textarea') ||
                   document.querySelector('#chat-input') ||
                   document.querySelector('.ql-editor[contenteditable="true"]') ||
                   document.querySelector('textarea[name="prompt"]') ||
                   document.querySelector('div[contenteditable="true"]') ||
                   document.querySelector('textarea');
          if (!ed) return 'no-editor';
          ed.focus();
          if (ed.tagName === 'TEXTAREA') {
            var setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
            setter.call(ed, text);
            ed.dispatchEvent(new Event('input', { bubbles: true }));
            ed.dispatchEvent(new Event('change', { bubbles: true }));
            return 'textarea-ok';
          }
          var sel = window.getSelection();
          var range = document.createRange();
          range.selectNodeContents(ed);
          sel.removeAllRanges();
          sel.addRange(range);
          var ok = document.execCommand('insertText', false, text);
          return ok ? 'inserted' : 'insert-failed';
        } catch (e) { return 'error:' + e; }
      };

      window.__minibrowser_click_send = function () {
        try {
          if (location.host.indexOf('gemini.google.com') !== -1) {
            var ged = document.querySelector('.ql-editor[contenteditable="true"]') ||
                      document.querySelector('div[contenteditable="true"]') ||
                      document.querySelector('rich-textarea');
            if (!ged) return 'no-editor';
            ged.focus();
            var ticks = 0;
            var timer = setInterval(function () {
              ticks++;
              var ed2 = document.querySelector('.ql-editor[contenteditable="true"]') || ged;
              if (!ed2 || !(ed2.innerText || '').trim()) { clearInterval(timer); return; }
              var sb = document.querySelector('button[aria-label="发送"]') ||
                       document.querySelector('button[aria-label="Send message"]') ||
                       document.querySelector('button[aria-label="Send"]');
              if ((sb && !sb.disabled) || ticks >= 8) {
                clearInterval(timer);
                ed2.focus();
                ed2.dispatchEvent(new KeyboardEvent('keydown', {
                  key: 'Enter', code: 'Enter', keyCode: 13, which: 13,
                  bubbles: true, cancelable: true
                }));
              }
            }, 250);
            return 'gemini-enter';
          }
          var selectors = [
            'button[data-testid="send-button"]',
            'button[data-testid="fruitjuice-send-button"]',
            'button[aria-label="发送消息"]',
            'button[aria-label="Send prompt"]',
            'button[aria-label="Send message"]'
          ];
          for (var i = 0; i < selectors.length; i++) {
            var sb = document.querySelector(selectors[i]);
            if (sb && !sb.disabled) { sb.click(); return 'clicked'; }
          }
          var t = document.querySelector('textarea') ||
                  document.querySelector('div[contenteditable="true"]');
          var node = t;
          while (node && node.querySelectorAll) {
            var cands = node.querySelectorAll('[role=button].ds-button--primary, button.ds-button--primary');
            if (cands.length) { cands[0].click(); return 'clicked-primary'; }
            node = node.parentElement;
          }
          var all = document.querySelectorAll('[role=button], button');
          for (var j = 0; j < all.length; j++) {
            var label = (all[j].getAttribute('aria-label') || '') + ' ' + (all[j].getAttribute('title') || '');
            if (/send|发送/i.test(label)) { all[j].click(); return 'clicked-aria'; }
          }
          return 'no-button';
        } catch (e) { return 'error:' + e; }
      };

      window.__minibrowser_pending_text = function () {
        try {
          var ed = document.querySelector('#mobile-composer-prompt') ||
                   document.querySelector('#prompt-textarea') ||
                   document.querySelector('#chat-input') ||
                   document.querySelector('.ql-editor[contenteditable="true"]') ||
                   document.querySelector('div[contenteditable="true"]') ||
                   document.querySelector('textarea');
          if (!ed) return '';
          return ((ed.innerText || ed.value || '') + '').trim();
        } catch (e) { return ''; }
      };

      window.__minibrowser_read_answer = function () {
        try {
          if (location.host.indexOf('gemini.google.com') !== -1) {
            var mrs = document.querySelectorAll('model-response');
            if (mrs.length) {
              var last = mrs[mrs.length - 1];
              var mds = last.querySelectorAll('.markdown');
              if (mds.length) return gemRead(mds[mds.length - 1]);
              var rt = last.querySelector('.model-response-text') ||
                       last.querySelector('message-content');
              return rt ? gemRead(rt) : '';
            }
            var allmd = document.querySelectorAll('.markdown');
            if (allmd.length) return gemRead(allmd[allmd.length - 1]);
            return '';
          }
          var els = document.querySelectorAll('[data-message-author-role="assistant"]');
          if (els.length) return (els[els.length - 1].innerText || '').trim();
          var turns = document.querySelectorAll('article[data-testid^="conversation-turn"]');
          for (var i = turns.length - 1; i >= 0; i--) {
            var role = turns[i].getAttribute('data-message-author-role') || turns[i].getAttribute('data-turn') || '';
            if (role === 'assistant') return (turns[i].innerText || '').trim();
          }
          var root = document.querySelector('[data-testid="mobile-app-shell-scroll-container"]') ||
                     document.querySelector('main') || document.body;
          var txt = (root.innerText || '').replace(/\u00a0/g, ' ');
          var labels = ['ChatGPT \u8bf4\uff1a', 'ChatGPT said:', 'ChatGPT \u8bf4:'];
          var idx = -1, hit = '';
          for (var k = 0; k < labels.length; k++) {
            var p = txt.lastIndexOf(labels[k]);
            if (p > idx) { idx = p; hit = labels[k]; }
          }
          if (idx >= 0) {
            var out = txt.slice(idx + hit.length).trim();
            out = out.split('ChatGPT \u662f AI')[0].split('ChatGPT can make mistakes')[0].trim();
            return out;
          }
          return '';
        } catch (e) { return ''; }
      };

      window.__minibrowser_watch_answer = function () {
        if (window.__mb_watching) return 'watching';
        window.__mb_watching = true;
        var isGem = location.host.indexOf('gemini.google.com') !== -1;
        var baseline = document.querySelectorAll('model-response').length;
        var last = '', stable = 0, ticks = 0;
        var iv = setInterval(function () {
          ticks++;
          var ready = !isGem || document.querySelectorAll('model-response').length > baseline;
          var t = ready ? window.__minibrowser_read_answer() : '';
          if (t && t === last) stable++; else { stable = 0; last = t; }
          if ((stable >= 4 && last) || ticks > 120) {
            clearInterval(iv);
            window.__mb_watching = false;
            post({ type: 'completion', format: 'text', url: location.href, body: last });
          }
        }, 500);
        return 'watching';
      };
    })();
    """;

    private const string FallbackEmptyMessage = "（网页端未返回内容，可能触发限流或页面未就绪，请稍后重试）";

    private const string LimitReachedText = "达到对话长度上限";
    private const string StartNewChatText = "请开启新对话";
    private const string NewConversationUrl = "https://chat.deepseek.com/";
    private const int MaxNewSessionRetries = 2;

    private static readonly Regex ToolCallFenceRegex = new(
        """```tool_call\s*(?<json>[\s\S]*?)```""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private sealed class ToolCallResult
    {
        public string Id { get; } = "call_" + Guid.NewGuid().ToString("N")[..24];
        public string Name { get; init; } = string.Empty;
        public string ArgumentsJson { get; init; } = "{}";
    }

    private sealed class ToolDef
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string ParametersJson { get; init; } = string.Empty;
    }

    private sealed class ChatRequestInfo
    {
        public bool Stream { get; init; }
        public string SystemPrompt { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public List<ToolDef> Tools { get; } = new();
        public List<string> HistoryLines { get; } = new();
    }

    private sealed class CompletionMessage
    {
        public string Format { get; init; } = "sse";
        public string Body { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
    }

    private HttpListener? _listener;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TaskCompletionSource<CompletionMessage>? _pendingCompletion;
    private volatile CompletionMessage? _lastCapture;
    private volatile string _currentUrl = string.Empty;

    private static readonly TimeSpan MinSendInterval = TimeSpan.FromSeconds(3);
    private DateTime _lastSendAt = DateTime.MinValue;

    private async Task InitChatServiceAsync()
    {
        var core = webView.CoreWebView2;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(InjectedScript);
        core.WebMessageReceived += OnPageMessage;

        _listener = new HttpListener();
        _listener.Prefixes.Add(ServicePrefix);
        try
        {
            _listener.Start();
            lblService.Text = "API: " + ServicePrefix.TrimEnd('/');
            lblService.ToolTipText = "POST " + ServicePrefix.TrimEnd('/') + "/chat  {\"prompt\":\"...\"}";
        }
        catch (Exception ex)
        {
            lblService.Text = "API 启动失败";
            lblService.ToolTipText = ex.Message;
            return;
        }
        _ = Task.Run(ListenLoopAsync);
    }

    private void DisposeChatService()
    {
        try
        {
            if (_listener is { IsListening: true }) _listener.Stop();
            _listener?.Close();
        }
        catch
        {
        }
    }

    private void OnPageMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var text = e.TryGetWebMessageAsString();
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) ||
                typeEl.GetString() != "completion") return;

            var format = root.TryGetProperty("format", out var f) ? (f.GetString() ?? "sse") : "sse";
            var body = root.TryGetProperty("body", out var b) ? (b.GetString() ?? string.Empty) : string.Empty;
            var url = root.TryGetProperty("url", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;

            var capture = new CompletionMessage { Format = format, Body = body, Url = url };
            _lastCapture = capture;

            if (format == "text" && string.IsNullOrWhiteSpace(body)) return;
            _pendingCompletion?.TrySetResult(capture);
        }
        catch (JsonException)
        {
        }
    }

    private async Task ListenLoopAsync()
    {
        var listener = _listener;
        if (listener is null) return;

        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => HandleRequestAsync(ctx));
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod;

            if (method == "GET" && path == "/health")
            {
                await WriteJsonAsync(res, 200, new
                {
                    ok = true,
                    ready = _ready,
                    page = _currentUrl,
                });
                return;
            }

            if (method == "POST" && path == "/chat")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var bodyText = await reader.ReadToEndAsync();
                var prompt = ExtractPrompt(bodyText);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    await WriteJsonAsync(res, 400, new { ok = false, error = "缺少 prompt / messages" });
                    return;
                }

                var content = await ChatAsync(prompt);
                if (string.IsNullOrWhiteSpace(content))
                {
                    content = FallbackEmptyMessage;
                }
                await WriteJsonAsync(res, 200, new { ok = true, content });
                return;
            }

            if (method == "POST" && path == "/v1/chat/completions")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var bodyText = await reader.ReadToEndAsync();
                var req = ParseChatRequest(bodyText);
                if (string.IsNullOrWhiteSpace(req.Prompt))
                {
                    await WriteJsonAsync(res, 400, new { ok = false, error = "缺少 messages" });
                    return;
                }

                var raw = await ChatAsync(req.Prompt);
                var consumed = new List<string>();
                var toolCalls = ExtractToolCalls(raw, req.Tools, consumed);
                var content = RemoveToolCallBlocks(raw, consumed);
                if (toolCalls.Count == 0 && string.IsNullOrWhiteSpace(content))
                {
                    content = FallbackEmptyMessage;
                }

                if (req.Stream)
                {
                    await WriteChatCompletionStreamAsync(res, content, toolCalls);
                }
                else
                {
                    await WriteJsonAsync(res, 200, ChatCompletionPayload(content, toolCalls));
                }
                return;
            }

            if (method == "POST" && path == "/debug/eval")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var js = await reader.ReadToEndAsync();
                var result = await OnUiAsync(async () => await webView.ExecuteScriptAsync(js));
                await WriteJsonAsync(res, 200, new { ok = true, result });
                return;
            }

            if (path == "/debug/record")
            {
                var on = ctx.Request.QueryString["on"] is "1" or "true";
                if (method == "POST")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                    var bodyText = await reader.ReadToEndAsync();
                    try
                    {
                        using var doc = JsonDocument.Parse(bodyText);
                        if (doc.RootElement.TryGetProperty("on", out var onEl) &&
                            (onEl.ValueKind == JsonValueKind.True || onEl.ValueKind == JsonValueKind.False))
                        {
                            on = onEl.ValueKind == JsonValueKind.True;
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
                await OnUiAsync(async () => SetRecording(on));
                await WriteJsonAsync(res, 200, new { ok = true, recording = _recording });
                return;
            }

            if (method == "GET" && path == "/debug/last")
            {
                await WriteJsonAsync(res, 200, new { ok = true, capture = _lastCapture });
                return;
            }

            if (method == "POST" && path == "/debug/extract")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var bodyText = await reader.ReadToEndAsync();
                var extractContent = bodyText;
                var toolDefs = new List<ToolDef>();
                try
                {
                    using var doc = JsonDocument.Parse(bodyText);
                    if (doc.RootElement.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                        extractContent = c.GetString() ?? "";
                    if (doc.RootElement.TryGetProperty("tools", out var ts) && ts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in ts.EnumerateArray())
                        {
                            var name = t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            var desc = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                            var parms = t.TryGetProperty("parameters", out var p)
                                ? (p.ValueKind == JsonValueKind.String ? p.GetString() ?? "{}" : p.GetRawText())
                                : "{}";
                            if (name.Length > 0)
                                toolDefs.Add(new ToolDef { Name = name, Description = desc, ParametersJson = parms });
                        }
                    }
                }
                catch (JsonException)
                {
                }

                var used = new List<string>();
                var extracted = ExtractToolCalls(extractContent, toolDefs, used);
                await WriteJsonAsync(res, 200, new
                {
                    ok = true,
                    count = extracted.Count,
                    calls = extracted.Select(x => new { name = x.Name, arguments = x.ArgumentsJson }).ToList(),
                    content = RemoveToolCallBlocks(extractContent, used),
                });
                return;
            }

            await WriteJsonAsync(res, 404, new { ok = false, error = "not found" });
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(res, 500, new { ok = false, error = ex.Message });
            }
            catch
            {
            }
        }
    }

    private static string ExtractPrompt(string bodyText)
    {
        try
        {
            using var doc = JsonDocument.Parse(bodyText);
            var root = doc.RootElement;

            if (root.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
            {
                string? last = null;
                foreach (var m in msgs.EnumerateArray())
                {
                    if (m.TryGetProperty("role", out var role) &&
                        role.GetString() == "user" &&
                        m.TryGetProperty("content", out var c))
                    {
                        last = c.ValueKind == JsonValueKind.String ? c.GetString() : c.ToString();
                    }
                }
                return last ?? string.Empty;
            }
        }
        catch (JsonException)
        {
        }
        return string.Empty;
    }

    private async Task<string> ChatAsync(string prompt)
    {
        if (!_ready) throw new InvalidOperationException("WebView2 尚未就绪");

        await _sendLock.WaitAsync();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var raw = await SendOnceAsync(prompt);

                var limited = IsConversationLimit(raw);
                if (!limited && string.IsNullOrWhiteSpace(raw) && IsDeepSeekPage())
                {
                    limited = await PageContainsAsync(LimitReachedText) ||
                              await PageContainsAsync(StartNewChatText);
                }

                if (!limited || attempt >= MaxNewSessionRetries || !IsDeepSeekPage())
                {
                    return raw;
                }

                await StartNewConversationAsync();
            }
        }
        finally
        {
            _pendingCompletion = null;
            _sendLock.Release();
        }
    }

    private async Task<string> SendOnceAsync(string prompt)
    {
        var wait = MinSendInterval - (DateTime.UtcNow - _lastSendAt);
        if (wait > TimeSpan.Zero) await Task.Delay(wait);
        _lastSendAt = DateTime.UtcNow;

        var tcs = new TaskCompletionSource<CompletionMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCompletion = tcs;
        try
        {
            var insertStatus = await OnUiAsync(async () => JsonSerializer.Deserialize<string>(
                await webView.ExecuteScriptAsync(
                    "window.__minibrowser_send(" + JsonSerializer.Serialize(prompt) + ")")));

            if (insertStatus is not ("inserted" or "textarea-ok"))
            {
                throw new InvalidOperationException(
                    "无法写入输入框（" + insertStatus + "），请确认当前页面是 chat.deepseek.com、chatgpt.com 或 gemini.google.com 且已登录");
            }

            await OnUiAsync(async () =>
                await webView.ExecuteScriptAsync("window.__minibrowser_click_send()"));

            if (IsChatGptPage() || IsGeminiPage())
            {
                await OnUiAsync(async () =>
                    await webView.ExecuteScriptAsync("window.__minibrowser_watch_answer()"));
            }

            if (await Task.WhenAny(tcs.Task, Task.Delay(6000)) != tcs.Task)
            {
                var pending = await OnUiAsync(async () => JsonSerializer.Deserialize<string>(
                    await webView.ExecuteScriptAsync("window.__minibrowser_pending_text()")));
                if (!string.IsNullOrWhiteSpace(pending))
                {
                    await OnUiAsync(async () =>
                    {
                        await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                            "Input.dispatchKeyEvent", EnterKeyDownJson);
                        await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                            "Input.dispatchKeyEvent", EnterKeyUpJson);
                    });
                }
            }

            var message = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(120));
            return message.Format == "text" ? message.Body : ParseSse(message.Body);
        }
        finally
        {
            _pendingCompletion = null;
        }
    }

    private async Task StartNewConversationAsync()
    {
        await OnUiAsync(() =>
        {
            webView.CoreWebView2.Navigate(NewConversationUrl);
            return Task.CompletedTask;
        });

        for (var i = 0; i < 80; i++)
        {
            await Task.Delay(500);
            var ready = await OnUiAsync(async () =>
                await webView.ExecuteScriptAsync("!!document.querySelector('textarea')"));
            if (ready == "true") return;
        }
    }

    private async Task<bool> PageContainsAsync(string marker)
    {
        var json = await OnUiAsync(async () => await webView.ExecuteScriptAsync(
            "!!(document.body && document.body.innerText && document.body.innerText.indexOf(" +
            JsonSerializer.Serialize(marker) + ") !== -1)"));
        return json == "true";
    }

    private static bool IsConversationLimit(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Contains(LimitReachedText, StringComparison.Ordinal) ||
               text.Contains(StartNewChatText, StringComparison.Ordinal);
    }

    private bool IsChatGptPage()
    {
        var url = _currentUrl;
        return url.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("chat.openai.com", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsDeepSeekPage()
    {
        var url = _currentUrl;
        return !string.IsNullOrEmpty(url) &&
               url.Contains("chat.deepseek.com", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsGeminiPage()
    {
        return _currentUrl.Contains("gemini.google.com", StringComparison.OrdinalIgnoreCase);
    }

    private Task<T> OnUiAsync<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        webView.BeginInvoke(new Action(async () =>
        {
            try
            {
                tcs.TrySetResult(await work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }));
        return tcs.Task;
    }

    private async Task OnUiAsync(Func<Task> work)
    {
        await OnUiAsync(async () =>
        {
            await work();
            return true;
        });
    }

    private static string ParseSse(string sse)
    {
        var sb = new StringBuilder();
        string? errorContent = null;
        string? evt = null;

        foreach (var raw in sse.Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                evt = line[6..].Trim();
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var json = line[5..].Trim();
            if (json.Length == 0) continue;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (evt == "hint")
                {
                    if (root.TryGetProperty("content", out var hc) && hc.ValueKind == JsonValueKind.String)
                    {
                        errorContent = hc.GetString();
                    }
                    continue;
                }

                if (TryParseChatGptMessage(root, out var gptText))
                {
                    if (!string.IsNullOrEmpty(gptText))
                    {
                        sb.Clear();
                        sb.Append(gptText);
                    }
                    continue;
                }

                if (!root.TryGetProperty("v", out var v)) continue;
                if (root.TryGetProperty("p", out var pathEl))
                {
                    var path = pathEl.GetString() ?? string.Empty;
                    var op = root.TryGetProperty("o", out var o) ? o.GetString() : null;
                    if (path.Contains("content") && op == "APPEND" && v.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(v.GetString());
                    }
                    continue;
                }

                if (v.ValueKind == JsonValueKind.String)
                {
                    sb.Append(v.GetString());
                }
                else if (v.ValueKind == JsonValueKind.Object &&
                         v.TryGetProperty("response", out var response) &&
                         response.TryGetProperty("fragments", out var fragments) &&
                         fragments.ValueKind == JsonValueKind.Array &&
                         fragments.GetArrayLength() > 0)
                {
                    var content = LastResponseContent(fragments);
                    if (content is not null)
                    {
                        sb.Clear();
                        sb.Append(content);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }
        return sb.Length > 0 ? sb.ToString() : (errorContent ?? string.Empty);
    }

    private static bool TryParseChatGptMessage(JsonElement root, out string text)
    {
        text = string.Empty;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("message", out var msg) ||
            msg.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var role = msg.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object
            ? GetString(author, "role")
            : null;
        if (role is not null && role != "assistant") return true;

        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object &&
            content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String) sb.Append(part.GetString());
            }
            text = sb.ToString();
        }
        return true;
    }

    private static string? LastResponseContent(JsonElement fragments)
    {
        string? found = null;
        foreach (var f in fragments.EnumerateArray())
        {
            if (f.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                found = c.GetString();
            }
        }
        return found;
    }

    private static ChatRequestInfo ParseChatRequest(string bodyText)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bodyText);
        }
        catch (JsonException)
        {
            return new ChatRequestInfo();
        }

        using (doc)
        {
            var root = doc.RootElement;
            var req = new ChatRequestInfo
            {
                Stream = root.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.True,
            };

            if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tools.EnumerateArray())
                {
                    var fn = t.ValueKind == JsonValueKind.Object && t.TryGetProperty("function", out var f) ? f : t;
                    var name = GetString(fn, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var parameters = string.Empty;
                    foreach (var key in new[] { "parameters", "inputSchema" })
                    {
                        if (fn.TryGetProperty(key, out var pa) &&
                            (pa.ValueKind == JsonValueKind.Object || pa.ValueKind == JsonValueKind.Array))
                        {
                            parameters = pa.GetRawText();
                            break;
                        }
                    }

                    req.Tools.Add(new ToolDef
                    {
                        Name = name,
                        Description = GetString(fn, "description") ?? string.Empty,
                        ParametersJson = parameters,
                    });
                }
            }

            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in msgs.EnumerateArray())
                {
                    var role = GetString(m, "role") ?? "user";
                    switch (role)
                    {
                        case "system":
                        case "developer":
                            var sc = MessageContent(m);
                            if (!string.IsNullOrWhiteSpace(sc))
                            {
                                req.SystemPrompt = string.IsNullOrWhiteSpace(req.SystemPrompt)
                                    ? sc
                                    : req.SystemPrompt + "\n" + sc;
                            }
                            break;

                        case "assistant":
                            if (m.TryGetProperty("tool_calls", out var tcs) &&
                                tcs.ValueKind == JsonValueKind.Array && tcs.GetArrayLength() > 0)
                            {
                                foreach (var tc in tcs.EnumerateArray())
                                {
                                    var fn = tc.ValueKind == JsonValueKind.Object &&
                                             tc.TryGetProperty("function", out var f) ? f : tc;
                                    var name = GetString(fn, "name") ?? "?";
                                    var args = GetString(fn, "arguments") ?? "{}";
                                    var a = args.TrimStart();
                                    var rendered = a.StartsWith("{") || a.StartsWith("[")
                                        ? "{\"name\":\"" + name + "\",\"arguments\":" + args + "}"
                                        : name + " " + args;
                                    req.HistoryLines.Add("[assistant 调用工具] " + rendered);
                                }
                            }
                            var ac = MessageContent(m);
                            if (!string.IsNullOrWhiteSpace(ac))
                            {
                                req.HistoryLines.Add("[assistant] " + ac);
                            }
                            break;

                        case "tool":
                        case "function":
                            req.HistoryLines.Add("[工具结果] " + MessageContent(m));
                            break;

                        default:
                            req.HistoryLines.Add("[user] " + MessageContent(m));
                            break;
                    }
                }
            }
            else if (root.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String)
            {
                var text = p.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    req.HistoryLines.Add("[user] " + text);
                }
            }

            req.Prompt = BuildPrompt(req);
            return req;
        }
    }

    private static string BuildPrompt(ChatRequestInfo req)
    {
        var sb = new StringBuilder();

        if (req.Tools.Count > 0)
        {
            sb.AppendLine("你是一个可以使用工具的 AI 助手。下面给你可用工具和对话历史。");
            sb.AppendLine();
            sb.AppendLine("## 可用工具");
            foreach (var t in req.Tools)
            {
                sb.AppendLine("- " + t.Name + ": " + t.Description);
                if (!string.IsNullOrWhiteSpace(t.ParametersJson))
                {
                    sb.AppendLine("  参数(JSON Schema): " + t.ParametersJson);
                }
            }
            sb.AppendLine();
            sb.AppendLine("## 工具调用格式");
            sb.AppendLine("当你需要调用工具时，只输出下面这种代码块（一次可以输出多个），不要输出任何其它文字：");
            sb.AppendLine("```tool_call");
            sb.AppendLine("{\"name\":\"工具名\",\"arguments\":{ } }");
            sb.AppendLine("```");
            sb.AppendLine("如果不需要调用工具，就直接用自然语言回答。");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(req.SystemPrompt))
        {
            sb.AppendLine("## 系统指令");
            sb.AppendLine(req.SystemPrompt);
            sb.AppendLine();
        }

        if (req.HistoryLines.Count > 0)
        {
            sb.AppendLine("## 对话历史");
            foreach (var line in req.HistoryLines)
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString().Trim();
    }

    private static List<ToolCallResult> ExtractToolCalls(
        string content, IReadOnlyList<ToolDef> tools, List<string>? consumed = null)
    {
        var calls = new List<ToolCallResult>();
        if (string.IsNullOrEmpty(content)) return calls;

        var knownTools = tools.Count > 0
            ? new HashSet<string>(tools.Select(t => t.Name), StringComparer.Ordinal)
            : null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var obj in ScanJsonObjects(content))
        {
            var before = calls.Count;
            try
            {
                using var doc = JsonDocument.Parse(
                    obj,
                    new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                    });
                CollectToolCalls(doc.RootElement, knownTools, tools, calls);
            }
            catch (JsonException)
            {
            }

            if (calls.Count == before) continue;
            for (var i = calls.Count - 1; i >= before; i--)
            {
                var key = calls[i].Name + " " + calls[i].ArgumentsJson;
                if (seen.Add(key)) continue;
                calls.RemoveAt(i);
            }
            consumed?.Add(obj);
        }

        return calls;
    }

    private static IEnumerable<string> ScanJsonObjects(string text)
    {
        var n = text.Length;
        var i = 0;
        while (i < n)
        {
            if (text[i] != '{')
            {
                i++;
                continue;
            }

            var start = i;
            var depth = 0;
            var inString = false;
            var escaped = false;
            var end = -1;

            for (var j = start; j < n; j++)
            {
                var c = text[j];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (c == '"') inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    if (--depth == 0)
                    {
                        end = j + 1;
                        break;
                    }
                }
            }

            if (end < 0) yield break;
            yield return text[start..end];
            i = end;
        }
    }

    private static void CollectToolCalls(
        JsonElement el, HashSet<string>? knownTools, IReadOnlyList<ToolDef> tools, List<ToolCallResult> output)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                CollectToolCalls(item, knownTools, tools, output);
            }
            return;
        }
        if (el.ValueKind != JsonValueKind.Object) return;

        if (el.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
        {
            CollectToolCalls(tcs, knownTools, tools, output);
            return;
        }

        var name = GetString(el, "name") ?? GetString(el, "tool") ?? GetString(el, "tool_name");

        if (string.IsNullOrWhiteSpace(name) &&
            el.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
        {
            CollectToolCalls(fn, knownTools, tools, output);
            return;
        }

        var argsJson = "{}";
        var hasArgsWrapper = false;
        foreach (var key in new[] { "arguments", "args", "parameters" })
        {
            if (!el.TryGetProperty(key, out var argsEl)) continue;
            hasArgsWrapper = true;
            argsJson = argsEl.ValueKind == JsonValueKind.String
                ? (argsEl.GetString() ?? "{}")
                : argsEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? argsEl.GetRawText()
                    : "{}";
            break;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = InferToolName(hasArgsWrapper ? null : el, tools);
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!hasArgsWrapper) argsJson = el.GetRawText();
        }
        else if (knownTools is { Count: > 0 } && !knownTools.Contains(name))
        {
            return;
        }

        output.Add(new ToolCallResult { Name = name, ArgumentsJson = argsJson });
    }

    private static string? InferToolName(JsonElement? argsObject, IReadOnlyList<ToolDef> tools)
    {
        if (tools.Count == 0) return null;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (argsObject is { ValueKind: JsonValueKind.Object })
        {
            foreach (var p in argsObject.Value.EnumerateObject()) keys.Add(p.Name);
        }

        if (tools.Count == 1)
        {
            var props = GetSchemaPropertyNames(tools[0].ParametersJson);
            return props.Count == 0 || keys.IsSubsetOf(props) ? tools[0].Name : null;
        }

        if (keys.Count == 0) return null;

        string? matched = null;
        foreach (var t in tools)
        {
            var props = GetSchemaPropertyNames(t.ParametersJson);
            if (props.Count == 0 || !keys.IsSubsetOf(props)) continue;
            if (matched is not null && matched != t.Name) return null;
            matched = t.Name;
        }
        return matched;
    }

    private static HashSet<string> GetSchemaPropertyNames(string parametersJson)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(parametersJson)) return names;
        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("properties", out var props) &&
                props.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in props.EnumerateObject()) names.Add(p.Name);
            }
        }
        catch (JsonException)
        {
        }
        return names;
    }

    private static string RemoveToolCallBlocks(string content, List<string>? consumed = null)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var result = ToolCallFenceRegex.Replace(content, string.Empty);
        if (consumed != null)
        {
            foreach (var obj in consumed) result = result.Replace(obj, string.Empty);
        }
        return result.Trim();
    }

    private static string? GetString(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        return el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string MessageContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var c)) return string.Empty;
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? string.Empty;
        if (c.ValueKind != JsonValueKind.Array) return string.Empty;

        var sb = new StringBuilder();
        foreach (var part in c.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                sb.Append(part.GetString());
            }
            else if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            {
                sb.Append(t.GetString());
            }
            else if (part.TryGetProperty("content", out var pc) && pc.ValueKind == JsonValueKind.String)
            {
                sb.Append(pc.GetString());
            }
        }
        return sb.ToString();
    }

    private static object ChatCompletionPayload(string content, IReadOnlyList<ToolCallResult> toolCalls)
    {
        object message = toolCalls.Count > 0
            ? new
            {
                role = "assistant",
                content = string.IsNullOrWhiteSpace(content) ? null : content,
                tool_calls = toolCalls.Select(WireToolCall).ToArray(),
            }
            : new { role = "assistant", content };

        return new
        {
            id = "chatcmpl-" + Guid.NewGuid().ToString("N"),
            @object = "chat.completion",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = "deepseek-web",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message,
                    finish_reason = toolCalls.Count > 0 ? "tool_calls" : "stop",
                },
            },
        };
    }

    private static object WireToolCall(ToolCallResult tc) => new
    {
        id = tc.Id,
        type = "function",
        function = new { name = tc.Name, arguments = tc.ArgumentsJson },
    };

    private static async Task WriteChatCompletionStreamAsync(
        HttpListenerResponse res, string content, IReadOnlyList<ToolCallResult> toolCalls)
    {
        var id = "chatcmpl-" + Guid.NewGuid().ToString("N");
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hasTools = toolCalls.Count > 0;

        res.StatusCode = 200;
        res.ContentType = "text/event-stream; charset=utf-8";
        res.Headers["Cache-Control"] = "no-cache";
        res.SendChunked = true;

        async Task EmitAsync(object chunk)
        {
            var bytes = Encoding.UTF8.GetBytes("data: " + JsonSerializer.Serialize(chunk) + "\n\n");
            await res.OutputStream.WriteAsync(bytes);
            await res.OutputStream.FlushAsync();
        }

        await EmitAsync(new
        {
            id,
            @object = "chat.completion.chunk",
            created,
            model = "deepseek-web",
            choices = new[]
            {
                new { index = 0, delta = new { role = "assistant", content = "" }, finish_reason = (string?)null },
            },
        });

        if (hasTools)
        {
            for (var i = 0; i < toolCalls.Count; i++)
            {
                var tc = toolCalls[i];
                await EmitAsync(new
                {
                    id,
                    @object = "chat.completion.chunk",
                    created,
                    model = "deepseek-web",
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            delta = new
                            {
                                tool_calls = new[]
                                {
                                    new
                                    {
                                        index = i,
                                        id = tc.Id,
                                        type = "function",
                                        function = new { name = tc.Name, arguments = tc.ArgumentsJson },
                                    },
                                },
                            },
                            finish_reason = (string?)null,
                        },
                    },
                });
            }
        }
        else if (content.Length > 0)
        {
            await EmitAsync(new
            {
                id,
                @object = "chat.completion.chunk",
                created,
                model = "deepseek-web",
                choices = new[]
                {
                    new { index = 0, delta = new { content }, finish_reason = (string?)null },
                },
            });
        }

        await EmitAsync(new
        {
            id,
            @object = "chat.completion.chunk",
            created,
            model = "deepseek-web",
            choices = new[]
            {
                new { index = 0, delta = new { }, finish_reason = hasTools ? "tool_calls" : "stop" },
            },
        });

        await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"));
        await res.OutputStream.FlushAsync();
        res.Close();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse res, int status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }
}
