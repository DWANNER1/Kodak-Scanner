using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using NTwain;
using NTwain.Data;
using NTwain.Internals;

namespace KodakScannerApp
{
    public class TwainScanner
    {
        public List<DeviceInfoDto> ListDevices()
        {
            var devices = new List<DeviceInfoDto>();
            var session = CreateSession();
            try
            {
                session.Open();
                foreach (var source in session.GetSources())
                {
                    devices.Add(new DeviceInfoDto
                    {
                        Id = source.Name ?? source.Id.ToString(),
                        Name = source.Name
                    });
                }
            }
            finally
            {
                session.Close();
            }
            return devices;
        }

        public List<string> ScanToFiles(ScanSettings settings, string outputDir, Action<string> onPageSaved)
        {
            var files = new List<string>();
            if (settings == null) settings = new ScanSettings();

            var session = CreateSession();
            try
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
                        if (e.NativeData == IntPtr.Zero && e.MemoryData == null && string.IsNullOrWhiteSpace(e.FileDataPath))
                        {
                            return;
                        }
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
                }

                if (error != null)
                {
                    throw new InvalidOperationException(error.Message, error);
                }
            }
            finally
            {
                session.Close();
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
                foreach (var src in session.GetSources())
                {
                    return src;
                }
                return null;
            }

            foreach (var source in session.GetSources())
            {
                if (string.Equals(source.Name, deviceId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(source.Id.ToString(), deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return source;
                }
            }

            foreach (var src in session.GetSources())
            {
                return src;
            }
            return null;
        }

        private static void ApplyCapabilities(DataSource source, ScanSettings settings)
        {
            if (source == null || !source.IsOpen) return;

            var dpi = settings.Dpi > 0 ? settings.Dpi : 300;
            var scanSide = NormalizeScanSide(settings);

            SetFix32(source.Capabilities.ICapXResolution, dpi);
            SetFix32(source.Capabilities.ICapYResolution, dpi);

            var pixel = MapPixelType(settings.ColorMode);
            TrySetPixel(source.Capabilities.ICapPixelType, pixel);

            var needsDuplexPath = scanSide == "back" || scanSide == "both";

            TrySetBool(source.Capabilities.CapFeederEnabled, BoolType.True);
            TrySetBool(source.Capabilities.CapDuplexEnabled, needsDuplexPath ? BoolType.True : BoolType.False);
            TrySetCameraSide(source.Capabilities.CapCameraSide, MapCameraSide(scanSide));

            if (settings.MaxPages > 0)
            {
                TrySetCount(source.Capabilities.CapXferCount, settings.MaxPages);
            }
        }

        private static void SetFix32(ICapWrapper<TWFix32> capability, int value)
        {
            if (capability == null) return;
            try
            {
                capability.SetValue(ToFix32(value));
            }
            catch { }
        }

        private static void TrySetPixel(ICapWrapper<PixelType> capability, PixelType value)
        {
            if (capability == null) return;
            try
            {
                capability.SetValue(value);
            }
            catch { }
        }

        private static void TrySetBool(ICapWrapper<BoolType> capability, BoolType value)
        {
            if (capability == null) return;
            try
            {
                capability.SetValue(value);
            }
            catch { }
        }

        private static void TrySetCameraSide(ICapWrapper<CameraSide> capability, CameraSide value)
        {
            if (capability == null) return;
            try
            {
                capability.SetValue(value);
            }
            catch { }
        }

        private static void TrySetCount(ICapWrapper<int> capability, int value)
        {
            if (capability == null) return;
            try
            {
                capability.SetValue(value);
            }
            catch { }
        }

        private static PixelType MapPixelType(string mode)
        {
            if (string.Equals(mode, "bw", StringComparison.OrdinalIgnoreCase)) return PixelType.BlackWhite;
            if (string.Equals(mode, "gray", StringComparison.OrdinalIgnoreCase)) return PixelType.Gray;
            return PixelType.RGB;
        }

        private static string NormalizeScanSide(ScanSettings settings)
        {
            var scanSide = settings?.ScanSide;
            if (string.IsNullOrWhiteSpace(scanSide))
            {
                return settings != null && settings.Duplex ? "both" : "front";
            }

            if (string.Equals(scanSide, "back", StringComparison.OrdinalIgnoreCase)) return "back";
            if (string.Equals(scanSide, "both", StringComparison.OrdinalIgnoreCase)) return "both";
            return "front";
        }

        private static CameraSide MapCameraSide(string scanSide)
        {
            if (string.Equals(scanSide, "back", StringComparison.OrdinalIgnoreCase)) return CameraSide.Bottom;
            if (string.Equals(scanSide, "both", StringComparison.OrdinalIgnoreCase)) return CameraSide.Both;
            return CameraSide.Top;
        }

        private static TWFix32 ToFix32(double value)
        {
            var whole = (short)Math.Floor(value);
            var fraction = (ushort)Math.Round((value - Math.Floor(value)) * 65536.0);
            return new TWFix32 { Whole = whole, Fraction = fraction };
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
