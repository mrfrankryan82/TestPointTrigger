// TestPoint Trigger - PCB Pad Finder
// Developer: HaKDMoDz™ · v2.0.0 · 2026-09-23
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
        public double MinAreaFrac = 2.5e-5;
        public double MaxAreaFrac = 1.0e-3;
        public double CircMin = 0.78;
        public double AspectMax = 1.9;
        public double IsoMax = 0.55;
        public double DarkSurroundV = 50;
        public double DonutRatio = 0.68;
        public bool DetectGold = true;
        public bool DetectWhite = true;
        public DetectorSettings Clone() => (DetectorSettings)MemberwiseClone();
    }

    public static class PadDetector
    {
        // Gold (HSV band) + white/tinned (bright, unsaturated) mask, cleaned with open/close.
        static Mat PadMask(Mat bgr, DetectorSettings s, out Mat v)
        {
            var hsv = new Mat();
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
            Mat[] ch = Cv2.Split(hsv);
            v = ch[2];
            var pad = new Mat(bgr.Size(), MatType.CV_8UC1, Scalar.All(0));
            if (s.DetectGold)
            {
                using (var gold = new Mat())
                {
                    Cv2.InRange(hsv, new Scalar(8, 55, 60), new Scalar(34, 255, 255), gold);
                    Cv2.BitwiseOr(pad, gold, pad);
                }
            }
            if (s.DetectWhite)
            {
                using (var sLow = new Mat())
                using (var vHigh = new Mat())
                using (var white = new Mat())
                {
                    Cv2.Threshold(ch[1], sLow, 85, 255, ThresholdTypes.BinaryInv); // S <= 85
                    Cv2.Threshold(ch[2], vHigh, 149, 255, ThresholdTypes.Binary);   // V >= 150
                    Cv2.BitwiseAnd(sLow, vHigh, white);
                    Cv2.BitwiseOr(pad, white, pad);
                }
            }
            using (var k = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3)))
            {
                Cv2.MorphologyEx(pad, pad, MorphTypes.Open, k);
                Cv2.MorphologyEx(pad, pad, MorphTypes.Close, k);
            }
            hsv.Dispose(); ch[0].Dispose(); ch[1].Dispose();
            return pad;
        }

        static byte[] Bytes(Mat m)
        {
            var c = m.IsContinuous() ? m : m.Clone();
            var b = new byte[c.Rows * c.Cols];
            Marshal.Copy(c.Data, b, 0, b.Length);
            if (!ReferenceEquals(c, m)) c.Dispose();
            return b;
        }

        /// <summary>Detect candidate pads. roi (optional) limits the search; results are in full-image coords.</summary>
        public static List<Pad> Detect(Mat bgrFull, DetectorSettings s, Rect? roi = null)
        {
            Rect r = roi ?? new Rect(0, 0, bgrFull.Width, bgrFull.Height);
            r = r.Intersect(new Rect(0, 0, bgrFull.Width, bgrFull.Height));
            var result = new List<Pad>();
            if (r.Width < 8 || r.Height < 8) return result;

            using (var src = new Mat(bgrFull, r))
            {
                int h = src.Height, w = src.Width;
                double scale = Math.Max(1.0, s.LongEdge / (double)Math.Max(h, w));
                using (var up = new Mat())
                {
                    Cv2.Resize(src, up, new Size((int)(w * scale), (int)(h * scale)), 0, 0, InterpolationFlags.Lanczos4);
                    int H = up.Height, W = up.Width;
                    Mat vMat;
                    using (var pad = PadMask(up, s, out vMat))
                    {
                        byte[] V = Bytes(vMat), P = Bytes(pad);
                        vMat.Dispose();
                        double total = (double)H * W, amin = s.MinAreaFrac * total, amax = s.MaxAreaFrac * total;

                        Cv2.FindContours(pad, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                        foreach (var c in contours)
                        {
                            double a = Cv2.ContourArea(c);
                            if (a < amin || a > amax) continue;
                            var br = Cv2.BoundingRect(c);
                            if (Math.Max(br.Width, br.Height) / (double)Math.Max(1, Math.Min(br.Width, br.Height)) > s.AspectMax) continue;
                            double p = Cv2.ArcLength(c, true);
                            double circ = p > 0 ? 4 * Math.PI * a / (p * p) : 0;
                            if (circ < s.CircMin) continue;
                            Cv2.MinEnclosingCircle(c, out Point2f ctr, out float rad);
                            double cx = ctr.X, cy = ctr.Y, rr = Math.Max(rad, 4.0);

                            // ring statistics over a local window only (fast)
                            double outerR = rr + 14;
                            int x0 = Math.Max(0, (int)(cx - outerR)), x1 = Math.Min(W - 1, (int)(cx + outerR) + 1);
                            int y0 = Math.Max(0, (int)(cy - outerR)), y1 = Math.Min(H - 1, (int)(cy + outerR) + 1);
                            double sIn = 0, sRing = 0, sOut = 0, sOutPad = 0; int nIn = 0, nRing = 0, nOut = 0;
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
                                    else if (d2 >= o0 && d2 <= o1) { sOut += vv; nOut++; if (P[row + x] > 0) sOutPad++; }
                                }
                            }
                            double vin = nIn > 0 ? sIn / nIn : 0, vring = nRing > 0 ? sRing / nRing : 1;
                            if (vring > 60 && vin < s.DonutRatio * vring) continue;               // screw hole / "O" glyph
                            if (nOut > 0 && sOut / nOut < s.DarkSurroundV) continue;              // silkscreen on black bezel
                            if (nOut > 0 && sOutPad / nOut > s.IsoMax) continue;                  // embedded in shield metal
                            result.Add(new Pad { X = cx / scale + r.X, Y = cy / scale + r.Y, R = rr / scale, Circularity = Math.Round(circ, 2) });
                        }
                    }
                }
            }
            return result;
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
                using (var mask = PadMask(up, new DetectorSettings(), out Mat v))
                {
                    v.Dispose();
                    Cv2.FindContours(mask, out Point[][] cs, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    double lx = (px - r.X) * k, ly = (py - r.Y) * k, best = double.MaxValue; Pad bp = null;
                    foreach (var c in cs)
                    {
                        if (Cv2.ContourArea(c) < 12) continue;
                        Cv2.MinEnclosingCircle(c, out Point2f ctr, out float rad);
                        if (rad > win * k * 0.45) continue;                     // too big: shield/label
                        double d = Math.Sqrt(Math.Pow(ctr.X - lx, 2) + Math.Pow(ctr.Y - ly, 2)) - rad;
                        if (d < best) { best = d; bp = new Pad { X = ctr.X / k + r.X, Y = ctr.Y / k + r.Y, R = rad / k, Manual = true }; }
                    }
                    return (bp != null && best < 3 * k) ? bp : fallback;
                }
            }
        }

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
