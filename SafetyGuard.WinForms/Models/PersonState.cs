using System;

namespace SafetyGuard.WinForms.Models;

public sealed class PersonState
{
    public int TrackId { get; init; }

    public bool HasHelmet { get; set; }
    public bool HasVest { get; set; }
    public bool HasGloves { get; set; }
    public bool HasSmoke { get; set; }

    public float HelmetConfidence { get; set; }
    public float VestConfidence { get; set; }
    public float GlovesConfidence { get; set; }
    public float SmokeConfidence { get; set; }

    // durations (seconds)
    public double NoHelmetSeconds { get; set; }
    public double NoVestSeconds { get; set; }
    public double NoGlovesSeconds { get; set; }
    public double SmokingSeconds { get; set; }

    // episode flags (đã sửa): log-once-per-track (đã log thì KHÔNG reset về false => không spam theo frame)
    private bool _noHelmetEpisodeLogged;
    private bool _noVestEpisodeLogged;
    private bool _noGlovesEpisodeLogged;
    private bool _smokingEpisodeLogged;

    public bool NoHelmetEpisodeLogged
    {
        get => _noHelmetEpisodeLogged;
        set { if (value) _noHelmetEpisodeLogged = true; }
    }

    public bool NoVestEpisodeLogged
    {
        get => _noVestEpisodeLogged;
        set { if (value) _noVestEpisodeLogged = true; }
    }

    public bool NoGlovesEpisodeLogged
    {
        get => _noGlovesEpisodeLogged;
        set { if (value) _noGlovesEpisodeLogged = true; }
    }

    public bool SmokingEpisodeLogged
    {
        get => _smokingEpisodeLogged;
        set { if (value) _smokingEpisodeLogged = true; }
    }

    public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
}
