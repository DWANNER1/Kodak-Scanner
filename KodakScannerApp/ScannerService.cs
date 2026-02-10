using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace KodakScannerApp
{
    public class ScannerService
    {
        private readonly object _lock = new object();
        private readonly string _outputRoot;
        private readonly WiaScanner _scanner;
        private readonly List<string> _scannedFiles;
        private ScanStatus _status;
        private bool _scanInProgress;

        public ScannerService(string outputRoot)
        {
            _outputRoot = outputRoot;
            _scanner = new WiaScanner();
            _scannedFiles = new List<string>();
            _status = new ScanStatus { State = "idle", Message = "Ready", PagesScanned = 0, Files = new List<string>() };
        }

        public List<DeviceInfoDto> ListDevices()
        {
            return _scanner.ListDevices();
        }

        public ScanStatus GetStatus()
        {
            lock (_lock)
            {
                return new ScanStatus
                {
                    State = _status.State,
                    Message = _status.Message,
                    PagesScanned = _status.PagesScanned,
                    Files = new List<string>(_scannedFiles)
                };
            }
        }

        public List<string> GetScannedFiles()
        {
            lock (_lock)
            {
                return new List<string>(_scannedFiles);
            }
        }

        public ApiResult StartScan(ScanSettings settings)
        {
            if (settings == null)
            {
                settings = new ScanSettings();
            }

            if (settings.Dpi <= 0) settings.Dpi = 300;
            if (string.IsNullOrWhiteSpace(settings.ColorMode)) settings.ColorMode = "color";
            if (settings.MaxPages <= 0) settings.MaxPages = 100;
            if (string.IsNullOrWhiteSpace(settings.DeviceId))
            {
                var devices = ListDevices();
                if (devices.Count > 0)
                {
                    settings.DeviceId = devices[0].Id;
                }
            }

            lock (_lock)
            {
                if (_scanInProgress)
                {
                    return new ApiResult { Ok = false, Message = "Scan already in progress" };
                }
                _scanInProgress = true;
                _status.State = "scanning";
                _status.Message = "Scanning...";
            }

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var jobDir = Path.Combine(_outputRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    Directory.CreateDirectory(jobDir);

                    var files = _scanner.ScanToFiles(settings, jobDir);

                    lock (_lock)
                    {
                        _scannedFiles.AddRange(files);
                        _status.State = "done";
                        _status.Message = "Scan complete";
                        _status.PagesScanned = _scannedFiles.Count;
                    }
                }
                catch (Exception ex)
                {
                    lock (_lock)
                    {
                        _status.State = "error";
                        _status.Message = ex.Message;
                    }
                }
                finally
                {
                    lock (_lock)
                    {
                        _scanInProgress = false;
                    }
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();

            return new ApiResult { Ok = true, Message = "Scan started" };
        }

        public ApiResult Export(ExportRequest request)
        {
            if (request == null)
            {
                return new ApiResult { Ok = false, Message = "Missing request" };
            }

            var format = (request.Format ?? "pdf").ToLowerInvariant();
            var outputDir = string.IsNullOrWhiteSpace(request.OutputPath) ? _outputRoot : request.OutputPath;
            var baseName = string.IsNullOrWhiteSpace(request.BaseName) ? "scan" : request.BaseName;

            Directory.CreateDirectory(outputDir);

            var files = GetScannedFiles();
            if (files.Count == 0)
            {
                return new ApiResult { Ok = false, Message = "No scanned pages" };
            }

            if (format == "pdf")
            {
                var outputFile = Path.Combine(outputDir, baseName + ".pdf");
                PdfWriter.WritePdfFromImages(files, outputFile);
                return new ApiResult { Ok = true, Message = "Saved PDF", Files = new List<string> { outputFile } };
            }

            if (format == "tiff" || format == "tif")
            {
                var outputFile = Path.Combine(outputDir, baseName + ".tif");
                ImageExporter.WriteMultipageTiff(files, outputFile);
                return new ApiResult { Ok = true, Message = "Saved TIFF", Files = new List<string> { outputFile } };
            }

            if (format == "jpg" || format == "jpeg" || format == "png")
            {
                var outFiles = ImageExporter.WriteImageSet(files, outputDir, baseName, format);
                return new ApiResult { Ok = true, Message = "Saved images", Files = outFiles };
            }

            return new ApiResult { Ok = false, Message = "Unsupported format" };
        }

        public void Clear()
        {
            lock (_lock)
            {
                _scannedFiles.Clear();
                _status.State = "idle";
                _status.Message = "Ready";
                _status.PagesScanned = 0;
            }
        }
    }
}
