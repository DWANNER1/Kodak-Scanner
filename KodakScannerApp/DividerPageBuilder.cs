using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace KodakScannerApp
{
    public static class DividerPageBuilder
    {
        public static string CreateDividerPage(string text, string outputDir, int dpi)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Header text is required.");
            }

            Directory.CreateDirectory(outputDir);

            if (dpi <= 0) dpi = 300;
            var width = (int)Math.Round(8.5 * dpi);
            var height = (int)Math.Round(11.0 * dpi);
            var filePath = Path.Combine(outputDir, "header_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");

            using (var bmp = new Bitmap(width, height))
            {
                bmp.SetResolution(dpi, dpi);

                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.White);

                    var bandTop = (int)(height * 0.14);
                    var bandHeight = (int)(height * 0.16);
                    var bandRect = new Rectangle(0, bandTop, width, bandHeight);

                    using (var bandBrush = new SolidBrush(Color.FromArgb(28, 46, 82)))
                    using (var accentBrush = new SolidBrush(Color.FromArgb(236, 190, 78)))
                    {
                        g.FillRectangle(bandBrush, bandRect);
                        g.FillRectangle(accentBrush, 0, bandRect.Bottom - (int)(height * 0.01), width, (int)(height * 0.01));
                    }

                    var textArea = new RectangleF(width * 0.12f, bandRect.Bottom + height * 0.12f, width * 0.76f, height * 0.45f);
                    var fontSize = 96f;
                    var chosenFont = GetBestFitFont(g, text, textArea, fontSize, 54f);

                    using (chosenFont)
                    using (var textBrush = new SolidBrush(Color.FromArgb(26, 29, 38)))
                    using (var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Near,
                        Trimming = StringTrimming.Word,
                        FormatFlags = StringFormatFlags.LineLimit
                    })
                    {
                        g.DrawString(text.Trim(), chosenFont, textBrush, textArea, format);
                    }

                    var lineY = textArea.Top + textArea.Height + height * 0.02f;
                    using (var linePen = new Pen(Color.FromArgb(216, 221, 232), height * 0.004f))
                    {
                        g.DrawLine(linePen, width * 0.2f, lineY, width * 0.8f, lineY);
                    }
                }

                bmp.Save(filePath, ImageFormat.Png);
            }

            return filePath;
        }

        private static Font GetBestFitFont(Graphics g, string text, RectangleF bounds, float startSize, float minSize)
        {
            var size = startSize;
            while (size >= minSize)
            {
                var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point);
                var measured = g.MeasureString(text, font, (int)bounds.Width);
                if (measured.Height <= bounds.Height)
                {
                    return font;
                }
                font.Dispose();
                size -= 4f;
            }

            return new Font("Segoe UI", minSize, FontStyle.Bold, GraphicsUnit.Point);
        }
    }
}
