# opencode-userwebai

> Use a real browser (WebView2) to drive web AI chat pages, and expose them to
> [opencode](https://opencode.ai) through a local OpenAI-compatible endpoint —
> no API key required.
>
> 用真实浏览器（WebView2）驱动网页版 AI 对话，并通过本地 OpenAI 兼容接口接入
> [opencode](https://opencode.ai) —— 无需 API Key。

Registered model / 注册模型名：**`opencode-WEB-MIX`**

---

## English

### What it is

`MiniBrowser` is a small C# WinForms app that hosts a Chromium-based WebView2.
You log in to the AI website once inside it. The app then:

1. Injects a script into the page that can type into the chat box and click send.
2. Runs a local HTTP server (`http://localhost:8787`) that turns incoming
   OpenAI-style requests into "type + send" actions on the page, and returns the
   page's answer.

So opencode (or any OpenAI-compatible client) can "call" the web model as if it
were a normal API model.

### Model connection address / 模型连接地址

| Item | Value |
| --- | --- |
| Base URL | `http://localhost:8787/v1` |
| API key | any value (e.g. `local-webview-forwarder`) |
| Model name | `opencode-WEB-MIX` |
| OpenAI-compatible endpoint | `POST /v1/chat/completions` (streaming supported) |

Supported pages / 支持的网页：

| Site | URL | Notes |
| --- | --- | --- |
| DeepSeek | `https://chat.deepseek.com/` | SSE capture, tool calls supported |
| ChatGPT | `https://chatgpt.com/` | captured from the page DOM |

### Requirements

- Windows 10/11
- .NET SDK (targets `net10.0-windows`)
- Microsoft Edge WebView2 Runtime (usually preinstalled)

### Build & run

```powershell
# Build
dotnet build -c Release ".\MiniBrowser\MiniBrowser.csproj"

# Run
& ".\MiniBrowser\bin\Release\net10.0-windows\MiniBrowser.exe"
```

On startup the app opens `https://chat.deepseek.com/`. Use the site drop-down in
the toolbar and click **跳转 (Jump)** to switch between DeepSeek and ChatGPT.
Log in on the page you want to use.

### Install the opencode config

`config\webai.opencode.json` holds the provider fragment. Merge it into your
opencode config with:

```powershell
pwsh -File .\install-webai.ps1
```

This reads the `provider` fragment and merges it into
`%USERPROFILE%\.config\opencode\opencode.jsonc`, backing up the original first.
Other settings (mcp, plugin, instructions, ...) are preserved.

Then fully restart opencode. Select the model:

```powershell
opencode run -m webai/opencode-WEB-MIX "answer with just: pong"
```

> The browser must stay open on a logged-in chat page, otherwise the service
> cannot type into the input box.

### HTTP endpoints

| Method | Path | Description |
| --- | --- | --- |
| POST | `/v1/chat/completions` | OpenAI-compatible (used by opencode). `stream:true` → SSE |
| POST | `/chat` | Simple `{"prompt":"..."}` → `{"ok":true,"content":"..."}` |
| GET | `/health` | Service + current page status |
| POST | `/debug/eval` | Run arbitrary JS in the page (troubleshooting) |
| GET | `/debug/last` | Last captured completion payload |
| POST | `/debug/record` | `{"on":true/false}` toggle CDP network recording |

### Repository layout

```
flow\
├─ MiniBrowser\                 # C# WinForms + WebView2
│  ├─ Form1.cs                  # main window: navigation, address bar, site drop-down
│  ├─ Form1.Designer.cs         # toolbar UI
│  ├─ ChatService.cs            # local HTTP service + injected page script + SSE/NDJSON parsing
│  └─ MiniBrowser.csproj
├─ config\
│  └─ webai.opencode.json       # opencode provider fragment
├─ install-webai.ps1            # merge the fragment into the opencode config
└─ README.md
```

### Notes & limitations

- The service drives the page that is currently open; use the drop-down to pick
  the site before calling the model.
- ChatGPT's anonymous mobile-web stream is not directly capturable, so its answer
  is read from the page DOM. Prefer DeepSeek for tool-calling workflows.
- Requests are serialized (one conversation at a time) with a small throttle to
  avoid rate limiting.
- Tool calling is implemented as a text-bridging prompt for DeepSeek and is
  heuristic.

---

## 中文

### 是什么

`MiniBrowser` 是一个基于 C# WinForms + WebView2（Chromium 内核）的小浏览器。
你在里面登录一次 AI 网页，程序会：

1. 向页面注入脚本，可以写入输入框并点击发送；
2. 启动本地 HTTP 服务（`http://localhost:8787`），把收到的 OpenAI 风格请求
   转换成「写入 + 发送」动作，再把网页的回答返回。

这样 opencode（或任何 OpenAI 兼容客户端）就能像调用普通 API 模型一样「调用」
网页版模型。

### 模型连接地址

| 项目 | 值 |
| --- | --- |
| 接口地址 | `http://localhost:8787/v1` |
| API Key | 任意值（例如 `local-webview-forwarder`） |
| 模型名 | `opencode-WEB-MIX` |
| OpenAI 兼容端点 | `POST /v1/chat/completions`（支持流式） |

支持的网页：

| 站点 | 地址 | 说明 |
| --- | --- | --- |
| DeepSeek | `https://chat.deepseek.com/` | 抓取 SSE，支持工具调用 |
| ChatGPT | `https://chatgpt.com/` | 从页面 DOM 读取回答 |

### 环境要求

- Windows 10/11
- .NET SDK（目标框架 `net10.0-windows`）
- Microsoft Edge WebView2 运行时（一般已预装）

### 构建与运行

```powershell
# 构建
dotnet build -c Release ".\MiniBrowser\MiniBrowser.csproj"

# 运行
& ".\MiniBrowser\bin\Release\net10.0-windows\MiniBrowser.exe"
```

启动后默认打开 `https://chat.deepseek.com/`。用工具栏的下拉框选择站点，点击
**跳转** 即可在 DeepSeek 与 ChatGPT 之间切换，并在对应网页登录。

### 写入 opencode 配置

`config\webai.opencode.json` 是 provider 配置片段，执行下面命令合并进 opencode 配置：

```powershell
pwsh -File .\install-webai.ps1
```

脚本会读取其中的 `provider` 片段合并进
`%USERPROFILE%\.config\opencode\opencode.jsonc`，写入前自动备份；其它配置
（mcp、plugin、instructions 等）保持不变。

然后**完全重启** opencode，选择模型：

```powershell
opencode run -m webai/opencode-WEB-MIX "只回复两个字：pong"
```

> 必须保持浏览器打开且停留在已登录的对话页面，否则服务无法写入输入框。

### HTTP 端点

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/v1/chat/completions` | OpenAI 兼容（opencode 使用）。`stream:true` 返回 SSE |
| POST | `/chat` | 简单接口 `{"prompt":"..."}` → `{"ok":true,"content":"..."}` |
| GET | `/health` | 服务与当前页面状态 |
| POST | `/debug/eval` | 在页面执行任意 JS（排障） |
| GET | `/debug/last` | 最近一次抓取的完成请求载荷 |
| POST | `/debug/record` | `{"on":true/false}` 开关 CDP 网络记录 |

### 目录结构

```
flow\
├─ MiniBrowser\                 # C# WinForms + WebView2
│  ├─ Form1.cs                  # 主窗体：导航、地址栏、站点下拉框
│  ├─ Form1.Designer.cs         # 工具栏 UI
│  ├─ ChatService.cs            # 本地 HTTP 服务 + 页面注入脚本 + SSE/NDJSON 解析
│  └─ MiniBrowser.csproj
├─ config\
│  └─ webai.opencode.json       # opencode provider 片段
├─ install-webai.ps1            # 把片段合并进 opencode 配置
└─ README.md
```

### 说明与限制

- 服务驱动的是「当前打开的页面」，调用模型前请先用下拉框选好站点。
- ChatGPT 匿名移动版（unauth-mweb）的流无法直接抓取，其回答从页面 DOM 读取；
  需要工具调用时建议使用 DeepSeek。
- 请求串行处理（同一时间一条对话），并带有轻微节流以避免限流。
- 工具调用对 DeepSeek 采用文本桥接（prompt 约定 + 解析），属启发式方案。
