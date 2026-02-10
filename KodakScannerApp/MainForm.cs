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
            try
            {
                _server.Start();
            }
            catch (HttpListenerException)
            {
                var user = Environment.UserDomainName + "\\" + Environment.UserName;
                var message =
                    "Unable to start the local web server.\n\n" +
                    "Fix options:\n" +
                    "1) Run the app as Administrator, or\n" +
                    "2) Reserve the URL with this command (run in an elevated PowerShell):\n\n" +
                    "netsh http add urlacl url=http://localhost:5005/ user=" + user + "\n\n" +
                    "Then restart the app.";
                MessageBox.Show(message, "Kodak Scanner", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }
            catch (InvalidOperationException ex)
            {
                var message =
                    ex.Message + "\n\n" +
                    "Close any app using port 5005, or change the port in HttpServer.cs.";
                MessageBox.Show(message, "Kodak Scanner", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

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
