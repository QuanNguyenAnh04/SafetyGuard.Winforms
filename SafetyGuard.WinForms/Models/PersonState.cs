using System;

namespace SafetyGuard.WinForms.Models;

public sealed class PersonState
{
    public int TrackId { get; init; }

    public bool HasHelmet { get; set; }
    public bool HasVest { get; set; }
    public bool HasGloves { get; set; }
    public bool HasSmoke { get; set; }
    public bool HasGlasses { get; set; }
    public bool HasBoots { get; set; }

    public float HelmetConfidence { get; set; }
    public float VestConfidence { get; set; }
    public float GlovesConfidence { get; set; }
    public float SmokeConfidence { get; set; }
    public float GlassesConfidence { get; set; }
    public float BootsConfidence { get; set; }

    // durations (seconds)
    public double NoHelmetSeconds { get; set; }
    public double NoVestSeconds { get; set; }
    public double NoGlovesSeconds { get; set; }
    public double NoGlassesSeconds { get; set; }
    public double NoBootsSeconds { get; set; }
    public double SmokingSeconds { get; set; }

    // episode flags (đã sửa): log-once-per-track (đã log thì KHÔNG reset về false => không spam theo frame)
    private bool _noHelmetEpisodeLogged;
    private bool _noVestEpisodeLogged;
    private bool _noGlovesEpisodeLogged;
    private bool _noGlassesEpisodeLogged;
    private bool _noBootsEpisodeLogged;
    private bool _smokingEpisodeLogged;

    public bool NoHelmetEpisodeLogged
    {
        get => _noHelmetEpisodeLogged;
        set => _noHelmetEpisodeLogged = value;
    }

    public bool NoVestEpisodeLogged
    {
        get => _noVestEpisodeLogged;
        set => _noVestEpisodeLogged = value;
    }

    public bool NoGlovesEpisodeLogged
    {
        get => _noGlovesEpisodeLogged;
        set => _noGlovesEpisodeLogged = value;
    }
    public bool NoGlassesEpisodeLogged
    {
        get => _noGlassesEpisodeLogged;
        set => _noGlassesEpisodeLogged = value;
    }

    public bool NoBootsEpisodeLogged
    {
        get => _noBootsEpisodeLogged;
        set => _noBootsEpisodeLogged = value;
    }

    public bool SmokingEpisodeLogged
    {
        get => _smokingEpisodeLogged;
        set => _smokingEpisodeLogged = value;
    }


    public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
}
