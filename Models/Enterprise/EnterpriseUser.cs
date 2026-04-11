using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

public class EnterpriseUser : ISoftDeletable
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EnterpriseAccountId { get; set; }

    [ForeignKey(nameof(EnterpriseAccountId))]
    public EnterpriseAccount EnterpriseAccount { get; set; } = null!;

    public int? RoleId { get; set; }

    [ForeignKey(nameof(RoleId))]
    public EnterpriseRole? Role { get; set; }

    /// <summary>Invited | Active | Suspended | Offboarded</summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Invited";

    // ISoftDeletable
    public bool      IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
