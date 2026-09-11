namespace PropFlow.Domain.Configuration;

// The closed set of entity types a configuration feature (custom fields, categories, and
// whatever else PF-S03 adds) can attach to. Shared rather than duplicated per feature — a
// second entity type is one addition here, read by every feature that uses AppliesTo, not a
// redesign of each. Starts with WorkItem only (PF-S03.01/.03); the FS-S02/S04/S06/S07 domains
// (properties, leases, applicants...) are the likely next additions once a feature needs one.
public enum ConfigurationEntityType { WorkItem }
