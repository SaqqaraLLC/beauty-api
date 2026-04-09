namespace Beauty.Api.Models.ApprovalHistory;

public class ApprovalHistory
{
    public int Id { get; set; }
    public string TargetUserId { get; set; } = null!;
    public string PerformedByUserId { get; set; } = null!;
    public string Action { get; set; } = null!;
    public DateTime PerformedAtUtc { get; set; }
}

