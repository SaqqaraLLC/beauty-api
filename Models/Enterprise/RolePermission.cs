using System.ComponentModel.DataAnnotations.Schema;

namespace Beauty.Api.Models.Enterprise;

/// <summary>Junction table linking roles to their granted permissions.</summary>
public class RolePermission
{
    public int RoleId { get; set; }

    [ForeignKey(nameof(RoleId))]
    public EnterpriseRole Role { get; set; } = null!;

    public int PermissionId { get; set; }

    [ForeignKey(nameof(PermissionId))]
    public Permission Permission { get; set; } = null!;
}
