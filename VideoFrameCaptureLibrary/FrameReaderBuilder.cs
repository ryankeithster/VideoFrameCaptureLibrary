using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace VideoFrameCaptureLibrary
{
    /// <summary>
    /// Builds a MediaFrameReader object that can receive video frames from the specified system camera device.
    /// </summary>
    public class FrameReaderBuilder
    {
        private MediaCapture captureDevice;

        private uint width;
        private uint height;
        private string? mediaEncoding;
        private string? cameraName;
        private string? deviceId;

        public static string VideoEncodingNV12
        {
            get => MediaEncodingSubtypes.Nv12;
        }

        public uint PreferredWidth
        {
            get => width;
            set => width = value;
        }
        public uint PreferredHeight
        {
            get => height;
            set => height = value;
        }
        public string? MediaEncoding
        {
            get => mediaEncoding;
            set => mediaEncoding = value;
        }

        public string? CameraName
        {
            get => cameraName;
            set => cameraName = value;
        }

        public FrameReaderBuilder(string cameraName, uint preferredWidth, uint preferredHeight, string encoding)
        {
            captureDevice = new MediaCapture();
            CameraName = cameraName;
            PreferredHeight = preferredHeight;
            PreferredWidth = preferredWidth;
            MediaEncoding = (!string.IsNullOrEmpty(encoding) ? encoding : string.Empty);
        }

        public async Task<MediaFrameReader?> Build()
        {
            if (CameraName == null)
            {
                throw new ArgumentNullException(nameof(CameraName));
            }

            // Find the system device id for the specified camera
            deviceId = await GetCameraDeviceId(CameraName);

            if (string.IsNullOrEmpty(deviceId))
            {
                Console.WriteLine("Could not find system camera device with the specified name");
                return null;
            }

            MediaFrameSourceGroup sourceGroup = await MediaFrameSourceGroup.FromIdAsync(deviceId);

            var settings = new MediaCaptureInitializationSettings()
            {
                SourceGroup = sourceGroup,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            };

            // Create and initialize the media capture device instance representing
            // the camera
            var mediaCaptureDevice = new MediaCapture();
            try
            {
                await mediaCaptureDevice.InitializeAsync(settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine("MediaCapture initialization failed: " + ex.Message);
                return null;
            }

            // Get reference that will allow us to manipulate settings related to color video
            MediaFrameSourceInfo colorSourceInfo = sourceGroup.SourceInfos[0];

            // If supported, this is how you would get references that let you manipulate settings related
            // to depth and IR
            //MediaFrameSourceInfo infraredSourceInfo = sourceGroup.SourceInfos[1];
            //MediaFrameSourceInfo depthSourceInfo = sourceGroup.SourceInfos[2];

            // Set captured video format
            bool videoFormatSetSuccess = await SetPreferredVideoFormat(mediaCaptureDevice, colorSourceInfo);

            if (!videoFormatSetSuccess)
            {
                Console.WriteLine("Unable to set the captured video format to the specified format");
                return null;
            }

            // Create a reader to receive video frames
            MediaFrameSource colorFrameSource = mediaCaptureDevice.FrameSources[colorSourceInfo.Id];
            MediaFrameReader frameReader = await mediaCaptureDevice.CreateFrameReaderAsync(colorFrameSource);

            return frameReader;
        }

        private async Task<string> GetCameraDeviceId(string CameraName)
        {
            string deviceId = string.Empty;

            // Finds all video capture devices
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

            foreach (DeviceInformation device in devices)
            {
                if (device.Name.Contains(CameraName))
                {
                    deviceId = device.Id;
                    break;
                }
            }

            return deviceId;
        }

        private async Task<bool> SetPreferredVideoFormat(MediaCapture captureDevice, MediaFrameSourceInfo colorSourceInfo)
        {
            MediaFrameSource colorFrameSource = captureDevice.FrameSources[colorSourceInfo.Id];

            MediaFrameFormat? preferredFormat = colorFrameSource.SupportedFormats.Where(format =>
            {
                return format.VideoFormat.Width >= PreferredWidth
                && format.Subtype.Equals(MediaEncoding, StringComparison.CurrentCultureIgnoreCase);

            }).FirstOrDefault();

            if (preferredFormat == null)
            {
                // Our desired format is not supported
                return false;
            }

            await colorFrameSource.SetFormatAsync(preferredFormat);
            return true;
        }

    }
}
