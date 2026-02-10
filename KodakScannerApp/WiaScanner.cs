using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace KodakScannerApp
{
    public class WiaScanner
    {
        private const int WIA_IPA_HORIZONTAL_RESOLUTION = 6147;
        private const int WIA_IPA_VERTICAL_RESOLUTION = 6148;
        private const int WIA_IPA_CURRENT_INTENT = 6146;
        private const int WIA_DPS_DOCUMENT_HANDLING_SELECT = 3088;
        private const int WIA_DPS_DOCUMENT_HANDLING_STATUS = 3087;

        private const int WIA_DPS_DOCUMENT_HANDLING_FEEDER = 0x1;
        private const int WIA_DPS_DOCUMENT_HANDLING_DUPLEX = 0x4;
        private const int WIA_DPS_DOCUMENT_HANDLING_STATUS_FEED_READY = 0x1;
        private const string WIA_FORMAT_BMP = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";
        private const string WIA_FORMAT_JPEG = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";

        public List<DeviceInfoDto> ListDevices()
        {
            var list = new List<DeviceInfoDto>();
            dynamic manager = Activator.CreateInstance(Type.GetTypeFromProgID("WIA.DeviceManager"));
            foreach (var info in manager.DeviceInfos)
            {
                list.Add(new DeviceInfoDto
                {
                    Id = info.DeviceID,
                    Name = info.Properties["Name"].Value.ToString()
                });
            }
            return list;
        }

        public List<string> ScanToFiles(ScanSettings settings, string outputDir)
        {
            var files = new List<string>();
            try
            {
                dynamic manager = Activator.CreateInstance(Type.GetTypeFromProgID("WIA.DeviceManager"));
                dynamic deviceInfo = null;

                foreach (var info in manager.DeviceInfos)
                {
                    if (string.Equals((string)info.DeviceID, settings.DeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        deviceInfo = info;
                        break;
                    }
                }

                if (deviceInfo == null)
                {
                    throw new InvalidOperationException("Scanner not found. Ensure WIA driver is installed.");
                }

                dynamic device = deviceInfo.Connect();
                if (device.Items == null || device.Items.Count == 0)
                {
                    throw new InvalidOperationException("Scanner has no items available.");
                }
                dynamic item = device.Items[1];

                SetProperty(item.Properties, WIA_IPA_HORIZONTAL_RESOLUTION, settings.Dpi);
                SetProperty(item.Properties, WIA_IPA_VERTICAL_RESOLUTION, settings.Dpi);
                SetProperty(item.Properties, WIA_IPA_CURRENT_INTENT, MapColorMode(settings.ColorMode));

                var handlingSelect = GetProperty(device.Properties, WIA_DPS_DOCUMENT_HANDLING_SELECT);
                if (handlingSelect != null)
                {
                    try
                    {
                        var value = WIA_DPS_DOCUMENT_HANDLING_FEEDER;
                        if (settings.Duplex)
                        {
                            value |= WIA_DPS_DOCUMENT_HANDLING_DUPLEX;
                        }
                        handlingSelect.Value = value;
                    }
                    catch
                    {
                        // Some drivers reject this property; ignore and continue.
                    }
                }

                dynamic common = Activator.CreateInstance(Type.GetTypeFromProgID("WIA.CommonDialog"));

                var page = 0;
                while (true)
                {
                    if (!IsFeederReady(device))
                    {
                        if (page == 0)
                        {
                            throw new InvalidOperationException("Feeder is empty or not ready.");
                        }
                        break;
                    }

                    dynamic image = TransferWithFallback(common, item);
                    if (image == null)
                    {
                        break;
                    }

                    page++;
                    var basePath = Path.Combine(outputDir, "page_" + page.ToString("000"));
                    var path = SaveImageWithFallback(image, basePath);
                    files.Add(path);

                    if (page >= settings.MaxPages)
                    {
                        break;
                    }

                    if (!IsFeederReady(device))
                    {
                        break;
                    }
                }

                return files;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("WIA error: " + ex.Message + " (0x" + ex.HResult.ToString("X") + ")", ex);
            }
        }

        private static int MapColorMode(string mode)
        {
            if (string.Equals(mode, "bw", StringComparison.OrdinalIgnoreCase)) return 4;
            if (string.Equals(mode, "gray", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private static void SetProperty(dynamic properties, int propertyId, object value)
        {
            try
            {
                var prop = GetProperty(properties, propertyId);
                if (prop != null)
                {
                    prop.Value = value;
                }
            }
            catch { }
        }

        private static dynamic GetProperty(dynamic properties, int propertyId)
        {
            foreach (var prop in properties)
            {
                if ((int)prop.PropertyID == propertyId)
                {
                    return prop;
                }
            }
            return null;
        }

        private static bool IsFeederReady(dynamic device)
        {
            try
            {
                var status = GetProperty(device.Properties, WIA_DPS_DOCUMENT_HANDLING_STATUS);
                if (status == null) return false;
                int value = (int)status.Value;
                return (value & WIA_DPS_DOCUMENT_HANDLING_STATUS_FEED_READY) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static dynamic TransferWithFallback(dynamic common, dynamic item)
        {
            Exception last = null;

            try
            {
                return common.ShowTransfer(item, WIA_FORMAT_BMP, false);
            }
            catch (Exception ex)
            {
                last = ex;
            }

            try
            {
                return common.ShowTransfer(item, WIA_FORMAT_BMP, true);
            }
            catch (Exception ex)
            {
                last = ex;
            }

            try
            {
                return item.Transfer(WIA_FORMAT_BMP);
            }
            catch (Exception ex)
            {
                last = ex;
            }

            try
            {
                return common.ShowTransfer(item, WIA_FORMAT_JPEG, false);
            }
            catch (Exception ex)
            {
                last = ex;
            }

            try
            {
                return common.ShowTransfer(item, WIA_FORMAT_JPEG, true);
            }
            catch (Exception ex)
            {
                last = ex;
            }

            try
            {
                return item.Transfer(WIA_FORMAT_JPEG);
            }
            catch (Exception ex)
            {
                last = ex;
            }

            try
            {
                return common.ShowTransfer(item);
            }
            catch (Exception ex)
            {
                last = ex;
            }

            try
            {
                return item.Transfer();
            }
            catch (Exception ex)
            {
                last = ex;
            }

            if (last != null)
            {
                throw new InvalidOperationException("WIA transfer failed: " + last.Message + " (0x" + last.HResult.ToString("X") + ")");
            }

            return null;
        }

        private static string SaveImageWithFallback(dynamic image, string basePath)
        {
            var jpgPath = basePath + ".jpg";
            try
            {
                image.SaveFile(jpgPath);
                return jpgPath;
            }
            catch
            {
                var bmpPath = basePath + ".bmp";
                image.SaveFile(bmpPath);
                return bmpPath;
            }
        }
    }
}
