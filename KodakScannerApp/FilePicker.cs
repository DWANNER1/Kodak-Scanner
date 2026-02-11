using System;
using System.Threading;
using System.Windows.Forms;

namespace KodakScannerApp
{
    public static class FilePicker
    {
        public static string PickPdf()
        {
            string result = null;
            var thread = new Thread(() =>
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Filter = "PDF files (*.pdf)|*.pdf";
                    dialog.Title = "Select PDF";
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        result = dialog.FileName;
                    }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }

        public static string SavePdf(string defaultName)
        {
            string result = null;
            var thread = new Thread(() =>
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Filter = "PDF files (*.pdf)|*.pdf";
                    dialog.Title = "Save PDF As";
                    dialog.OverwritePrompt = true;
                    if (!string.IsNullOrWhiteSpace(defaultName))
                    {
                        dialog.FileName = defaultName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                            ? defaultName
                            : defaultName + ".pdf";
                    }
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        result = dialog.FileName;
                    }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }
    }
}
