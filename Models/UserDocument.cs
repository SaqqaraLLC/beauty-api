namespace Beauty.Api.Models;

public class UserDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty; // Artist, Location, Client, Agent, Company
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Verified, Rejected
    public string? RejectionReason { get; set; }
    public string? FileUrl { get; set; } // Azure Blob URL (Phase 2)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
}
