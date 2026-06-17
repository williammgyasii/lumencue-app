using System.Text.Json;
using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Api;

// Dapper row DTOs. Property names match the snake_case column aliases selected in queries.

public sealed class BranchRow
{
    public string id { get; set; } = "";
    public string organization_id { get; set; } = "";
    public string name { get; set; } = "";
    public string password_hash { get; set; } = "";
    public string organization_name { get; set; } = "";
}

public sealed class SeatRow
{
    public long id { get; set; }
    public string organization_id { get; set; } = "";
    public string branch_id { get; set; } = "";
    public string device_id { get; set; } = "";
    public string hardware_id { get; set; } = "";
}

// Resolved plan + entitlement + subscription state for a branch (joined for sign-in/validate).
public sealed class AccessRow
{
    public int seats { get; set; }
    public int stt_minutes_per_day { get; set; }
    public string plan_code { get; set; } = "";
    public string status { get; set; } = "";
}

// Deepgram POST /v1/auth/grant response (snake_case to match the API payload).
public sealed record DeepgramGrant(string access_token, double? expires_in);

// What the client receives from POST /stt/token.
public sealed record SttTokenResponse(string AccessToken, double ExpiresIn);

public sealed class SongRow
{
    public Guid id { get; set; }
    public string organization_id { get; set; } = "";
    public string title { get; set; } = "";
    public string? artist { get; set; }
    public string? ccli_number { get; set; }
    public string? copyright_info { get; set; }
    public string? tags { get; set; }
    public int lines_per_slide { get; set; }
    public string? sections { get; set; }
    public bool deleted { get; set; }
    public DateTime updated_at { get; set; }

    public Song ToSong() => new()
    {
        CloudId = id.ToString(),
        OrganizationId = organization_id,
        Title = title,
        Artist = artist,
        CcliNumber = ccli_number,
        CopyrightInfo = copyright_info,
        Tags = tags,
        LinesPerSlide = lines_per_slide,
        Deleted = deleted,
        UpdatedAt = DateTime.SpecifyKind(updated_at, DateTimeKind.Utc),
        Sections = string.IsNullOrWhiteSpace(sections)
            ? []
            : JsonSerializer.Deserialize<List<SongSection>>(sections) ?? [],
    };
}
