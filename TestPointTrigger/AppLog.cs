// Mobile Surgery - central append-only log
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TestPointTrigger
{
    /// <summary>One parsed log line, as raised live and as read back from disk.</summary>
    public sealed class LogEntry
    {
        public DateTime Time;      // local
        public string Source;
        public string Level;       // INFO, WARN, ERROR, USB, EYES, VOICE...
        public string Message;
        public string Raw;         // exact on-disk line
    }

    /// <summary>
    /// Process-wide, append-only text log. Every module routes its lines here
    /// so there is ONE file to review across sessions. Writes are serialized
    /// and never throw into the caller - logging must never break the app.
    ///
    /// On-disk format, one line each:
    ///   2026-09-26 14:03:11 | LiveCoach | INFO | message text
    /// </summary>
    public static class AppLog
    {
        public const string Sep = " | ";

        public static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "TestPointTrigger");
        public static readonly string FilePath = Path.Combine(Dir, "testpoint-trigger.log");

        private static readonly object _gate = new object();

        /// <summary>Raised on every appended line (on the calling thread).</summary>
        public static event Action<LogEntry> Entry;

        static AppLog()
        {
            try { Directory.CreateDirectory(Dir); } catch { }
            int pid = 0;
            try { pid = Process.GetCurrentProcess().Id; } catch { }
            Append("App", "INFO",
                "===== Session started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                " \u00b7 v" + AppInfo.VersionText + " \u00b7 PID " + pid + " =====");
        }

        public static void Append(string source, string level, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var now = DateTime.Now;
            source = Clean(string.IsNullOrEmpty(source) ? "App" : source);
            level = Clean(string.IsNullOrEmpty(level) ? "INFO" : level);
            var msg = message.Replace("\r", " ").Replace("\n", "  ").Trim();
            var raw = now.ToString("yyyy-MM-dd HH:mm:ss") + Sep + source + Sep + level + Sep + msg;

            lock (_gate)
            {
                try { File.AppendAllText(FilePath, raw + Environment.NewLine, Encoding.UTF8); }
                catch { /* swallow - never break the caller over a log write */ }
            }

            try { Entry?.Invoke(new LogEntry { Time = now, Source = source, Level = level, Message = msg, Raw = raw }); }
            catch { }
        }

        public static void Info(string source, string message) => Append(source, "INFO", message);
        public static void Warn(string source, string message) => Append(source, "WARN", message);
        public static void Error(string source, string message) => Append(source, "ERROR", message);

        /// <summary>Best-effort parse of a stored line back into its fields.</summary>
        public static LogEntry Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            var e = new LogEntry { Raw = line, Source = "", Level = "", Message = line };
            var p = line.Split(new[] { Sep }, 4, StringSplitOptions.None);
            if (p.Length == 4)
            {
                DateTime.TryParse(p[0], out var t);
                e.Time = t; e.Source = p[1]; e.Level = p[2]; e.Message = p[3];
            }
            return e;
        }

        private static string Clean(string s) => (s ?? "").Replace("|", "/").Trim();
    }
}
