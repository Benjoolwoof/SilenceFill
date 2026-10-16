namespace SilenceFill;

internal static class DpiScale
{
    public static int Pixels(Control control, int logical) =>
        (int)Math.Round(logical * control.DeviceDpi / 96f);
}
