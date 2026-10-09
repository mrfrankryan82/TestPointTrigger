// Mobile Surgery - Jig wiring assistant: port scan, serial link, guided checks, wiring diagram
// Developer: HaKDMoDz™ · v1.1.0 · 2026-10-09
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    // ───────────────────────────── Arduino / COM detection ─────────────────────────────

    internal sealed class SerialPortInfo
    {
        public string Port, Name, Vid, Pid, Kind;
        public bool LooksArduino;
        public override string ToString() => Port + "  " + Kind + (string.IsNullOrEmpty(Vid) ? "" : "  [" + Vid + ":" + Pid + "]");
    }

    internal static class PortScanner
    {
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "2341:0042", "Arduino Mega 2560 R3" }, { "2341:0010", "Arduino Mega 2560" }, { "2341:0242", "Arduino Mega 2560 (R3 alt)" },
            { "2A03:0042", "Arduino Mega 2560 (arduino.org)" }, { "2341:0043", "Arduino Uno R3" }, { "2341:0001", "Arduino Uno" },
            { "2341:0243", "Arduino Uno R3 (alt)" }, { "2A03:0043", "Arduino Uno R3 (arduino.org)" }, { "2341:1002", "Arduino Uno R4" },
            { "1A86:7523", "CH340 USB-serial (Mega/Uno clone?)" }, { "1A86:7522", "CH340 USB-serial" },
            { "10C4:EA60", "CP210x USB-serial" }, { "0403:6001", "FTDI FT232" }, { "0403:6010", "FTDI FT2232" },
        };

        public static List<SerialPortInfo> Scan()
        {
            var list = new List<SerialPortInfo>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string name = (o["Name"] as string) ?? "", id = (o["DeviceID"] as string) ?? "";
                        var c = Regex.Match(name, @"\((COM\d+)\)");
                        if (!c.Success) continue;
                        var v = Regex.Match(id, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                        var info = new SerialPortInfo { Port = c.Groups[1].Value, Name = name, Kind = "Serial port" };
                        if (v.Success)
                        {
                            info.Vid = v.Groups[1].Value.ToUpperInvariant(); info.Pid = v.Groups[2].Value.ToUpperInvariant();
                            info.Kind = Known.TryGetValue(info.Vid + ":" + info.Pid, out var k) ? k : "USB serial";
                            info.LooksArduino = Known.ContainsKey(info.Vid + ":" + info.Pid) || name.IndexOf("arduino", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        list.Add(info);
                    }
            }
            catch { }
            foreach (var p in SerialPort.GetPortNames())
                if (!list.Any(x => x.Port.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new SerialPortInfo { Port = p, Name = p, Kind = "Serial port" });
            return list.OrderByDescending(x => x.LooksArduino).ThenBy(x => x.Port.Length).ThenBy(x => x.Port).ToList();
        }

        public static HashSet<string> UsbSnapshot()
        {
            var h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID_%'"))
                    foreach (ManagementObject o in s.Get()) h.Add((o["DeviceID"] as string) ?? "");
            }
            catch { }
            return h;
        }
    }

    // ───────────────────────────── Board profiles (Uno / Mega) ─────────────────────────────

    internal enum JigBoard { Uno, Mega }

    /// <summary>
    /// Pin names for one jig build. The firmware (PhoneBootController 2.2.0) is the
    /// same on both boards apart from these pins, so the wiring guide and the diagram
    /// are generated from this table.
    /// </summary>
    internal sealed class BoardPins
    {
        public JigBoard Board;
        public string Name, Sketch, Tp, Up, Dn, Pwr, Vcc, Btemp, VbusPc, Oe, UTx, URx, UartKind, UartNote;

        public static readonly BoardPins Uno = new BoardPins
        {
            Board = JigBoard.Uno, Name = "Arduino Uno", Sketch = "PhoneBootController_Uno.ino",
            Tp = "D2", Up = "D3", Dn = "D4", Pwr = "D5", Vcc = "D6", Btemp = "D7", VbusPc = "D8", Oe = "D9",
            UTx = "D11", URx = "D12", UartKind = "SoftwareSerial",
            UartNote = "115200 transmit is fine; receive at 115200 is best effort on a Uno (57600 and below is solid)."
        };

        public static readonly BoardPins Mega = new BoardPins
        {
            Board = JigBoard.Mega, Name = "Arduino Mega 2560", Sketch = "PhoneBootController_Mega.ino",
            Tp = "D22", Up = "D23", Dn = "D24", Pwr = "D25", Vcc = "D26", Btemp = "D27", VbusPc = "D28", Oe = "D30",
            UTx = "D18 (TX1)", URx = "D19 (RX1)", UartKind = "Serial1",
            UartNote = "Use 115200 baud (921600 is too far off on a 16 MHz board)."
        };

        public static BoardPins For(JigBoard b) => b == JigBoard.Uno ? Uno : Mega;

        /// <summary>Board named in an IDENT reply ("IDENT,PBJ,2.2.0,UNO,..."), or null if unknown.</summary>
        public static JigBoard? FromIdent(string ident)
        {
            if (string.IsNullOrEmpty(ident)) return null;
            var f = ident.Split(',');
            string b = f.Length > 3 ? f[3].ToUpperInvariant() : "";
            if (b.StartsWith("UNO")) return JigBoard.Uno;
            if (b.StartsWith("MEGA")) return JigBoard.Mega;
            return null;
        }
    }

    // ───────────────────────────── Serial link to the jig ─────────────────────────────

    internal sealed class JigLink : IDisposable
    {
        private SerialPort _p;
        private readonly StringBuilder _buf = new StringBuilder();
        private readonly object _gate = new object();

        public event Action<string> Line;          // every complete line, on a worker thread
        public string PortName => _p?.PortName;
        public bool IsOpen => _p != null && _p.IsOpen;
        public string Ident;                       // "IDENT,PBJ,2.2.0,UNO,..." once verified
        public bool ShieldOk;
        public JigBoard? Board => BoardPins.FromIdent(Ident);

        public bool Open(string port, int baud, out string err)
        {
            Close();
            try
            {
                // Opening an Uno or Mega resets it (DTR). The caller waits for the boot banner.
                _p = new SerialPort(port, baud, Parity.None, 8, StopBits.One)
                { NewLine = "\n", ReadTimeout = 500, WriteTimeout = 1000, DtrEnable = true, RtsEnable = false };
                _p.DataReceived += OnData;
                _p.Open();
                err = null; return true;
            }
            catch (Exception ex) { err = ex.Message; try { _p?.Dispose(); } catch { } _p = null; return false; }
        }

        public void Close()
        {
            try { if (_p != null) { _p.DataReceived -= OnData; if (_p.IsOpen) _p.Close(); _p.Dispose(); } } catch { }
            _p = null; Ident = null; ShieldOk = false;
        }

        private void OnData(object s, SerialDataReceivedEventArgs e)
        {
            string chunk;
            try { chunk = _p.ReadExisting(); } catch { return; }
            var lines = new List<string>();
            lock (_gate)
            {
                _buf.Append(chunk);
                string all = _buf.ToString();
                int i;
                while ((i = all.IndexOf('\n')) >= 0) { lines.Add(all.Substring(0, i).TrimEnd('\r')); all = all.Substring(i + 1); }
                _buf.Clear(); _buf.Append(all);
            }
            foreach (var l in lines) if (l.Length > 0) try { Line?.Invoke(l); } catch { }
        }

        public bool Send(string cmd)
        {
            try { if (!IsOpen) return false; _p.Write(cmd + "\n"); return true; } catch { return false; }
        }

        /// <summary>Send a command and collect lines until isEnd says stop (or timeout).</summary>
        public List<string> Ask(string cmd, Func<string, bool> isEnd, int timeoutMs)
        {
            var got = new List<string>();
            using (var done = new ManualResetEventSlim(false))
            {
                Action<string> h = l => { lock (got) got.Add(l); if (isEnd(l)) done.Set(); };
                Line += h;
                try { if (Send(cmd)) done.Wait(timeoutMs); }
                finally { Line -= h; }
            }
            lock (got) return new List<string>(got);
        }

        public string AskFirst(string cmd, string prefix, int timeoutMs) =>
            Ask(cmd, l => l.StartsWith(prefix, StringComparison.Ordinal), timeoutMs)
                .FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>Ask the sketch to identify itself. True only for our firmware.</summary>
        public bool Handshake()
        {
            string id = AskFirst("ident", "IDENT,PBJ", 2500) ?? AskFirst("ident", "IDENT,PBJ", 2500);
            Ident = id;
            ShieldOk = id != null && id.Contains("shield=1");
            return id != null;
        }

        public void Dispose() => Close();
    }

    // ───────────────────────────── Guided wiring steps ─────────────────────────────

    internal enum StepState { Pending, Pass, Warn, Fail }

    internal sealed class CheckCtx
    {
        public JigLink Link;
        public Action<string> Log;
        public BoardPins Pins = BoardPins.Mega;
        public Dictionary<string, string> SelfTest = new Dictionary<string, string>();
        public DateTime SelfTestAt = DateTime.MinValue;
    }

    internal sealed class WiringStep
    {
        public string Id, Group, Title, Place, Parts, Pins, Nodes;
        public bool Manual;                    // operator confirms (no electrical test possible)
        public string[] Drive;                 // serial commands the "Drive" button sends for manual steps
        public Func<CheckCtx, KeyValuePair<StepState, string>> Check;
        public StepState State = StepState.Pending;
        public string Detail = "";
        public bool HasCheck => Check != null;
    }

    internal static class WiringGuide
    {
        private static KeyValuePair<StepState, string> R(StepState s, string d) => new KeyValuePair<StepState, string>(s, d);

        public static List<WiringStep> Build(BoardPins p)
        {
            string pads = p.Tp + "-" + p.Pwr;
            return new List<WiringStep>
            {
                new WiringStep { Id = "mega", Group = "1 Controller", Title = p.Name + " connected and running the jig firmware", Nodes = "mega",
                    Place = "Plug the " + p.Name + " into the PC with its USB cable and nothing else. Upload " + p.Sketch + " v2.2.0 (repo folder firmware/). Pick its COM port above (or press Auto-detect).",
                    Parts = p.Name + ", USB cable that carries data", Pins = "USB (D0/D1 belong to the USB-serial chip; keep them free)",
                    Check = c => { if (!c.Link.IsOpen) return R(StepState.Fail, "No serial link. Connect first.");
                        if (!c.Link.Handshake()) return R(StepState.Fail, "Port opened but no IDENT reply. Wrong port, wrong baud (115200) or the jig sketch is not uploaded.");
                        var b = c.Link.Board;
                        if (b != null && b != p.Board) return R(StepState.Warn, c.Link.Ident + "  -> this firmware is the " + BoardPins.For(b.Value).Name + " build, but the guide is set to " + p.Name + ". Change Board above.");
                        return R(StepState.Pass, c.Link.Ident); } },

                new WiringStep { Id = "gnd", Group = "1 Controller", Title = "Common ground (star point)", Nodes = "gnd,mega", Manual = true,
                    Place = "Join ONE ground point: " + p.Name + " GND, bench supply negative, phone battery-negative pad / board ground, level-shifter GND. Use short, thick leads. Every later check depends on this.",
                    Parts = "Hookup wire, battery-negative contact", Pins = "GND (any), supply -, phone GND" },

                new WiringStep { Id = "pads", Group = "2 Pads", Title = "Pad leads " + pads + " (TP, Vol+, Vol-, Power) are open-drain", Nodes = "pads",
                    Place = "Run one lead from each of " + p.Tp + " (test point), " + p.Up + " (Vol+), " + p.Dn + " (Vol-), " + p.Pwr + " (Power) to its pad through a 100-470 ohm series resistor. The board only pulls these LOW or lets go - never connect them to a supply. Pads are 1.8 V: never drive them high.",
                    Parts = "4 x 100-470 ohm resistor, fine-pitch clips or pogo pins", Pins = p.Tp + " TP, " + p.Up + " VOL+, " + p.Dn + " VOL-, " + p.Pwr + " PWR",
                    Check = PadsCheck },

                new WiringStep { Id = "vcc", Group = "3 Power", Title = "VCC switch (bench supply to phone battery+)", Nodes = "vcc,sense",
                    Place = "Supply + -> P-FET source. P-FET drain -> Schottky anode; Schottky cathode -> phone BATT+. 2N7002 drain -> P-FET gate (10k gate-source pull-up); 2N7002 source -> GND; 2N7002 gate <- " + p.Vcc + " with 100k pull-down. Sense: phone-side rail -> 10k/10k divider -> A0.",
                    Parts = "P-FET rated for the phone's current, 2N7002, Schottky, 100k, 10k x3", Pins = p.Vcc + " VCC_EN, A0 sense",
                    Check = c => SelfTestPart(c, "vcc") },

                new WiringStep { Id = "vpc", Group = "3 Power", Title = "VBUS from PC (to phone VBUS)", Nodes = "vpc,sense",
                    Place = "PC 5 V -> P-FET -> Schottky -> phone VBUS, driven by a 2N7002 from " + p.VbusPc + " (same gate network as VCC). Sense on A1 through a 10k/10k divider on the phone side.",
                    Parts = "P-FET, 2N7002, Schottky, 100k, 10k x3", Pins = p.VbusPc + " VBUS_PC, A1 sense",
                    Check = c => SelfTestPart(c, "vbus_pc") },

                new WiringStep { Id = "btemp", Group = "3 Power", Title = "Fake battery-temperature resistor (BTEMP)", Nodes = "btemp", Manual = true,
                    Drive = new[] { "btemp on" },
                    Place = "Phone BTEMP/NTC pad -> resistor (usually 10k; match your model) -> 2N7002 drain; source -> GND; gate <- " + p.Btemp + ". With 'btemp on' a meter between the pad and GND reads the resistor value; with 'btemp off' it is open.",
                    Parts = "Resistor (model dependent), 2N7002", Pins = p.Btemp + " BTEMP" },

                new WiringStep { Id = "mux", Group = "4 USB path", Title = "TS3USB221 mux: phone USB connected / disconnected", Nodes = "mux", Check = MuxCheck,
                    Place = "TS3USB221: D+/D- common -> phone USB; D1 -> PC USB data; S tied to GND (PC side); OE# <- " + p.Oe + " through a 1k/2k divider (5 V -> 3.3 V). Keep D+/D- short and matched. This test needs the phone attached with BTEMP and VCC wired.",
                    Parts = "TS3USB221 (or FSUSB42), 1k + 2k resistors, decoupling cap", Pins = p.Oe + " OE#, S to GND" },

                new WiringStep { Id = "uartloop", Group = "5 UART", Title = "UART loopback (board side)", Nodes = "uart", Check = UartLoopCheck,
                    Place = "Unplug the level shifter. Jumper " + p.UTx + " to " + p.URx + " on the board. The test sends a pattern and expects it back. Remove the jumper afterwards.",
                    Parts = "1 jumper wire", Pins = p.UTx + " TX, " + p.URx + " RX (" + p.UartKind + ")" },

                new WiringStep { Id = "uartphone", Group = "5 UART", Title = "Phone UART through the level shifter", Nodes = "uart", Check = UartPhoneCheck,
                    Place = "Board TX " + p.UTx + " -> shifter 5 V side -> 1.8 V side -> phone RX pad; phone TX pad -> shifter -> board RX " + p.URx + ". Reference the shifter's low side to the phone's 1.8 V rail. Share GND. " + p.UartNote,
                    Parts = "Bi-directional level shifter (TXS0108E or BSS138-based), 3 leads", Pins = p.UTx + " TX, " + p.URx + " RX" },
            };
        }

        private static string Pad(string pins, string key)
        {
            var m = Regex.Match(pins, key + @"=(held|rel)/pad([01])");
            return m.Success ? m.Groups[1].Value + "/" + m.Groups[2].Value : "?";
        }

        private static KeyValuePair<StepState, string> PadsCheck(CheckCtx c)
        {
            string p0 = c.Link.AskFirst("pins", "PINS", 2000);
            if (p0 == null) return R(StepState.Fail, "No PINS reply.");
            var sb = new StringBuilder("Idle: TP " + Pad(p0, "TP") + ", UP " + Pad(p0, "UP") + ", DN " + Pad(p0, "DN") + ", PWR " + Pad(p0, "PWR") + ". ");
            bool bad = false;
            foreach (var k in new[] { "tp", "up", "dn", "pwr" })
            {
                c.Link.Send("key " + k + " hold"); Thread.Sleep(120);
                string held = c.Link.AskFirst("pins", "PINS", 2000) ?? "";
                c.Link.Send("key " + k + " rel"); Thread.Sleep(80);
                string tag = Pad(held, k.ToUpperInvariant());
                if (tag != "held/0") { bad = true; sb.Append(k.ToUpperInvariant() + " held reads " + tag + " (expected held/0: pulled up by an external supply or a short?). "); }
            }
            c.Link.Send("off");
            return bad ? R(StepState.Fail, sb.ToString().Trim())
                       : R(StepState.Pass, sb + "Each line pulls its pad to 0 and releases cleanly. (This proves the board pin works; the clip being on the right pad is confirmed when the phone enters the mode.)");
        }

        private static void EnsureSelfTest(CheckCtx c)
        {
            if ((DateTime.Now - c.SelfTestAt).TotalSeconds < 20 && c.SelfTest.Count > 0) return;
            c.Log?.Invoke("selftest: switching each supply on in turn (phone unplugged or dummy load)...");
            var lines = c.Link.Ask("selftest", l => l == "ST,done", 9000);
            c.SelfTest.Clear();
            foreach (var l in lines) { var p = l.Split(','); if (p.Length >= 5 && p[0] == "ST") c.SelfTest[p[1]] = l; }
            c.SelfTestAt = DateTime.Now;
        }

        private static KeyValuePair<StepState, string> SelfTestPart(CheckCtx c, string key)
        {
            if (!c.Link.IsOpen) return R(StepState.Fail, "No serial link.");
            EnsureSelfTest(c);
            if (!c.SelfTest.TryGetValue(key, out var line)) return R(StepState.Fail, "No selftest reply. Is the sketch v2.2.0?");
            if (line.EndsWith("PASS")) return R(StepState.Pass, line);
            if (line.EndsWith("FAIL_LEAK")) return R(StepState.Fail, line + "  -> rail present while OFF: gate pull-down missing, FET shorted, or sense divider floating.");
            return R(StepState.Fail, line + "  -> no voltage when ON: check supply, FET orientation (P-FET source to supply), 2N7002 gate wiring, or the A0/A1 sense divider.");
        }

        private static KeyValuePair<StepState, string> MuxCheck(CheckCtx c)
        {
            if (!c.Link.IsOpen) return R(StepState.Fail, "No serial link.");
            c.Link.Send("off"); Thread.Sleep(900);
            var s0 = PortScanner.UsbSnapshot();
            c.Link.Send("btemp on"); c.Link.Send("vcc on"); Thread.Sleep(150);
            c.Link.Send("usb pc"); Thread.Sleep(6000);
            var s1 = PortScanner.UsbSnapshot();
            var added = s1.Except(s0).ToList();
            c.Link.Send("usb off"); Thread.Sleep(4000);
            var s2 = PortScanner.UsbSnapshot();
            var gone = added.Where(a => !s2.Contains(a)).ToList();
            c.Link.Send("off");
            if (added.Count == 0)
                return R(StepState.Warn, "Nothing new appeared on the PC with 'usb pc'. Either the phone is not powering/booting (try a mode such as 'fastboot' first), or the PC path is open: check D1, OE# (" + c.Pins.Oe + "), S to GND and VBUS_PC.");
            string head = "PC path: +" + added.Count + " USB device(s). ";
            if (gone.Count == added.Count)
                return R(StepState.Pass, head + "'usb off' removed them again, so OE# isolates D+/D- cleanly.");
            return R(StepState.Fail, head + "After 'usb off' the PC still sees " + (added.Count - gone.Count) + " of them: the mux is not isolating D+/D- (check the OE# divider on " + c.Pins.Oe + ", S to GND, solder bridges).");
        }

        private static KeyValuePair<StepState, string> UartLoopCheck(CheckCtx c)
        {
            string r = c.Link.AskFirst("uart loop", "UARTLOOP", 3000);
            if (r == null) return R(StepState.Fail, "No reply (sketch older than 2.1.0?).");
            if (r.Contains("PASS")) return R(StepState.Pass, "Loopback intact: " + c.Pins.UTx + " and " + c.Pins.URx + " work. " + r);
            if (r.Contains("GARBLED")) return R(StepState.Fail, "Loopback came back wrong. " + r + "  -> loose jumper, noise, or the level shifter still attached.");
            return R(StepState.Fail, r + "  -> nothing came back: jumper " + c.Pins.UTx + " -> " + c.Pins.URx + " missing, or the level shifter is still attached and loading the line.");
        }

        private static KeyValuePair<StepState, string> UartPhoneCheck(CheckCtx c)
        {
            c.Link.Send("off"); Thread.Sleep(600);
            c.Link.Send("btemp on"); c.Link.Send("uart on"); Thread.Sleep(100);
            c.Link.Send("vcc on");
            string r = c.Link.AskFirst("uart listen 5000", "UARTLISTEN", 8000);
            c.Link.Send("off");
            if (r == null) return R(StepState.Fail, "No reply from 'uart listen'.");
            var b = Regex.Match(r, @"bytes=(\d+)"); var j = Regex.Match(r, @"junk=(\d+)");
            int bytes = b.Success ? int.Parse(b.Groups[1].Value) : 0, junk = j.Success ? int.Parse(j.Groups[1].Value) : 0;
            if (bytes == 0) return R(StepState.Fail, r + "  -> silent: TX/RX swapped, no GND, shifter not referenced to the phone's 1.8 V rail, or wrong pads. Some phones print nothing unless the boot ROM/bootloader runs.");
            if (junk * 100 / bytes > 30) return R(StepState.Warn, r + "  -> data arrives but looks like garbage: wrong baud (try 'uart baud 115200'). " + c.Pins.UartNote);
            return R(StepState.Pass, r + "  -> readable phone boot log received.");
        }
    }

    // ───────────────────────────── Interactive wiring diagram ─────────────────────────────

    /// <summary>
    /// Board pin header (Uno or Mega) on the left, the parts it drives on the right. Link and
    /// part colours follow the state of the wiring steps that mention them, so
    /// the bench build can be read at a glance. Click a part to jump to its step.
    /// </summary>
    public sealed class WiringPanel : Panel
    {
        private sealed class PinRow { public string Label, Node, Note; }
        private sealed class NodeBox { public string Id, Title, Sub; public int R0, R1; public Rectangle Rect; }

        private PinRow[] Rows = new PinRow[0];
        private NodeBox[] _nodes = new NodeBox[0];
        private string _boardName = "";

        private static PinRow[] RowsFor(BoardPins p) => new[]
        {
            new PinRow { Label = p.Tp + "  TP",          Node = "pads",  Note = "open-drain" },
            new PinRow { Label = p.Up + "  VOL+",        Node = "pads",  Note = "open-drain" },
            new PinRow { Label = p.Dn + "  VOL-",        Node = "pads",  Note = "open-drain" },
            new PinRow { Label = p.Pwr + "  PWR",        Node = "pads",  Note = "open-drain" },
            new PinRow { Label = p.Vcc + "  VCC_EN",     Node = "vcc",   Note = "gate drive" },
            new PinRow { Label = p.Btemp + "  BTEMP",    Node = "btemp", Note = "gate drive" },
            new PinRow { Label = p.VbusPc + "  VBUS_PC", Node = "vpc",   Note = "gate drive" },
            new PinRow { Label = p.Oe + "  USB_OE",      Node = "mux",   Note = "via 1k/2k" },
            new PinRow { Label = p.UTx + "  TX",         Node = "uart",  Note = "5V -> 1.8V" },
            new PinRow { Label = p.URx + "  RX",         Node = "uart",  Note = "1.8V -> 5V" },
            new PinRow { Label = "A0   SENSE_VCC",       Node = "sense", Note = "10k/10k" },
            new PinRow { Label = "A1   SENSE_VBUS",      Node = "sense", Note = "10k/10k" },
            new PinRow { Label = "GND",                  Node = "gnd",   Note = "star point" },
        };

        private static readonly (string Id, string Title, string Sub)[] Parts =
        {
            ("pads",  "Phone pads",           "TP / Vol+ / Vol- / Power (1.8 V)"),
            ("vcc",   "VCC switch",           "P-FET + 2N7002 + Schottky"),
            ("btemp", "BTEMP resistor",       "2N7002 + NTC stand-in"),
            ("vpc",   "VBUS switch (PC)",     "P-FET + 2N7002 + Schottky"),
            ("mux",   "TS3USB221 USB mux",    "D=phone  D1=PC  S to GND"),
            ("uart",  "Level shifter / UART", "phone debug TX/RX"),
            ("sense", "Sense dividers",       "VCC rail and VBUS to ADC"),
            ("gnd",   "Common ground",        "supply -, phone GND, shifter"),
        };

        /// <summary>Lay the diagram out for one board's pin map.</summary>
        internal void SetBoard(BoardPins p)
        {
            _boardName = p.Name;
            Rows = RowsFor(p);
            _nodes = Parts.Select(x =>
            {
                int r0 = Array.FindIndex(Rows, r => r.Node == x.Id), r1 = Array.FindLastIndex(Rows, r => r.Node == x.Id);
                return new NodeBox { Id = x.Id, Title = x.Title, Sub = x.Sub, R0 = r0, R1 = r1,
                                     Rect = new Rectangle(NodeX, Top0 + r0 * RowH + 3, NodeW, (r1 - r0 + 1) * RowH - 6) };
            }).ToArray();
            AutoScrollMinSize = new Size(NodeX + NodeW + 24, Top0 + RowH * Rows.Length + 24);
            Invalidate();
        }

        private IList<WiringStep> _steps = new List<WiringStep>();
        private string _selected = "";
        private const int RowH = 32, Top0 = 36, MegaX = 16, MegaW = 190, NodeX = 440, NodeW = 280;

        public event Action<string> NodeClicked;

        public WiringPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            AutoScroll = true;
            BackColor = Color.White;
            SetBoard(BoardPins.Mega);
        }

        internal void SetSteps(IList<WiringStep> steps) { _steps = steps; Invalidate(); }
        public void Highlight(string nodesCsv) { _selected = nodesCsv ?? ""; Invalidate(); }

        private StepState NodeState(string id)
        {
            var rel = _steps.Where(s => (s.Nodes ?? "").Split(',').Contains(id)).ToList();
            if (rel.Count == 0) return StepState.Pending;
            if (rel.Any(s => s.State == StepState.Fail)) return StepState.Fail;
            if (rel.Any(s => s.State == StepState.Warn)) return StepState.Warn;
            if (rel.All(s => s.State == StepState.Pass)) return StepState.Pass;
            return StepState.Pending;
        }

        private static Color Col(StepState s)
        {
            switch (s)
            {
                case StepState.Pass: return Color.FromArgb(46, 160, 67);
                case StepState.Warn: return Color.FromArgb(210, 153, 34);
                case StepState.Fail: return Color.FromArgb(218, 54, 51);
                default: return Color.FromArgb(150, 156, 165);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

            using (var bold = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var norm = new Font("Segoe UI", 8.5f))
            using (var small = new Font("Segoe UI", 7.5f))
            {
                // Controller board
                var mega = new Rectangle(MegaX, Top0 - 8, MegaW, Rows.Length * RowH + 16);
                bool megaSel = _selected.Split(',').Contains("mega");
                using (var b = new SolidBrush(Color.FromArgb(0, 104, 120)))
                    g.FillRectangle(b, mega);
                using (var p = new Pen(megaSel ? Color.DodgerBlue : Color.FromArgb(0, 70, 82), megaSel ? 3f : 1.5f))
                    g.DrawRectangle(p, mega);
                g.DrawString(_boardName, bold, Brushes.White, MegaX + 10, 8);

                for (int i = 0; i < Rows.Length; i++)
                {
                    var row = Rows[i];
                    int y = Top0 + i * RowH + RowH / 2;
                    var st = NodeState(row.Node);
                    bool sel = _selected.Split(',').Contains(row.Node);
                    using (var pen = new Pen(Col(st), sel ? 3.5f : 2f))
                        g.DrawLine(pen, MegaX + MegaW, y, NodeX, y);
                    using (var b = new SolidBrush(Col(st)))
                        g.FillEllipse(b, MegaX + MegaW - 5, y - 5, 10, 10);
                    g.DrawString(row.Label, norm, Brushes.White, MegaX + 10, y - 8);
                    var sz = g.MeasureString(row.Note, small);
                    g.DrawString(row.Note, small, Brushes.DimGray, (MegaX + MegaW + NodeX) / 2f - sz.Width / 2, y - 14);
                }

                foreach (var n in _nodes)
                {
                    var st = NodeState(n.Id);
                    bool sel = _selected.Split(',').Contains(n.Id);
                    using (var b = new SolidBrush(Color.FromArgb(sel ? 60 : 28, Col(st))))
                        g.FillRectangle(b, n.Rect);
                    using (var p = new Pen(sel ? Color.DodgerBlue : Col(st), sel ? 3f : 2f))
                        g.DrawRectangle(p, n.Rect);
                    g.DrawString(n.Title, bold, Brushes.Black, n.Rect.X + 8, n.Rect.Y + 4);
                    if (n.Rect.Height > 40) g.DrawString(n.Sub, small, Brushes.DimGray, n.Rect.X + 8, n.Rect.Y + 22);
                    else g.DrawString(n.Sub, small, Brushes.DimGray, n.Rect.X + 8, n.Rect.Y + 17);
                    string tag = st == StepState.Pass ? "OK" : st == StepState.Fail ? "FAIL" : st == StepState.Warn ? "CHECK" : "";
                    if (tag.Length > 0)
                    {
                        var w = g.MeasureString(tag, bold).Width;
                        g.DrawString(tag, bold, new SolidBrush(Col(st)), n.Rect.Right - w - 6, n.Rect.Y + 4);
                    }
                }

                g.DrawString("Green = verified   Amber = check   Red = failed   Grey = not tested   Blue outline = selected step",
                    small, Brushes.DimGray, MegaX, Top0 + Rows.Length * RowH + 8);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var pt = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
            foreach (var n in _nodes)
                if (n.Rect.Contains(pt)) { NodeClicked?.Invoke(n.Id); return; }
            if (new Rectangle(MegaX, Top0 - 8, MegaW, Rows.Length * RowH + 16).Contains(pt)) NodeClicked?.Invoke("mega");
        }
    }
}
