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
        public static List<string> RenderPdfToImages(string pdfPath, string outputDir)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                throw new InvalidOperationException("PDF file not found.");
            }

            Directory.CreateDirectory(outputDir);
            var output = new List<string>();

            using (var document = PdfDocument.Load(pdfPath))
            {
                var dpi = 300;
                for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
                {
                    using (var image = document.Render(pageIndex, dpi, dpi, true))
                    {
                        var filePath = Path.Combine(outputDir, "page_" + (pageIndex + 1).ToString("000") + ".jpg");
                        using (var bmp = new Bitmap(image))
                        {
                            bmp.SetResolution(dpi, dpi);
                            var encoder = GetJpegEncoder();
                            if (encoder != null)
                            {
                                using (var parameters = new EncoderParameters(1))
                                {
                                    parameters.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
                                    bmp.Save(filePath, encoder, parameters);
                                }
                            }
                            else
                            {
                                bmp.Save(filePath, ImageFormat.Jpeg);
                            }
                        }
                        output.Add(filePath);
                    }
                }
            }

            return output;
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.MimeType == "image/jpeg")
                {
                    return codec;
                }
            }
            return null;
        }
    }
}
