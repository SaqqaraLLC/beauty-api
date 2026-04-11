namespace Beauty.Api.Models.Enterprise;

/// <summary>
/// Implemented by all business entities that support soft deletes.
/// EF Core global query filter: HasQueryFilter(e => !e.IsDeleted)
/// </summary>
public interface ISoftDeletable
{
    bool      IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}
