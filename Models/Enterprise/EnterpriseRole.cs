using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class EnterpriseRole
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "";

    /// <summary>Platform | Enterprise</summary>
    [Required]
    [MaxLength(50)]
    public string Scope { get; set; } = "Enterprise";

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
