using Beauty.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Gifts;

public class CollabSession
{
    public long Id { get; set; }

    // The host's stream — all collab artists join this ACS room
    public int StreamId { get; set; }
    public Beauty.Api.Models.Enterprise.Stream Stream { get; set; } = null!;

    [Required]
    public string HostArtistUserId { get; set; } = "";

    public CollabStatus Status { get; set; } = CollabStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt  { get; set; }

    public ICollection<CollabParticipant> Participants { get; set; } = new List<CollabParticipant>();
}

public class CollabParticipant
{
    public long Id { get; set; }

    public long CollabSessionId { get; set; }
    public CollabSession CollabSession { get; set; } = null!;

    [Required]
    public string ArtistUserId { get; set; } = "";

    public CollabInviteStatus Status { get; set; } = CollabInviteStatus.Invited;

    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;
    public DateTime? JoinedAt { get; set; }
}

public enum CollabStatus       { Active = 1, Ended = 2 }
public enum CollabInviteStatus { Invited = 1, Joined = 2, Declined = 3, Left = 4 }
