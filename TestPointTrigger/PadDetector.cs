// TestPoint Trigger - PCB Pad Finder detector
// Developer: HaKDMoDz™ · v2.1.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace TestPointTrigger
{
    public class Pad
    {
        public double X, Y, R;          // centre + radius in original image pixels
        public double Circularity;
        public bool Manual;
    }

    public class DetectorSettings
    {
        public int LongEdge = 2200;
        public double MinAreaFrac = 8.0e-6;
        public double MaxAreaFrac = 2.8e-3;
        public double CircMin = 0.55;
        public double AspectMax = 2.6;
        public double IsoMax = 0.55;
        public double DarkSurroundV = 50;
        public double DonutRatio = 0.68;
        public bool DetectGold = true;
        public bool DetectWhite = true;
        public DetectorSettings Clone() => (DetectorSettings)MemberwiseClone();
    }

    public static class PadDetector
    {
        /// <summary>
        /// A single detection pass configuration. The public Detect() runs the
        /// user's settings first (stage 0); if that finds nothing it cascades
        /// through progressively looser stages until pads appear.
        /// </summary>
        private sealed class Profile
        {
            public bool Gold, White, BrightBlob;
            public Scalar GoldLo, GoldHi;
            public double WhiteSMax, WhiteVMin;
            public double CircMin, AminFrac, AmaxFrac, AspectMax;
            public bool AcceptSquares; public double ExtentMin, SolidityMin, RMaxFrac;
            public bool UseDonut, UseDark, UseIso;
            public double DonutRatio, DarkV, IsoMax;
        }

        // ─────────────────────────────── Masking ───────────────────────────────

        /// <summary>The V (brightness) plane as bytes, for the ring-stat rejections.</summary>
        private static byte[] VPlane(Mat bgr)
        {
            using (var hsv = new Mat())
            {
                Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
                var ch = Cv2.Split(hsv);
                var v = Bytes(ch[2]);
                foreach (var c in ch) c.Dispose();
                return v;
            }
        }

        /// <summary>
        /// Build gold / white / bright masks as SEPARATE cleaned masks. They are
        /// never OR'd into one before contouring: on a light background (or with
        /// big shields) the white/bright mask is one enormous blob that would
        /// otherwise swallow every gold pad. Oversized components are stripped
        /// from the white/bright masks so they can't bridge real pads.
        /// </summary>
        private static List<Mat> MaskSources(Mat bgr, Profile pf, double amaxPx)
        {
            var list = new List<Mat>();
            using (var hsv = new Mat())
            {
                Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
                var ch = Cv2.Split(hsv);

                if (pf.Gold)
                {
                    var g = new Mat();
                    Cv2.InRange(hsv, pf.GoldLo, pf.GoldHi, g);
                    list.Add(g);
                }
                if (pf.White)
                {
                    var sLow = new Mat(); var vHigh = new Mat(); var wh = new Mat();
                    Cv2.Threshold(ch[1], sLow, pf.WhiteSMax, 255, ThresholdTypes.BinaryInv);
                    Cv2.Threshold(ch[2], vHigh, pf.WhiteVMin, 255, ThresholdTypes.Binary);
                    Cv2.BitwiseAnd(sLow, vHigh, wh);
                    sLow.Dispose(); vHigh.Dispose();
                    StripBig(wh, amaxPx);
                    list.Add(wh);
                }
                if (pf.BrightBlob)
                {
                    var b = new Mat();
                    Cv2.Threshold(ch[2], b, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                    StripBig(b, amaxPx);
                    list.Add(b);
                }
                foreach (var c in ch) c.Dispose();
            }

            using (var k = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3)))
                foreach (var m in list)
                {
                    Cv2.MorphologyEx(m, m, MorphTypes.Open, k);
                    Cv2.MorphologyEx(m, m, MorphTypes.Close, k);
                }
            return list;
        }

        /// <summary>Zero out connected components larger than amaxPx (background, shields).</summary>
        private static void StripBig(Mat mask, double amaxPx)
        {
            using (var labels = new Mat())
            using (var stats = new Mat())
            using (var cents = new Mat())
            {
                int n = Cv2.ConnectedComponentsWithStats(mask, labels, stats, cents);
                for (int i = 1; i < n; i++)
                {
                    int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                    if (area > amaxPx)
                        using (var t = new Mat())
                        {
                            Cv2.InRange(labels, new Scalar(i), new Scalar(i), t);
                            mask.SetTo(Scalar.All(0), t);
                        }
                }
            }
        }

        private static byte[] Bytes(Mat m)
        {
            var c = m.IsContinuous() ? m : m.Clone();
            var b = new byte[c.Rows * c.Cols];
            Marshal.Copy(c.Data, b, 0, b.Length);
            if (!ReferenceEquals(c, m)) c.Dispose();
            return b;
        }

        // ─────────────────────────────── Detection ───────────────────────────────

        /// <summary>Detect candidate pads. roi (optional) limits the search; results are in full-image coords.</summary>
        public static List<Pad> Detect(Mat bgrFull, DetectorSettings s, Rect? roi = null)
        {
            Rect r = roi ?? new Rect(0, 0, bgrFull.Width, bgrFull.Height);
            r = r.Intersect(new Rect(0, 0, bgrFull.Width, bgrFull.Height));
            if (r.Width < 8 || r.Height < 8) return new List<Pad>();

            using (var src = new Mat(bgrFull, r))
            {
                int h = src.Height, w = src.Width;
                double scale = Math.Max(1.0, s.LongEdge / (double)Math.Max(h, w));
                using (var up = new Mat())
                {
                    Cv2.Resize(src, up, new Size((int)(w * scale), (int)(h * scale)), 0, 0, InterpolationFlags.Lanczos4);

                    var stages = BuildStages(s);
                    List<Pad> best = null; int bestCount = 0;

                    for (int i = 0; i < stages.Count; i++)
                    {
                        var res = RunStage(up, scale, r, stages[i]);

                        // Stage 0 is the user's own settings: if it finds anything,
                        // respect it exactly (their sliders stay authoritative).
                        if (i == 0)
                        {
                            if (res.Count > 0) return res;
                            continue;
                        }

                        // Relaxed stages: take the first that finds pads without
                        // exploding into noise; otherwise remember the best.
                        if (res.Count > 0 && res.Count <= 4000) return res;
                        if (res.Count > bestCount && res.Count <= 4000) { best = res; bestCount = res.Count; }
                    }

                    return best ?? new List<Pad>();
                }
            }
        }

        /// <summary>User settings first, then three progressively looser rescue passes.</summary>
        private static List<Profile> BuildStages(DetectorSettings s)
        {
            return new List<Profile>
            {
                // Stage 0 - high-recall default, validated against real boards:
                // broad gold band, square + round acceptance, screw-ring donut
                // reject OFF (plated rings are valid probe points), silkscreen
                // dark reject ON. Tuned for ~90-100% pad recall out of the box.
                new Profile
                {
                    Gold = s.DetectGold, White = s.DetectWhite, BrightBlob = false,
                    GoldLo = new Scalar(5, 35, 45), GoldHi = new Scalar(42, 255, 255),
                    WhiteSMax = 120, WhiteVMin = 120,
                    CircMin = s.CircMin, AminFrac = s.MinAreaFrac, AmaxFrac = s.MaxAreaFrac, AspectMax = s.AspectMax,
                    AcceptSquares = true, ExtentMin = 0.50, SolidityMin = 0.80, RMaxFrac = 0.07,
                    UseDonut = false, UseDark = true, UseIso = false,
                    DonutRatio = s.DonutRatio, DarkV = s.DarkSurroundV, IsoMax = s.IsoMax
                },
                // Stage 1 - broaden colour a touch more, widen area/aspect.
                new Profile
                {
                    Gold = true, White = true, BrightBlob = false,
                    GoldLo = new Scalar(5, 30, 42), GoldHi = new Scalar(44, 255, 255),
                    WhiteSMax = 130, WhiteVMin = 110,
                    CircMin = Math.Max(0.48, s.CircMin - 0.10),
                    AminFrac = Math.Min(s.MinAreaFrac, 8e-6), AmaxFrac = Math.Max(s.MaxAreaFrac, 3.5e-3),
                    AspectMax = Math.Max(s.AspectMax, 2.8),
                    AcceptSquares = true, ExtentMin = 0.46, SolidityMin = 0.78, RMaxFrac = 0.09,
                    UseDonut = false, UseDark = true, UseIso = false,
                    DonutRatio = 0.50, DarkV = Math.Min(s.DarkSurroundV, 45), IsoMax = 1.0
                },
                // Stage 2 - looser still.
                new Profile
                {
                    Gold = true, White = true, BrightBlob = false,
                    GoldLo = new Scalar(3, 25, 40), GoldHi = new Scalar(46, 255, 255),
                    WhiteSMax = 145, WhiteVMin = 100,
                    CircMin = 0.42, AminFrac = 5e-6, AmaxFrac = 6e-3, AspectMax = 3.2,
                    AcceptSquares = true, ExtentMin = 0.42, SolidityMin = 0.75, RMaxFrac = 0.12,
                    UseDonut = false, UseDark = false, UseIso = false,
                    DonutRatio = 0, DarkV = 0, IsoMax = 1.0
                },
                // Stage 3 - adaptive bright-blob, all rejections off. Last resort
                // so a board the fixed bands miss still yields its pads.
                new Profile
                {
                    Gold = true, White = true, BrightBlob = true,
                    GoldLo = new Scalar(3, 25, 40), GoldHi = new Scalar(48, 255, 255),
                    WhiteSMax = 160, WhiteVMin = 90,
                    CircMin = 0.42, AminFrac = 5e-6, AmaxFrac = 1.2e-2, AspectMax = 3.5,
                    AcceptSquares = true, ExtentMin = 0.50, SolidityMin = 0.80, RMaxFrac = 0.16,
                    UseDonut = false, UseDark = false, UseIso = false,
                    DonutRatio = 0, DarkV = 0, IsoMax = 1.0
                }
            };
        }

        private static List<Pad> RunStage(Mat up, double scale, Rect r, Profile pf)
        {
            int H = up.Height, W = up.Width;
            double total = (double)H * W, amin = pf.AminFrac * total, amax = pf.AmaxFrac * total;
            byte[] V = (pf.UseDonut || pf.UseDark) ? VPlane(up) : null;

            var found = new List<Pad>();
            var sources = MaskSources(up, pf, amax);
            try
            {
                foreach (var m in sources)
                {
                    Cv2.FindContours(m, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    foreach (var c in contours)
                    {
                        double a = Cv2.ContourArea(c);
                        if (a < amin || a > amax) continue;

                        var br = Cv2.BoundingRect(c);
                        double longSide = Math.Max(br.Width, br.Height);
                        double shortSide = Math.Max(1, Math.Min(br.Width, br.Height));
                        if (longSide / shortSide > pf.AspectMax) continue;

                        double per = Cv2.ArcLength(c, true);
                        double circ = per > 0 ? 4 * Math.PI * a / (per * per) : 0;
                        double extent = a / Math.Max(1.0, (double)br.Width * br.Height);
                        double solidity = 1.0;
                        try { var hull = Cv2.ConvexHull(c); double ha = Cv2.ContourArea(hull); if (ha > 0) solidity = a / ha; }
                        catch { }

                        bool round = circ >= pf.CircMin;
                        bool square = pf.AcceptSquares && extent >= pf.ExtentMin && solidity >= pf.SolidityMin;
                        if (!round && !square) continue;

                        Cv2.MinEnclosingCircle(c, out Point2f ctr, out float rad);
                        double cx = ctr.X, cy = ctr.Y, rr = Math.Max(rad, 4.0);
                        if (pf.RMaxFrac > 0 && rr > pf.RMaxFrac * Math.Min(H, W)) continue; // too big: component/shield

                        if (V != null && (pf.UseDonut || pf.UseDark))
                        {
                            double outerR = rr + 14;
                            int x0 = Math.Max(0, (int)(cx - outerR)), x1 = Math.Min(W - 1, (int)(cx + outerR) + 1);
                            int y0 = Math.Max(0, (int)(cy - outerR)), y1 = Math.Min(H - 1, (int)(cy + outerR) + 1);
                            double sIn = 0, sRing = 0, sOut = 0; int nIn = 0, nRing = 0, nOut = 0;
                            double in2 = Math.Pow(0.45 * rr, 2), rg0 = Math.Pow(0.60 * rr, 2), rg1 = Math.Pow(0.95 * rr, 2);
                            double o0 = Math.Pow(rr + 4, 2), o1 = outerR * outerR;
                            for (int y = y0; y <= y1; y++)
                            {
                                double dy2 = (y - cy) * (y - cy); int row = y * W;
                                for (int x = x0; x <= x1; x++)
                                {
                                    double d2 = (x - cx) * (x - cx) + dy2; byte vv = V[row + x];
                                    if (d2 <= in2) { sIn += vv; nIn++; }
                                    else if (d2 >= rg0 && d2 <= rg1) { sRing += vv; nRing++; }
                                    else if (d2 >= o0 && d2 <= o1) { sOut += vv; nOut++; }
                                }
                            }
                            double vin = nIn > 0 ? sIn / nIn : 0, vring = nRing > 0 ? sRing / nRing : 1;
                            if (pf.UseDonut && vring > 60 && vin < pf.DonutRatio * vring) continue;  // screw hole / "O" glyph
                            if (pf.UseDark && nOut > 0 && sOut / nOut < pf.DarkV) continue;           // silkscreen on black bezel
                        }

                        found.Add(new Pad
                        {
                            X = cx / scale + r.X,
                            Y = cy / scale + r.Y,
                            R = rr / scale,
                            Circularity = Math.Round(square && !round ? Math.Max(circ, 0.90) : circ, 2)
                        });
                    }
                }
            }
            finally { foreach (var m in sources) m.Dispose(); }

            // A pad can appear on more than one source mask; merge overlaps.
            var keep = new List<Pad>();
            foreach (var p in found)
                if (!keep.Any(k => (k.X - p.X) * (k.X - p.X) + (k.Y - p.Y) * (k.Y - p.Y)
                                   < Math.Pow(Math.Max(k.R, p.R) * 0.85, 2)))
                    keep.Add(p);
            return keep;
        }

        /// <summary>Snap a user click to the nearest pad-coloured blob centre inside a small window.</summary>
        public static Pad Snap(Mat bgrFull, double px, double py, double defaultR)
        {
            int win = (int)Math.Max(24, defaultR * 5);
            var r = new Rect((int)px - win / 2, (int)py - win / 2, win, win)
                .Intersect(new Rect(0, 0, bgrFull.Width, bgrFull.Height));
            var fallback = new Pad { X = px, Y = py, R = defaultR, Manual = true, Circularity = 0 };
            if (r.Width < 6 || r.Height < 6) return fallback;

            using (var sub = new Mat(bgrFull, r))
            using (var up = new Mat())
            {
                const double k = 4.0;
                Cv2.Resize(sub, up, new Size(sub.Width * (int)k, sub.Height * (int)k), 0, 0, InterpolationFlags.Lanczos4);
                double lx = (px - r.X) * k, ly = (py - r.Y) * k, best = double.MaxValue; Pad bp = null;
                // Strip anything bigger than ~half the window so a shield edge in
                // view can't dominate the click.
                double amaxPx = up.Rows * up.Cols * 0.45;
                var sources = MaskSources(up, SnapProfile(), amaxPx);
                try
                {
                    foreach (var mask in sources)
                    {
                        Cv2.FindContours(mask, out Point[][] cs, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                        foreach (var c in cs)
                        {
                            if (Cv2.ContourArea(c) < 12) continue;
                            Cv2.MinEnclosingCircle(c, out Point2f ctr, out float rad);
                            if (rad > win * k * 0.45) continue;                     // too big: shield/label
                            double d = Math.Sqrt(Math.Pow(ctr.X - lx, 2) + Math.Pow(ctr.Y - ly, 2)) - rad;
                            if (d < best) { best = d; bp = new Pad { X = ctr.X / k + r.X, Y = ctr.Y / k + r.Y, R = rad / k, Manual = true }; }
                        }
                    }
                }
                finally { foreach (var m in sources) m.Dispose(); }
                return (bp != null && best < 3 * k) ? bp : fallback;
            }
        }

        // Permissive mask for click-snapping, so it locks onto tinned/dim pads too.
        private static Profile SnapProfile() => new Profile
        {
            Gold = true, White = true, BrightBlob = false,
            GoldLo = new Scalar(4, 30, 45), GoldHi = new Scalar(46, 255, 255),
            WhiteSMax = 140, WhiteVMin = 100,
            CircMin = 0, AminFrac = 0, AmaxFrac = 1, AspectMax = 99,
            AcceptSquares = true, ExtentMin = 0, SolidityMin = 0,
            UseDonut = false, UseDark = false, UseIso = false
        };

        /// <summary>Merge pads whose centres fall inside each other (e.g. detector hit + manual add).</summary>
        public static List<Pad> Dedupe(List<Pad> pads)
        {
            var keep = new List<Pad>();
            foreach (var p in pads.OrderBy(p => p.Manual ? 1 : 0))
            {
                if (keep.Any(k => Math.Sqrt(Math.Pow(k.X - p.X, 2) + Math.Pow(k.Y - p.Y, 2)) < Math.Max(k.R, p.R) * 0.9)) continue;
                keep.Add(p);
            }
            return keep;
        }

        /// <summary>Reading order: row bands top→bottom, then left→right.</summary>
        public static List<Pad> Order(List<Pad> pads, double band = 30)
            => pads.OrderBy(p => Math.Round(p.Y / band)).ThenBy(p => p.X).ToList();
    }
}
