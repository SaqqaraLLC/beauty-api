using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Expenses;

public class Expense
{
    public long Id { get; set; }

    [Required, MaxLength(450)]
    public string SubmittedByUserId { get; set; } = "";

    [Required, MaxLength(200)]
    public string SubmittedByName { get; set; } = "";

    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;

    /// <summary>Amount in cents to avoid floating-point rounding.</summary>
    public int AmountCents { get; set; }

    [Required, MaxLength(500)]
    public string Description { get; set; } = "";

    /// <summary>Date the expense was incurred (not submitted).</summary>
    public DateTime ExpenseDate { get; set; }

    [MaxLength(1000)]
    public string? ReceiptUrl { get; set; }

    public ExpenseStatus Status { get; set; } = ExpenseStatus.Pending;

    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }

    [MaxLength(200)]
    public string? ReviewedByName { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(500)]
    public string? ReviewNotes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
