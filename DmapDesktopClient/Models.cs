namespace DmapDesktopClient;

public class ShowDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SeasonDto
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string? Year { get; set; }
    public string? Description { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CampaignDto
{
    public string Id { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string? DungeonMaster { get; set; }
    public string? Setting { get; set; }
    public string? Description { get; set; }
    public string? CoverImage { get; set; }
    public DateTime? FilmingStart { get; set; }
    public DateTime? FilmingEnd { get; set; }
    public string? FilmingLocation { get; set; }
    public List<EpisodeCastMemberDto> Characters { get; set; } = [];
    public DateTime? UpdatedAt { get; set; }
}

public class EpisodeDto
{
    public string Id { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string? DungeonMaster { get; set; }
    public string? Summary { get; set; }
    public DateTime? DateFilmed { get; set; }
    public string? FilmingLocation { get; set; }
    public string? Runtime { get; set; }
    public string? ProductionNotes { get; set; }
    public List<Guid> CastIds { get; set; } = [];
    public List<EpisodeCastMemberDto> GuestCast { get; set; } = [];
    public List<EpisodeKeyMomentDto> KeyMoments { get; set; } = [];
    public List<EpisodeAirDateDto> AirDates { get; set; } = [];
    public DateTime? UpdatedAt { get; set; }
}

public class EpisodeCastMemberDto
{
    public Guid Id { get; set; }
    public string? PlayerName { get; set; }
    public string? CharacterName { get; set; }
    public string? CharacterSummary { get; set; }
    public string? CharacterDescription { get; set; }
    public string? ImagePath { get; set; }
}

public class EpisodeKeyMomentDto
{
    public string? Timestamp { get; set; }
    public string? Description { get; set; }
}

public class EpisodeAirDateDto
{
    public DateTime? Date { get; set; }
    public string? Platform { get; set; }
    public string? Notes { get; set; }
}
