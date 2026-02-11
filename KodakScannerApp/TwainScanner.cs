using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using NTwain;
using NTwain.Data;
using NTwain.Internals;
using System.Linq;

namespace KodakScannerApp
{
    public class TwainScanner
    {
        public List<DeviceInfoDto> ListDevices()
        {
            var devices = new List<DeviceInfoDto>();
            using (var session = CreateSession())
            {
                session.Open();
                foreach (var source in session)
                {
                    devices.Add(new DeviceInfoDto
                    {
                        Id = source.Name ?? source.Identity.Id.ToString(),
                        Name = source.Name
                    });
                }
                session.Close();
            }
            return devices;
        }

        public List<string> ScanToFiles(ScanSettings settings, string outputDir, Action<string> onPageSaved)
        {
            var files = new List<string>();
            if (settings == null) settings = new ScanSettings();

            using (var session = CreateSession())
            {
                session.Open();

                var source = FindSource(session, settings.DeviceId);
                if (source == null)
                {
                    throw new InvalidOperationException("TWAIN scanner not found.");
                }

                source.Open();

                ApplyCapabilities(source, settings);

                var done = new ManualResetEvent(false);
                Exception error = null;
                var page = 0;

                session.TransferReady += (s, e) =>
                {
                    if (settings.MaxPages > 0 && page >= settings.MaxPages)
                    {
                        e.CancelAll = true;
                    }
                };

                session.DataTransferred += (s, e) =>
                {
                    try
                    {
                        if (e.Data == IntPtr.Zero) return;
                        using (var stream = e.GetNativeImageStream())
                        {
                            if (stream == null) return;
                            using (var image = Image.FromStream(stream))
                            {
                                page++;
                                var basePath = Path.Combine(outputDir, "page_" + page.ToString("000"));
                                var path = SaveImageWithFallback(image, basePath);
                                files.Add(path);
                                onPageSaved?.Invoke(path);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                };

                session.TransferError += (s, e) =>
                {
                    error = e.Exception ?? new InvalidOperationException("TWAIN transfer error.");
                };

                session.SourceDisabled += (s, e) =>
                {
                    done.Set();
                };

                try
                {
                    source.Enable(SourceEnableMode.NoUI, false, IntPtr.Zero);
                    done.WaitOne();
                }
                finally
                {
                    if (source.IsOpen)
                    {
                        source.Close();
                    }
                    session.Close();
                }

                if (error != null)
                {
                    throw new InvalidOperationException(error.Message, error);
                }
            }

            return files;
        }

        private static TwainSession CreateSession()
        {
            var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, typeof(TwainScanner).Assembly);
            var session = new TwainSession(appId);
            return session;
        }

        private static DataSource FindSource(TwainSession session, string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return session.FirstOrDefault();
            }

            foreach (var source in session)
            {
                if (string.Equals(source.Name, deviceId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(source.Identity.Id.ToString(), deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return source;
                }
            }

            return session.FirstOrDefault();
        }

        private static void ApplyCapabilities(DataSource source, ScanSettings settings)
        {
            if (source == null || !source.IsOpen) return;

            var dpi = settings.Dpi > 0 ? settings.Dpi : 300;

            TrySet(source.Capabilities.ICapXResolution, dpi);
            TrySet(source.Capabilities.ICapYResolution, dpi);

            var pixel = MapPixelType(settings.ColorMode);
            if (source.Capabilities.ICapPixelType.CanSet &&
                source.Capabilities.ICapPixelType.GetValues().Contains(pixel))
            {
                source.Capabilities.ICapPixelType.SetValue(pixel);
            }

            if (source.Capabilities.CapFeederEnabled.CanSet)
            {
                source.Capabilities.CapFeederEnabled.SetValue(true);
            }

            if (settings.Duplex && source.Capabilities.CapDuplexEnabled.CanSet)
            {
                source.Capabilities.CapDuplexEnabled.SetValue(true);
            }

            if (settings.MaxPages > 0 && source.Capabilities.CapXferCount.CanSet)
            {
                source.Capabilities.CapXferCount.SetValue((short)settings.MaxPages);
            }
        }

        private static void TrySet(OneValueCapability<float> capability, float value)
        {
            if (capability == null) return;
            if (capability.CanSet && capability.GetValues().Contains(value))
            {
                capability.SetValue(value);
            }
        }

        private static PixelType MapPixelType(string mode)
        {
            if (string.Equals(mode, "bw", StringComparison.OrdinalIgnoreCase)) return PixelType.BlackWhite;
            if (string.Equals(mode, "gray", StringComparison.OrdinalIgnoreCase)) return PixelType.Gray;
            return PixelType.RGB;
        }

        private static string SaveImageWithFallback(Image image, string basePath)
        {
            var jpgPath = basePath + ".jpg";
            try
            {
                image.Save(jpgPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                return jpgPath;
            }
            catch
            {
                var bmpPath = basePath + ".bmp";
                image.Save(bmpPath, System.Drawing.Imaging.ImageFormat.Bmp);
                return bmpPath;
            }
        }
    }
}
