# Document workflows (FS-S17)

Document templates are versioned on every revision. Generated packets capture the template
version and a SHA-256 integrity hash of the rendered content. Packet retention and expiration are
stored explicitly, and signature completion requires the authenticated user's identity to match
the signer record; the client cannot choose a different signer identity at completion time.

Provider adapters can use `SignatureRequest.RecordFailure` for retryable failures. The request
remains pending until the configured attempt cap, then becomes terminally failed. Signed packets
retain the packet hash as tamper evidence. All entities use the communications store's composite
tenant key, query filter, write guard, and row-level security migration conventions.
