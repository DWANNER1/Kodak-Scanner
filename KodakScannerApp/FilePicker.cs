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
                    dialog.Title = "Select PDF to Append";
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
    }
}
