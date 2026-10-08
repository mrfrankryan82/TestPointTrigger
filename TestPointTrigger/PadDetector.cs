// Mobile Surgery - PCB Pad Finder detector (distance-transform core)
// Developer: HaKDMoDz™ · v2.2.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.Linq;
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
        public double MinRadiusPx = 4;    // smallest pad radius to accept (original px)
        public double MaxRadiusPx = 45;   // largest  pad radius to accept (original px)
        public double CircMin = 0.35;     // core compactness floor (drops leaked traces)
        public bool DetectGold = true;
        public bool DetectWhite = true;
        public DetectorSettings Clone() => (DetectorSettings)MemberwiseClone();
    }

    public static class PadDetector
    {
        // ─────────────────────────────── Masking ───────────────────────────────

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

        /// <summary>
        /// Gold + white/tinned pad mask. Gold and tinned are unioned here (this
        /// is a solid-fill mask feeding a distance transform, not contouring),
        /// and oversized blobs — the light background and metal shields — are
        /// stripped so they don't distort the transform.
        /// </summary>
        private static Mat PadMask(Mat bgr, DetectorSettings s, out Mat sPlane)
        {
            var hsv = new Mat();
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
            var ch = Cv2.Split(hsv);
            sPlane = ch[1].Clone();

            double amaxPx = bgr.Rows * bgr.Cols * 0.06;   // anything past ~6% of area is background/shield
            var mask = new Mat(bgr.Size(), MatType.CV_8UC1, Scalar.All(0));

            if (s.DetectGold)
                using (var gold = new Mat())
                {
                    // Gold band widened down to hue 3 to catch coppery-orange pads.
                    Cv2.InRange(hsv, new Scalar(3, 35, 48), new Scalar(42, 255, 255), gold);
                    Cv2.BitwiseOr(mask, gold, mask);
                }
            if (s.DetectWhite)
                using (var sLow = new Mat())
                using (var vHigh = new Mat())
                using (var wh = new Mat())
                {
                    Cv2.Threshold(ch[1], sLow, 100, 255, ThresholdTypes.BinaryInv);
                    Cv2.Threshold(ch[2], vHigh, 140, 255, ThresholdTypes.Binary);
                    Cv2.BitwiseAnd(sLow, vHigh, wh);
                    StripBig(wh, amaxPx);
                    Cv2.BitwiseOr(mask, wh, mask);
                }

            StripBig(mask, amaxPx);
            using (var k3 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3)))
                Cv2.MorphologyEx(mask, mask, MorphTypes.Open, k3);
            using (var k5 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5)))
                Cv2.MorphologyEx(mask, mask, MorphTypes.Close, k5);

            hsv.Dispose(); ch[0].Dispose(); ch[1].Dispose(); ch[2].Dispose();
            return mask;
        }

        // ─────────────────────────────── Detection ───────────────────────────────

        /// <summary>
        /// Detect pads via a distance transform of the pad mask: pad cores are
        /// the "thick" regions, so a pad connected to gold traces is still found
        /// (the old contour approach rejected those as non-round). roi limits the
        /// search; results are in full-image coords.
        /// </summary>
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

                    using (var sPlane = new Mat())
                    using (var mask = PadMask(up, s, out var sp))
                    using (sp)
                    using (var dist = new Mat())
                    using (var core = new Mat())
                    {
                        Cv2.DistanceTransform(mask, dist, DistanceTypes.L2, DistanceTransformMasks.Mask5);

                        // Radius band in ORIGINAL pixels → scale-invariant across any crop/box size.
                        double rmin = Math.Max(2.0, s.MinRadiusPx) * scale;
                        double rmax = Math.Max(rmin + 1, s.MaxRadiusPx * scale);
                        Cv2.InRange(dist, new Scalar(rmin), new Scalar(rmax), core);

                        var result = new List<Pad>();
                        using (var labels = new Mat())
                        using (var stats = new Mat())
                        using (var cents = new Mat())
                        {
                            int n = Cv2.ConnectedComponentsWithStats(core, labels, stats, cents);
                            for (int i = 1; i < n; i++)
                            {
                                int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                                if (area < 4) continue;
                                int cw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                                int cs = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                                double comp = area / Math.Max(1.0, (double)cw * cs);   // round core ~0.78, trace ~low
                                if (comp < s.CircMin) continue;

                                double cx = cents.At<double>(i, 0), cy = cents.At<double>(i, 1);
                                int ix = Math.Min(up.Cols - 1, Math.Max(0, (int)Math.Round(cx)));
                                int iy = Math.Min(up.Rows - 1, Math.Max(0, (int)Math.Round(cy)));
                                double rr = Math.Max(dist.At<float>(iy, ix), 3.0);

                                result.Add(new Pad
                                {
                                    X = cx / scale + r.X,
                                    Y = cy / scale + r.Y,
                                    R = rr / scale,
                                    Circularity = Math.Round(comp, 2)
                                });
                            }
                        }

                        // merge cores that resolve to the same pad
                        var keep = new List<Pad>();
                        foreach (var p in result.OrderByDescending(p => p.R))
                            if (!keep.Any(k => (k.X - p.X) * (k.X - p.X) + (k.Y - p.Y) * (k.Y - p.Y)
                                               < Math.Pow(Math.Max(k.R, p.R) * 0.9, 2)))
                                keep.Add(p);
                        return keep;
                    }
                }
            }
        }

        /// <summary>Snap a user click to the nearest pad-core centre inside a small window.</summary>
        public static Pad Snap(Mat bgrFull, double px, double py, double defaultR)
        {
            int win = (int)Math.Max(24, defaultR * 6);
            var r = new Rect((int)px - win / 2, (int)py - win / 2, win, win)
                .Intersect(new Rect(0, 0, bgrFull.Width, bgrFull.Height));
            var fallback = new Pad { X = px, Y = py, R = defaultR, Manual = true, Circularity = 0 };
            if (r.Width < 6 || r.Height < 6) return fallback;

            using (var sub = new Mat(bgrFull, r))
            using (var up = new Mat())
            {
                const double k = 4.0;
                Cv2.Resize(sub, up, new Size(sub.Width * (int)k, sub.Height * (int)k), 0, 0, InterpolationFlags.Lanczos4);
                var ds = new DetectorSettings { MinRadiusPx = 2, MaxRadiusPx = win, CircMin = 0.20 };
                using (var mask = PadMask(up, ds, out var sp))
                using (sp)
                using (var dist = new Mat())
                using (var core = new Mat())
                {
                    Cv2.DistanceTransform(mask, dist, DistanceTypes.L2, DistanceTransformMasks.Mask5);
                    Cv2.InRange(dist, new Scalar(2.5 * k), new Scalar(win * k), core);
                    double lx = (px - r.X) * k, ly = (py - r.Y) * k, best = double.MaxValue; Pad bp = null;
                    using (var labels = new Mat())
                    using (var stats = new Mat())
                    using (var cents = new Mat())
                    {
                        int n = Cv2.ConnectedComponentsWithStats(core, labels, stats, cents);
                        for (int i = 1; i < n; i++)
                        {
                            if (stats.At<int>(i, (int)ConnectedComponentsTypes.Area) < 4) continue;
                            double cx = cents.At<double>(i, 0), cy = cents.At<double>(i, 1);
                            int ix = Math.Min(up.Cols - 1, Math.Max(0, (int)cx)), iy = Math.Min(up.Rows - 1, Math.Max(0, (int)cy));
                            double rad = dist.At<float>(iy, ix);
                            double d = Math.Sqrt((cx - lx) * (cx - lx) + (cy - ly) * (cy - ly)) - rad;
                            if (d < best) { best = d; bp = new Pad { X = cx / k + r.X, Y = cy / k + r.Y, R = rad / k, Manual = true }; }
                        }
                    }
                    return (bp != null && best < 4 * k) ? bp : fallback;
                }
            }
        }

        /// <summary>Merge pads whose centres fall inside each other.</summary>
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
