using Azure.Communication;
using Azure.Communication.Identity;
using Azure.Communication.Rooms;
using Beauty.Api.Data;
using Beauty.Api.Models.Enterprise;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Services;

public class AcsStreamingService
{
    private readonly CommunicationIdentityClient _identityClient;
    private readonly RoomsClient _roomsClient;
    private readonly BeautyDbContext _db;
    private readonly string _acsEndpoint;

    public AcsStreamingService(IConfiguration config, BeautyDbContext db)
    {
        var connStr = config["ACS_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("ACS_CONNECTION_STRING is not configured.");
        _acsEndpoint = config["ACS_ENDPOINT"]
            ?? "https://saqqarallc-tv.unitedstates.communication.azure.com";
        _identityClient = new CommunicationIdentityClient(connStr);
        _roomsClient = new RoomsClient(connStr);
        _db = db;
    }

    // ── Create ACS user + issue token ──────────────────────────────────────
    public async Task<(string userId, string token)> CreateUserTokenAsync(
        TimeSpan? lifetime = null)
    {
        var tokenLifetime = lifetime ?? TimeSpan.FromHours(4);
        var response = await _identityClient.CreateUserAndTokenAsync(
            scopes: new[] { CommunicationTokenScope.VoIP },
            tokenExpiresIn: tokenLifetime);
        var result = response.Value;
        return (result.User.Id, result.AccessToken.Token);
    }

    // ── Issue token for existing ACS user ──────────────────────────────────
    public async Task<string> IssueTokenAsync(string acsUserId,
        TimeSpan? lifetime = null)
    {
        var tokenLifetime = lifetime ?? TimeSpan.FromHours(4);
        var user = new CommunicationUserIdentifier(acsUserId);
        var response = await _identityClient.GetTokenAsync(
            user,
            scopes: new[] { CommunicationTokenScope.VoIP },
            tokenExpiresIn: tokenLifetime);
        return response.Value.Token;
    }

    // ── Start a broadcast room ─────────────────────────────────────────────
    public async Task<StreamStartResult> StartBroadcastAsync(
        int artistProfileId, string artistUserId, string title,
        string? thumbnailUrl, string[] tags)
    {
        // Create ACS user for this artist session
        var (acsUserId, acsToken) = await CreateUserTokenAsync(TimeSpan.FromHours(8));

        // Create ACS Room (live call room)
        var validFrom  = DateTimeOffset.UtcNow;
        var validUntil = validFrom.AddHours(8);

        var presenter = new RoomParticipant(new CommunicationUserIdentifier(acsUserId))
        {
            Role = ParticipantRole.Presenter
        };

        var roomOptions = new CreateRoomOptions
        {
            ValidFrom  = validFrom,
            ValidUntil = validUntil,
            Participants = { presenter }
        };

        var roomResponse = await _roomsClient.CreateRoomAsync(roomOptions);
        var roomId = roomResponse.Value.Id;

        // Persist stream record
        var stream = new Models.Enterprise.Stream
        {
            ArtistProfileId = artistProfileId,
            Title           = title,
            ThumbnailUrl    = thumbnailUrl,
            IsLive          = true,
            ViewerCount     = 0,
            TagsJson        = System.Text.Json.JsonSerializer.Serialize(tags),
            AcsRoomId       = roomId,
            AcsHostUserId   = acsUserId,
            IsActive        = true,
        };
        _db.Streams.Add(stream);
        await _db.SaveChangesAsync();

        return new StreamStartResult
        {
            StreamId   = stream.StreamId,
            RoomId     = roomId,
            AcsToken   = acsToken,
            AcsUserId  = acsUserId,
            AcsEndpoint = _acsEndpoint,
        };
    }

    // ── Join a broadcast as viewer ─────────────────────────────────────────
    public async Task<StreamJoinResult> JoinBroadcastAsync(int streamId, string? displayName)
    {
        var stream = await _db.Streams.FindAsync(streamId)
            ?? throw new KeyNotFoundException($"Stream {streamId} not found.");

        if (!stream.IsLive)
            throw new InvalidOperationException("Stream is not currently live.");

        // Create a viewer ACS user
        var (acsUserId, acsToken) = await CreateUserTokenAsync(TimeSpan.FromHours(4));

        // Add viewer to room as attendee
        var viewer = new RoomParticipant(new CommunicationUserIdentifier(acsUserId))
        {
            Role = ParticipantRole.Attendee
        };
        await _roomsClient.AddOrUpdateParticipantsAsync(
            stream.AcsRoomId!, new[] { viewer });

        // Increment viewer count
        stream.ViewerCount++;
        await _db.SaveChangesAsync();

        return new StreamJoinResult
        {
            StreamId    = streamId,
            RoomId      = stream.AcsRoomId!,
            AcsToken    = acsToken,
            AcsUserId   = acsUserId,
            AcsEndpoint = _acsEndpoint,
            Title       = stream.Title,
        };
    }

    // ── End a broadcast ────────────────────────────────────────────────────
    public async Task EndBroadcastAsync(int streamId, string artistUserId)
    {
        var stream = await _db.Streams
            .FirstOrDefaultAsync(s => s.StreamId == streamId)
            ?? throw new KeyNotFoundException($"Stream {streamId} not found.");

        stream.IsLive     = false;
        stream.RecordedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}

public record StreamStartResult
{
    public int    StreamId    { get; init; }
    public string RoomId      { get; init; } = "";
    public string AcsToken    { get; init; } = "";
    public string AcsUserId   { get; init; } = "";
    public string AcsEndpoint { get; init; } = "";
}

public record StreamJoinResult
{
    public int    StreamId    { get; init; }
    public string RoomId      { get; init; } = "";
    public string AcsToken    { get; init; } = "";
    public string AcsUserId   { get; init; } = "";
    public string AcsEndpoint { get; init; } = "";
    public string Title       { get; init; } = "";
}
