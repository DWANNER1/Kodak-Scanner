using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PdfiumViewer;

namespace KodakScannerApp
{
    public static class PdfImporter
    {
        public static List<PageItem> RenderPdfToImages(string pdfPath, string outputDir)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                throw new InvalidOperationException("PDF file not found.");
            }

            Directory.CreateDirectory(outputDir);
            var output = new List<PageItem>();

            using (var document = PdfDocument.Load(pdfPath))
            {
                var dpi = 300;
                for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
                {
                    using (var image = document.Render(pageIndex, dpi, dpi, true))
                    {
                        var baseName = "page_" + (pageIndex + 1).ToString("000");
                        var filePath = Path.Combine(outputDir, baseName + ".jpg");
                        using (var bmp = new Bitmap(image))
                        {
                            bmp.SetResolution(dpi, dpi);
                            bmp.Save(filePath, ImageFormat.Jpeg);
                        }
                        var pageSize = document.PageSizes[pageIndex];
                        output.Add(new PageItem
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Path = filePath,
                            WidthPt = pageSize.Width,
                            HeightPt = pageSize.Height
                        });
                    }
                }
            }

            return output;
        }
    }
}
