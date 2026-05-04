using System;
using System.Drawing;

namespace LiveSplit.UI;

/// <summary>Builds a pre-multiplied alpha bitmap used under split icons (same nominal size as the legacy 30x30 asset).</summary>
public static class IconShadow
{
    private const int W = 30;
    private const int H = 30;
    private const int Inner = 24;
    private const int Origin = 3;

    public static Bitmap Generate(Image image, Color shadowColor)
        => Generate(image, shadowColor, 0f, 100f, 28f);

    /// <param name="offsetPixels">Signed shift in pixels (fractional allowed): + = south-east, − = north-west.</param>
    /// <param name="transparencyPercent">0 = invisible, 100 = full strength (after <paramref name="shadowColor"/> alpha curve).</param>
    /// <param name="blurPercent">0 = sharp, 100 = soft; scales blur sigma linearly.</param>
    public static Bitmap Generate(Image image, Color shadowColor, float offsetPixels, float transparencyPercent, float blurPercent)
    {
        byte red = shadowColor.R;
        byte green = shadowColor.G;
        byte blue = shadowColor.B;
        double baseAlpha = 255.0 * (Math.Pow((shadowColor.A / 255.0) - 1.0, 3.0) + 1.0);
        float transparency = Math.Min(100f, Math.Max(0f, transparencyPercent)) / 100f;
        // Scale only by user transparency; do not cap at 0.8 or shadows never reach full strength from ShadowsColor.
        baseAlpha *= transparency;

        float blur = Math.Min(100f, Math.Max(0f, blurPercent)) / 100f;
        float sigma = blur <= 0.001f ? 0f : 0.12f + blur * 3.6f;

        float off = Math.Min(10f, Math.Max(-10f, offsetPixels));

        using var scaledCopy = new Bitmap(image, Inner, Inner);
        var alpha = new float[W, H];
        for (int iy = 0; iy < Inner; iy++)
        {
            for (int ix = 0; ix < Inner; ix++)
            {
                float a = scaledCopy.GetPixel(ix, iy).A / 255f;
                if (a <= 0f)
                {
                    continue;
                }

                float cx = Origin + ix + off;
                float cy = Origin + iy + off;
                SplatBilinear(alpha, W, H, cx, cy, a);
            }
        }

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                alpha[x, y] = Math.Min(1f, Math.Max(0f, alpha[x, y]));
            }
        }

        if (sigma > 0.08f)
        {
            BlurSeparable(alpha, W, H, sigma);
        }

        var resultImage = new Bitmap(W, H);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int aOut = (int)Math.Round(Math.Min(255.0, Math.Max(0.0, baseAlpha * alpha[x, y])));
                resultImage.SetPixel(x, y, Color.FromArgb(aOut, red, green, blue));
            }
        }

        return resultImage;
    }

    private static void SplatBilinear(float[,] buf, int w, int h, float cx, float cy, float a)
    {
        int x0 = (int)Math.Floor(cx);
        int y0 = (int)Math.Floor(cy);
        float wx = cx - x0;
        float wy = cy - y0;

        void add(int x, int y, float weight)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || weight <= 0f)
            {
                return;
            }

            buf[x, y] += a * weight;
        }

        add(x0, y0, (1f - wx) * (1f - wy));
        add(x0 + 1, y0, wx * (1f - wy));
        add(x0, y0 + 1, (1f - wx) * wy);
        add(x0 + 1, y0 + 1, wx * wy);
    }

    private static void BlurSeparable(float[,] a, int w, int h, float sigma)
    {
        int radius = Math.Min(10, (int)Math.Ceiling(2.75 * sigma));
        if (radius <= 0)
        {
            return;
        }

        int len = 2 * radius + 1;
        var kernel = new float[len];
        double sum = 0.0;
        for (int i = -radius; i <= radius; i++)
        {
            float g = (float)Math.Exp(-(i * i) / (2.0 * sigma * sigma));
            kernel[i + radius] = g;
            sum += g;
        }

        for (int i = 0; i < len; i++)
        {
            kernel[i] = (float)(kernel[i] / sum);
        }

        var tmp = new float[w, h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float acc = 0f;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = x + k;
                    if (sx < 0)
                    {
                        sx = 0;
                    }
                    else if (sx >= w)
                    {
                        sx = w - 1;
                    }

                    acc += a[sx, y] * kernel[k + radius];
                }

                tmp[x, y] = acc;
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float acc = 0f;
                for (int k = -radius; k <= radius; k++)
                {
                    int sy = y + k;
                    if (sy < 0)
                    {
                        sy = 0;
                    }
                    else if (sy >= h)
                    {
                        sy = h - 1;
                    }

                    acc += tmp[x, sy] * kernel[k + radius];
                }

                a[x, y] = acc;
            }
        }
    }
}
