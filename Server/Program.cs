using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Hubs;
using Server.Services;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

static void TryLoadDotEnvFile(string path)
{
    if (string.IsNullOrWhiteSpace(path)) return;
    if (!File.Exists(path)) return;

    foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
    {
        var line = rawLine.Trim();
        if (line.Length == 0) continue;
        if (line.StartsWith("#", StringComparison.Ordinal)) continue;

        var eq = line.IndexOf('=');
        if (eq <= 0) continue;

        var key = line[..eq].Trim();
        if (key.Length == 0) continue;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key))) continue;

        var value = line[(eq + 1)..].Trim();
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            value = value[1..^1];
        }

        Environment.SetEnvironmentVariable(key, value);
    }
}

static void TryLoadDotEnv()
{
    // Support running from repo root (dotnet run --project Server) or from Server/ directory.
    var cwd = Directory.GetCurrentDirectory();
    var candidates = new[]
    {
        Path.Combine(cwd, ".env.local"),
        Path.Combine(cwd, ".env"),
        Path.Combine(cwd, "Server", ".env.local"),
        Path.Combine(cwd, "Server", ".env"),
    };

    foreach (var p in candidates)
        TryLoadDotEnvFile(p);
}

TryLoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

static string[] GetDefaultUrls(int port)
{
    var urls = new List<string>
    {
        $"http://127.0.0.1:{port}",
        $"http://[::1]:{port}"
    };

    try
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var props = nic.GetIPProperties();
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;

                var s = ua.Address.ToString();
                if (s.StartsWith("169.254.", StringComparison.Ordinal)) continue; // link-local
                urls.Add($"http://{s}:{port}");
            }
        }
    }
    catch
    {
        // ignore: best-effort URL list
    }

    return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

// Keep port 5000, but avoid wildcard bindings on macOS that can collide with system services.
var configuredUrls =
    builder.Configuration["urls"] ??
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(configuredUrls))
{
    builder.WebHost.UseUrls(GetDefaultUrls(5000));
}

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);
builder.Logging.AddFilter("Server", LogLevel.Information);

builder.Services.AddSignalR(o =>
{
    o.KeepAliveInterval = TimeSpan.FromSeconds(10);
    o.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    o.MaximumReceiveMessageSize = 10 * 1024 * 1024;
});

builder.Services.AddSingleton<SystemService>();
builder.Services.AddSingleton<WebcamService>();
builder.Services.AddSingleton<WebcamProofStore>();
builder.Services.AddSingleton<WebcamStreamManager>();
builder.Services.AddSingleton<InputService>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<AuthService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin => true) 
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // để SignalR hoạt động
    });
});

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var all = app.Urls?.ToArray() ?? Array.Empty<string>();
    var port = 5000;
    foreach (var u in all)
    {
        if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Port > 0)
        {
            port = uri.Port;
            break;
        }
    }

    var localBase = $"http://localhost:{port}";
    string? lanBase = null;
    foreach (var u in all)
    {
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) continue;
        if (uri.HostNameType != UriHostNameType.IPv4) continue;
        if (IPAddress.TryParse(uri.Host, out var ip) && !IPAddress.IsLoopback(ip))
        {
            lanBase = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            break;
        }
    }

    Console.WriteLine(lanBase == null
        ? $"Server started: {localBase}"
        : $"Server started: {localBase} (LAN: {lanBase})");
    Console.WriteLine($"Hub: {localBase}/controlHub | Key capture: {localBase}/server-keycapture (local-only)");
});


// Kích hoạt CORS với policy đã định nghĩa ở trên
app.UseCors("AllowAll");

app.MapHub<ControlHub>("/controlHub");

app.MapGet("/", () => "Server điều khiển từ xa đang chạy! Hãy kết nối qua SignalR tại /controlHub");

static bool IsLocalLoopback(IPAddress? remoteIp)
{
    if (remoteIp == null) return false;
    if (IPAddress.IsLoopback(remoteIp)) return true;
    if (remoteIp.IsIPv4MappedToIPv6) return IPAddress.IsLoopback(remoteIp.MapToIPv4());
    return false;
}

