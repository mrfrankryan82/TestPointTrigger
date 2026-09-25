// TestPoint Trigger - PCB Pad Finder
// Developer: HaKDMoDz™ · v2.0.0 · 2026-09-23
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TestPointTrigger
{
    public enum MarkStyle { LabelsOnly, CirclesAndLabels }

    public class RenderOptions
    {
        public MarkStyle Style = MarkStyle.LabelsOnly;
        public float LabelPx = 11f;              // label height in ORIGINAL image pixels
        public Color LabelColor = Color.Yellow;
        public Color CircleColor = Color.Red;
        public bool Footer = true;
    }

    public static class PadRenderer
    {
        /// <summary>
        /// Draw marks onto g. map converts image coords to target coords; scale = target px per image px.
        /// Labels try 8 positions around each pad and take the first that hits no pad and no earlier label.
        /// </summary>
        public static void Draw(Graphics g, IList<Pad> pads, RenderOptions o, Func<PointF, PointF> map, float scale, int highlight = -1)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            float em = Math.Max(6f, o.LabelPx * scale);
            var padRects = new List<RectangleF>();
            foreach (var p in pads)
            {
                var c = map(new PointF((float)p.X, (float)p.Y)); float r = (float)p.R * scale;
                padRects.Add(new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r));
            }
            var placed = new List<RectangleF>();
            using (var fam = new FontFamily("Segoe UI"))
            using (var outline = new Pen(Color.Black, Math.Max(2f, em * 0.22f)) { LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(o.LabelColor))
            using (var hiFill = new SolidBrush(Color.Cyan))
            using (var circ = new Pen(o.CircleColor, Math.Max(1.5f, 1.6f * scale)))
            using (var hiPen = new Pen(Color.Cyan, Math.Max(2f, 2f * scale)))
            {
                for (int i = 0; i < pads.Count; i++)
                {
                    var pr = padRects[i];
                    var c = new PointF(pr.X + pr.Width / 2, pr.Y + pr.Height / 2); float r = pr.Width / 2;
                    if (o.Style == MarkStyle.CirclesAndLabels || i == highlight)
                        g.DrawEllipse(i == highlight ? hiPen : circ, c.X - r - 3, c.Y - r - 3, 2 * r + 6, 2 * r + 6);

                    string t = (i + 1).ToString();
                    using (var path = new GraphicsPath())
                    {
                        path.AddString(t, fam, (int)FontStyle.Bold, em, PointF.Empty, StringFormat.GenericTypographic);
                        var b = path.GetBounds();
                        float gap = r + (o.Style == MarkStyle.CirclesAndLabels ? 5 : 2);
                        // candidate label anchors: NE, E, NW, W, SE, SW, N, S
                        var cands = new[]
                        {
                            new PointF(c.X + gap*0.7f, c.Y - gap*0.7f - b.Height),
                            new PointF(c.X + gap + 1, c.Y - b.Height/2),
                            new PointF(c.X - gap*0.7f - b.Width, c.Y - gap*0.7f - b.Height),
                            new PointF(c.X - gap - 1 - b.Width, c.Y - b.Height/2),
                            new PointF(c.X + gap*0.7f, c.Y + gap*0.7f),
                            new PointF(c.X - gap*0.7f - b.Width, c.Y + gap*0.7f),
                            new PointF(c.X - b.Width/2, c.Y - gap - b.Height - 1),
                            new PointF(c.X - b.Width/2, c.Y + gap + 1),
                        };
                        RectangleF chosen = RectangleF.Empty; bool ok = false;
                        foreach (var a in cands)
                        {
                            var rect = new RectangleF(a.X, a.Y, b.Width, b.Height); var pad = RectangleF.Inflate(rect, 1, 1);
                            bool hit = false;
                            foreach (var q in placed) if (q.IntersectsWith(pad)) { hit = true; break; }
                            if (!hit) for (int j = 0; j < padRects.Count; j++) if (padRects[j].IntersectsWith(pad)) { hit = true; break; }
                            if (!hit) { chosen = rect; ok = true; break; }
                        }
                        if (!ok) chosen = new RectangleF(cands[0].X, cands[0].Y, b.Width, b.Height);
                        placed.Add(chosen);
                        using (var m = new Matrix())
                        {
                            m.Translate(chosen.X - b.X, chosen.Y - b.Y);
                            path.Transform(m);
                        }
                        g.DrawPath(outline, path);
                        g.FillPath(i == highlight ? hiFill : fill, path);
                    }
                }
            }
        }

        public static Bitmap Export(Bitmap src, Rectangle crop, IList<Pad> pads, RenderOptions o, float upscale, string footer)
        {
            int w = (int)(crop.Width * upscale), h = (int)(crop.Height * upscale);
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h), crop, GraphicsUnit.Pixel);
                Draw(g, pads, o, p => new PointF((p.X - crop.X) * upscale, (p.Y - crop.Y) * upscale), upscale);
                if (o.Footer && !string.IsNullOrEmpty(footer))
                {
                    using (var f = new Font("Segoe UI", Math.Max(9f, h / 70f), FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                    {
                        var sz = g.MeasureString(footer, f);
                        g.FillRectangle(bg, 0, h - sz.Height - 8, sz.Width + 16, sz.Height + 8);
                        g.DrawString(footer, f, Brushes.White, 8, h - sz.Height - 4);
                    }
                }
            }
            return bmp;
        }
    }
}
