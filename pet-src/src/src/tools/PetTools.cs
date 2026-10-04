using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

// pet-tools: 立绘素材处理小工具（纯 .NET Framework + System.Drawing）
//   key   <in> <out> [--t0 N] [--t1 N] [--despill 0..1] [--nolargest] [--alpha-boost N]  抠掉纯色背景 -> 透明 PNG
//   trim  <in> <out> [--pad N]                                          裁掉透明边距
//   scale <in> <out> <maxWidth>                                         高质量缩放（保比例）
//   info  <in>                                                          打印尺寸 / 透明像素比例
class PetTools
{
    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: pet-tools key|trim|scale|info ..."); return 2; }
        string cmd = args[0].ToLowerInvariant();
        try
        {
            if (cmd == "key") return Key(args);
            if (cmd == "trim") return Trim(args);
            if (cmd == "scale") return Scale(args);
            if (cmd == "info") return Info(args);
            Console.WriteLine("unknown command: " + cmd);
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    static double Arg(string[] a, string name, double def)
    {
        for (int i = 3; i + 1 < a.Length; i++)
            if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase))
                return double.Parse(a[i + 1], CultureInfo.InvariantCulture);
        return def;
    }

    static bool Flag(string[] a, string name)
    {
        for (int i = 3; i < a.Length; i++)
            if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static Bitmap Load32(string path)
    {
        using (Image src = Image.FromFile(path))
        {
            Bitmap bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            }
            return bmp;
        }
    }

    static byte[] GetPixels(Bitmap bmp, out int stride, out int w, out int h)
    {
        w = bmp.Width; h = bmp.Height;
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        stride = data.Stride;
        byte[] px = new byte[stride * h];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        return px;
    }

    static void PutPixels(Bitmap bmp, byte[] px)
    {
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
    }

    // 抠背景：四角采样背景色 -> 颜色距离映射为 alpha -> 去背景溢色 -> 只保留主连通块 -> 裁剪
    static int Key(string[] a)
    {
        string inPath = a[1], outPath = a[2];
        double t0 = Arg(a, "--t0", 55);
        double t1 = Arg(a, "--t1", 145);
        double despill = Arg(a, "--despill", 0.85);   // 0 = 不去溢色（保留原色，边缘可能留一点品红）
        bool keepLargest = !Flag(a, "--nolargest");
        Bitmap bmp = Load32(inPath);
        int stride, w, h;
        byte[] px = GetPixels(bmp, out stride, out w, out h);

        // 四角各取 8x8 均值，再用**多数票**选出真正的背景色：
        // 若某两个角被前景（例如立绘下方的桌面）占据，旧版直接平均会把背景色算歪 → 抠出大片残留。
        double[] cr = new double[4], cg = new double[4], cb = new double[4];
        int[] cx = { 2, w - 10, 2, w - 10 };
        int[] cy = { 2, 2, h - 10, h - 10 };
        for (int c = 0; c < 4; c++)
        {
            double r2 = 0, g2 = 0, b2 = 0; int n2 = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int o = (cy[c] + y) * stride + (cx[c] + x) * 4;
                    b2 += px[o]; g2 += px[o + 1]; r2 += px[o + 2]; n2++;
                }
            cr[c] = r2 / n2; cg[c] = g2 / n2; cb[c] = b2 / n2;
        }
        int best = 0, bestVotes = -1;
        for (int i = 0; i < 4; i++)
        {
            int votes = 0;
            for (int j = 0; j < 4; j++)
            {
                double dd = Math.Sqrt((cr[i] - cr[j]) * (cr[i] - cr[j]) + (cg[i] - cg[j]) * (cg[i] - cg[j]) + (cb[i] - cb[j]) * (cb[i] - cb[j]));
                if (dd < 60) votes++;
            }
            if (votes > bestVotes) { bestVotes = votes; best = i; }   // 票数相同时取编号小的（左上优先）
        }
        double sr = cr[best], sg = cg[best], sb = cb[best];
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "bg = #{0:X2}{1:X2}{2:X2}  (corner {3}, {4}/4 票)  size={5}x{6}",
            (int)sr, (int)sg, (int)sb, best, bestVotes, w, h));

        byte[] alpha = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int x = 0; x < w; x++)
            {
                int o = row + x * 4;
                double b = px[o], g = px[o + 1], r = px[o + 2];
                double d = Math.Sqrt((r - sr) * (r - sr) + (g - sg) * (g - sg) + (b - sb) * (b - sb));
                double al;
                if (d <= t0) al = 0;
                else if (d >= t1) al = 1;
                else al = (d - t0) / (t1 - t0);
                if (al <= 0) { alpha[y * w + x] = 0; px[o] = 0; px[o + 1] = 0; px[o + 2] = 0; px[o + 3] = 0; continue; }
                // 去背景溢色（v2）：只在半透明边缘按「品红程度」把 R/B 往 G 拉，**绝不做 c=(c-(1-a)·bg)/a 的反预乘**。
                // 反预乘在 JPEG 压缩过的柔和阴影上会把 G 除爆再截断，结果是把紫色染成绿色
                // （实测云朵立绘平均色 G 从 188 涨到 205、B 从 214 掉到 207 —— 用户一眼看出"缎带变绿了"）。
                double mag = Math.Min(r, b) - g;            // 品红程度（>0 说明偏品红/紫）
                if (mag > 0 && al < 1.0)
                {
                    double k = (1.0 - al) * despill;
                    r -= mag * k; b -= mag * k;
                }
                px[o + 2] = Clamp(r); px[o + 1] = Clamp(g); px[o] = Clamp(b);
                px[o + 3] = (byte)Math.Round(al * 255);
                alpha[y * w + x] = px[o + 3];
            }
        }

        if (keepLargest)
        {
            int removed = DropSmallBlobs(alpha, px, stride, w, h, 0.10);
            Console.WriteLine("dropped " + removed + " px of small detached blobs");
        }

        PutPixels(bmp, px);
        bmp.Save(outPath, ImageFormat.Png);
        bmp.Dispose();
        Console.WriteLine("saved " + outPath);
        return Trim(new string[] { "trim", outPath, outPath, "--pad", "2" });
    }

    static byte Clamp(double v)
    {
        if (v < 0) return 0; if (v > 255) return 255; return (byte)Math.Round(v);
    }

    // 连通块过滤：保留面积 >= 最大块 8% 且 >= 64px 的块（去掉飘散的气泡/杂点），保留块内部的洞
    static int DropSmallBlobs(byte[] alpha, byte[] px, int stride, int w, int h, double ratio)
    {
        int total = w * h;
        bool[] seen = new bool[total];
        int[] stack = new int[total];
        List<int[]> blobs = new List<int[]>();
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };
        for (int i = 0; i < total; i++)
        {
            if (seen[i] || alpha[i] < 16) continue;
            int sp = 0; stack[sp++] = i; seen[i] = true;
            List<int> members = new List<int>();
            while (sp > 0)
            {
                int cur = stack[--sp];
                members.Add(cur);
                int cxi = cur % w, cyi = cur / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cxi + dx[k], ny = cyi + dy[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (seen[ni] || alpha[ni] < 16) continue;
                    seen[ni] = true; stack[sp++] = ni;
                }
            }
            blobs.Add(new int[] { members.Count, members[0] });
            // 记录成员，稍后按面积决定是否清除
            blobMembers.Add(members);
        }
        int max = 1;
        for (int i = 0; i < blobs.Count; i++) if (blobs[i][0] > max) max = blobs[i][0];
        int removed = 0;
        for (int i = 0; i < blobMembers.Count; i++)
        {
            List<int> m = blobMembers[i];
            if (m.Count >= max * ratio && m.Count >= 64) continue;
            for (int j = 0; j < m.Count; j++)
            {
                alpha[m[j]] = 0;
                int o = (m[j] / w) * stride + (m[j] % w) * 4;
                px[o] = 0; px[o + 1] = 0; px[o + 2] = 0; px[o + 3] = 0;
            }
            removed += m.Count;
        }
        blobMembers.Clear();
        return removed;
    }

    [ThreadStatic] static List<List<int>> blobMembersStorage;
    static List<List<int>> blobMembers
    {
        get
        {
            if (blobMembersStorage == null) blobMembersStorage = new List<List<int>>();
            return blobMembersStorage;
        }
    }

    static int Trim(string[] a)
    {
        string inPath = a[1], outPath = a[2];
        int pad = (int)Arg(a, "--pad", 2);
        Bitmap bmp = Load32(inPath);
        int stride, w, h;
        byte[] px = GetPixels(bmp, out stride, out w, out h);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * stride + x * 4 + 3] > 8)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        bmp.Dispose();
        if (maxX < 0) { Console.WriteLine("WARN: fully transparent, nothing trimmed"); return 1; }
        minX = Math.Max(0, minX - pad); minY = Math.Max(0, minY - pad);
        maxX = Math.Min(w - 1, maxX + pad); maxY = Math.Min(h - 1, maxY + pad);
        int cw = maxX - minX + 1, ch = maxY - minY + 1;
        using (Bitmap src = Load32(inPath))
        using (Bitmap dst = new Bitmap(cw, ch, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, cw, ch), new Rectangle(minX, minY, cw, ch), GraphicsUnit.Pixel);
            }
            dst.Save(outPath, ImageFormat.Png);
        }
        Console.WriteLine("trimmed " + w + "x" + h + " -> " + cw + "x" + ch + " : " + outPath);
        return 0;
    }

    static int Scale(string[] a)
    {
        string inPath = a[1], outPath = a[2];
        int maxW = int.Parse(a[3], CultureInfo.InvariantCulture);
        using (Bitmap src = Load32(inPath))
        {
            double k = Math.Min(1.0, (double)maxW / src.Width);
            int nw = Math.Max(1, (int)Math.Round(src.Width * k));
            int nh = Math.Max(1, (int)Math.Round(src.Height * k));
            using (Bitmap dst = new Bitmap(nw, nh, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(dst))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(src, new Rectangle(0, 0, nw, nh), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                dst.Save(outPath, ImageFormat.Png);
                Console.WriteLine("scaled " + src.Width + "x" + src.Height + " -> " + nw + "x" + nh + " : " + outPath);
            }
        }
        return 0;
    }

    static int Info(string[] a)
    {
        using (Bitmap bmp = Load32(a[1]))
        {
            int stride, w, h;
            byte[] px = GetPixels(bmp, out stride, out w, out h);
            long opaque = 0, partial = 0;
            for (int i = 0; i < w * h; i++)
            {
                byte al = px[(i / w) * stride + (i % w) * 4 + 3];
                if (al > 240) opaque++; else if (al > 8) partial++;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0}: {1}x{2}  opaque={3:P1}  edge={4:P1}", a[1], w, h,
                (double)opaque / (w * h), (double)partial / (w * h)));
        }
        return 0;
    }
}
