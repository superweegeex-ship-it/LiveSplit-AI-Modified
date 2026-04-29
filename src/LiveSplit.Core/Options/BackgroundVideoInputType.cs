namespace LiveSplit.Options;

public enum BackgroundVideoInputType
{
    File,
    VlcMrl,
}

public enum BackgroundVideoBackend
{
    MpvReadback = 0,
    MpvWindowEmbed = 1,
    MpvReadbackThreaded = 2,
}

public enum BackgroundVideoBlurType
{
    Gaussian = 0,
    Directional = 1,
    Box = 2,
    Rotational = 3,
}
