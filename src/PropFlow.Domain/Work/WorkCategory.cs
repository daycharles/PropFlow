namespace PropFlow.Domain.Work;

public sealed class WorkCategory(Guid organizationId, Guid id, string name, int sortOrder) : TenantEntity(organizationId, id)
{
    public string Name { get; private set; } = !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100
        ? name.Trim() : throw new ArgumentException("Category name must contain 1 to 100 characters.", nameof(name));
    public int SortOrder { get; private set; } = sortOrder >= 0 ? sortOrder : throw new ArgumentOutOfRangeException(nameof(sortOrder));
    public bool IsArchived { get; private set; }
    public void Rename(string name) => Name = !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100
        ? name.Trim() : throw new ArgumentException("Category name must contain 1 to 100 characters.", nameof(name));
    public void Reorder(int sortOrder) => SortOrder = sortOrder >= 0 ? sortOrder : throw new ArgumentOutOfRangeException(nameof(sortOrder));
    public void Archive() => IsArchived = true;
}
