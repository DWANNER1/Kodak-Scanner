using System;
using System.Collections.Generic;

namespace KodakScannerApp
{
    public class DeviceInfoDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class ScanSettings
    {
        public string DeviceId { get; set; }
        public int Dpi { get; set; }
        public string ColorMode { get; set; }
        public bool Duplex { get; set; }
        public int MaxPages { get; set; }
    }

    public class ExportRequest
    {
        public string Format { get; set; }
        public string OutputPath { get; set; }
        public string BaseName { get; set; }
    }

    public class ApiResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public List<string> Files { get; set; }
    }

    public class ScanStatus
    {
        public string State { get; set; }
        public string Message { get; set; }
        public int PagesScanned { get; set; }
        public List<string> Files { get; set; }
        public string OutputRoot { get; set; }
    }
}
