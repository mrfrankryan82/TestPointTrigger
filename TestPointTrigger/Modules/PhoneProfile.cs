// Mobile Surgery - Phone profile model, store, chipset->mode map and ADB/fastboot probe
// Developer: HaKDMoDz™ · v1.0.0 · 2026-10-09
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Everything we learn about one phone. Keyed on the USB/ADB serial so the
    /// expensive discovery is only repeated when the build fingerprint changes,
    /// a probe failed, or the operator asks for a rescan. The raw IMEI is never
    /// stored - only a short SHA-256 tag of it, when the phone will give it up.
    /// </summary>
    public sealed class DeviceProfile
    {
        public string Key;
        public string Serial, ImeiHash, ImeiStatus;
        public string Brand, Manufacturer, Model, MarketName, DeviceCode, Product;
        public string Platform, Hardware, SocVendor, SocModel, ChipVendor;
        public string AndroidVersion, Sdk, BuildId, BuildFingerprint, SecurityPatch;
        public string Bootloader, Baseband, Abi, Kernel, BootState, FlashLocked, Treble, SlotSuffix;
        public string Display, Density, Ram, Storage, Battery;
        public string UsbVid, UsbPid, UsbName;
        public string Source;            // "adb" or "fastboot"
        public string ImagePath, ImageUrl, ImageSource, ImageMatch, Notes;
        public DateTime FirstSeen, LastSeen, LastProbe;
        public int ProbeCount;
        public bool NeedsRescan;
        public List<string> Modes = new List<string>();
        public string Procedure = "";
        public SortedDictionary<string, string> Props = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public SortedDictionary<string, string> FastbootVars = new SortedDictionary<string, string>(StringComparer.Ordinal);

        [JsonIgnore]
        public string DisplayName =>
            string.Join(" ", new[] { Brand, string.IsNullOrWhiteSpace(MarketName) ? Model : MarketName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>One boot mode the Uno/Mega jig can drive (mirrors the sketch's mode table).</summary>
    public sealed class ModeInfo
    {
        public string Name, Keys, Usb, SoftwareCmd, Note;
        public ModeInfo(string n, string k, string u, string sw, string note) { Name = n; Keys = k; Usb = u; SoftwareCmd = sw; Note = note; }
    }

    public static class JigModes
    {
        public static readonly ModeInfo[] All =
        {
            new ModeInfo("edl",        "Test point",            "PC",     "adb reboot edl",        "Qualcomm 9008. TP shorted, then VCC on."),
            new ModeInfo("brom",       "Test point",            "PC",     "",                      "MediaTek BootROM. TP shorted, then VCC on."),
            new ModeInfo("eub",        "Test point",            "PC",     "",                      "Exynos USB Boot. TP shorted, then VCC on."),
            new ModeInfo("preloader",  "None (UART trigger)",   "PC",     "adb reboot",            "MediaTek preloader window. Release on UART boot-log match."),
            new ModeInfo("fastboot",   "Vol- + Power",          "PC",     "adb reboot bootloader", "Android fastboot."),
            new ModeInfo("bootloader", "Vol+ + Vol- + Power",   "PC",     "adb reboot bootloader", "Vendor bootloader menus."),
            new ModeInfo("download",   "Vol+ + Vol-",           "PC",     "adb reboot download",   "Samsung Odin / LG / Unisoc download."),
            new ModeInfo("recovery",   "Vol+ + Power",          "PC",     "adb reboot recovery",   "Recovery."),
            new ModeInfo("normal",     "Power",                 "PC",     "",                      "Normal boot; USB to the PC for ADB."),
        };

        public static ModeInfo Get(string name) =>
            All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Chipset/brand -> which boot modes apply, plus a written procedure.</summary>
    public static class ChipsetMap
    {
        public static void Classify(DeviceProfile p)
        {
            string hay = string.Join(" ", new[] { p.Platform, p.Hardware, p.SocVendor, p.SocModel, p.Product, p.Manufacturer })
                .ToLowerInvariant();
            string plat = (p.Platform ?? "").ToLowerInvariant();
            string hw = (p.Hardware ?? "").ToLowerInvariant();
            string brand = (p.Brand ?? p.Manufacturer ?? "").ToLowerInvariant();
            var modes = new List<string>();
            string vendor;

            if (Regex.IsMatch(plat + " " + hw, @"(^|\s)mt\d") || hay.Contains("mediatek") || hay.Contains("mtk"))
            { vendor = "MediaTek"; modes.AddRange(new[] { "brom", "preloader", "fastboot", "recovery", "normal" }); }
            else if (hay.Contains("qualcomm") || hw == "qcom" || Regex.IsMatch(plat, @"^(msm|apq|sdm|sm|qm)\d") ||
                     Regex.IsMatch(plat, @"^(lahaina|kona|bengal|trinket|msmnile|atoll|holi|taro|kalama|waipio|crow|parrot|pineapple|lito|sun|volcano)"))
            { vendor = "Qualcomm"; modes.AddRange(new[] { "edl", "fastboot", "recovery", "normal" }); }
            else if (Regex.IsMatch(plat + " " + hw, @"(^|\s)(ums|sp|sc|ud)\d") || hay.Contains("unisoc") || hay.Contains("spreadtrum") || hay.Contains("sprd"))
            { vendor = "Unisoc"; modes.AddRange(new[] { "download", "fastboot", "recovery", "normal" }); }
            else if (hay.Contains("exynos") || Regex.IsMatch(plat + " " + hw, @"(^|\s)(s5e\d|universal\d|erd\d)"))
            { vendor = "Samsung Exynos"; modes.AddRange(new[] { "download", "recovery", "eub", "normal" }); }
            else if (hay.Contains("kirin") || Regex.IsMatch(plat + " " + hw, @"(^|\s)hi\d{4}"))
            { vendor = "HiSilicon Kirin"; modes.AddRange(new[] { "fastboot", "recovery", "normal" }); }
            else if (Regex.IsMatch(plat + " " + hw, @"(^|\s)(gs\d{3}|zuma|laguna|ripcurrent)") || hay.Contains("tensor"))
            { vendor = "Google Tensor"; modes.AddRange(new[] { "fastboot", "recovery", "normal" }); }
            else
            { vendor = string.IsNullOrWhiteSpace(plat) ? "Unknown" : "Unclassified (" + plat + ")"; modes.AddRange(new[] { "fastboot", "recovery", "normal" }); }

            if ((brand.Contains("samsung") || brand.Contains("lg")) && !modes.Contains("download"))
                modes.Insert(Math.Min(1, modes.Count), "download");

            p.ChipVendor = vendor;
            p.Modes = modes.Distinct().ToList();
            p.Procedure = BuildProcedure(p);
        }

        public static string BuildProcedure(DeviceProfile p)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Chipset family: " + p.ChipVendor + " (inferred from platform '" + p.Platform + "'; confirm on the bench).");
            sb.AppendLine("Common set-up: battery out, jig GND to phone GND, VCC and BTEMP wired, TP clip on the pad for TP modes.");
            foreach (var name in p.Modes)
            {
                var m = JigModes.Get(name);
                if (m == null) continue;
                sb.AppendLine();
                sb.AppendLine("[" + m.Name.ToUpperInvariant() + "] " + m.Note);
                sb.AppendLine("  Jig:      serial command '" + m.Name + "' (keys: " + m.Keys + ", USB: " + m.Usb + ").");
                if (name == "preloader") sb.AppendLine("  Jig:      then 'trigger <boot-log text>' so the keys release on the matching UART line.");
                if (!string.IsNullOrEmpty(m.SoftwareCmd) && p.Source != "fastboot")
                    sb.AppendLine("  Software: " + m.SoftwareCmd + " (needs working ADB).");
                sb.AppendLine("  Verify:   PC enumerates the expected USB id; use 'off' when finished.");
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>JSON store of profiles with atomic write and a rolling backup.</summary>
    public sealed class ProfileStore
    {
        private readonly object _gate = new object();
        private Dictionary<string, DeviceProfile> _map = new Dictionary<string, DeviceProfile>(StringComparer.OrdinalIgnoreCase);
        public static readonly string PathJson = System.IO.Path.Combine(AppLog.Dir, "phone-profiles.json");

        public ProfileStore() { Load(); }

        public void Load()
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(PathJson)) return;
                    var list = JsonConvert.DeserializeObject<List<DeviceProfile>>(File.ReadAllText(PathJson)) ?? new List<DeviceProfile>();
                    _map = list.Where(d => !string.IsNullOrEmpty(d.Key))
                               .ToDictionary(d => d.Key, d => d, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) { AppLog.Warn("PhoneJig", "Profile load failed: " + ex.Message); }
            }
        }

        public DeviceProfile Get(string key)
        {
            lock (_gate) return key != null && _map.TryGetValue(key, out var d) ? d : null;
        }

        public List<DeviceProfile> All()
        {
            lock (_gate) return _map.Values.OrderBy(d => d.Brand).ThenBy(d => d.Model).ThenBy(d => d.Serial).ToList();
        }

        public void Upsert(DeviceProfile d)
        {
            lock (_gate) { _map[d.Key] = d; SaveLocked(); }
        }

        public void Remove(string key)
        {
            lock (_gate) { _map.Remove(key); SaveLocked(); }
        }

        private void SaveLocked()
        {
            try
            {
                Directory.CreateDirectory(AppLog.Dir);
                string tmp = PathJson + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_map.Values.ToList(), Formatting.Indented), Encoding.UTF8);
                if (File.Exists(PathJson)) File.Copy(PathJson, PathJson + ".bak", true);
                if (File.Exists(PathJson)) File.Delete(PathJson);
                File.Move(tmp, PathJson);
            }
            catch (Exception ex) { AppLog.Warn("PhoneJig", "Profile save failed: " + ex.Message); }
        }
    }

    /// <summary>ADB / fastboot discovery. Static, blocking - call from a worker thread.</summary>
    public static class DeviceProbe
    {
        public static string Adb, Fastboot;

        public static List<KeyValuePair<string, string>> ListAdb()
        {
            var res = new List<KeyValuePair<string, string>>();
            if (Adb == null) return res;
            foreach (var line in CliRunner.Capture(Adb, "devices", 6000).Split('\n').Skip(1))
            {
                var p = line.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2) res.Add(new KeyValuePair<string, string>(p[0], p[1]));
            }
            return res;
        }

        public static List<string> ListFastboot()
        {
            var res = new List<string>();
            if (Fastboot == null) return res;
            foreach (var line in CliRunner.Capture(Fastboot, "devices", 6000).Split('\n'))
            {
                var p = line.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2 && p[1].StartsWith("fastboot", StringComparison.OrdinalIgnoreCase)) res.Add(p[0]);
            }
            return res;
        }

        private static string Sh(string serial, string cmd, int ms = 8000) =>
            CliRunner.Capture(Adb, "-s " + serial + " shell " + cmd, ms);

        /// <summary>One cheap call: lets us skip the full probe for a phone we already know.</summary>
        public static string QuickFingerprint(string serial) => Sh(serial, "getprop ro.build.fingerprint", 5000).Trim();

        public static DeviceProfile ProbeAdb(string serial, DeviceProfile old, Action<string> log)
        {
            var d = new DeviceProfile { Key = serial, Serial = serial, Source = "adb" };
            log?.Invoke("probe: getprop");
            string all = Sh(serial, "getprop", 15000);
            foreach (Match m in Regex.Matches(all, @"^\[(.+?)\]: \[(.*)\]\s*$", RegexOptions.Multiline))
                d.Props[m.Groups[1].Value] = m.Groups[2].Value.Trim();

            string P(params string[] k) { foreach (var x in k) if (d.Props.TryGetValue(x, out var v) && !string.IsNullOrWhiteSpace(v)) return v; return ""; }
            d.Brand = P("ro.product.brand", "ro.product.vendor.brand");
            d.Manufacturer = P("ro.product.manufacturer", "ro.product.vendor.manufacturer");
            d.Model = P("ro.product.model", "ro.product.vendor.model");
            d.MarketName = P("ro.product.marketname", "ro.product.vendor.marketname", "ro.product.odm.marketname",
                             "ro.config.marketing_name", "ro.vendor.oplus.market.name", "ro.oppo.market.name");
            d.DeviceCode = P("ro.product.device", "ro.product.vendor.device");
            d.Product = P("ro.product.name", "ro.product.vendor.name");
            d.Platform = P("ro.board.platform");
            d.Hardware = P("ro.hardware", "ro.boot.hardware");
            d.SocVendor = P("ro.soc.manufacturer");
            d.SocModel = P("ro.soc.model", "ro.hardware.chipname");
            d.AndroidVersion = P("ro.build.version.release");
            d.Sdk = P("ro.build.version.sdk");
            d.BuildId = P("ro.build.display.id", "ro.build.id");
            d.BuildFingerprint = P("ro.build.fingerprint");
            d.SecurityPatch = P("ro.build.version.security_patch");
            d.Bootloader = P("ro.bootloader", "ro.boot.bootloader");
            d.Baseband = P("gsm.version.baseband", "ro.baseband");
            d.Abi = P("ro.product.cpu.abi");
            d.BootState = P("ro.boot.verifiedbootstate");
            d.FlashLocked = P("ro.boot.flash.locked", "ro.boot.vbmeta.device_state");
            d.Treble = P("ro.treble.enabled");
            d.SlotSuffix = P("ro.boot.slot_suffix");

            log?.Invoke("probe: display, memory, battery, storage, kernel");
            d.Display = Regex.Match(Sh(serial, "wm size"), @"(\d+x\d+)").Value;
            d.Density = Regex.Match(Sh(serial, "wm density"), @"(\d+)").Value;
            var mem = Regex.Match(Sh(serial, "cat /proc/meminfo"), @"MemTotal:\s+(\d+)");
            if (mem.Success) d.Ram = Math.Round(long.Parse(mem.Groups[1].Value) / 1048576.0, 1) + " GB";
            var df = Sh(serial, "df -h /data").Split('\n').Skip(1).FirstOrDefault();
            if (df != null) { var c = df.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (c.Length >= 4) d.Storage = c[1] + " total, " + c[3] + " free"; }
            string bat = Sh(serial, "dumpsys battery");
            var lv = Regex.Match(bat, @"level:\s*(\d+)"); var vt = Regex.Match(bat, @"voltage:\s*(\d+)");
            var tp = Regex.Match(bat, @"temperature:\s*(\d+)"); var tc = Regex.Match(bat, @"technology:\s*(\S+)");
            d.Battery = (lv.Success ? lv.Groups[1].Value + "%" : "?") + (vt.Success ? ", " + vt.Groups[1].Value + " mV" : "") +
                        (tp.Success ? ", " + int.Parse(tp.Groups[1].Value) / 10.0 + " C" : "") + (tc.Success ? ", " + tc.Groups[1].Value : "");
            d.Kernel = Sh(serial, "uname -r").Trim();

            log?.Invoke("probe: IMEI (restricted on Android 10+)");
            d.ImeiStatus = "restricted"; 
            string imei = TryImei(serial);
            if (imei != null) { d.ImeiHash = HashTag(imei); d.ImeiStatus = "hashed"; }

            FillUsb(d);
            Finish(d, old);
            return d;
        }

        public static DeviceProfile ProbeFastboot(string serial, DeviceProfile old, Action<string> log)
        {
            var d = new DeviceProfile { Key = serial, Serial = serial, Source = "fastboot", ImeiStatus = "n/a (fastboot)" };
            log?.Invoke("probe: fastboot getvar all");
            foreach (var line in CliRunner.Capture(Fastboot, "-s " + serial + " getvar all", 20000).Split('\n'))
            {
                var m = Regex.Match(line.Trim(), @"^(?:\(bootloader\)\s*)?([A-Za-z0-9_\-:\.]+):\s*(.*)$");
                if (m.Success && !m.Groups[1].Value.StartsWith("Finished") && !m.Groups[1].Value.StartsWith("all"))
                    d.FastbootVars[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }
            string V(params string[] k) { foreach (var x in k) if (d.FastbootVars.TryGetValue(x, out var v) && v.Length > 0) return v; return ""; }
            d.Model = V("product"); d.DeviceCode = V("product"); d.Bootloader = V("version-bootloader");
            d.Baseband = V("version-baseband"); d.FlashLocked = V("unlocked", "secure"); d.SlotSuffix = V("current-slot");
            d.Platform = V("hw-revision", "cpu"); d.Brand = V("manufacturer", "oem");
            FillUsb(d);
            Finish(d, old);
            return d;
        }

        private static void Finish(DeviceProfile d, DeviceProfile old)
        {
            var now = DateTime.Now;
            d.FirstSeen = old?.FirstSeen ?? now; d.LastSeen = now; d.LastProbe = now;
            d.ProbeCount = (old?.ProbeCount ?? 0) + 1;
            if (old != null)
            {
                d.ImagePath = old.ImagePath; d.ImageUrl = old.ImageUrl; d.ImageSource = old.ImageSource; d.ImageMatch = old.ImageMatch; d.Notes = old.Notes;
                if (string.IsNullOrEmpty(d.ImeiHash)) d.ImeiHash = old.ImeiHash;
                // An ADB profile is richer than a fastboot one - keep its props if this probe had none.
                if (d.Props.Count == 0) foreach (var kv in old.Props) d.Props[kv.Key] = kv.Value;
                if (string.IsNullOrEmpty(d.Brand)) d.Brand = old.Brand;
                if (string.IsNullOrEmpty(d.Platform) || d.Source == "fastboot") { d.Platform = string.IsNullOrEmpty(old.Platform) ? d.Platform : old.Platform; d.Hardware = old.Hardware; d.SocVendor = old.SocVendor; d.SocModel = old.SocModel; d.MarketName = old.MarketName; d.Manufacturer = old.Manufacturer; }
            }
            ChipsetMap.Classify(d);
        }

        private static string TryImei(string serial)
        {
            string o = Sh(serial, "service call iphonesubinfo 1");
            if (!o.Contains("Parcel(") || o.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            var sb = new StringBuilder();
            foreach (Match m in Regex.Matches(o, @"'([^']*)'")) sb.Append(m.Groups[1].Value);
            string digits = Regex.Replace(sb.ToString(), @"[^0-9]", "");
            return digits.Length >= 15 && Luhn(digits.Substring(0, 15)) ? digits.Substring(0, 15) : null;
        }

        private static bool Luhn(string s)
        {
            int sum = 0; bool dbl = false;
            for (int i = s.Length - 1; i >= 0; i--)
            { int n = s[i] - '0'; if (dbl) { n *= 2; if (n > 9) n -= 9; } sum += n; dbl = !dbl; }
            return sum % 10 == 0;
        }

        public static string HashTag(string s)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("tpt:" + s))).Replace("-", "").Substring(0, 12);
        }

        private static void FillUsb(DeviceProfile d)
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID_%'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string id = (o["DeviceID"] as string) ?? "";
                        if (id.IndexOf("\\" + d.Serial, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var m = Regex.Match(id, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                        if (!m.Success) continue;
                        d.UsbVid = m.Groups[1].Value.ToUpperInvariant(); d.UsbPid = m.Groups[2].Value.ToUpperInvariant();
                        d.UsbName = o["Name"] as string; return;
                    }
            }
            catch { }
        }
    }
}
