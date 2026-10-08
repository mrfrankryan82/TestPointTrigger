// TestPoint Trigger - Device workbook export (.xlsx, one worksheet per phone) and model image lookup
// Developer: HaKDMoDz™ · v1.0.0 · 2026-10-09
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Newtonsoft.Json.Linq;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Finds a product photo for the exact model using Wikipedia / Wikimedia Commons (open APIs).
    /// Commercial spec sites such as GSMArena put scripted requests behind a bot check, so they are
    /// not used. A result is labelled "Exact" only when the article title equals the phone's name, or
    /// "Model-code match" when the article text contains the model code; anything weaker is saved
    /// as "Closest match, verify" so the workbook never claims more than was verified.
    /// </summary>
    internal static class ImageFetcher
    {
        private static readonly HttpClient Http = Make();
        public static readonly string Dir = Path.Combine(AppLog.Dir, "DeviceImages");

        private static HttpClient Make()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("TestPointTrigger/3.6 (bench tool; contact via GitHub mrfrankryan82)");
            return h;
        }

        private static string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), @"[^a-z0-9]", "");
        private static string Join(string a, string b) => string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));

        private sealed class Candidate { public string Name, Page, ImageUrl, Source; }

        public static async Task<bool> FindAsync(DeviceProfile d, Action<string> log)
        {
            string brand = d.Brand ?? d.Manufacturer ?? "";
            var names = new List<string>(); var codes = new List<string>();
            void AddN(string q) { q = (q ?? "").Trim(); if (q.Length > 2 && !names.Contains(q, StringComparer.OrdinalIgnoreCase)) names.Add(q); }
            void AddC(string q) { q = (q ?? "").Trim(); if (q.Length > 2 && !codes.Contains(q, StringComparer.OrdinalIgnoreCase)) codes.Add(q); }
            AddN(Join(brand, d.MarketName)); AddN(d.MarketName);
            AddC(d.Model); AddC(d.DeviceCode);
            if (names.Count == 0 && codes.Count == 0) { log?.Invoke("image: no model name to search for"); d.ImageMatch = "Not found"; return false; }

            var wantNames = names.Select(Norm).Where(x => x.Length > 0).ToList();
            wantNames.Add(Norm(Join(brand, d.Model)));
            Candidate closest = null;
            try
            {
                foreach (var q in names)
                {
                    log?.Invoke("image: Wikipedia '" + q + "'");
                    foreach (var c in await WikipediaSearch(q))
                    {
                        if (wantNames.Contains(Norm(c.Name))) return await Save(d, c, "Exact name match (title = " + c.Name + ")", log);
                        if (closest == null && Related(q, c.Name)) closest = c;
                    }
                }
                foreach (var q in codes)
                {
                    log?.Invoke("image: Wikipedia model code '" + q + "'");
                    var hits = await WikipediaSearch(q);
                    if (q.Length >= 5 && q.Any(char.IsDigit) && hits.Count > 0)
                        return await Save(d, hits[0], "Model-code match, verify (article mentions " + q + "): " + hits[0].Name, log);
                }
                foreach (var q in names.Concat(codes))
                {
                    log?.Invoke("image: Wikimedia Commons '" + q + "'");
                    var c = await CommonsSearch(q, wantNames);
                    if (c != null) return await Save(d, c, "Commons file name matches: " + c.Name, log);
                }
            }
            catch (Exception ex) { log?.Invoke("image: lookup failed (" + ex.Message + ")"); }

            if (closest != null) return await Save(d, closest, "Closest match, verify: " + closest.Name, log);
            log?.Invoke("image: nothing found - use 'Set image...' to supply one");
            d.ImageMatch = "Not found";
            return false;
        }

        // A "closest" candidate must share nearly all words with the query, so generic pages never get attached.
        private static bool Related(string query, string title)
        {
            var q = Regex.Split(query.ToLowerInvariant(), @"[^a-z0-9]+").Where(x => x.Length > 1).ToList();
            var t = Regex.Split(title.ToLowerInvariant(), @"[^a-z0-9]+").Where(x => x.Length > 1).ToList();
            return q.Count > 0 && q.Count(x => t.Contains(x)) >= Math.Max(1, q.Count - 1);
        }

        private static async Task<List<Candidate>> WikipediaSearch(string q)
        {
            var res = new List<Candidate>();
            string url = "https://en.wikipedia.org/w/api.php?action=query&format=json&generator=search&gsrlimit=5&gsrsearch=" +
                         Uri.EscapeDataString("\"" + q + "\"") + "&prop=pageimages&piprop=original";
            var j = JObject.Parse(await Http.GetStringAsync(url));
            var pages = j["query"]?["pages"] as JObject;
            if (pages == null) return res;
            foreach (var p in pages.Properties().Select(x => x.Value).OrderBy(x => (int?)x["index"] ?? 99))
            {
                string src = p["original"]?["source"]?.ToString(), title = p["title"]?.ToString();
                if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(title)) continue;
                if (src.IndexOf(".svg", StringComparison.OrdinalIgnoreCase) >= 0 || title.StartsWith("List of", StringComparison.OrdinalIgnoreCase)) continue;
                res.Add(new Candidate { Name = title, ImageUrl = src, Source = "Wikipedia",
                    Page = "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')) });
            }
            return res;
        }

        private static async Task<Candidate> CommonsSearch(string q, List<string> want)
        {
            string url = "https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit=8&gsrsearch=" +
                         Uri.EscapeDataString(q) + "&prop=imageinfo&iiprop=url|mime";
            var j = JObject.Parse(await Http.GetStringAsync(url));
            var pages = j["query"]?["pages"] as JObject;
            if (pages == null) return null;
            foreach (var p in pages.Properties().Select(x => x.Value).OrderBy(x => (int?)x["index"] ?? 99))
            {
                string title = p["title"]?.ToString() ?? "", src = p["imageinfo"]?[0]?["url"]?.ToString(), mime = p["imageinfo"]?[0]?["mime"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(src) || !mime.StartsWith("image/") || mime.Contains("svg")) continue;
                string n = Norm(title);
                if (want.Any(w => w.Length > 4 && n.Contains(w)) || (Norm(q).Length > 4 && n.Contains(Norm(q))))
                    return new Candidate { Name = title, ImageUrl = src, Source = "Wikimedia Commons",
                        Page = "https://commons.wikimedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')) };
            }
            return null;
        }

        private static async Task<bool> Save(DeviceProfile d, Candidate c, string how, Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var bytes = await Http.GetByteArrayAsync(c.ImageUrl);
                string ext = Path.GetExtension(new Uri(c.ImageUrl).AbsolutePath).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".gif") ext = ".jpg";
                string file = Path.Combine(Dir, Regex.Replace(d.Key, @"[^A-Za-z0-9_\-]", "_") + ext);
                File.WriteAllBytes(file, bytes);
                d.ImagePath = file;
                d.ImageUrl = c.ImageUrl;
                d.ImageSource = c.Source + ": " + c.Name + " - " + c.Page;
                d.ImageMatch = how;
                log?.Invoke("image: saved (" + how + ")");
                return true;
            }
            catch (Exception ex) { log?.Invoke("image: download failed (" + ex.Message + ")"); return false; }
        }
    }

    /// <summary>Writes the device database as an .xlsx workbook: an Index sheet plus one worksheet per phone.</summary>
    internal static class WorkbookExporter
    {
        public static string Export(IList<DeviceProfile> devices, string path)
        {
            using (var wb = new XLWorkbook())
            {
                var index = wb.Worksheets.Add("Index");
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Index" };
                var sheets = new Dictionary<string, string>();
                int n = 0;
                foreach (var d in devices)
                {
                    n++;
                    string name = SheetName(d, used);
                    sheets[d.Key] = name;
                    BuildDeviceSheet(wb.Worksheets.Add(name), d, n);
                }
                BuildIndex(index, devices, sheets);
                wb.Properties.Title = "TestPoint Trigger device workbook";
                wb.Properties.Author = AppInfo.DeveloperName;
                wb.Properties.Comments = "Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " by TestPoint Trigger";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                wb.SaveAs(path);
            }
            return path;
        }

        private static string SheetName(DeviceProfile d, HashSet<string> used)
        {
            string tail = (d.Serial ?? "").Length > 6 ? d.Serial.Substring(d.Serial.Length - 6) : d.Serial ?? "";
            string baseName = Regex.Replace(string.Join(" ", new[] { d.Brand, string.IsNullOrWhiteSpace(d.MarketName) ? d.Model : d.MarketName, tail }.Where(s => !string.IsNullOrWhiteSpace(s))),
                                            @"[\[\]\:\*\?\/\\]", "");
            if (baseName.Length == 0) baseName = "Device";
            if (baseName.Length > 31) baseName = baseName.Substring(0, 31);
            string nm = baseName; int i = 2;
            while (!used.Add(nm)) { string suf = " " + i++; nm = baseName.Substring(0, Math.Min(baseName.Length, 31 - suf.Length)) + suf; }
            return nm;
        }

        private static void BuildIndex(IXLWorksheet ws, IList<DeviceProfile> devices, Dictionary<string, string> sheets)
        {
            ws.Cell(1, 1).Value = "TestPoint Trigger - device workbook";
            ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 16;
            ws.Cell(2, 1).Value = "Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · " + AppInfo.DeveloperName + " · v" + AppInfo.VersionText;
            var hdr = new[] { "Device", "Brand", "Model", "Serial", "Chip family", "Applicable modes", "Android", "Image match", "Last seen", "Sheet" };
            for (int c = 0; c < hdr.Length; c++) ws.Cell(4, c + 1).Value = hdr[c];
            int r = 4;
            foreach (var d in devices)
            {
                r++;
                var v = new[] { d.DisplayName, d.Brand, d.Model, d.Serial, d.ChipVendor, string.Join(", ", d.Modes ?? new List<string>()),
                                d.AndroidVersion, d.ImageMatch, d.LastSeen.ToString("yyyy-MM-dd HH:mm") };
                for (int c = 0; c < v.Length; c++) ws.Cell(r, c + 1).Value = v[c] ?? "";
                var link = ws.Cell(r, 10);
                link.Value = sheets[d.Key];
                link.SetHyperlink(new XLHyperlink("'" + sheets[d.Key] + "'!A1"));
            }
            if (r == 4) { r = 5; ws.Cell(5, 1).Value = "(no devices yet)"; }
            var t = ws.Range(4, 1, r, hdr.Length).CreateTable("tbl_index");
            t.Theme = XLTableTheme.TableStyleMedium2;
            double[] w = { 30, 14, 18, 20, 18, 34, 10, 30, 18, 30 };
            for (int c = 0; c < w.Length; c++) ws.Column(c + 1).Width = w[c];
        }

        private static void BuildDeviceSheet(IXLWorksheet ws, DeviceProfile d, int idx)
        {
            ws.Cell(1, 1).Value = d.DisplayName.Length > 0 ? d.DisplayName : "Unknown device";
            ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 16;
            ws.Cell(2, 1).Value = "Serial " + d.Serial + " · last probed " + d.LastProbe.ToString("yyyy-MM-dd HH:mm") + " · " + AppInfo.DeveloperName;
            ws.Column(1).Width = 30; ws.Column(2).Width = 52; ws.Column(3).Width = 24; ws.Column(4).Width = 22; ws.Column(5).Width = 30; ws.Column(6).Width = 36;

            int row = 4;
            // 1. Identity
            var id = new List<string[]>();
            void Add(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) id.Add(new[] { k, v }); }
            Add("Serial", d.Serial); Add("IMEI status", d.ImeiStatus); Add("IMEI tag (SHA-256, not the IMEI)", d.ImeiHash);
            Add("Brand", d.Brand); Add("Manufacturer", d.Manufacturer); Add("Model", d.Model); Add("Marketing name", d.MarketName);
            Add("Device code", d.DeviceCode); Add("Product", d.Product); Add("Chip family", d.ChipVendor); Add("Board platform", d.Platform);
            Add("Hardware", d.Hardware); Add("SoC vendor", d.SocVendor); Add("SoC model", d.SocModel);
            Add("Android version", d.AndroidVersion); Add("SDK level", d.Sdk); Add("Build", d.BuildId); Add("Build fingerprint", d.BuildFingerprint);
            Add("Security patch", d.SecurityPatch); Add("Bootloader", d.Bootloader); Add("Baseband", d.Baseband); Add("CPU ABI", d.Abi);
            Add("Kernel", d.Kernel); Add("Verified boot state", d.BootState); Add("Bootloader lock", d.FlashLocked); Add("Treble", d.Treble);
            Add("Slot suffix", d.SlotSuffix); Add("Display", d.Display); Add("Density", d.Density); Add("RAM", d.Ram); Add("Storage", d.Storage);
            Add("Battery (live)", d.Battery); Add("USB VID", d.UsbVid); Add("USB PID", d.UsbPid); Add("USB name", d.UsbName);
            Add("Probed via", d.Source); Add("First seen", d.FirstSeen.ToString("yyyy-MM-dd HH:mm")); Add("Last seen", d.LastSeen.ToString("yyyy-MM-dd HH:mm"));
            Add("Probe count", d.ProbeCount.ToString());
            Add("Image match", d.ImageMatch); Add("Image source", d.ImageSource); Add("Image URL", d.ImageUrl); Add("Notes", d.Notes);
            row = Table(ws, row, "Device identity", "tbl_" + idx + "_id", new[] { "Property", "Value" }, id);

            // 2. Modes
            var modes = new List<string[]>();
            foreach (var mn in d.Modes ?? new List<string>())
            {
                var m = JigModes.Get(mn); if (m == null) continue;
                modes.Add(new[] { m.Name, "serial: " + m.Name, m.Keys, m.Usb, string.IsNullOrEmpty(m.SoftwareCmd) ? "-" : m.SoftwareCmd, m.Note });
            }
            row = Table(ws, row, "Applicable boot modes (jig)", "tbl_" + idx + "_modes",
                        new[] { "Mode", "Jig command", "Keys / pads", "USB path", "Software command", "Note" }, modes);

            // 3. Procedure
            var proc = new List<string[]>(); int ln = 0;
            foreach (var line in (d.Procedure ?? "").Split('\n')) if (line.Trim().Length > 0) proc.Add(new[] { (++ln).ToString(), line.TrimEnd() });
            row = Table(ws, row, "Procedure", "tbl_" + idx + "_proc", new[] { "#", "Step" }, proc);

            // 4. Fastboot
            if (d.FastbootVars != null && d.FastbootVars.Count > 0)
                row = Table(ws, row, "Fastboot variables", "tbl_" + idx + "_fb", new[] { "Variable", "Value" },
                            d.FastbootVars.Select(kv => new[] { kv.Key, kv.Value }).ToList());

            // 5. All props
            if (d.Props != null && d.Props.Count > 0)
                row = Table(ws, row, "All Android properties (getprop)", "tbl_" + idx + "_props", new[] { "Property", "Value" },
                            d.Props.Select(kv => new[] { kv.Key, kv.Value }).ToList());

            ws.Range(1, 1, row, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            ws.Range(1, 2, row, 6).Style.Alignment.WrapText = true;

            // Model image to the right of the tables.
            try
            {
                if (!string.IsNullOrEmpty(d.ImagePath) && File.Exists(d.ImagePath))
                {
                    int w, h;
                    using (var img = Image.FromFile(d.ImagePath)) { w = img.Width; h = img.Height; }
                    double s = Math.Min(280.0 / Math.Max(w, 1), 360.0 / Math.Max(h, 1));
                    ws.AddPicture(d.ImagePath).MoveTo(ws.Cell(4, 8)).WithSize((int)(w * s), (int)(h * s));
                }
            }
            catch { }
        }

        private static int Table(IXLWorksheet ws, int row, string title, string name, string[] header, List<string[]> rows)
        {
            ws.Cell(row, 1).Value = title;
            ws.Cell(row, 1).Style.Font.Bold = true; ws.Cell(row, 1).Style.Font.FontSize = 12;
            row++;
            for (int c = 0; c < header.Length; c++) ws.Cell(row, c + 1).Value = header[c];
            int first = row;
            if (rows.Count == 0) { row++; ws.Cell(row, 1).Value = "(none)"; }
            foreach (var r in rows)
            {
                row++;
                for (int c = 0; c < header.Length && c < r.Length; c++) ws.Cell(row, c + 1).Value = r[c] ?? "";
            }
            var t = ws.Range(first, 1, row, header.Length).CreateTable(name);
            t.Theme = XLTableTheme.TableStyleMedium2;
            return row + 3;
        }
    }
}
