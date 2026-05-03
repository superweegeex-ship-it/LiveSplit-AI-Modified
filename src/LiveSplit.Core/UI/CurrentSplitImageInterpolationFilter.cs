namespace LiveSplit.UI;

/// <summary>Interpolation used when stretching the current-split background image to the row.</summary>
public enum CurrentSplitImageInterpolationFilter
{
    Nearest,
    Bilinear,
    Bicubic,
    /// <summary>Approximate area/box-style sampling (GDI+ Low).</summary>
    Area,
    /// <summary>Sharpest high-quality resample available in GDI+ (HighQualityBicubic; closest to Lanczos).</summary>
    Lanczos
}
