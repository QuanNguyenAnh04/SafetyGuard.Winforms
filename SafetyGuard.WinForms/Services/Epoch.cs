using System;

namespace SafetyGuard.WinForms.Services;

public static class Epoch
{
    public static long ToMs(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    public static DateTime FromMs(long ms)
        => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
}
