using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace KodakScannerApp
{
    public class CloudAgent
    {
        private readonly ScannerService _scannerService;
        private readonly JavaScriptSerializer _json;
        private CancellationTokenSource _cts;
        private Task _worker;
        private string _cloudUrl;
        private string _username;
        private string _password;
        private string _deviceName;

        public CloudAgent(ScannerService scannerService)
        {
            _scannerService = scannerService;
            _json = new JavaScriptSerializer();
        }

        public void Start()
        {
            LoadSettings();
            if (string.IsNullOrWhiteSpace(_cloudUrl))
            {
                Logger.Log("cloud agent disabled: missing CloudUrl");
                return;
            }
            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(_cts.Token));
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _worker?.Wait(2000);
            }
            catch { }
        }

        private void LoadSettings()
        {
            _cloudUrl = ConfigurationManager.AppSettings["CloudUrl"] ?? "";
            _username = ConfigurationManager.AppSettings["CloudUser"] ?? "kodak";
            _password = ConfigurationManager.AppSettings["CloudPass"] ?? "kodak";
            _deviceName = ConfigurationManager.AppSettings["DeviceName"] ?? Environment.MachineName;
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var sessionToken = await LoginAsync(token);
                    if (string.IsNullOrWhiteSpace(sessionToken))
                    {
                        await Task.Delay(5000, token);
                        continue;
                    }

                    using (var ws = new ClientWebSocket())
                    {
                        var wsUrl = BuildWsUrl(_cloudUrl, sessionToken);
                        await ws.ConnectAsync(new Uri(wsUrl), token);

                        await SendMessage(ws, new { type = "agent_hello", device = _deviceName });
                        await SendDevices(ws);

                        var statusLoop = Task.Run(() => StatusLoop(ws, token), token);
                        await ReceiveLoop(ws, token);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("cloud agent error " + ex);
                }

                await Task.Delay(5000, token);
            }
        }

        private async Task<string> LoginAsync(CancellationToken token)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var loginUrl = _cloudUrl.TrimEnd('/') + "/login";
                    var body = _json.Serialize(new { username = _username, password = _password });
                    var response = await client.PostAsync(loginUrl, new StringContent(body, Encoding.UTF8, "application/json"), token);
                    var json = await response.Content.ReadAsStringAsync();
                    var data = _json.Deserialize<Dictionary<string, object>>(json);
                    if (data != null && data.ContainsKey("token"))
                    {
                        return data["token"]?.ToString() ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("cloud login failed " + ex.Message);
            }
            return "";
        }

        private string BuildWsUrl(string cloudUrl, string token)
        {
            var uri = new Uri(cloudUrl);
            var scheme = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            return scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port) + "/?token=" + token + "&role=agent";
        }

        private async Task StatusLoop(ClientWebSocket ws, CancellationToken token)
        {
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var status = _scannerService.GetStatus();
                await SendMessage(ws, new { type = "status", status });
                await Task.Delay(2000, token);
            }
        }

        private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[8192];
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                    break;
                }
                var payload = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleMessage(ws, payload);
            }
        }

        private void HandleMessage(ClientWebSocket ws, string payload)
        {
            Dictionary<string, object> data;
            try
            {
                data = _json.Deserialize<Dictionary<string, object>>(payload);
            }
            catch
            {
                return;
            }

            if (data == null || !data.ContainsKey("type")) return;
            var type = data["type"]?.ToString() ?? "";

            if (type == "get_devices")
            {
                _ = SendDevices(ws);
                return;
            }

            if (type == "scan_start")
            {
                var settings = ExtractSettings(data);
                var result = _scannerService.StartScan(settings);
                _ = SendMessage(ws, new { type = "scan_result", result });
                return;
            }
        }

        private ScanSettings ExtractSettings(Dictionary<string, object> data)
        {
            var settings = new ScanSettings();
            if (!data.ContainsKey("settings")) return settings;

            var dict = data["settings"] as Dictionary<string, object>;
            if (dict == null) return settings;

            if (dict.ContainsKey("DeviceId")) settings.DeviceId = dict["DeviceId"]?.ToString();
            if (dict.ContainsKey("Dpi")) settings.Dpi = Convert.ToInt32(dict["Dpi"]);
            if (dict.ContainsKey("ColorMode")) settings.ColorMode = dict["ColorMode"]?.ToString();
            if (dict.ContainsKey("Duplex")) settings.Duplex = Convert.ToBoolean(dict["Duplex"]);
            if (dict.ContainsKey("MaxPages")) settings.MaxPages = Convert.ToInt32(dict["MaxPages"]);
            return settings;
        }

        private async Task SendDevices(ClientWebSocket ws)
        {
            var devices = _scannerService.ListDevices();
            await SendMessage(ws, new { type = "devices", devices });
        }

        private async Task SendMessage(ClientWebSocket ws, object payload)
        {
            if (ws.State != WebSocketState.Open) return;
            var json = _json.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
