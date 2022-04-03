using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Capture.Frames;
using Windows.Storage.Streams;

namespace VideoFrameCaptureLibrary
{
    public class VideoFrameReader
    {
        private FrameReaderBuilder _frameReaderBuilder;
        private MediaFrameReader? _frameReader;
        private uint _framesPerSecond;
        private double minMillisecondsBetweenFrames;
        private DateTime prevFrameTime;
        private static object _lock = new object();

        public VideoFrameReader(uint framesPerSecond)
        {
            //string cameraName = "C615";
            string cameraName = "Surface";

            _framesPerSecond = framesPerSecond;

            minMillisecondsBetweenFrames = 1000.0 / framesPerSecond;

            // Build a video frame reader to capture 720p video.
            // NV12 seems to be the preferred format. Attempting to use MJPG instead for instance still results in NV12 imagery.
            // NV12 is similar to YUV420: YUV color space with 420 chroma subsampling
            _frameReaderBuilder = new FrameReaderBuilder(cameraName, 1280, 720, FrameReaderBuilder.VideoEncodingNV12);
        }

        public async Task InitializeAsync()
        {
            _frameReader = await _frameReaderBuilder.Build();
        }

        public async Task StartCapture()
        {
            if (_frameReader != null)
            {
                _frameReader.FrameArrived += ColorFrameReader_FrameArrivedAsync;
                await _frameReader.StartAsync();
            }
        }

        public async Task StopCapture()
        {
            await _frameReader?.StopAsync();

            if (_frameReader != null)
            {
                _frameReader.FrameArrived -= ColorFrameReader_FrameArrivedAsync;
            }
        }

        /// <summary>
        /// Grab the latest video frame, convert it and write it to disk as a JPEG image.
        /// </summary>
        /// <seealso cref="https://stackoverflow.com/questions/34291291/how-to-get-byte-array-from-softwarebitmap"/>
        /// <seealso cref="https://docs.microsoft.com/en-us/windows/uwp/audio-video-camera/process-media-frames-with-mediaframereader"/>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void ColorFrameReader_FrameArrivedAsync(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            // Grab the current timestamp immediately. This will be used to generate a unique
            // output file name, and waiting until later in the method to grab the timestamp
            // can result in resource contention as other threads grab the same timestamp and
            // try and create a file that has already been created by previous threads
            DateTime dtNow = DateTime.UtcNow;

            // Ensure that prevFrameTime is only modified by one thread at a time in this critical
            // section so that we continue to capture frames at the desired frames per second.
            lock (_lock)
            {
                TimeSpan timeSinceLastFrame = dtNow - prevFrameTime;
                double msSinceLastFrame = timeSinceLastFrame.TotalMilliseconds;

                if (msSinceLastFrame < minMillisecondsBetweenFrames)
                {
                    return;
                }
                prevFrameTime = dtNow;
            }

            Console.WriteLine(DateTime.Now.Millisecond.ToString() + " " + Thread.CurrentThread.ManagedThreadId + " ==> Frame received");

            StringBuilder filePath = new StringBuilder(@"C:\temp\video_frames\");
            MediaFrameReference mediaFrameReference;
            if ((mediaFrameReference = sender.TryAcquireLatestFrame()) != null)
            {
                Console.WriteLine(DateTime.Now.Millisecond.ToString() + " " + Thread.CurrentThread.ManagedThreadId + " ==> Acquired latest frame");
                VideoMediaFrame videoFrame = mediaFrameReference.VideoMediaFrame;

                if (videoFrame != null)
                {
                    byte[] frameBytes;
                    using (var ms = new InMemoryRandomAccessStream())
                    {
                        // Get encoder to convert the frame to JPEG
                        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, ms);

                        // The video frame will likely be in NV12 format. NV12 uses a YUV color space instead of an RGB color space.
                        // BitmapEncoder will only work with imagery in RGB color space, so convert to RGB first
                        SoftwareBitmap interimRgbSwBmp = SoftwareBitmap.Convert(videoFrame.SoftwareBitmap, BitmapPixelFormat.Rgba8);

                        encoder.SetSoftwareBitmap(interimRgbSwBmp);

                        try
                        {
                            // We don't want to encode any more frames so commit it
                            await encoder.FlushAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(DateTime.Now.Millisecond.ToString() + " " + Thread.CurrentThread.ManagedThreadId + " ==> Caught exception");
                            return;
                        }

                        frameBytes = new byte[ms.Size];
                        await ms.ReadAsync(frameBytes.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);

                        // frameBytes now contains the JPEG data; write it out!
                        string fileName = @"C:\temp\video_frames\" + dtNow.Hour.ToString() + "h_" + dtNow.Minute.ToString() + "m_" + dtNow.Second.ToString() + "s_" + dtNow.Millisecond.ToString() + "ms.jpg";

                        using (FileStream fs = File.Open(fileName, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            fs.Write(frameBytes, 0, frameBytes.Length);
                            fs.Flush();
                        }

                        interimRgbSwBmp.Dispose();
                        //File.WriteAllBytes(fileName, frameBytes);
                    }

                }

                mediaFrameReference.Dispose();
            }
        }
    }
}
