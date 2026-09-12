using System.Security.Cryptography;
using System.Text;

namespace PropFlow.Application.Integrations;

// A stable content hash for a canonical record. `record` value equality tells us two instances
// are equal in memory; this gives a persable fingerprint so a re-pull can tell an unchanged
// record from a changed one without storing the whole payload. The record's own ToString()
// (compiler-generated, includes every property) is the canonical form.
public static class CanonicalHash
{
    public static string Of<T>(T record) where T : notnull
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(record.ToString() ?? ""));
        return Convert.ToHexStringLower(bytes);
    }

    // The content of the resident an occupancy carries. A resident has no canonical record of its
    // own — CanonicalOccupancy holds the person — so its fingerprint is the person's fields and
    // nothing else. ONE named place, because the ingest pass and the reconciler both compute it
    // and a drift between them would make every resident look changed on every run.
    public static string ForResident(CanonicalOccupancy occupancy)
    {
        ArgumentNullException.ThrowIfNull(occupancy);
        return Of((occupancy.ResidentName, occupancy.Email, occupancy.Phone));
    }
}
