using PropFlow.Domain.Configuration;

namespace PropFlow.Domain.Work;

// PF-S03.03. AppliesTo generalizes what was a Work-only category list to the same closed
// ConfigurationEntityType vocabulary CustomFieldDefinition uses (PF-S03.01), so a future entity
// type shares this CRUD surface instead of getting a second one. Defaults to WorkItem —
// existing rows and existing callers of the four-argument constructor are unaffected. Immutable
// after creation, same as CustomFieldDefinition.AppliesTo: retagging a category to a different
// entity type is a delete-and-recreate, not an edit.
public sealed class WorkCategory(Guid organizationId, Guid id, string name, int sortOrder,
    ConfigurationEntityType appliesTo = ConfigurationEntityType.WorkItem) : TenantEntity(organizationId, id)
{
    public string Name { get; private set; } = !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100
        ? name.Trim() : throw new ArgumentException("Category name must contain 1 to 100 characters.", nameof(name));
    public int SortOrder { get; private set; } = sortOrder >= 0 ? sortOrder : throw new ArgumentOutOfRangeException(nameof(sortOrder));
    public ConfigurationEntityType AppliesTo { get; private set; } = Enum.IsDefined(appliesTo)
        ? appliesTo : throw new ArgumentOutOfRangeException(nameof(appliesTo));
    public bool IsArchived { get; private set; }
    public void Rename(string name) => Name = !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100
        ? name.Trim() : throw new ArgumentException("Category name must contain 1 to 100 characters.", nameof(name));
    public void Reorder(int sortOrder) => SortOrder = sortOrder >= 0 ? sortOrder : throw new ArgumentOutOfRangeException(nameof(sortOrder));
    public void Archive() => IsArchived = true;
}
