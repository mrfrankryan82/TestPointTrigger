// Mobile Surgery - forensic tool / licence model + JSON store
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TestPointTrigger
{
    /// <summary>One forensic/servicing tool or licence held on the bench.</summary>
    public class ToolLicense
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Tool { get; set; } = "";        // UnlockTool, Chimera, Octoplus, DFT, EFT, Cellebrite...
        public string Vendor { get; set; } = "";
        public string Category { get; set; } = "Servicing";   // Forensic / Servicing / FRP / Flashing
        public string LicenseType { get; set; } = "Activation"; // Dongle / Activation / Credits / Subscription / Perpetual
        public string LicenseKey { get; set; } = "";
        public string Account { get; set; } = "";
        public string HardwareId { get; set; } = "";   // dongle / smart-card serial
        public DateTime? PurchaseDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int Credits { get; set; }
        public decimal Cost { get; set; }
        public string Status { get; set; } = "Active";  // Active / Expired / Suspended
        public string Url { get; set; } = "";
        public string Notes { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Days until expiry (negative = already expired); null when no expiry set.</summary>
        [JsonIgnore]
        public double? DaysToExpiry =>
            ExpiryDate.HasValue ? (double?)(ExpiryDate.Value.Date - DateTime.Today).TotalDays : null;
    }

    /// <summary>
    /// JSON-backed store for tool licences. Same durability model as
    /// RepairStore: atomic write + rolling backups.
    /// </summary>
    public class LicenseStore
    {
        public static readonly string Dir = AppLog.Dir;
        public static readonly string FilePath = Path.Combine(Dir, "licenses.json");
        public static readonly string BackupDir = Path.Combine(Dir, "backups");

        private readonly object _gate = new object();
        public List<ToolLicense> Items { get; private set; } = new List<ToolLicense>();

        public void Load()
        {
            lock (_gate)
            {
                try
                {
                    if (File.Exists(FilePath))
                        Items = JsonConvert.DeserializeObject<List<ToolLicense>>(File.ReadAllText(FilePath))
                                ?? new List<ToolLicense>();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Licenses", "Load failed (keeping empty set): " + ex.Message);
                    Items = new List<ToolLicense>();
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
                    File.WriteAllText(tmp, JsonConvert.SerializeObject(Items, Formatting.Indented));
                    if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                    else File.Move(tmp, FilePath);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Licenses", "Save failed: " + ex.Message);
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
                    Path.Combine(BackupDir, "licenses-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true);
                foreach (var f in Directory.GetFiles(BackupDir, "licenses-*.json")
                                           .OrderByDescending(f => f).Skip(20))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }
    }
}
