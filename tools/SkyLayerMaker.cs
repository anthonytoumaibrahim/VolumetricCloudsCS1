// The sky layer's pictures (1.5.0): how much cloud there is in each direction of a photographed
// sky, cut out of an unclipped HDR panorama (Radiance .hdr, equirectangular, Poly Haven's CC0
// skies). Offline only: tools/make-skylayer.ps1 compiles this with Add-Type. Nothing here ships.
//
// The idea: a clear sky's colour depends on two angles only, how high the direction is and how far
// it is from the sun (every sky model, Preetham's or Hosek's, is a function of those two). So the
// photo's own blue gaps, gathered by those two angles from all round the sky, say what clear sky
// looks like everywhere, including behind a cloud. A cloud is whiter than that: how much of the way
// a pixel's colour is from the clear sky's to a cloud's is how much cloud there is.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class SkyLayerMaker
{
    /// <summary>The upper half of an equirectangular HDR: row 0 straight up, the last row the horizon.</summary>
    public sealed class Sky
    {
        public int Width;
        public int Height;
        public float[] R, G, B;
    }

    /// <summary>The cut-out: cloud 0..1 per texel, same layout as <see cref="Sky"/>.</summary>
    public sealed class Cut
    {
        public int Width;
        public int Height;
        public float[] Cloud;
        public float[] ClearS;      // the clear sky's blueness the cut was measured against
        public float SunAzimuth;    // degrees, 0 at column 0, growing with the column
        public float SunElevation;  // degrees
        public float SunRadius;     // degrees: what was taken as the disc and its glare core
        public float CloudS;        // a cloud's blueness
        public float[] Skyline;     // per column, how high the ground stands (degrees)
        public string Notes;
    }

    /// <summary>How the cloud is measured: 0 blueness only, 1 the split into sky and cloud light, 2 the larger of the two.</summary>
    public static int Mode = 2;

    // ---- reading ---------------------------------------------------------------------------------

    /// <summary>A Radiance .hdr (RGBE; RLE scanlines or flat), its upper half only.</summary>
    public static Sky ReadUpperHalf(string path)
    {
        using (var stream = new BufferedStream(File.OpenRead(path), 1 << 20))
        {
            string line;
            bool rgbe = false;
            while ((line = ReadLine(stream)) != null && line.Length > 0)
            {
                if (line.StartsWith("FORMAT=", StringComparison.Ordinal))
                    rgbe = line == "FORMAT=32-bit_rle_rgbe";
            }

            if (!rgbe)
                throw new InvalidDataException(path + ": not 32-bit_rle_rgbe");

            string size = ReadLine(stream);
            string[] parts = size.Split(' ');
            if (parts.Length != 4 || parts[0] != "-Y" || parts[2] != "+X")
                throw new InvalidDataException(path + ": unsupported orientation '" + size + "'");

            int height = int.Parse(parts[1]);
            int width = int.Parse(parts[3]);
            int rows = height / 2;

            var sky = new Sky { Width = width, Height = rows, R = new float[width * rows], G = new float[width * rows], B = new float[width * rows] };
            var scan = new byte[width * 4];
            for (int y = 0; y < rows; y++)
            {
                ReadScanline(stream, scan, width);
                for (int x = 0; x < width; x++)
                {
                    int e = scan[x * 4 + 3];
                    int i = y * width + x;
                    if (e == 0)
                        continue;

                    float f = (float)Math.Pow(2.0, e - 136);
                    sky.R[i] = (scan[x * 4] + 0.5f) * f;
                    sky.G[i] = (scan[x * 4 + 1] + 0.5f) * f;
                    sky.B[i] = (scan[x * 4 + 2] + 0.5f) * f;
                }
            }

            return sky;
        }
    }

    private static string ReadLine(Stream s)
    {
        var sb = new StringBuilder();
        int c;
        while ((c = s.ReadByte()) >= 0)
        {
            if (c == '\n')
                return sb.ToString();
            sb.Append((char)c);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static int Next(Stream s)
    {
        int c = s.ReadByte();
        if (c < 0)
            throw new EndOfStreamException();
        return c;
    }

    // One scanline into scan as RGBE quadruples.
    private static void ReadScanline(Stream s, byte[] scan, int width)
    {
        int b0 = Next(s), b1 = Next(s), b2 = Next(s), b3 = Next(s);
        if (b0 != 2 || b1 != 2 || (b2 & 0x80) != 0 || width < 8 || width > 0x7fff)
        {
            // Flat: these four bytes were the first pixel.
            scan[0] = (byte)b0; scan[1] = (byte)b1; scan[2] = (byte)b2; scan[3] = (byte)b3;
            for (int i = 4; i < width * 4; i++)
                scan[i] = (byte)Next(s);
            return;
        }

        if (((b2 << 8) | b3) != width)
            throw new InvalidDataException("scanline width mismatch");

        for (int channel = 0; channel < 4; channel++)
        {
            int x = 0;
            while (x < width)
            {
                int count = Next(s);
                if (count > 128)
                {
                    count -= 128;
                    byte value = (byte)Next(s);
                    for (int k = 0; k < count; k++)
                        scan[(x++) * 4 + channel] = value;
                }
                else
                {
                    for (int k = 0; k < count; k++)
                        scan[(x++) * 4 + channel] = (byte)Next(s);
                }
            }
        }
    }

    // ---- geometry --------------------------------------------------------------------------------

    public static float ElevationOf(int y, int height)
    {
        return 90f * (1f - (y + 0.5f) / height);
    }

    public static float AzimuthOf(int x, int width)
    {
        return 360f * (x + 0.5f) / width;
    }

    private static void Direction(float azimuthDeg, float elevationDeg, out double dx, out double dy, out double dz)
    {
        double a = azimuthDeg * Math.PI / 180.0, e = elevationDeg * Math.PI / 180.0;
        dx = Math.Cos(e) * Math.Cos(a);
        dz = Math.Cos(e) * Math.Sin(a);
        dy = Math.Sin(e);
    }

    // ---- the cut ---------------------------------------------------------------------------------

    private static float Lum(Sky s, int i)
    {
        return 0.2126f * s.R[i] + 0.7152f * s.G[i] + 0.0722f * s.B[i];
    }

    /// <summary>Blueness: (B - R) / (B + R). Clear sky ~0.3-0.7, a white cloud ~0, a sunset cloud below 0.</summary>
    private static float Blueness(Sky s, int i)
    {
        float sum = s.B[i] + s.R[i];
        return sum > 1e-9f ? (s.B[i] - s.R[i]) / sum : 0f;
    }

    // The table of clear-sky blueness: elevation in ElevBin-degree rows, angle from the sun in
    // SunBin-degree columns.
    private const float ElevBin = 2f;
    private const float SunBin = 3f;

    /// <summary>
    /// Cuts the clouds out. <paramref name="horizonCut"/>: degrees over the horizon where the cloud
    /// starts fading in, over <paramref name="horizonFade"/> degrees (what stands higher -- a tree, a
    /// hill -- is found per column and painted over from the cloud beside it). <paramref name="sunClear"/>:
    /// degrees round the photo's sun where its glare (lens streaks, a starburst) is taken for sky, not
    /// cloud; 0 = only the disc. <paramref name="sunFill"/>: degrees round the sun filled smoothly from
    /// round it (at least the disc and the core of its glare, found by brightness).
    /// </summary>
    public static Cut Extract(Sky sky, float horizonCut, float horizonFade, float sunClear, float sunFill, bool rays)
    {
        int w = sky.Width, h = sky.Height;
        var notes = new StringBuilder();

        // The sun: the brightest texel (unclipped HDR: the disc is thousands of times the sky).
        int sunIndex = 0;
        float sunLum = 0f;
        var lums = new float[w * h];
        for (int i = 0; i < lums.Length; i++)
        {
            lums[i] = Lum(sky, i);
            if (lums[i] > sunLum)
            {
                sunLum = lums[i];
                sunIndex = i;
            }
        }

        float[] sorted = (float[])lums.Clone();
        Array.Sort(sorted);
        float median = sorted[sorted.Length / 2];
        float sunAz = AzimuthOf(sunIndex % w, w);
        float sunEl = ElevationOf(sunIndex / w, h);
        bool sunVisible = sunLum > 200f * median;
        notes.AppendFormat("sun az {0:F1} el {1:F1}, {2:F0}x the median sky{3}; ", sunAz, sunEl, sunLum / Math.Max(median, 1e-9f),
                           sunVisible ? "" : " (hidden: no clear disc)");

        // The disc and the core of its glare: everything round the sun over 30x the median sky.
        float sunRadius = 0f;
        if (sunVisible)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (lums[i] > 30f * median)
                        sunRadius = Math.Max(sunRadius, Angle(AzimuthOf(x, w), ElevationOf(y, h), sunAz, sunEl));
                }
            }
            sunRadius = Math.Min(sunRadius, 12f) + 0.75f;
        }

        // Per texel: its blueness and its two angles.
        var blue = new float[w * h];
        var gamma = new float[w * h];
        double sx, sy, sz;
        Direction(sunAz, sunEl, out sx, out sy, out sz);
        for (int y = 0; y < h; y++)
        {
            float el = ElevationOf(y, h);
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                blue[i] = Blueness(sky, i);
                double dx, dy, dz;
                Direction(AzimuthOf(x, w), el, out dx, out dy, out dz);
                gamma[i] = (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, dx * sx + dy * sy + dz * sz))) * 180.0 / Math.PI);
            }
        }

        // A cloud's blueness: a white cloud's at first, then what the clearly cloudy texels have
        // (a sunrise's clouds are pink, a shaded base bluish).
        float cloudS = 0.05f;

        // Where the ground stands over the horizon, per column (trees, hills, buildings).
        float[] skyline = Skyline(sky, lums, w, h, 20f);
        float above = horizonCut + horizonFade;

        int eBins = (int)Math.Ceiling(90f / ElevBin);
        int gBins = (int)Math.Ceiling(180f / SunBin);

        // First guess: per bin, a high percentile -- the blue end of what that bin holds.
        float[,] table = BinPercentile(blue, gamma, w, h, eBins, gBins, sunRadius, null, 0.85f, 40);
        Fill(table);
        Smooth(table, 2);

        var cloud = new float[w * h];
        for (int pass = 0; pass < 3; pass++)
        {
            Measure(blue, gamma, w, h, table, cloudS, cloud);

            // Again from the texels that came out clear: their median.
            bool[] clear = new bool[w * h];
            int clearCount = 0;
            for (int i = 0; i < clear.Length; i++)
            {
                clear[i] = cloud[i] < 0.12f;
                if (clear[i])
                    clearCount++;
            }

            float[,] next = BinPercentile(blue, gamma, w, h, eBins, gBins, sunRadius, clear, 0.5f, 25);
            // Where a bin had no clear texels the old value stays (Fill uses only the valid ones).
            for (int e = 0; e < eBins; e++)
                for (int g = 0; g < gBins; g++)
                    if (float.IsNaN(next[e, g]) && !float.IsNaN(table[e, g]))
                        next[e, g] = table[e, g];
            Fill(next);
            Smooth(next, 2);
            table = next;
            if (pass == 2)
                notes.AppendFormat("clear share {0:F0}%; ", 100.0 * clearCount / clear.Length);
        }

        Measure(blue, gamma, w, h, table, cloudS, cloud);

        // Blueness alone cannot tell a thin veil from a thick cloud: a cirrus over blue sky is bluish
        // and came out thin or solid depending on what a cloud was taken to look like. So, as a last
        // step, each texel is split into the clear sky seen through it and the cloud's own light:
        // texel = (1 - cloud) x clear sky + light x the clouds' colour. The cloud that comes out is the
        // share of the sky behind it that it hides -- the opacity the layer is drawn with. The clear
        // sky's colour AND brightness come from the clear texels, by the same two angles.
        float[] cloudColour = CloudColour(sky, cloud, gamma, skyline, w, h, sunRadius, above);
        notes.AppendFormat("cloud colour ({0:F2},{1:F2},{2:F2}); ", cloudColour[0], cloudColour[1], cloudColour[2]);
        float[] byBlue = (float[])cloud.Clone();
        if (Mode != 0)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool[] clear = new bool[w * h];
                for (int i = 0; i < clear.Length; i++)
                    clear[i] = cloud[i] < 0.1f;
                float[,] tr = SkyTable(sky.R, gamma, w, h, eBins, gBins, sunRadius, clear);
                float[,] tg = SkyTable(sky.G, gamma, w, h, eBins, gBins, sunRadius, clear);
                float[,] tb = SkyTable(sky.B, gamma, w, h, eBins, gBins, sunRadius, clear);
                Unmix(sky, gamma, w, h, tr, tg, tb, cloudColour, cloud);
            }

            if (Mode == 2)
                for (int i = 0; i < cloud.Length; i++)
                    cloud[i] = Math.Max(cloud[i], byBlue[i]);
        }

        // Inside the disc and the core of its glare: filled smoothly from round it, so it is neither
        // a hole nor a blob of one value.
        if (sunVisible)
        {
            float fill = Math.Max(sunRadius + 0.5f, sunFill);
            var hole = new bool[w * h];
            for (int i = 0; i < hole.Length; i++)
                hole[i] = gamma[i] <= fill;
            int iterations = Inpaint(cloud, hole, w, h);
            notes.AppendFormat("disc+glare {0:F1} deg, filled to {1:F1} ({2} rounds); ", sunRadius, fill, iterations);
        }

        // The rest of the glare (lens streaks, a starburst): taken for sky, coming back in to the edge.
        if (sunClear > sunRadius)
        {
            for (int i = 0; i < cloud.Length; i++)
            {
                if (gamma[i] < sunClear)
                {
                    float t = Clamp01((gamma[i] - sunRadius) / (sunClear - sunRadius));
                    cloud[i] *= t * t * (3f - 2f * t);
                }
            }
            notes.AppendFormat("glare cleared to {0:F0} deg; ", sunClear);
        }

        // The sun's rays (2026-10-13, the author: "some maps have the sun rays visible in them, which
        // looks really bad"): a starburst's streaks and its bokeh dots reach 25-40 degrees out, past any
        // circle that could be cleared without the real clouds round the sun. They are long and thin
        // ALONG the bearing round the sun, a cloud is wide: an opening (the least, then the most) across
        // that bearing takes out anything narrower than RayWidth and gives a wider cloud its shape back.
        // Only for a sky with a starburst: round a sun in a sky of small puffs it would thin the puffs.
        if (sunVisible && rays)
        {
            int opened = OpenAcrossRays(cloud, w, h, sx, sy, sz, RayReach, RayWidth);
            notes.AppendFormat("rays opened to {0:F0} deg ({1} texels); ", RayReach, opened);
        }

        // What stands over the horizon: painted over from the cloud beside it.
        float highest = 0f;
        foreach (float s in skyline)
            highest = Math.Max(highest, s);
        int painted = FillUnder(cloud, skyline, w, h, 0.4f);
        notes.AppendFormat("ground up to {0:F1} deg, {1} texels painted over; ", highest, painted);

        // The horizon: nothing below the cut, in over the fade.
        for (int y = 0; y < h; y++)
        {
            float el = ElevationOf(y, h);
            float t = Clamp01((el - horizonCut) / Math.Max(horizonFade, 0.01f));
            float keep = t * t * (3f - 2f * t);
            for (int x = 0; x < w; x++)
                cloud[y * w + x] *= keep;
        }

        // The clear-sky blueness per texel, for the check pictures.
        var clearS = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                clearS[i] = Lookup(table, ElevationOf(y, h), gamma[i]);
            }

        return new Cut
        {
            Width = w, Height = h, Cloud = cloud, ClearS = clearS, Skyline = skyline,
            SunAzimuth = sunAz, SunElevation = sunEl, SunRadius = sunRadius, CloudS = cloudS,
            Notes = notes.ToString(),
        };
    }

    private static float Clamp01(float v)
    {
        return v < 0f ? 0f : v > 1f ? 1f : v;
    }

    private static float Angle(float az1, float el1, float az2, float el2)
    {
        double ax, ay, az, bx, by, bz;
        Direction(az1, el1, out ax, out ay, out az);
        Direction(az2, el2, out bx, out by, out bz);
        return (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, ax * bx + ay * by + az * bz))) * 180.0 / Math.PI);
    }

    private static void Measure(float[] blue, float[] gamma, int w, int h, float[,] table, float cloudS, float[] cloud)
    {
        for (int y = 0; y < h; y++)
        {
            float el = ElevationOf(y, h);
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float clear = Lookup(table, el, gamma[i]);
                float span = Math.Max(clear - cloudS, 0.04f);
                cloud[i] = Clamp01((clear - blue[i]) / span);
            }
        }
    }

    // The clear sky's radiance in one channel per (elevation, sun angle) bin: the median of the clear
    // texels there, filled and smoothed in log (it climbs steeply towards the sun).
    private static float[,] SkyTable(float[] channel, float[] gamma, int w, int h, int eBins, int gBins, float sunRadius, bool[] clear)
    {
        var logs = new float[channel.Length];
        for (int i = 0; i < logs.Length; i++)
            logs[i] = (float)Math.Log(Math.Max(channel[i], 1e-6f));
        float[,] t = BinPercentile(logs, gamma, w, h, eBins, gBins, sunRadius, clear, 0.5f, 25);
        Fill(t);
        Smooth(t, 2);
        for (int e = 0; e < eBins; e++)
            for (int g = 0; g < gBins; g++)
                t[e, g] = (float)Math.Exp(t[e, g]);
        return t;
    }

    // The clouds' colour (r + g + b = 1): the mean chromaticity of the clearly cloudy texels.
    private static float[] CloudColour(Sky sky, float[] cloud, float[] gamma, float[] skyline, int w, int h, float sunRadius, float above)
    {
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = 0; y < h; y += 2)
        {
            float el = ElevationOf(y, h);
            if (el < above)
                continue;
            for (int x = 0; x < w; x += 2)
            {
                int i = y * w + x;
                if (cloud[i] < 0.7f || gamma[i] < sunRadius + 2f || el < skyline[x] + 1f)
                    continue;
                double sum = sky.R[i] + sky.G[i] + sky.B[i];
                if (sum <= 1e-9)
                    continue;
                r += sky.R[i] / sum; g += sky.G[i] / sum; b += sky.B[i] / sum;
                n++;
            }
        }
        if (n < 100)
            return new[] { 1f / 3f, 1f / 3f, 1f / 3f };
        return new[] { (float)(r / n), (float)(g / n), (float)(b / n) };
    }

    // texel - S = -cloud x S + light x W, least squares for (cloud, light) with light >= 0, a little
    // pull towards "no cloud" where the clear sky's colour is too close to the cloud's to tell.
    private static void Unmix(Sky sky, float[] gamma, int w, int h, float[,] tr, float[,] tg, float[,] tb, float[] colour, float[] cloud)
    {
        const double Ridge = 0.05;
        double wr = colour[0], wg = colour[1], wb = colour[2];
        double ww = wr * wr + wg * wg + wb * wb;
        for (int y = 0; y < h; y++)
        {
            float el = ElevationOf(y, h);
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                double sr = Lookup(tr, el, gamma[i]), sg = Lookup(tg, el, gamma[i]), sb = Lookup(tb, el, gamma[i]);
                double dr = sky.R[i] - sr, dg = sky.G[i] - sg, db = sky.B[i] - sb;
                double ss = sr * sr + sg * sg + sb * sb;
                double sw = sr * wr + sg * wg + sb * wb;
                double a11 = ss * (1.0 + Ridge), a12 = -sw, a22 = ww;
                double b1 = -(sr * dr + sg * dg + sb * db), b2 = wr * dr + wg * dg + wb * db;
                double det = a11 * a22 - a12 * a12;
                double alpha = 0.0;
                if (det > 1e-20)
                {
                    alpha = (b1 * a22 - a12 * b2) / det;
                    double light = (a11 * b2 - a12 * b1) / det;
                    if (light < 0.0)
                        alpha = b1 / a11;
                }
                cloud[i] = Clamp01((float)alpha);
            }
        }
    }

    // What clearly cloudy texels look like: the median of those over 60% cloud (a typical cloud, not
    // the pinkest), above the ground and away from the sun.
    private static float CloudBlueness(float[] blue, float[] cloud, float[] gamma, float[] skyline, int w, int h, float sunRadius, float above)
    {
        var list = new List<float>();
        for (int y = 0; y < h; y += 2)
        {
            float el = ElevationOf(y, h);
            if (el < above)
                continue;
            for (int x = 0; x < w; x += 2)
            {
                int i = y * w + x;
                if (cloud[i] > 0.6f && gamma[i] > sunRadius + 2f && el > skyline[x] + 1f)
                    list.Add(blue[i]);
            }
        }

        if (list.Count < 200)
            return 0.05f;

        list.Sort();
        return Math.Max(-0.2f, Math.Min(0.12f, list[list.Count / 2]));
    }

    /// <summary>
    /// Per column, the highest elevation (degrees) the ground reaches under <paramref name="maxElev"/>:
    /// from the horizon up, texels much darker than the sky, or green (no sky or cloud is greener than
    /// it is blue and red), until 0.6 degree of sky; then widened by 0.3 degree each way. 0 where the
    /// horizon itself is sky. "The sky" is the median of the band 10-14 degrees up: lower down, a
    /// row's median is mostly ground (distant hazy hills are ~40% of the sky there; 2026-10-12, Malagas).
    /// </summary>
    public static float[] Skyline(Sky sky, float[] lums, int w, int h, float maxElev)
    {
        int yTop = Math.Max(0, (int)((1f - maxElev / 90f) * h));
        int yBandTop = (int)((1f - 14f / 90f) * h), yBandBottom = (int)((1f - 10f / 90f) * h);
        var band = new List<float>();
        for (int y = yBandTop; y <= yBandBottom; y++)
            for (int x = 0; x < w; x += 2)
                band.Add(lums[y * w + x]);
        band.Sort();
        float reference = band[band.Count / 2];
        var rowSky = new float[h];
        for (int y = yTop; y < h; y++)
            rowSky[y] = reference;

        int gap = Math.Max(2, (int)(0.6f / 90f * h));
        var line = new float[w];
        for (int x = 0; x < w; x++)
        {
            int run = 0;
            int top = h;
            for (int y = h - 1; y >= yTop; y--)
            {
                int i = y * w + x;
                bool green = sky.G[i] > sky.B[i] * 1.02f && sky.G[i] > sky.R[i];
                bool ground = lums[i] < 0.55f * rowSky[y] || green;
                if (ground)
                {
                    top = y;
                    run = 0;
                }
                else if (++run > gap)
                {
                    break;
                }
            }
            line[x] = top >= h ? 0f : ElevationOf(top, h) + 45f / h;
        }

        int widen = Math.Max(1, (int)(0.3f / 360f * w));
        var wide = new float[w];
        for (int x = 0; x < w; x++)
        {
            float m = 0f;
            for (int k = -widen; k <= widen; k++)
                m = Math.Max(m, line[((x + k) % w + w) % w]);
            wide[x] = m;
        }
        return wide;
    }

    // Under the skyline (plus margin): each row's gap filled from the cloud either side of it,
    // mirrored and blended across (cloud texture, not a smear); what a mirror cannot fill (a gap wider
    // than a quarter turn, or one landing in another gap) is filled smoothly from round it (Inpaint).
    // Returns how many texels were painted.
    private static int FillUnder(float[] cloud, float[] skyline, int w, int h, float margin)
    {
        int painted = 0;
        var hole = new bool[w];
        var row = new float[w];
        var left = new bool[w * h];
        bool anyLeft = false;
        for (int y = 0; y < h; y++)
        {
            float el = ElevationOf(y, h);
            int holes = 0;
            for (int x = 0; x < w; x++)
            {
                hole[x] = skyline[x] > 0f && el < skyline[x] + margin;
                if (hole[x])
                    holes++;
            }

            if (holes == 0)
                continue;

            painted += holes;
            int o = y * w;
            if (holes == w)
            {
                for (int x = 0; x < w; x++)
                    left[o + x] = true;
                anyLeft = true;
                continue;
            }

            Array.Copy(cloud, o, row, 0, w);
            int start = 0;
            while (hole[start])
                start++;

            // Walk the row once from a texel that is sky, finding each gap.
            int k = 0;
            while (k < w)
            {
                int x = (start + k) % w;
                if (!hole[x])
                {
                    k++;
                    continue;
                }

                int n = 0;
                while (k + n < w && hole[(start + k + n) % w])
                    n++;

                int leftEdge = (start + k - 1 + w) % w;
                int rightEdge = (start + k + n) % w;
                for (int j = 0; j < n; j++)
                {
                    int xx = (start + k + j) % w;
                    int lx = ((leftEdge - j) % w + w) % w;
                    int rx = (rightEdge + (n - 1 - j)) % w;
                    if (n > w / 4 || hole[lx] || hole[rx])
                    {
                        left[o + xx] = true;
                        anyLeft = true;
                    }
                    else
                    {
                        float t = (j + 1f) / (n + 1f);
                        cloud[o + xx] = row[lx] + (row[rx] - row[lx]) * t;
                    }
                }

                k += n;
            }
        }

        if (anyLeft)
            Inpaint(cloud, left, w, h);
        return painted;
    }

    // Laplace fill of the holes from their edges (successive over-relaxation, in the holes' box).
    /// <summary>How far round the sun its rays are looked for (degrees); faded back to the cut over the last 10.</summary>
    public static float RayReach = 50f;

    /// <summary>Anything narrower than this across the bearing round the sun (degrees of arc) is a ray.</summary>
    public static float RayWidth = 3f;

    /// <summary>
    /// A morphological opening of <paramref name="cloud"/> across the bearing round the sun (s): each
    /// texel within <paramref name="reach"/> takes the least of its neighbours turned round the sun axis
    /// by up to half <paramref name="width"/> of arc either way, then the most of those. Returns how many
    /// texels it lowered.
    /// </summary>
    private static int OpenAcrossRays(float[] cloud, int w, int h, double sx, double sy, double sz,
        float reach, float width)
    {
        const int Steps = 6;
        float cosReach = (float)Math.Cos(reach * Math.PI / 180.0);
        var eroded = (float[])cloud.Clone();
        var inside = new bool[w * h];

        for (int pass = 0; pass < 2; pass++)
        {
            float[] src = pass == 0 ? cloud : eroded;
            var dst = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                float el = ElevationOf(y, h);
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    double dx, dy, dz;
                    Direction(AzimuthOf(x, w), el, out dx, out dy, out dz);
                    double c = dx * sx + dy * sy + dz * sz;
                    if (c < cosReach)
                    {
                        dst[i] = src[i];
                        continue;
                    }
                    inside[i] = true;

                    // The texel's angle from the sun; the bearing turn that spans half the width of arc.
                    double gamma = Math.Acos(Math.Min(1.0, c));
                    double half = Math.Min(Math.PI / 4.0, (width * 0.5 * Math.PI / 180.0) / Math.Max(Math.Sin(gamma), 0.02));
                    float best = src[i];
                    for (int k = -Steps; k <= Steps; k++)
                    {
                        if (k == 0)
                            continue;
                        double a = half * k / Steps;
                        double rx, ry, rz;
                        Rotate(dx, dy, dz, sx, sy, sz, a, out rx, out ry, out rz);
                        if (ry <= 0.0)
                            continue;
                        int j = TexelOf(rx, ry, rz, w, h);
                        best = pass == 0 ? Math.Min(best, src[j]) : Math.Max(best, src[j]);
                    }
                    dst[i] = best;
                }
            }
            eroded = dst;   // after the second pass: the opened field
        }

        // Only the faint is taken (rays are faint, 0.1-0.25; a cloud's body is bright, and opened it came
        // out smeared round the sun in bands), back to the cut over the last 10 degrees of the reach.
        int lowered = 0;
        for (int y = 0; y < h; y++)
        {
            float el = ElevationOf(y, h);
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!inside[i])
                    continue;
                double dx, dy, dz;
                Direction(AzimuthOf(x, w), el, out dx, out dy, out dz);
                float gamma = (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, dx * sx + dy * sy + dz * sz))) * 180.0 / Math.PI);
                float t = Clamp01((reach - gamma) / 10f);
                float f = Clamp01((0.5f - cloud[i]) / 0.25f);
                float v = cloud[i] + (Math.Min(eroded[i], cloud[i]) - cloud[i]) * (t * t * (3f - 2f * t)) * (f * f * (3f - 2f * f));
                if (v < cloud[i] - 0.01f)
                    lowered++;
                cloud[i] = v;
            }
        }

        // The glare's veil: the same all round the sun, so per ring (half a degree from the sun) its
        // median is the veil there, taken off (a cloud keeps all but that), back over the last 10 degrees.
        const float Bin = 0.5f;
        int bins = (int)Math.Ceiling(reach / Bin);
        var rings = new List<float>[bins];
        var ringOf = new int[w * h];
        for (int i = 0; i < ringOf.Length; i++)
            ringOf[i] = -1;
        for (int y = 0; y < h; y++)
        {
            float el = ElevationOf(y, h);
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!inside[i])
                    continue;
                double dx, dy, dz;
                Direction(AzimuthOf(x, w), el, out dx, out dy, out dz);
                float gamma = (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, dx * sx + dy * sy + dz * sz))) * 180.0 / Math.PI);
                int b = Math.Min(bins - 1, (int)(gamma / Bin));
                ringOf[i] = b;
                if (rings[b] == null)
                    rings[b] = new List<float>();
                rings[b].Add(cloud[i]);
            }
        }
        var veil = new float[bins];
        for (int b = 0; b < bins; b++)
        {
            if (rings[b] == null || rings[b].Count == 0)
                continue;
            rings[b].Sort();
            float t = Clamp01((reach - (b + 0.5f) * Bin) / 10f);
            veil[b] = rings[b][rings[b].Count / 2] * t * t * (3f - 2f * t);
        }
        for (int i = 0; i < cloud.Length; i++)
            if (ringOf[i] >= 0)
                cloud[i] = Math.Max(0f, cloud[i] - veil[ringOf[i]]);

        return lowered;
    }

    /// <summary>(d) turned by <paramref name="angle"/> radians round the unit axis (a) (Rodrigues).</summary>
    private static void Rotate(double dx, double dy, double dz, double ax, double ay, double az, double angle,
        out double rx, out double ry, out double rz)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle), dot = dx * ax + dy * ay + dz * az;
        double cx = ay * dz - az * dy, cy = az * dx - ax * dz, cz = ax * dy - ay * dx;
        rx = dx * c + cx * s + ax * dot * (1 - c);
        ry = dy * c + cy * s + ay * dot * (1 - c);
        rz = dz * c + cz * s + az * dot * (1 - c);
    }

    /// <summary>The texel a direction above the horizon falls in (the inverse of AzimuthOf/ElevationOf).</summary>
    private static int TexelOf(double dx, double dy, double dz, int w, int h)
    {
        double az = Math.Atan2(dz, dx) * 180.0 / Math.PI;
        if (az < 0)
            az += 360.0;
        double el = Math.Asin(Math.Max(-1.0, Math.Min(1.0, dy))) * 180.0 / Math.PI;
        int x = (int)(az / 360.0 * w);
        int y = (int)((1.0 - el / 90.0) * h);
        x = ((x % w) + w) % w;
        y = Math.Max(0, Math.Min(h - 1, y));
        return y * w + x;
    }

    private static int Inpaint(float[] cloud, bool[] hole, int w, int h)
    {
        int x0 = w, x1 = -1, y0 = h, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (hole[y * w + x])
                {
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                    y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
        if (x1 < 0)
            return 0;

        // The disc can straddle column 0: then the box is the whole width.
        double sum = 0;
        int count = 0;
        for (int y = Math.Max(0, y0 - 1); y <= Math.Min(h - 1, y1 + 1); y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!hole[i] && ((x >= x0 - 1 && x <= x1 + 1) || x1 - x0 > w / 2))
                {
                    bool edge = (x > 0 && hole[i - 1]) || (x < w - 1 && hole[i + 1]) || (y > 0 && hole[i - w]) || (y < h - 1 && hole[i + w]);
                    if (edge)
                    {
                        sum += cloud[i];
                        count++;
                    }
                }
            }
        float start = count > 0 ? (float)(sum / count) : 0f;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (hole[y * w + x])
                    cloud[y * w + x] = start;

        int rounds = 0;
        for (; rounds < 4000; rounds++)
        {
            double change = 0;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * w + x;
                    if (!hole[i])
                        continue;
                    float l = cloud[y * w + (x + w - 1) % w];
                    float r = cloud[y * w + (x + 1) % w];
                    float u = y > 0 ? cloud[i - w] : cloud[i];
                    float d = y < h - 1 ? cloud[i + w] : cloud[i];
                    float target = 0.25f * (l + r + u + d);
                    float next = cloud[i] + 1.9f * (target - cloud[i]);
                    change = Math.Max(change, Math.Abs(next - cloud[i]));
                    cloud[i] = next;
                }
            }
            if (change < 1e-4)
                break;
        }

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                cloud[y * w + x] = Clamp01(cloud[y * w + x]);
        return rounds;
    }

    // Bilinear in the table.
    private static float Lookup(float[,] table, float el, float gamma)
    {
        int eBins = table.GetLength(0), gBins = table.GetLength(1);
        float fe = Math.Max(0f, Math.Min(eBins - 1.001f, el / ElevBin - 0.5f));
        float fg = Math.Max(0f, Math.Min(gBins - 1.001f, gamma / SunBin - 0.5f));
        int e0 = (int)fe, g0 = (int)fg;
        float te = fe - e0, tg = fg - g0;
        float a = table[e0, g0] + (table[e0, g0 + 1] - table[e0, g0]) * tg;
        float b = table[e0 + 1, g0] + (table[e0 + 1, g0 + 1] - table[e0 + 1, g0]) * tg;
        return a + (b - a) * te;
    }

    // The q-quantile of blueness per (elevation, sun angle) bin, over the texels `use` allows
    // (all when null), outside the sun's disc; NaN where fewer than minCount.
    private static float[,] BinPercentile(float[] blue, float[] gamma, int w, int h, int eBins, int gBins, float sunRadius,
                                          bool[] use, float q, int minCount)
    {
        var lists = new List<float>[eBins, gBins];
        for (int y = 0; y < h; y += 2)
        {
            float el = ElevationOf(y, h);
            int e = Math.Min(eBins - 1, (int)(el / ElevBin));
            for (int x = 0; x < w; x += 2)
            {
                int i = y * w + x;
                if (gamma[i] <= sunRadius + 1f)
                    continue;
                if (use != null && !use[i])
                    continue;
                int g = Math.Min(gBins - 1, (int)(gamma[i] / SunBin));
                if (lists[e, g] == null)
                    lists[e, g] = new List<float>();
                lists[e, g].Add(blue[i]);
            }
        }

        var table = new float[eBins, gBins];
        for (int e = 0; e < eBins; e++)
        {
            for (int g = 0; g < gBins; g++)
            {
                List<float> list = lists[e, g];
                if (list == null || list.Count < minCount)
                {
                    table[e, g] = float.NaN;
                    continue;
                }
                list.Sort();
                table[e, g] = list[Math.Min(list.Count - 1, (int)(list.Count * q))];
            }
        }
        return table;
    }

    // NaN bins take the average of their valid neighbours, growing outwards until none is left.
    private static void Fill(float[,] t)
    {
        int n0 = t.GetLength(0), n1 = t.GetLength(1);
        bool any = false;
        for (int a = 0; a < n0 && !any; a++)
            for (int b = 0; b < n1; b++)
                if (!float.IsNaN(t[a, b])) { any = true; break; }
        if (!any)
        {
            for (int a = 0; a < n0; a++)
                for (int b = 0; b < n1; b++)
                    t[a, b] = 0.4f;
            return;
        }

        for (int round = 0; round < n0 + n1; round++)
        {
            bool changed = false;
            var copy = (float[,])t.Clone();
            for (int a = 0; a < n0; a++)
            {
                for (int b = 0; b < n1; b++)
                {
                    if (!float.IsNaN(copy[a, b]))
                        continue;
                    double sum = 0;
                    int count = 0;
                    for (int da = -1; da <= 1; da++)
                        for (int db = -1; db <= 1; db++)
                        {
                            int aa = a + da, bb = b + db;
                            if (aa < 0 || aa >= n0 || bb < 0 || bb >= n1 || float.IsNaN(copy[aa, bb]))
                                continue;
                            sum += copy[aa, bb];
                            count++;
                        }
                    if (count > 0)
                    {
                        t[a, b] = (float)(sum / count);
                        changed = true;
                    }
                }
            }
            if (!changed)
                break;
        }
    }

    private static void Smooth(float[,] t, int rounds)
    {
        int n0 = t.GetLength(0), n1 = t.GetLength(1);
        for (int r = 0; r < rounds; r++)
        {
            var copy = (float[,])t.Clone();
            for (int a = 0; a < n0; a++)
                for (int b = 0; b < n1; b++)
                {
                    double sum = 0;
                    int count = 0;
                    for (int da = -1; da <= 1; da++)
                        for (int db = -1; db <= 1; db++)
                        {
                            int aa = a + da, bb = b + db;
                            if (aa < 0 || aa >= n0 || bb < 0 || bb >= n1)
                                continue;
                            sum += copy[aa, bb];
                            count++;
                        }
                    t[a, b] = (float)(sum / count);
                }
        }
    }

    // ---- output ----------------------------------------------------------------------------------

    /// <summary>The cloud box-filtered down to outW x outH (whole multiples of it).</summary>
    public static float[] Downsample(float[] src, int w, int h, int outW, int outH)
    {
        int fx = w / outW, fy = h / outH;
        var dst = new float[outW * outH];
        for (int y = 0; y < outH; y++)
            for (int x = 0; x < outW; x++)
            {
                double sum = 0;
                for (int j = 0; j < fy; j++)
                    for (int i = 0; i < fx; i++)
                        sum += src[(y * fy + j) * w + x * fx + i];
                dst[y * outW + x] = (float)(sum / (fx * fy));
            }
        return dst;
    }

    /// <summary>An 8-bit grayscale PNG (palette of 256 greys), rows as given.</summary>
    public static long SaveGray(float[] values, int w, int h, string path)
    {
        using (var bmp = new Bitmap(w, h, PixelFormat.Format8bppIndexed))
        {
            ColorPalette pal = bmp.Palette;
            for (int i = 0; i < 256; i++)
                pal.Entries[i] = Color.FromArgb(i, i, i);
            bmp.Palette = pal;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            var row = new byte[d.Stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    row[y * d.Stride + x] = (byte)Math.Round(255f * Clamp01(values[y * w + x]));
            Marshal.Copy(row, 0, d.Scan0, row.Length);
            bmp.UnlockBits(d);
            bmp.Save(path, ImageFormat.Png);
        }
        return new FileInfo(path).Length;
    }

    /// <summary>The same values as a JPEG (grey in RGB), for its size.</summary>
    public static long SaveGrayJpeg(float[] values, int w, int h, string path, long quality)
    {
        using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
        {
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var row = new byte[d.Stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte v = (byte)Math.Round(255f * Clamp01(values[y * w + x]));
                    int o = y * d.Stride + x * 3;
                    row[o] = v; row[o + 1] = v; row[o + 2] = v;
                }
            Marshal.Copy(row, 0, d.Scan0, row.Length);
            bmp.UnlockBits(d);
            ImageCodecInfo jpg = null;
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                if (c.MimeType == "image/jpeg")
                    jpg = c;
            var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            bmp.Save(path, jpg, p);
        }
        return new FileInfo(path).Length;
    }

    /// <summary>
    /// The check picture: the photo (exposed on its median sky), the cut (white = cloud), and the cut
    /// laid as white cloud over its own clear sky, one above the other, each outW x outH.
    /// </summary>
    public static void SaveCheck(Sky sky, Cut cut, int outW, int outH, string path)
    {
        int w = sky.Width, h = sky.Height;
        int fx = w / outW, fy = h / outH;
        float[] lums = new float[w * h];
        for (int i = 0; i < lums.Length; i++)
            lums[i] = Lum(sky, i);
        float[] sorted = (float[])lums.Clone();
        Array.Sort(sorted);
        float exposure = 0.5f / Math.Max(sorted[sorted.Length / 2], 1e-9f);

        using (var bmp = new Bitmap(outW, outH * 3, PixelFormat.Format24bppRgb))
        {
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, outW, outH * 3), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var data = new byte[d.Stride * outH * 3];
            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    double r = 0, g = 0, b = 0, c = 0;
                    for (int j = 0; j < fy; j++)
                        for (int i = 0; i < fx; i++)
                        {
                            int k = (y * fy + j) * w + x * fx + i;
                            r += sky.R[k]; g += sky.G[k]; b += sky.B[k]; c += cut.Cloud[k];
                        }
                    double n = fx * fy;
                    r /= n; g /= n; b /= n; c /= n;
                    Put(data, d.Stride, x, y, Tone(r * exposure), Tone(g * exposure), Tone(b * exposure));

                    // Where the ground was found to stand: a red line on the photo.
                    if (cut.Skyline != null)
                    {
                        float line = 0f;
                        for (int i = 0; i < fx; i++)
                            line = Math.Max(line, cut.Skyline[x * fx + i]);
                        float top = ElevationOf(y * fy, h), bottom = ElevationOf(y * fy + fy - 1, h);
                        if (line > 0f && line <= top + 0.2f && line >= bottom - 0.2f)
                            Put(data, d.Stride, x, y, 255, 0, 0);
                    }
                    byte v = (byte)Math.Round(255 * Clamp01((float)c));
                    Put(data, d.Stride, x, y + outH, v, v, v);

                    // A plain sky (deep blue up, paler down) with white cloud over it.
                    float el = ElevationOf(y * fy, h) / 90f;
                    double sr = 0.25 + 0.35 * (1 - el), sg = 0.45 + 0.25 * (1 - el), sb = 0.9;
                    Put(data, d.Stride, x, y + 2 * outH, Tone(sr + (1.0 - sr) * c), Tone(sg + (1.0 - sg) * c), Tone(sb + (0.95 - sb) * c));
                }
            }
            Marshal.Copy(data, 0, d.Scan0, data.Length);
            bmp.UnlockBits(d);
            bmp.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>
    /// A camera's view of a stored picture (rows from straight up to the horizon, bilinear, wrapping
    /// round), white cloud over a plain blue sky: to see how sharp a size looks on screen.
    /// </summary>
    public static void SaveView(float[] map, int w, int h, int outW, int outH, float yawDeg, float pitchDeg, float vfovDeg, string path)
    {
        double yaw = yawDeg * Math.PI / 180.0, pitch = pitchDeg * Math.PI / 180.0;
        double tanHalf = Math.Tan(vfovDeg * 0.5 * Math.PI / 180.0);
        double aspect = (double)outW / outH;
        using (var bmp = new Bitmap(outW, outH, PixelFormat.Format24bppRgb))
        {
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, outW, outH), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var data = new byte[d.Stride * outH];
            for (int py = 0; py < outH; py++)
            {
                for (int px = 0; px < outW; px++)
                {
                    // Camera space: x right, y up, z forward; pitched up, then turned.
                    double cx = (2.0 * (px + 0.5) / outW - 1.0) * tanHalf * aspect;
                    double cy = (1.0 - 2.0 * (py + 0.5) / outH) * tanHalf;
                    double cz = 1.0;
                    double y1 = cy * Math.Cos(pitch) + cz * Math.Sin(pitch);
                    double z1 = -cy * Math.Sin(pitch) + cz * Math.Cos(pitch);
                    double x2 = cx * Math.Cos(yaw) + z1 * Math.Sin(yaw);
                    double z2 = -cx * Math.Sin(yaw) + z1 * Math.Cos(yaw);
                    double len = Math.Sqrt(x2 * x2 + y1 * y1 + z2 * z2);
                    double el = Math.Asin(y1 / len) * 180.0 / Math.PI;
                    double az = Math.Atan2(z2, x2) * 180.0 / Math.PI;
                    if (az < 0) az += 360.0;

                    double c = 0;
                    if (el > 0)
                    {
                        double fx = az / 360.0 * w - 0.5;
                        double fy = (1.0 - el / 90.0) * h - 0.5;
                        int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
                        double tx = fx - ix, ty = fy - iy;
                        double a = Sample(map, w, h, ix, iy) * (1 - tx) + Sample(map, w, h, ix + 1, iy) * tx;
                        double b = Sample(map, w, h, ix, iy + 1) * (1 - tx) + Sample(map, w, h, ix + 1, iy + 1) * tx;
                        c = a * (1 - ty) + b * ty;
                    }

                    double e = Math.Max(0, el) / 90.0;
                    double sr = 0.25 + 0.35 * (1 - e), sg = 0.45 + 0.25 * (1 - e), sb = 0.9;
                    if (el <= 0) { sr = 0.18; sg = 0.2; sb = 0.16; }
                    Put(data, d.Stride, px, py, Tone(sr + (1.0 - sr) * c), Tone(sg + (1.0 - sg) * c), Tone(sb + (0.95 - sb) * c));
                }
            }
            Marshal.Copy(data, 0, d.Scan0, data.Length);
            bmp.UnlockBits(d);
            bmp.Save(path, ImageFormat.Png);
        }
    }

    private static double Sample(float[] map, int w, int h, int x, int y)
    {
        x = ((x % w) + w) % w;
        y = Math.Max(0, Math.Min(h - 1, y));
        return map[y * w + x];
    }

    private static byte Tone(double v)
    {
        double t = v / (1.0 + v) * 1.6;
        return (byte)Math.Round(255 * Math.Pow(Math.Max(0.0, Math.Min(1.0, t)), 1.0 / 2.2));
    }

    private static void Put(byte[] data, int stride, int x, int y, byte r, byte g, byte b)
    {
        int o = y * stride + x * 3;
        data[o] = b; data[o + 1] = g; data[o + 2] = r;
    }
}
