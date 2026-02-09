using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace KodakScannerApp
{
    public static class ImageExporter
    {
        public static void WriteMultipageTiff(List<string> files, string outputFile)
        {
            if (files == null || files.Count == 0)
            {
                throw new InvalidOperationException("No images to export");
            }

            var codec = GetEncoder(ImageFormat.Tiff);
            if (codec == null)
            {
                throw new InvalidOperationException("TIFF encoder not found");
            }

            using (var first = Image.FromFile(files[0]))
            {
                var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
                first.Save(outputFile, codec, ep);

                for (int i = 1; i < files.Count; i++)
                {
                    using (var img = Image.FromFile(files[i]))
                    {
                        ep.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.FrameDimensionPage);
                        first.SaveAdd(img, ep);
                    }
                }

                ep.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.Flush);
                first.SaveAdd(ep);
            }
        }

        public static List<string> WriteImageSet(List<string> files, string outputDir, string baseName, string format)
        {
            var outFiles = new List<string>();
            var ext = format.ToLowerInvariant();
            var imgFormat = (ext == "png") ? ImageFormat.Png : ImageFormat.Jpeg;

            for (int i = 0; i < files.Count; i++)
            {
                var name = baseName + "_page" + (i + 1).ToString("00") + "." + ext;
                var outPath = Path.Combine(outputDir, name);
                using (var img = Image.FromFile(files[i]))
                {
                    img.Save(outPath, imgFormat);
                }
                outFiles.Add(outPath);
            }

            return outFiles;
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}
