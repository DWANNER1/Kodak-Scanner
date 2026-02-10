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
        private string _lastJobDir;

        public string OutputRoot => _outputRoot;

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
                    Files = new List<string>(_scannedFiles),
                    OutputRoot = _outputRoot,
                    CurrentJobDir = _lastJobDir
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
                    _lastJobDir = jobDir;
                    Directory.CreateDirectory(jobDir);

                    _scanner.ScanToFiles(settings, jobDir, path =>
                    {
                        lock (_lock)
                        {
                            _scannedFiles.Add(path);
                            _status.State = "scanning";
                            _status.Message = "Scanning... (" + _scannedFiles.Count + ")";
                            _status.PagesScanned = _scannedFiles.Count;
                        }
                    });

                    lock (_lock)
                    {
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
                    PdfWriter.WritePdfFromImages(files, outputFile);
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
                _scannedFiles.Clear();
                _status.State = "idle";
                _status.Message = "Ready";
                _status.PagesScanned = 0;
            }
        }

        public ApiResult DeleteFile(string filePath, int? index, string folderPath)
        {
            if (!string.IsNullOrWhiteSpace(folderPath) && index.HasValue)
            {
                filePath = ResolvePathByIndex(folderPath, index.Value);
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new ApiResult { Ok = false, Message = "Missing file path" };
            }

            lock (_lock)
            {
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
                    var sameFolder = new List<string>();
                    var otherFolders = new List<string>();
                    foreach (var f in _scannedFiles)
                    {
                        if (string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase))
                        {
                            sameFolder.Add(f);
                        }
                        else
                        {
                            otherFolders.Add(f);
                        }
                    }

                    sameFolder.Sort(ComparePageNames);
                    var updated = RenumberFilesInFolder(folder, sameFolder);
                    _scannedFiles.Clear();
                    _scannedFiles.AddRange(otherFolders);
                    _scannedFiles.AddRange(updated);
                    _scannedFiles.Sort(CompareFolderThenPage);
                    _status.PagesScanned = _scannedFiles.Count;
                    return new ApiResult { Ok = true, Message = "Deleted", Files = new List<string>(_scannedFiles) };
                }
                catch (Exception ex)
                {
                    return new ApiResult { Ok = false, Message = ex.Message };
                }
            }
        }

        private string ResolvePathByIndex(string folder, int index)
        {
            if (index < 0) return null;
            var list = new List<string>();
            foreach (var f in _scannedFiles)
            {
                if (string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(f);
                }
            }
            list.Sort(ComparePageNames);
            if (index >= list.Count) return null;
            return list[index];
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

        public ApiResult ReorderFiles(List<string> orderedFiles)
        {
            if (orderedFiles == null || orderedFiles.Count == 0)
            {
                return new ApiResult { Ok = false, Message = "No files to reorder" };
            }

            var folder = Path.GetDirectoryName(orderedFiles[0]) ?? "";
            foreach (var file in orderedFiles)
            {
                if (!IsUnderOutputRoot(file) || !File.Exists(file))
                {
                    return new ApiResult { Ok = false, Message = "Invalid file path" };
                }
                if (!string.Equals(Path.GetDirectoryName(file), folder, StringComparison.OrdinalIgnoreCase))
                {
                    return new ApiResult { Ok = false, Message = "Files must be in the same folder" };
                }
            }

            try
            {
                var newList = RenumberFilesInFolder(folder, orderedFiles);
                lock (_lock)
                {
                    _scannedFiles.Clear();
                    _scannedFiles.AddRange(newList);
                    _scannedFiles.Sort(CompareFolderThenPage);
                    _status.PagesScanned = _scannedFiles.Count;
                }

                return new ApiResult { Ok = true, Message = "Reordered", Files = newList };
            }
            catch (Exception ex)
            {
                return new ApiResult { Ok = false, Message = ex.Message };
            }
        }

        private static List<string> RenumberFilesInFolder(string folder, List<string> orderedFiles)
        {
            var tempMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < orderedFiles.Count; i++)
            {
                var file = orderedFiles[i];
                var ext = Path.GetExtension(file);
                var temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ext);
                File.Move(file, temp);
                tempMap[file] = temp;
            }

            var newList = new List<string>();
            for (int i = 0; i < orderedFiles.Count; i++)
            {
                var original = orderedFiles[i];
                var ext = Path.GetExtension(original);
                var newPath = Path.Combine(folder, "page_" + (i + 1).ToString("000") + ext);
                File.Move(tempMap[original], newPath);
                newList.Add(newPath);
            }

            return newList;
        }

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

        private static int CompareFolderThenPage(string left, string right)
        {
            var leftDir = Path.GetDirectoryName(left) ?? "";
            var rightDir = Path.GetDirectoryName(right) ?? "";
            var dirCompare = string.Compare(leftDir, rightDir, StringComparison.OrdinalIgnoreCase);
            if (dirCompare != 0)
            {
                return dirCompare;
            }
            return ComparePageNames(left, right);
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
