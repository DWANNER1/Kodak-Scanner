using System;
using System.IO;
using System.Windows.Forms;

namespace KodakScannerApp
{
    public partial class MainForm : Form
    {
        private HttpServer _server;
        private ScannerService _scannerService;

        public MainForm()
        {
            InitializeComponent();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            var outputRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
            Directory.CreateDirectory(outputRoot);

            _scannerService = new ScannerService(outputRoot);
            _server = new HttpServer(_scannerService, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "www"));
            _server.Start();

            webBrowser1.Navigate(_server.BaseUrl);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_server != null)
            {
                _server.Stop();
            }

            base.OnFormClosing(e);
        }
    }
}
