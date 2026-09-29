using System;
using System.Text;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// THE RAINBOW'S COLOURS (1.3.0): how much of the sun the rain sends back at each angle from the
    /// point opposite the sun, per colour, as a PHASE FUNCTION (x isotropic scattering) -- what the
    /// rain march multiplies the direct sun by (CloudRaymarch.shader, <see cref="Rainbow"/>).
    /// </summary>
    /// <remarks>
    /// Airy's theory of the rainbow (1838) per wavelength and drop size: near Descartes' ray the
    /// wavefront leaving a drop is cubic, and its far field is Ai^2 -- scaled so that away from the
    /// bow it averages to geometric optics (two rays, dsigma/dOmega = a^2 b |db/dTheta| T / sin
    /// Theta). The primary (one reflection inside the drop) and the secondary (two), Fresnel's share
    /// for each, over Marshall-Palmer drops at 8 mm/h, over the visible wavelengths through the CIE
    /// 1931 observer into linear sRGB (white-balanced: a grey scatterer gives exactly grey), smeared
    /// by the sun's 0.53 degree disc. So: violet at 40.6 degrees, red at 42.5, a brighter sky inside,
    /// Alexander's dark band, and the fainter secondary at 50-53.5 with its colours reversed. Chosen
    /// on the offline preview (docs/previews/render-rainbow.ps1, row B: "B is perfect!").
    ///
    /// Pure managed maths, the same for every city: made once per session on a city load's worker
    /// thread (~0.2 s), like the blue noise. tools/test-rainbow.ps1.
    /// </remarks>
    public static class RainbowTable
    {
        /// <summary>Degrees from the antisolar point the table covers; beyond it, nothing.</summary>
        public const float MaxAngle = 64f;

        /// <summary>Entries across the table (0.125 degrees each).</summary>
        public const int Size = 512;

        /// <summary>
        /// The texture's rows: R, G and B, each value in every channel of its texel (the shader
        /// reads alpha, never sRGB-decoded), and a fourth, empty, for a power-of-two height.
        /// </summary>
        public const int Rows = 4;

        /// <summary>The rain the table is made for, mm/h: drops of mostly 0.3-0.5 mm radius.</summary>
        public const double RainRate = 8.0;

        private const int Fine = 2048;              // working resolution (0.03125 degrees)
        private const double SunRadius = 0.2665;    // degrees

        private static readonly object Gate = new object();
        private static byte[] _bytes;
        private static float _scale;
        private static int _milliseconds;

        /// <summary>
        /// The texture, row by row (<see cref="Size"/> x <see cref="Rows"/>), one byte a texel: the
        /// value / <see cref="Scale"/> x 255. Made the first time it is asked for (a city load's
        /// worker thread), then kept. Throws if it cannot be made.
        /// </summary>
        public static byte[] Bytes
        {
            get
            {
                lock (Gate)
                {
                    if (_bytes == null)
                    {
                        DateTime start = DateTime.UtcNow;
                        double[] radii, weights;
                        RainDrops(RainRate, 24, out radii, out weights);
                        float scale;
                        byte[] bytes = Encode(Build(radii, weights, Size, true), out scale);
                        _scale = scale;
                        _milliseconds = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                        _bytes = bytes;
                    }

                    return _bytes;
                }
            }
        }

        /// <summary>What a byte of 255 stands for (the table's largest value). 0 until made.</summary>
        public static float Scale
        {
            get { lock (Gate) return _scale; }
        }

        /// <summary>How long the table took to make, for the log.</summary>
        public static int Milliseconds
        {
            get { lock (Gate) return _milliseconds; }
        }

        /// <summary>The table as the texture's bytes, 255 = its largest value (returned in <paramref name="scale"/>).</summary>
        public static byte[] Encode(float[] table, out float scale)
        {
            int size = table.Length / 3;
            scale = 0f;
            foreach (float v in table)
                scale = Math.Max(scale, v);
            if (scale <= 0f)
                throw new InvalidOperationException("the rainbow table came out empty");

            byte[] bytes = new byte[size * Rows];
            for (int c = 0; c < 3; c++)
                for (int e = 0; e < size; e++)
                    bytes[c * size + e] = (byte)Math.Round(Math.Max(0f, Math.Min(1f, table[3 * e + c] / scale)) * 255f);
            return bytes;
        }

        /// <summary>
        /// Ai(x): its power series near 0, the asymptotic forms beyond |x| = 5 (each to its first
        /// correction; they meet the series to ~1e-3 there).
        /// </summary>
        public static double Airy(double x)
        {
            if (x > 5.0)
            {
                double z = 2.0 / 3.0 * x * Math.Sqrt(x);
                return Math.Exp(-z) / (2.0 * Math.Sqrt(Math.PI) * Math.Pow(x, 0.25)) * (1.0 - 5.0 / (72.0 * z));
            }

            if (x < -5.0)
            {
                double zz = -x, z = 2.0 / 3.0 * zz * Math.Sqrt(zz), ph = z + Math.PI / 4.0;
                return (Math.Sin(ph) - 5.0 / (72.0 * z) * Math.Cos(ph)) / (Math.Sqrt(Math.PI) * Math.Pow(zz, 0.25));
            }

            double x3 = x * x * x, f = 1.0, g = x, tf = 1.0, tg = x;
            for (int k = 1; k < 80; k++)
            {
                tf *= x3 / ((3.0 * k - 1.0) * (3.0 * k));
                tg *= x3 / ((3.0 * k) * (3.0 * k + 1.0));
                f += tf;
                g += tg;
                if (Math.Abs(tf) + Math.Abs(tg) < 1e-18)
                    break;
            }

            return 0.355028053887817239 * f - 0.258819403792806798 * g;
        }

        /// <summary>Water's refractive index, a Cauchy fit (1.3435 at 400 nm, 1.3328 at 589, 1.3302 at 700).</summary>
        public static double WaterIndex(double nm)
        {
            return 1.324 + 3046.0 / (nm * nm);
        }

        /// <summary>
        /// Angle from the antisolar point (radians) of the ray that entered a drop at impact
        /// parameter <paramref name="b"/> (0..1) and left after <paramref name="k"/> reflections inside.
        /// </summary>
        public static double Psi(double b, double n, int k)
        {
            double i = Math.Asin(b), r = Math.Asin(b / n);
            double d = 2.0 * (i - r) + k * (Math.PI - 2.0 * r);
            d %= 2.0 * Math.PI;
            if (d < 0.0)
                d += 2.0 * Math.PI;
            double theta = d <= Math.PI ? d : 2.0 * Math.PI - d;
            return Math.PI - theta;
        }

        /// <summary>Fresnel: the share of unpolarised light that enters, reflects k times inside and leaves.</summary>
        public static double Transmission(double b, double n, int k)
        {
            double ci = Math.Sqrt(1.0 - b * b), sr = b / n, cr = Math.Sqrt(1.0 - sr * sr);
            double rs = (ci - n * cr) / (ci + n * cr);
            rs *= rs;
            double rp = (n * ci - cr) / (n * ci + cr);
            rp *= rp;
            return 0.5 * ((1.0 - rs) * (1.0 - rs) * Math.Pow(rs, k) + (1.0 - rp) * (1.0 - rp) * Math.Pow(rp, k));
        }

        /// <summary>
        /// Descartes' ray for k reflections: its impact parameter, its angle from the antisolar
        /// point (radians) and how that angle curves there (d2psi/db2).
        /// </summary>
        public static void BowRay(double n, int k, out double b, out double psi, out double curve)
        {
            b = Math.Sqrt(1.0 - (n * n - 1.0) / (k * (k + 2.0)));
            psi = Psi(b, n, k);
            const double h = 1e-4;
            curve = (Psi(b + h, n, k) - 2.0 * psi + Psi(b - h, n, k)) / (h * h);
        }

        private static double G(double x, double mu, double s1, double s2)
        {
            double t = (x - mu) / (x < mu ? s1 : s2);
            return Math.Exp(-0.5 * t * t);
        }

        /// <summary>The CIE 1931 2-degree observer, Wyman, Sloan and Shirley's multi-lobe fit (JCGT 2013).</summary>
        public static void Xyz(double nm, out double x, out double y, out double z)
        {
            x = 1.056 * G(nm, 599.8, 37.9, 31.0) + 0.362 * G(nm, 442.0, 16.0, 26.7) - 0.065 * G(nm, 501.1, 20.4, 26.2);
            y = 0.821 * G(nm, 568.8, 46.9, 40.5) + 0.286 * G(nm, 530.9, 16.3, 31.1);
            z = 1.217 * G(nm, 437.0, 11.8, 36.0) + 0.681 * G(nm, 459.0, 26.0, 13.8);
        }

        /// <summary>
        /// Rain: Marshall-Palmer drops, N(D) ~ exp(-4.1 R^-0.21 D) (D in mm, R in mm/h), radii
        /// 0.05..1.25 mm (bigger drops flatten and make no bow), weighted by number x cross-section.
        /// </summary>
        public static void RainDrops(double mmPerHour, int count, out double[] radiiMm, out double[] weights)
        {
            double lambda = 4.1 * Math.Pow(mmPerHour, -0.21);
            radiiMm = new double[count];
            weights = new double[count];
            double lo = Math.Log(0.05), hi = Math.Log(1.25), dl = (hi - lo) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                double a = Math.Exp(lo + dl * i);
                radiiMm[i] = a;
                weights[i] = Math.Exp(-lambda * 2.0 * a) * a * a * a * dl;   // N x a^2 x da (da = a dl)
            }
        }

        /// <summary>The table: <paramref name="size"/> entries of RGB, entry j at (j + 0.5) x MaxAngle / size degrees.</summary>
        public static float[] Build(double[] radiiMm, double[] weights, int size, bool secondary)
        {
            if (size <= 0 || Fine % size != 0)
                throw new ArgumentException("the size must divide " + Fine, "size");

            double dpsi = MaxAngle / (double)Fine * Math.PI / 180.0;
            double[][] acc = { new double[Fine], new double[Fine], new double[Fine] };

            // Each wavelength's share of R, G and B, balanced so that a flat spectrum is exactly grey.
            const int wavelengths = 71;
            double[][] rgb = new double[wavelengths][];
            double[] sum = new double[3];
            for (int l = 0; l < wavelengths; l++)
            {
                double x, y, z;
                Xyz(380.0 + 5.0 * l, out x, out y, out z);
                rgb[l] = new[]
                {
                    3.2406 * x - 1.5372 * y - 0.4986 * z,
                    -0.9689 * x + 1.8758 * y + 0.0415 * z,
                    0.0557 * x - 0.2040 * y + 1.0570 * z,
                };
                for (int c = 0; c < 3; c++)
                    sum[c] += rgb[l][c];
            }

            double total = 0.0;
            foreach (double w in weights)
                total += w;

            for (int l = 0; l < wavelengths; l++)
            {
                double nm = 380.0 + 5.0 * l, n = WaterIndex(nm);
                for (int k = 1; k <= (secondary ? 2 : 1); k++)
                {
                    double bR, psiR, curve;
                    BowRay(n, k, out bR, out psiR, out curve);
                    double sign = curve < 0.0 ? -1.0 : 1.0, bend = Math.Abs(curve);
                    double transmission = Transmission(bR, n, k), sinTheta = Math.Sin(Math.PI - psiR);

                    for (int a = 0; a < radiiMm.Length; a++)
                    {
                        double beta = 2.0 * Math.PI * radiiMm[a] * 1e6 / nm;
                        double s = Math.Pow(2.0 * beta * beta / bend, 1.0 / 3.0);
                        double scale = 4.0 * Math.PI * bR * transmission * Math.Sqrt(2.0 * s / bend) / sinTheta * weights[a] / total;

                        for (int j = 0; j < Fine; j++)
                        {
                            double x = sign * (psiR - (j + 0.5) * dpsi) * s;
                            if (x > 9.0)
                                continue;

                            // Where Ai^2 swings faster than 3 cells, its mean -- what the sun's disc
                            // and the spread of drop sizes leave of it anyway: 1 / (2 pi sqrt(-x)).
                            double v;
                            if (x < -5.0 && Math.PI / (Math.Sqrt(-x) * s) < 3.0 * dpsi)
                            {
                                v = 1.0 / (2.0 * Math.PI * Math.Sqrt(-x));
                            }
                            else
                            {
                                double ai = Airy(x);
                                v = ai * ai;
                            }

                            double p = scale * v;
                            for (int c = 0; c < 3; c++)
                                acc[c][j] += p * rgb[l][c] / sum[c];
                        }
                    }
                }
            }

            // The sun's disc (a chord-length kernel).
            double cell = MaxAngle / (double)Fine;
            int radius = (int)Math.Ceiling(SunRadius / cell);
            double[] kernel = new double[2 * radius + 1];
            double kernelSum = 0.0;
            for (int q = -radius; q <= radius; q++)
            {
                double u = q * cell / SunRadius;
                kernel[q + radius] = u * u < 1.0 ? Math.Sqrt(1.0 - u * u) : 0.0;
                kernelSum += kernel[q + radius];
            }

            double[][] smooth = { new double[Fine], new double[Fine], new double[Fine] };
            for (int c = 0; c < 3; c++)
                for (int j = 0; j < Fine; j++)
                {
                    double v = 0.0;
                    for (int q = -radius; q <= radius; q++)
                        v += acc[c][Math.Max(0, Math.Min(Fine - 1, j + q))] * kernel[q + radius];
                    smooth[c][j] = v / kernelSum;
                }

            // Colours outside the sRGB gamut pulled towards their own luminance (a rainbow's
            // spectral colours are purer than any screen), then a fade over the last 6 degrees.
            for (int j = 0; j < Fine; j++)
            {
                double r = smooth[0][j], g = smooth[1][j], b = smooth[2][j];
                double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                double lowest = Math.Min(r, Math.Min(g, b));
                if (lowest < 0.0)
                {
                    double t = luminance > 0.0 ? luminance / (luminance - lowest) : 0.0;
                    r = luminance + (r - luminance) * t;
                    g = luminance + (g - luminance) * t;
                    b = luminance + (b - luminance) * t;
                }

                double degrees = (j + 0.5) * cell;
                double edge = Math.Min(1.0, Math.Max(0.0, (MaxAngle - degrees) / 6.0));
                edge = edge * edge * (3.0 - 2.0 * edge);

                // The pull lands the lowest channel on 0 give or take a rounding (-1e-21 seen).
                smooth[0][j] = Math.Max(0.0, r) * edge;
                smooth[1][j] = Math.Max(0.0, g) * edge;
                smooth[2][j] = Math.Max(0.0, b) * edge;
            }

            // Box-averaged down to the table's size.
            float[] table = new float[3 * size];
            int per = Fine / size;
            for (int e = 0; e < size; e++)
                for (int c = 0; c < 3; c++)
                {
                    double v = 0.0;
                    for (int q = 0; q < per; q++)
                        v += smooth[c][e * per + q];
                    table[3 * e + c] = (float)(v / per);
                }

            return table;
        }

        /// <summary>
        /// One line for the log and the test: where each colour peaks in the primary and the
        /// secondary, how dark Alexander's band gets, how bright the inside of the bow is.
        /// </summary>
        public static string Describe(float[] table)
        {
            int size = table.Length / 3;
            StringBuilder text = new StringBuilder();
            string[] names = { "R", "G", "B" };
            text.Append("primary");
            for (int c = 0; c < 3; c++)
            {
                float at, value;
                Peak(table, c, 30f, 47f, out at, out value);
                text.Append(' ').Append(names[c]).Append(' ').Append(value.ToString("F2")).Append('@').Append(at.ToString("F1"));
            }

            text.Append(" | secondary");
            for (int c = 0; c < 3; c++)
            {
                float at, value;
                Peak(table, c, 48f, 60f, out at, out value);
                text.Append(' ').Append(names[c]).Append(' ').Append(value.ToString("F2")).Append('@').Append(at.ToString("F1"));
            }

            text.Append(" | dark band ").Append(Least(table, 1, 44f, 50f).ToString("F3"));
            text.Append(" | inside ").Append(Mean(table, 1, 25f, 35f).ToString("F3"));
            return text.ToString();
        }

        /// <summary>Where channel <paramref name="c"/> peaks between two angles (degrees), and how high.</summary>
        public static void Peak(float[] table, int c, float from, float to, out float at, out float value)
        {
            int size = table.Length / 3;
            at = 0f;
            value = -1f;
            for (int e = 0; e < size; e++)
            {
                float degrees = (e + 0.5f) * MaxAngle / size;
                if (degrees < from || degrees > to)
                    continue;
                if (table[3 * e + c] > value)
                {
                    value = table[3 * e + c];
                    at = degrees;
                }
            }
        }

        /// <summary>Channel <paramref name="c"/>'s least value between two angles.</summary>
        public static float Least(float[] table, int c, float from, float to)
        {
            int size = table.Length / 3;
            float least = float.MaxValue;
            for (int e = 0; e < size; e++)
            {
                float degrees = (e + 0.5f) * MaxAngle / size;
                if (degrees >= from && degrees <= to)
                    least = Math.Min(least, table[3 * e + c]);
            }

            return least;
        }

        /// <summary>Channel <paramref name="c"/>'s mean between two angles.</summary>
        public static float Mean(float[] table, int c, float from, float to)
        {
            int size = table.Length / 3;
            float total = 0f;
            int count = 0;
            for (int e = 0; e < size; e++)
            {
                float degrees = (e + 0.5f) * MaxAngle / size;
                if (degrees >= from && degrees <= to)
                {
                    total += table[3 * e + c];
                    count++;
                }
            }

            return count > 0 ? total / count : 0f;
        }
    }
}
