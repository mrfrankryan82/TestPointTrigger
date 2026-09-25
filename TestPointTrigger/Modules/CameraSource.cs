// TestPoint Trigger - Camera source abstraction
// Developer: HaKDMoDz™ · v3.1.0 · 2026-09-26
using System;
using OpenCvSharp;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Where Live Coach gets its frames. A local USB webcam is opened by
    /// device index; a phone is opened by stream URL. OpenCvSharp handles
    /// both through VideoCapture, so the rest of the module does not care
    /// which is in use.
    /// </summary>
    public class CameraSource
    {
        public enum SourceKind { LocalDevice, NetworkStream }

        public SourceKind Kind { get; set; } = SourceKind.LocalDevice;
        public int DeviceIndex { get; set; } = 0;
        public string Url { get; set; } = "";

        public override string ToString()
        {
            return Kind == SourceKind.LocalDevice
                ? "Local camera " + DeviceIndex
                : "Phone: " + Url;
        }

        /// <summary>
        /// Open the capture. Throws nothing; returns null when the source
        /// cannot be opened so the caller can report it to the operator.
        /// </summary>
        public VideoCapture Open()
        {
            try
            {
                var cap = Kind == SourceKind.LocalDevice
                    ? new VideoCapture(DeviceIndex)
                    : new VideoCapture(Url);

                if (!cap.IsOpened()) { cap.Dispose(); return null; }

                // Keep the buffer short so coaching reflects the present,
                // not a queue of stale frames. Ignored by some backends.
                try { cap.Set(VideoCaptureProperties.BufferSize, 1); } catch { }
                return cap;
            }
            catch { return null; }
        }

        /// <summary>
        /// Build a stream URL for the common phone camera apps. Most serve
        /// MJPEG over HTTP on the phone's wifi address.
        /// </summary>
        public static string BuildPhoneUrl(string host, string app)
        {
            if (string.IsNullOrWhiteSpace(host)) return "";
            host = host.Trim();

            switch ((app ?? "").ToLowerInvariant())
            {
                case "ip webcam":
                    // Android "IP Webcam" - default port 8080
                    return host.Contains(":")
                        ? "http://" + host + "/video"
                        : "http://" + host + ":8080/video";

                case "droidcam":
                    // DroidCam - default port 4747
                    return host.Contains(":")
                        ? "http://" + host + "/mjpegfeed"
                        : "http://" + host + ":4747/mjpegfeed";

                case "iriun":
                case "rtsp":
                    return host.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                        ? host
                        : "rtsp://" + host;

                default:
                    // Assume the operator pasted a full URL.
                    return host;
            }
        }
    }
}
