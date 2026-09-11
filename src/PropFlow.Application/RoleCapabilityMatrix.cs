namespace PropFlow.Application;

// PF-S01.04: additive, per-organization customization of Capabilities.ForRole's fixed defaults.
// Deliberately layers on top of the defaults rather than replacing them - an organization with no
// overrides gets exactly today's behavior, and a revoked default can always be re-granted, so
// there is no way to lock every admin out of a role by misconfiguring it here (see
// RequireAtLeastOneManager below for the one thing this module actively guards against).
public static class RoleCapabilityMatrix
{
    // A capability the built-in defaults never assign for this role, revoked (no-op) or granted
    // (a genuine per-organization addition) - both are legal; Effective is a pure set operation
    // and does not judge whether a grant "makes sense" for a role.
    public static IReadOnlyList<string> Effective(
        IEnumerable<string> defaults, IEnumerable<string> grants, IEnumerable<string> revocations)
    {
        var revoked = revocations as ICollection<string> ?? revocations.ToArray();
        var effective = new List<string>();
        var seen = new HashSet<string>();
        foreach (var capability in defaults.Concat(grants))
            if (!revoked.Contains(capability) && seen.Add(capability))
                effective.Add(capability);
        return effective;
    }
}
