using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace KodakScannerApp
{
    public static class PdfWriter
    {
        public static void WritePdfFromImages(List<string> imageFiles, string outputFile)
        {
            if (imageFiles == null || imageFiles.Count == 0)
            {
                throw new InvalidOperationException("No images to export");
            }

            var totalObjects = 2 + (imageFiles.Count * 3);
            var xrefPositions = new List<long>(totalObjects + 1);

            using (var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n"));
                xrefPositions.Add(0);

                WriteCatalog(writer, xrefPositions, 1, 2);
                WritePagesRoot(writer, xrefPositions, 2, imageFiles.Count);

                for (int i = 0; i < imageFiles.Count; i++)
                {
                    int pageId = 3 + (i * 3);
                    int contentId = pageId + 1;
                    int imageId = pageId + 2;

                    WritePage(writer, xrefPositions, pageId, 2, contentId, imageId, imageFiles[i]);
                    WritePageContent(writer, xrefPositions, contentId, imageId, imageFiles[i]);
                    WriteImageObject(writer, xrefPositions, imageId, imageFiles[i]);
                }

                var xrefStart = writer.BaseStream.Position;
                writer.Write(Encoding.ASCII.GetBytes("xref\n"));
                writer.Write(Encoding.ASCII.GetBytes("0 " + (totalObjects + 1) + "\n"));
                writer.Write(Encoding.ASCII.GetBytes("0000000000 65535 f \n"));

                for (int i = 1; i < xrefPositions.Count; i++)
                {
                    var pos = xrefPositions[i];
                    writer.Write(Encoding.ASCII.GetBytes(pos.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n"));
                }

                writer.Write(Encoding.ASCII.GetBytes("trailer\n"));
                writer.Write(Encoding.ASCII.GetBytes("<< /Size " + (totalObjects + 1) + " /Root 1 0 R >>\n"));
                writer.Write(Encoding.ASCII.GetBytes("startxref\n"));
                writer.Write(Encoding.ASCII.GetBytes(xrefStart.ToString(CultureInfo.InvariantCulture) + "\n"));
                writer.Write(Encoding.ASCII.GetBytes("%%EOF\n"));
            }
        }

        private static void WriteCatalog(BinaryWriter writer, List<long> xref, int catalogId, int pagesId)
        {
            xref.Add(writer.BaseStream.Position);
            writer.Write(Encoding.ASCII.GetBytes(catalogId + " 0 obj\n"));
            writer.Write(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages " + pagesId + " 0 R >>\n"));
            writer.Write(Encoding.ASCII.GetBytes("endobj\n"));
        }

        private static void WritePagesRoot(BinaryWriter writer, List<long> xref, int pagesId, int pageCount)
        {
            xref.Add(writer.BaseStream.Position);
            writer.Write(Encoding.ASCII.GetBytes(pagesId + " 0 obj\n"));

            var kids = new StringBuilder();
            for (int i = 0; i < pageCount; i++)
            {
                int pageId = 3 + (i * 3);
                kids.Append(pageId + " 0 R ");
            }

            writer.Write(Encoding.ASCII.GetBytes("<< /Type /Pages /Count " + pageCount + " /Kids [ " + kids + "] >>\n"));
            writer.Write(Encoding.ASCII.GetBytes("endobj\n"));
        }

        private static void WritePage(BinaryWriter writer, List<long> xref, int pageId, int pagesId, int contentId, int imageId, string imageFile)
        {
            var size = GetPageSize(imageFile);

            xref.Add(writer.BaseStream.Position);
            writer.Write(Encoding.ASCII.GetBytes(pageId + " 0 obj\n"));
            writer.Write(Encoding.ASCII.GetBytes("<< /Type /Page /Parent " + pagesId + " 0 R "));
            writer.Write(Encoding.ASCII.GetBytes("/Resources << /XObject << /Im1 " + imageId + " 0 R >> >> "));
            writer.Write(Encoding.ASCII.GetBytes("/MediaBox [0 0 " + size.WidthPt + " " + size.HeightPt + "] "));
            writer.Write(Encoding.ASCII.GetBytes("/Contents " + contentId + " 0 R >>\n"));
            writer.Write(Encoding.ASCII.GetBytes("endobj\n"));
        }

        private static void WritePageContent(BinaryWriter writer, List<long> xref, int contentId, int imageId, string imageFile)
        {
            var size = GetPageSize(imageFile);
            var content = "q\n" + size.WidthPt + " 0 0 " + size.HeightPt + " 0 0 cm\n/Im1 Do\nQ\n";
            var bytes = Encoding.ASCII.GetBytes(content);

            xref.Add(writer.BaseStream.Position);
            writer.Write(Encoding.ASCII.GetBytes(contentId + " 0 obj\n"));
            writer.Write(Encoding.ASCII.GetBytes("<< /Length " + bytes.Length + " >>\n"));
            writer.Write(Encoding.ASCII.GetBytes("stream\n"));
            writer.Write(bytes);
            writer.Write(Encoding.ASCII.GetBytes("endstream\nendobj\n"));
        }

        private static void WriteImageObject(BinaryWriter writer, List<long> xref, int imageId, string imageFile)
        {
            var ext = Path.GetExtension(imageFile).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
            {
                WriteJpegObject(writer, xref, imageId, imageFile);
                return;
            }

            WriteRgbObject(writer, xref, imageId, imageFile);
        }

        private static void WriteJpegObject(BinaryWriter writer, List<long> xref, int imageId, string imageFile)
        {
            byte[] jpegBytes = File.ReadAllBytes(imageFile);
            int width;
            int height;

            using (var image = Image.FromFile(imageFile))
            {
                width = image.Width;
                height = image.Height;
            }

            xref.Add(writer.BaseStream.Position);
            writer.Write(Encoding.ASCII.GetBytes(imageId + " 0 obj\n"));
            writer.Write(Encoding.ASCII.GetBytes("<< /Type /XObject /Subtype /Image "));
            writer.Write(Encoding.ASCII.GetBytes("/Width " + width + " /Height " + height + " "));
            writer.Write(Encoding.ASCII.GetBytes("/ColorSpace /DeviceRGB /BitsPerComponent 8 "));
            writer.Write(Encoding.ASCII.GetBytes("/Filter /DCTDecode /Length " + jpegBytes.Length + " >>\n"));
            writer.Write(Encoding.ASCII.GetBytes("stream\n"));
            writer.Write(jpegBytes);
            writer.Write(Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));
        }

        private static void WriteRgbObject(BinaryWriter writer, List<long> xref, int imageId, string imageFile)
        {
            int width;
            int height;
            byte[] rgbBytes;

            using (var image = Image.FromFile(imageFile))
            using (var bmp = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.DrawImage(image, 0, 0, image.Width, image.Height);
                width = bmp.Width;
                height = bmp.Height;
                var rect = new Rectangle(0, 0, width, height);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var stride = data.Stride;
                    var rowBytes = width * 3;
                    var raw = new byte[stride * height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, raw.Length);

                    rgbBytes = new byte[width * height * 3];
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.BlockCopy(raw, y * stride, rgbBytes, y * rowBytes, rowBytes);
                    }
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
            }

            byte[] compressed;
            using (var ms = new MemoryStream())
            {
                using (var deflate = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    deflate.Write(rgbBytes, 0, rgbBytes.Length);
                }
                compressed = ms.ToArray();
            }

            xref.Add(writer.BaseStream.Position);
            writer.Write(Encoding.ASCII.GetBytes(imageId + " 0 obj\n"));
            writer.Write(Encoding.ASCII.GetBytes("<< /Type /XObject /Subtype /Image "));
            writer.Write(Encoding.ASCII.GetBytes("/Width " + width + " /Height " + height + " "));
            writer.Write(Encoding.ASCII.GetBytes("/ColorSpace /DeviceRGB /BitsPerComponent 8 "));
            writer.Write(Encoding.ASCII.GetBytes("/Filter /FlateDecode /Length " + compressed.Length + " >>\n"));
            writer.Write(Encoding.ASCII.GetBytes("stream\n"));
            writer.Write(compressed);
            writer.Write(Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));
        }

        private static PageSize GetPageSize(string imageFile)
        {
            using (var image = Image.FromFile(imageFile))
            {
                var dpiX = image.HorizontalResolution <= 1 ? 300 : image.HorizontalResolution;
                var dpiY = image.VerticalResolution <= 1 ? 300 : image.VerticalResolution;
                var widthPt = image.Width * 72.0 / dpiX;
                var heightPt = image.Height * 72.0 / dpiY;

                return new PageSize
                {
                    WidthPt = widthPt.ToString("0.###", CultureInfo.InvariantCulture),
                    HeightPt = heightPt.ToString("0.###", CultureInfo.InvariantCulture)
                };
            }
        }

        private class PageSize
        {
            public string WidthPt { get; set; }
            public string HeightPt { get; set; }
        }
    }
}
