using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestPointTrigger
{
    /// <summary>
    /// Sends camera frames to a local Ollama vision model and raises short
    /// coaching lines. ADVISORY ONLY - never used to confirm boot-mode entry;
    /// BootModeWatcher owns that decision.
    /// </summary>
    public class VisionCoach : IDisposable
    {
        private const string Endpoint = "http://localhost:11434/api/generate";

        private const string Prompt =
            "You are watching a phone repair bench. Report ONLY: whether a test " +
            "point is currently shorted, whether the USB cable is inserted, and " +
            "whether the board is visible. Answer in under ten words. Do not " +
            "speculate about success or failure.";

        public event EventHandler<string> Observation;
        public string Model { get; set; } = "minicpm-v";

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private CancellationTokenSource _cts;

        /// <summary>
        /// Begin polling. frameSource must return the latest camera frame,
        /// or null if none is available yet.
        /// </summary>
        public void Start(Func<Bitmap> frameSource, int intervalMs = 1500)
        {
            Stop();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Observation?.Invoke(this, "[vision loop started, model " + Model + "]");
            var waitingLogged = false;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using (var frame = frameSource())
                        {
                            if (frame == null)
                            {
                                if (!waitingLogged)
                                {
                                    waitingLogged = true;
                                    Observation?.Invoke(this, "[waiting for first frame...]");
                                }
                            }
                            else
                            {
                                var text = await DescribeAsync(frame, token).ConfigureAwait(false);
                                Observation?.Invoke(this, string.IsNullOrWhiteSpace(text)
                                    ? "[empty reply from model]"
                                    : text.Trim());
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Observation?.Invoke(this, "[vision unavailable: " + ex.Message + "]");
                    }

                    try { await Task.Delay(intervalMs, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }, token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async Task<string> DescribeAsync(Bitmap frame, CancellationToken token)
        {
            string b64;
            using (var ms = new MemoryStream())
            {
                frame.Save(ms, ImageFormat.Jpeg);
                b64 = Convert.ToBase64String(ms.ToArray());
            }

            var json = "{\"model\":\"" + Model + "\",\"stream\":false,\"prompt\":\"" +
                       Prompt.Replace("\"", "\\\"") + "\",\"images\":[\"" + b64 + "\"]}";

            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var resp = await _http.PostAsync(Endpoint, content, token).ConfigureAwait(false))
            {
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var marker = "\"response\":\"";
                var i = body.IndexOf(marker, StringComparison.Ordinal);
                if (i < 0) return null;
                i += marker.Length;
                var sb = new StringBuilder();
                for (; i < body.Length && body[i] != '"'; i++)
                {
                    if (body[i] == '\\' && i + 1 < body.Length)
                    {
                        i++;
                        sb.Append(body[i] == 'n' ? ' ' : body[i]);
                    }
                    else sb.Append(body[i]);
                }
                return sb.ToString();
            }
        }

        public void Dispose() { Stop(); _http?.Dispose(); }
    }
}
