using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using System.Drawing;

namespace KodakScannerApp
{
    public class ScannerService
    {
        private readonly object _lock = new object();
        private readonly string _outputRoot;
        private readonly TwainScanner _scanner;
        private readonly List<PageItem> _pages;
        private ScanStatus _status;
        private bool _scanInProgress;
        private string _lastJobDir;
        private string _mode;
        private string _editSourcePath;

        public string OutputRoot => _outputRoot;

        public ScannerService(string outputRoot)
        {
            _outputRoot = outputRoot;
            Logger.Initialize(outputRoot);
            _scanner = new TwainScanner();
            _pages = new List<PageItem>();
            _status = new ScanStatus { State = "idle", Message = "Ready", PagesScanned = 0, Pages = new List<PageItem>() };
            _mode = "idle";
            _editSourcePath = "";
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
                    Pages = new List<PageItem>(_pages),
                    OutputRoot = _outputRoot,
                    CurrentJobDir = _lastJobDir,
                    Mode = _mode,
                    EditSourcePath = _editSourcePath
                };
            }
        }

        public List<string> GetScannedFiles()
        {
            lock (_lock)
            {
                var list = new List<string>();
                foreach (var page in _pages)
                {
                    list.Add(page.Path);
                }
                return list;
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
            if (string.IsNullOrWhiteSpace(settings.ScanSide))
            {
                settings.ScanSide = settings.Duplex ? "both" : "front";
            }
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
                _mode = "scan";
                _editSourcePath = "";
            }

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var jobDir = Path.Combine(_outputRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    _lastJobDir = jobDir;
                    Directory.CreateDirectory(jobDir);

                    _scanner.ScanToFiles(settings, jobDir, path =>
                    {
                        lock (_lock)
                        {
                            _pages.Add(new PageItem { Id = Guid.NewGuid().ToString("N"), Path = path });
                            _status.State = "scanning";
                            _status.Message = "Scanning... (" + _pages.Count + ")";
                            _status.PagesScanned = _pages.Count;
                            Logger.Log("page saved " + path);
                        }
                    });

                    lock (_lock)
                    {
                        _status.State = "done";
                        _status.Message = "Scan complete";
                        _status.PagesScanned = _pages.Count;
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

        public ApiResult AddHeaderPage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ApiResult { Ok = false, Message = "Header text is required" };
            }

            lock (_lock)
            {
                if (_scanInProgress)
                {
                    return new ApiResult { Ok = false, Message = "Cannot add header while scanning" };
                }
            }

            try
            {
                var targetDir = ResolveActiveJobDir();
                var dpi = GuessTargetDpi();
                var filePath = DividerPageBuilder.CreateDividerPage(text, targetDir, dpi);
                var page = new PageItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Path = filePath,
                    WidthPt = 8.5 * 72.0,
                    HeightPt = 11.0 * 72.0
                };

                lock (_lock)
                {
                    _pages.Insert(0, page);
                    _status.State = "ready";
                    _status.Message = "Header page added";
                    _status.PagesScanned = _pages.Count;
                }

                Logger.Log("header page created " + filePath);
                return new ApiResult { Ok = true, Message = "Header page created" };
            }
            catch (Exception ex)
            {
                Logger.Log("header page error " + ex);
                return new ApiResult { Ok = false, Message = ex.Message };
            }
        }

        public ApiResult LoadPdf(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                return new ApiResult { Ok = false, Message = "Missing PDF path" };
            }

            lock (_lock)
            {
                if (_scanInProgress)
                {
                    return new ApiResult { Ok = false, Message = "Cannot load PDF while scanning" };
                }
            }

            try
            {
                var jobDir = Path.Combine(_outputRoot, "edit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                var pages = PdfImporter.RenderPdfToImages(pdfPath, jobDir);
                if (pages.Count == 0)
                {
                    return new ApiResult { Ok = false, Message = "No pages found in PDF" };
                }

                lock (_lock)
                {
                    _pages.Clear();
                    _pages.AddRange(pages);
                    _lastJobDir = jobDir;
                    _status.State = "ready";
                    _status.Message = "PDF loaded";
                    _status.PagesScanned = _pages.Count;
                    _mode = "edit";
                    _editSourcePath = pdfPath;
                }

                Logger.Log("pdf loaded " + pdfPath);
                return new ApiResult { Ok = true, Message = "PDF loaded" };
            }
            catch (Exception ex)
            {
                Logger.Log("pdf load error " + ex);
                return new ApiResult { Ok = false, Message = ex.Message };
            }
        }

        public AboutInfo GetAboutInfo()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version?.ToString() ?? "";
                var location = assembly.Location;
                var buildTime = File.Exists(location)
                    ? File.GetLastWriteTime(location).ToString("MMMM d, yyyy 'at' h:mm tt")
                    : DateTime.Now.ToString("MMMM d, yyyy 'at' h:mm tt");

                return new AboutInfo
                {
                    Version = version,
                    BuildTime = buildTime
                };
            }
            catch
            {
                return new AboutInfo
                {
                    Version = "",
                    BuildTime = DateTime.Now.ToString("MMMM d, yyyy 'at' h:mm tt")
                };
            }
        }

        public ApiResult Export(ExportRequest request)
        {
            if (request == null)
            {
                return new ApiResult { Ok = false, Message = "Missing request" };
            }

            var format = (request.Format ?? "pdf").ToLowerInvariant();
            var outputDir = string.IsNullOrWhiteSpace(request.OutputPath) ? (_lastJobDir ?? _outputRoot) : request.OutputPath;
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
                if (request.Append && !string.IsNullOrWhiteSpace(request.AppendPath))
                {
                    outputFile = request.AppendPath;
                    outputDir = Path.GetDirectoryName(outputFile) ?? outputDir;
                }

                if (request.Append && File.Exists(outputFile))
                {
                    var merged = new List<string>();
                    merged.AddRange(GetImagesInFolder(outputDir));
                    foreach (var f in files)
                    {
                        if (!merged.Exists(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase)))
                        {
                            merged.Add(f);
                        }
                    }
                    PdfWriter.WritePdfFromImages(merged, outputFile);
                }
                else
                {
                    PdfWriter.WritePdfFromPages(new List<PageItem>(_pages), outputFile);
                }
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

        private static List<string> GetImagesInFolder(string folder)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return list;
            }

            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"
            };

            foreach (var file in Directory.GetFiles(folder))
            {
                if (exts.Contains(Path.GetExtension(file)))
                {
                    list.Add(file);
                }
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _pages.Clear();
                _status.State = "idle";
                _status.Message = "Ready";
                _status.PagesScanned = 0;
                _mode = "idle";
                _editSourcePath = "";
            }
        }

        public ApiResult DeleteFile(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return new ApiResult { Ok = false, Message = "Missing page id" };
            }

            lock (_lock)
            {
                Logger.Log("delete requested id=" + id);
                var page = _pages.Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                if (page == null)
                {
                    Logger.Log("delete not found id=" + id);
                    return new ApiResult { Ok = false, Message = "Page not found" };
                }

                var filePath = page.Path;
                Logger.Log("delete path=" + filePath);
                if (!IsUnderOutputRoot(filePath))
                {
                    return new ApiResult { Ok = false, Message = "Invalid file path" };
                }
                if (!File.Exists(filePath))
                {
                    return new ApiResult { Ok = false, Message = "File not found" };
                }

                try
                {
                    File.Delete(filePath);
                    var folder = Path.GetDirectoryName(filePath) ?? "";
                    var sameFolder = new List<PageItem>();
                    var otherFolders = new List<PageItem>();
                    foreach (var p in _pages)
                    {
                        if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (string.Equals(Path.GetDirectoryName(p.Path), folder, StringComparison.OrdinalIgnoreCase))
                        {
                            sameFolder.Add(p);
                        }
                        else
                        {
                            otherFolders.Add(p);
                        }
                    }

                    sameFolder.Sort((a, b) => ComparePageNames(a.Path, b.Path));
                    _pages.Clear();
                    _pages.AddRange(otherFolders);
                    _pages.AddRange(sameFolder);
                    _status.PagesScanned = _pages.Count;
                    Logger.Log("delete done count=" + _pages.Count);
                    return new ApiResult { Ok = true, Message = "Deleted" };
                }
                catch (Exception ex)
                {
                    Logger.Log("delete error " + ex.Message);
                    return new ApiResult { Ok = false, Message = ex.Message };
                }
            }
        }

        public ApiResult RotateFile(string filePath, string direction)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new ApiResult { Ok = false, Message = "Missing file path" };
            }

            if (!IsUnderOutputRoot(filePath))
            {
                return new ApiResult { Ok = false, Message = "Invalid file path" };
            }

            if (!File.Exists(filePath))
            {
                return new ApiResult { Ok = false, Message = "File not found" };
            }

            var rotateFlip = ParseRotate(direction);
            if (rotateFlip == null)
            {
                return new ApiResult { Ok = false, Message = "Invalid rotate direction" };
            }

            try
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var tempPath = filePath + ".tmp";

                using (var image = System.Drawing.Image.FromFile(filePath))
                {
                    image.RotateFlip(rotateFlip.Value);
                    var format = GetImageFormat(ext);
                    image.Save(tempPath, format);
                }

                File.Copy(tempPath, filePath, true);
                File.Delete(tempPath);

                return new ApiResult { Ok = true, Message = "Rotated" };
            }
            catch (Exception ex)
            {
                return new ApiResult { Ok = false, Message = ex.Message };
            }
        }

        public ApiResult ReorderFiles(List<string> orderedIds)
        {
            if (orderedIds == null || orderedIds.Count == 0)
            {
                return new ApiResult { Ok = false, Message = "No pages to reorder" };
            }

            Logger.Log("reorder requested ids=" + string.Join(",", orderedIds));
            var first = _pages.Find(p => string.Equals(p.Id, orderedIds[0], StringComparison.OrdinalIgnoreCase));
            if (first == null)
            {
                return new ApiResult { Ok = false, Message = "Page not found" };
            }
            var folder = Path.GetDirectoryName(first.Path) ?? "";

            try
            {
                var orderedPages = new List<PageItem>();
                foreach (var id in orderedIds)
                {
                    var page = _pages.Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (page == null)
                    {
                        return new ApiResult { Ok = false, Message = "Page not found" };
                    }
                    if (!string.Equals(Path.GetDirectoryName(page.Path), folder, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ApiResult { Ok = false, Message = "Pages must be in the same folder" };
                    }
                    orderedPages.Add(page);
                }

                lock (_lock)
                {
                    var rebuilt = new List<PageItem>();
                    foreach (var page in _pages)
                    {
                        if (!string.Equals(Path.GetDirectoryName(page.Path), folder, StringComparison.OrdinalIgnoreCase))
                        {
                            rebuilt.Add(page);
                        }
                    }
                    rebuilt.AddRange(orderedPages);
                    _pages.Clear();
                    _pages.AddRange(rebuilt);
                    _status.PagesScanned = _pages.Count;
                }

                Logger.Log("reorder done");
                return new ApiResult { Ok = true, Message = "Reordered" };
            }
            catch (Exception ex)
            {
                Logger.Log("reorder error " + ex.Message);
                return new ApiResult { Ok = false, Message = ex.Message };
            }
        }

        // No filename renumbering; order is tracked in-memory by PageItem list.

        private static int ComparePageNames(string left, string right)
        {
            var leftNum = ExtractPageNumber(left);
            var rightNum = ExtractPageNumber(right);
            if (leftNum != rightNum)
            {
                return leftNum.CompareTo(rightNum);
            }
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int ExtractPageNumber(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path) ?? "";
            var index = name.LastIndexOf("_", StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && int.TryParse(name.Substring(index + 1), out var num))
            {
                return num;
            }
            return int.MaxValue;
        }

        private bool IsUnderOutputRoot(string filePath)
        {
            try
            {
                var fullPath = Path.GetFullPath(filePath);
                var rootPath = Path.GetFullPath(_outputRoot + Path.DirectorySeparatorChar);
                return fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private string ResolveActiveJobDir()
        {
            if (_pages.Count > 0)
            {
                var first = _pages[0];
                var folder = Path.GetDirectoryName(first.Path);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    return folder;
                }
            }

            if (!string.IsNullOrWhiteSpace(_lastJobDir))
            {
                return _lastJobDir;
            }

            var jobDir = Path.Combine(_outputRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            _lastJobDir = jobDir;
            Directory.CreateDirectory(jobDir);
            return jobDir;
        }

        private int GuessTargetDpi()
        {
            try
            {
                if (_pages.Count > 0)
                {
                    var first = _pages[0];
                    if (File.Exists(first.Path))
                    {
                        using (var image = System.Drawing.Image.FromFile(first.Path))
                        {
                            var dpi = (int)Math.Round(image.HorizontalResolution);
                            if (dpi >= 72) return dpi;
                        }
                    }
                }
            }
            catch { }
            return 300;
        }

        private static System.Drawing.Imaging.ImageFormat GetImageFormat(string ext)
        {
            if (ext == ".png") return System.Drawing.Imaging.ImageFormat.Png;
            if (ext == ".bmp") return System.Drawing.Imaging.ImageFormat.Bmp;
            if (ext == ".tif" || ext == ".tiff") return System.Drawing.Imaging.ImageFormat.Tiff;
            return System.Drawing.Imaging.ImageFormat.Jpeg;
        }

        private static System.Drawing.RotateFlipType? ParseRotate(string direction)
        {
            if (string.Equals(direction, "left", StringComparison.OrdinalIgnoreCase))
            {
                return System.Drawing.RotateFlipType.Rotate270FlipNone;
            }
            if (string.Equals(direction, "right", StringComparison.OrdinalIgnoreCase))
            {
                return System.Drawing.RotateFlipType.Rotate90FlipNone;
            }
            return null;
        }
    }
}
