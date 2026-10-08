// Mobile Surgery - repair record model + JSON store
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TestPointTrigger
{
    /// <summary>One completed board/phone job.</summary>
    public class RepairRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Date { get; set; } = DateTime.Today;
        public string Brand { get; set; } = "";
        public string Model { get; set; } = "";
        public string Chipset { get; set; } = "";     // Qualcomm / MediaTek / Exynos / Unisoc / Kirin
        public string Imei { get; set; } = "";
        public string Serial { get; set; } = "";
        public string BoardId { get; set; } = "";      // short label the tech assigns
        public string Fault { get; set; } = "";
        public string Procedure { get; set; } = "";    // EDL test-point / BROM / FRP / flash / format...
        public string TestPoint { get; set; } = "";
        public string Outcome { get; set; } = "Success"; // Success / Partial / Failed / Abandoned
        public string Tools { get; set; } = "";
        public int TimeMinutes { get; set; }
        public string Technician { get; set; } = "HaKDMoDz";
        public string ImagePath { get; set; } = "";     // link to a labelled Pad Finder PNG
        public string Notes { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// JSON-backed store for repair records. Single file, atomic writes
    /// (temp + replace) and a rolling backup on every save so a bad write or
    /// a fat-fingered delete is always recoverable.
    /// </summary>
    public class RepairStore
    {
        public static readonly string Dir = AppLog.Dir;   // share the app-data folder
        public static readonly string FilePath = Path.Combine(Dir, "repairs.json");
        public static readonly string BackupDir = Path.Combine(Dir, "backups");

        private readonly object _gate = new object();
        public List<RepairRecord> Records { get; private set; } = new List<RepairRecord>();

        public void Load()
        {
            lock (_gate)
            {
                try
                {
                    if (File.Exists(FilePath))
                        Records = JsonConvert.DeserializeObject<List<RepairRecord>>(File.ReadAllText(FilePath))
                                  ?? new List<RepairRecord>();
                }
                catch (Exception ex)
                {
                    AppLog.Error("RepairDb", "Load failed (keeping empty set): " + ex.Message);
                    Records = new List<RepairRecord>();
                }
            }
        }

        public void Save()
        {
            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    BackupIfExists();
                    var tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, JsonConvert.SerializeObject(Records, Formatting.Indented));
                    if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                    else File.Move(tmp, FilePath);
                }
                catch (Exception ex)
                {
                    AppLog.Error("RepairDb", "Save failed: " + ex.Message);
                }
            }
        }

        private void BackupIfExists()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                Directory.CreateDirectory(BackupDir);
                File.Copy(FilePath,
                    Path.Combine(BackupDir, "repairs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true);
                // keep only the newest 20 backups
                foreach (var f in Directory.GetFiles(BackupDir, "repairs-*.json")
                                           .OrderByDescending(f => f).Skip(20))
                    try { File.Delete(f); } catch { }
            }
            catch { /* backup is best-effort */ }
        }
    }
}