// Local-only server key capture page.
// This does NOT capture system-wide keystrokes: it only captures while this page is focused.
app.MapGet("/server-keycapture", (HttpContext ctx) =>
{
    var remoteIp = ctx.Connection.RemoteIpAddress;
    if (!IsLocalLoopback(remoteIp))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Results.Text(
            "HTTP 403: /server-keycapture chỉ truy cập được từ chính máy chạy Server qua http://127.0.0.1:<port>/server-keycapture (hoặc http://localhost:<port>/server-keycapture).\n" +
            $"RemoteIp={remoteIp}",
            "text/plain; charset=utf-8");
    }

    // On macOS, port 5000 can be shared by system services; force 127.0.0.1 to avoid hostname ambiguity.
    var host = ctx.Request.Host.Host;
    if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Redirect($"http://127.0.0.1:{ctx.Request.Host.Port ?? 5000}/server-keycapture", permanent: false);
    }

    ctx.Response.Headers["Cache-Control"] = "no-store";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; " +
        "connect-src 'self' ws: wss:; img-src 'self' data:; style-src 'unsafe-inline'; " +
        "script-src 'unsafe-inline' https://cdnjs.cloudflare.com";

    const string html = """
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Server Key Capture (Local)</title>
  <style>
    :root {
      --bg0: #0b1020;
      --bg1: #0f1a35;
      --card: rgba(255, 255, 255, 0.06);
      --border: rgba(255, 255, 255, 0.10);
      --text: rgba(255, 255, 255, 0.92);
      --muted: rgba(255, 255, 255, 0.70);
      --ok: #22c55e;
      --warn: #f59e0b;
      --bad: #ef4444;
      --mono: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
      --sans: system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
    }

    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      font-family: var(--sans);
      color: var(--text);
      background:
        radial-gradient(1200px 800px at 20% 10%, rgba(96, 165, 250, 0.28), transparent 60%),
        radial-gradient(1000px 700px at 90% 20%, rgba(167, 139, 250, 0.22), transparent 55%),
        linear-gradient(180deg, var(--bg0), var(--bg1));
      padding: 28px 16px;
    }

    .wrap { max-width: 980px; margin: 0 auto; }
    .top { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; flex-wrap: wrap; margin-bottom: 16px; }
    .title h1 { margin: 0 0 6px 0; font-size: 22px; letter-spacing: 0.2px; }
    .title p { margin: 0; color: var(--muted); line-height: 1.5; max-width: 70ch; }

    .badge {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 10px 12px;
      border: 1px solid var(--border);
      background: var(--card);
      border-radius: 12px;
      font-family: var(--mono);
      font-size: 12px;
      color: var(--muted);
      white-space: nowrap;
    }

    .dot { width: 10px; height: 10px; border-radius: 999px; background: var(--bad); box-shadow: 0 0 0 4px rgba(239, 68, 68, 0.14); }
    .dot.ok { background: var(--ok); box-shadow: 0 0 0 4px rgba(34, 197, 94, 0.14); }
    .dot.warn { background: var(--warn); box-shadow: 0 0 0 4px rgba(245, 158, 11, 0.16); }

    .grid { display: grid; grid-template-columns: 1fr; gap: 12px; }
    @media (min-width: 920px) { .grid { grid-template-columns: 1fr 1fr; } }

    .card { border: 1px solid var(--border); background: var(--card); border-radius: 16px; overflow: hidden; }
    .card-h { padding: 14px 16px; border-bottom: 1px solid var(--border); }
    .card-h h2 { margin: 0; font-size: 14px; letter-spacing: 0.2px; }
    .card-b { padding: 14px 16px; display: grid; gap: 12px; }

    .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
    button {
      appearance: none;
      border: 1px solid var(--border);
      background: rgba(255,255,255,0.08);
      color: var(--text);
      padding: 10px 12px;
      border-radius: 12px;
      cursor: pointer;
      font-weight: 650;
    }
    button:hover { background: rgba(255,255,255,0.12); }
    button.primary { background: rgba(34,197,94,0.18); border-color: rgba(34,197,94,0.35); }
    button.danger { background: rgba(239,68,68,0.16); border-color: rgba(239,68,68,0.35); }
    button:disabled { opacity: 0.55; cursor: not-allowed; }

    textarea {
      width: 100%;
      min-height: 220px;
      background: rgba(0,0,0,0.26);
      border: 1px solid var(--border);
      border-radius: 12px;
      padding: 12px;
      color: var(--text);
      font-family: var(--mono);
      font-size: 12.5px;
      line-height: 1.45;
      resize: vertical;
    }

    .hint { color: var(--muted); font-size: 12.5px; line-height: 1.45; }
    .mono { font-family: var(--mono); }
  </style>
  <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"></script>
</head>
<body>
  <div class="wrap">
    <div class="top">
      <div class="title">
        <h1>Server Key Capture (Local-only)</h1>
        <p>
          Trang này chỉ bắt phím khi bạn <span class="mono">focus tab</span> và bấm <span class="mono">Enable</span>.
          Không gửi dữ liệu đi đâu và chỉ truy cập được từ <span class="mono">localhost</span>.
        </p>
      </div>
      <div class="badge" title="Capture status">
        <span id="dot" class="dot"></span>
        <span id="statusText">disabled</span>
      </div>
    </div>

    <div class="grid">
      <div class="card">
        <div class="card-h"><h2>Capture Pad</h2></div>
        <div class="card-b">
          <div class="row">
            <button id="enableBtn" class="primary">Enable</button>
            <button id="disableBtn" class="danger" disabled>Disable</button>
            <button id="clearBtn">Clear</button>
          </div>
          <div class="hint">
            Click vào ô dưới rồi gõ thử: Enter/Tab/Arrow/F-keys. Khi enabled, trang sẽ <span class="mono">preventDefault</span> để không nhập text thật.
          </div>
          <textarea id="pad" spellcheck="false" placeholder="Click vào đây rồi gõ..."></textarea>
        </div>
      </div>

      <div class="card">
        <div class="card-h"><h2>Preview</h2></div>
        <div class="card-b">
          <textarea id="preview" readonly placeholder="Output..."></textarea>
          <div class="hint">Gợi ý: Enter sẽ xuống dòng; Tab sẽ hiện <span class="mono">[TAB]</span>.</div>
        </div>
      </div>
    </div>
  </div>

  <script>
    let capturing = false;
    let connection = null;

    const dotEl = document.getElementById("dot");
    const statusTextEl = document.getElementById("statusText");
    const padEl = document.getElementById("pad");
    const previewEl = document.getElementById("preview");
    const enableBtn = document.getElementById("enableBtn");
    const disableBtn = document.getElementById("disableBtn");
    const clearBtn = document.getElementById("clearBtn");

    function setStatus(text, kind) {
      statusTextEl.textContent = text;
      dotEl.className = "dot" + (kind ? (" " + kind) : "");
    }

    function normalizeUi() {
      enableBtn.disabled = capturing;
      disableBtn.disabled = !capturing;
      padEl.readOnly = !capturing;
      if (capturing) padEl.focus();
    }

	    function formatKeydown(e) {
	      if (!e) return "";

	      const key = typeof e.key === "string" ? e.key : "";
	      const code = typeof e.code === "string" ? e.code : "";

	      if (key === "Enter") return "[ENTER]\n";
	      if (key === " ") return " ";
	      if (key === "Backspace") return "[BACKSPACE]";
	      if (key === "Tab") return "[TAB]";
	      if (key === "Escape") return "[ESC]";
	      if (key === "Delete") return "[DEL]";
	      if (key === "ArrowUp") return "[UP]";
	      if (key === "ArrowDown") return "[DOWN]";
	      if (key === "ArrowLeft") return "[LEFT]";
	      if (key === "ArrowRight") return "[RIGHT]";
	      if (key && key.startsWith("F") && key.length <= 3) return "[" + key + "]";

	      // If browser gives the actual character, use it.
	      if (key.length === 1) return key;

	      // Fallback for letters when key=Process/Unidentified (common on IME/browsers).
	      if (code.startsWith("Key") && code.length === 4) {
	        const base = code.substring(3); // "A"
	        const caps = typeof e.getModifierState === "function" && e.getModifierState("CapsLock");
	        const upper = (e.shiftKey ? 1 : 0) ^ (caps ? 1 : 0);
	        return upper ? base : base.toLowerCase();
	      }

	      if (code.startsWith("Digit") && code.length === 6) {
	        return code.substring(5);
	      }
	      if (code.startsWith("Numpad") && code.length === 7) {
	        const d = code.substring(6);
	        if (d >= "0" && d <= "9") return d;
	      }

	      if (key === "Shift" || key === "Control" || key === "Alt" || key === "Meta") return "";
	      if (!key || key === "Process" || key === "Unidentified" || key === "Dead") return "";
	      return "[" + key + "]";
	    }

    function appendPreview(s) {
      if (!s) return;
      previewEl.value += s;
      previewEl.scrollTop = previewEl.scrollHeight;
    }

    function setDot(kind) {
      dotEl.className = "dot" + (kind ? (" " + kind) : "");
    }

    async function connect() {
      if (connection && connection.state === "Connected") return true;

      for (let attempt = 1; attempt <= 5; attempt++) {
        try {
          statusTextEl.textContent = attempt === 1 ? "connecting..." : ("retry " + attempt + "...");
          setDot("warn");

          connection = new signalR.HubConnectionBuilder()
            .withUrl("/controlHub", {
              transport: signalR.HttpTransportType.WebSockets,
              skipNegotiation: true
            })
            .withAutomaticReconnect([0, 2000, 5000, 10000])
            .build();

          connection.onclose(() => {
            setDot("bad");
            statusTextEl.textContent = capturing ? "enabled (disconnected)" : "disabled (disconnected)";
          });
          connection.onreconnecting(() => {
            setDot("warn");
            statusTextEl.textContent = capturing ? "enabled (reconnecting...)" : "disabled (reconnecting...)";
          });
          connection.onreconnected(() => {
            setDot(capturing ? "ok" : "bad");
            statusTextEl.textContent = capturing ? "enabled" : "disabled";
          });

          await connection.start();
          setDot(capturing ? "ok" : "bad");
          statusTextEl.textContent = capturing ? "enabled" : "disabled";
          return true;
        } catch (e) {
          console.error(e);
          try { if (connection) await connection.stop(); } catch {}
          connection = null;
          setDot("bad");
          statusTextEl.textContent = "connect failed";
          await new Promise(r => setTimeout(r, 350 * attempt));
        }
      }

      return false;
    }

    enableBtn.addEventListener("click", () => {
      (async () => {
        const ok = await connect();
        if (!ok) return;

        capturing = true;
        setStatus("enabled", "ok");
        normalizeUi();
      })();
    });

    disableBtn.addEventListener("click", () => {
      capturing = false;
      setStatus("disabled", "bad");
      normalizeUi();
    });

    clearBtn.addEventListener("click", () => {
      previewEl.value = "";
      padEl.value = "";
      padEl.focus();
    });

    function sendKeyData(keyData) {
      if (!keyData) return;
      appendPreview(keyData);
      try {
        if (connection && connection.state === "Connected") {
          connection.invoke("SendServerCapturedKey", keyData);
        }
      } catch (e) {
        // ignore
      }
    }

    // Keydown: handle special keys that don't always generate text events.
    padEl.addEventListener("keydown", (e) => {
      if (!capturing) return;
      if (e.key === "F5" || e.ctrlKey || e.metaKey) return;

      const keyData = formatKeydown(e);
      if (!keyData) return;
      e.preventDefault();
      sendKeyData(keyData);
    });

    // beforeinput: capture real text (letters/numbers/paste). This is more reliable under IME.
    let composing = false;
    padEl.addEventListener("compositionstart", () => { composing = true; });
    padEl.addEventListener("compositionend", (e) => {
      composing = false;
      if (!capturing) return;
      const data = (e && typeof e.data === "string") ? e.data : "";
      if (data) sendKeyData(data);
      padEl.value = "";
    });

    padEl.addEventListener("beforeinput", (e) => {
      if (!capturing) return;
      if (composing) return;

      const inputType = e && e.inputType ? String(e.inputType) : "";
      const data = e && typeof e.data === "string" ? e.data : "";

      if (inputType === "deleteContentBackward") {
        e.preventDefault();
        sendKeyData("[BACKSPACE]");
        padEl.value = "";
        return;
      }
      if (inputType === "deleteContentForward") {
        e.preventDefault();
        sendKeyData("[DEL]");
        padEl.value = "";
        return;
      }

      if (data) {
        e.preventDefault();
        sendKeyData(data);
        padEl.value = "";
      }
    });

    setStatus("disabled", "bad");
    normalizeUi();
    connect();
  </script>
</body>
</html>
""";

    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();
