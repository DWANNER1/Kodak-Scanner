using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace KodakScannerApp
{
    public class HttpServer
    {
        private readonly ScannerService _scannerService;
        private readonly string _wwwRoot;
        private HttpListener _listener;
        private readonly JavaScriptSerializer _json;
        private CancellationTokenSource _cts;

        public string BaseUrl { get; private set; }

        public HttpServer(ScannerService scannerService, string wwwRoot)
        {
            _scannerService = scannerService;
            _wwwRoot = wwwRoot;
            _json = new JavaScriptSerializer();
        }

        public void Start()
        {
            var port = 5005;
            if (!IsPortFree(port))
            {
                throw new InvalidOperationException("Port " + port + " is already in use.");
            }
            var started = TryStartListener("localhost", port) || TryStartListener("127.0.0.1", port);
            if (!started)
            {
                throw new HttpListenerException(5, "Access denied while starting local HTTP listener.");
            }

            _cts = new CancellationTokenSource();
            Task.Run(() => ListenLoop(_cts.Token));
        }

        public void Stop()
        {
            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                }
                if (_listener != null && _listener.IsListening)
                {
                    _listener.Stop();
                }
            }
            catch { }
        }

        private bool TryStartListener(string host, int port)
        {
            try
            {
                var listener = new HttpListener();
                var baseUrl = "http://" + host + ":" + port + "/";
                listener.Prefixes.Add(baseUrl);
                listener.Start();
                _listener = listener;
                BaseUrl = baseUrl;
                return true;
            }
            catch (HttpListenerException)
            {
                return false;
            }
        }

        private void ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = _listener.GetContext();
                    Task.Run(() => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch { }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath.TrimStart('/');
            if (path.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            {
                HandleApi(context, path);
                return;
            }

            if (path.StartsWith("scans/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = path.Substring("scans/".Length);
                var fullPath = Path.GetFullPath(Path.Combine(_scannerService.OutputRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                var rootPath = Path.GetFullPath(_scannerService.OutputRoot + Path.DirectorySeparatorChar);
                if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                WriteFile(context, fullPath);
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                path = "index.html";
            }

            var filePath = Path.Combine(_wwwRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(filePath))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            WriteFile(context, filePath);
        }

        private void HandleApi(HttpListenerContext context, string path)
        {
            try
            {
                if (path.Equals("api/devices", StringComparison.OrdinalIgnoreCase))
                {
                    var devices = _scannerService.ListDevices();
                    WriteJson(context, devices);
                    return;
                }

                if (path.Equals("api/status", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context, _scannerService.GetStatus());
                    return;
                }

                if (path.Equals("api/files", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context, _scannerService.GetScannedFiles());
                    return;
                }

                if (path.Equals("api/scan", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "POST")
                {
                    var body = ReadBody(context.Request);
                    var settings = _json.Deserialize<ScanSettings>(body);
                    var result = _scannerService.StartScan(settings);
                    WriteJson(context, result);
                    return;
                }

                if (path.Equals("api/export", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "POST")
                {
                    var body = ReadBody(context.Request);
                    var request = _json.Deserialize<ExportRequest>(body);
                    var result = _scannerService.Export(request);
                    WriteJson(context, result);
                    return;
                }

                if (path.Equals("api/clear", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "POST")
                {
                    _scannerService.Clear();
                    WriteJson(context, new ApiResult { Ok = true, Message = "Cleared" });
                    return;
                }

                if (path.Equals("api/delete", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "POST")
                {
                    var body = ReadBody(context.Request);
                    var request = _json.Deserialize<DeleteRequest>(body);
                    var result = _scannerService.DeleteFile(request?.Path);
                    WriteJson(context, result);
                    return;
                }

                if (path.Equals("api/rotate", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "POST")
                {
                    var body = ReadBody(context.Request);
                    var request = _json.Deserialize<RotateRequest>(body);
                    var result = _scannerService.RotateFile(request?.Path, request?.Direction);
                    WriteJson(context, result);
                    return;
                }

                if (path.Equals("api/reorder", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "POST")
                {
                    var body = ReadBody(context.Request);
                    var request = _json.Deserialize<ReorderRequest>(body);
                    var result = _scannerService.ReorderFiles(request?.Files);
                    WriteJson(context, result);
                    return;
                }

                if (path.Equals("api/pickpdf", StringComparison.OrdinalIgnoreCase))
                {
                    var picked = FilePicker.PickPdf();
                    WriteJson(context, new { Path = picked ?? "" });
                    return;
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            }
            catch (Exception ex)
            {
                WriteJson(context, new ApiResult { Ok = false, Message = ex.Message });
            }
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }

        private void WriteJson(HttpListenerContext context, object payload)
        {
            var json = _json.Serialize(payload);
            var buffer = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }

        private void WriteFile(HttpListenerContext context, string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            context.Response.ContentType = GetContentType(filePath);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        private static string GetContentType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".html") return "text/html";
            if (ext == ".js") return "application/javascript";
            if (ext == ".css") return "text/css";
            if (ext == ".png") return "image/png";
            if (ext == ".jpg" || ext == ".jpeg") return "image/jpeg";
            return "application/octet-stream";
        }

        private static bool IsPortFree(int port)
        {
            try
            {
                var test = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                test.Start();
                test.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
