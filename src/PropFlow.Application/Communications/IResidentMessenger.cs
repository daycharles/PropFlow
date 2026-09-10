namespace PropFlow.Application.Communications;

public enum ResidentMessageOutcome
{
    /// <summary>A new outbox row was created for this idempotency key.</summary>
    Queued,
    /// <summary>An outbox row already existed for this idempotency key.</summary>
    Deduplicated,
    /// <summary>The work item does not exist in this tenant.</summary>
    WorkNotFound,
    /// <summary>The work item has no resident to contact.</summary>
    NoResident,
    /// <summary>The resident has not consented to the template's channel, or has no address for it.</summary>
    NoConsent,
    /// <summary>The template does not exist in this tenant.</summary>
    TemplateNotFound,
    /// <summary>The template exists but is archived.</summary>
    TemplateInactive,
    /// <summary>The template body or subject could not be rendered against the work item.</summary>
    RenderFailed,
}

public sealed record ResidentMessageResult(ResidentMessageOutcome Outcome, string? Detail = null)
{
    public bool Persisted => Outcome is ResidentMessageOutcome.Queued or ResidentMessageOutcome.Deduplicated;
}

/// <summary>
/// Renders a message template for a work item's resident and queues it on the outbox — the shared
/// use case behind the resident-message endpoint and the automation <c>SendResidentMessage</c>
/// action. The <paramref name="idempotencyKey"/> is the caller's dedupe boundary;
/// <paramref name="extraValues"/> adds placeholders on top of the standard set (e.g.
/// <c>note.text</c> for a note-triggered message).
/// </summary>
public interface IResidentMessenger
{
    Task<ResidentMessageResult> QueueForWorkAsync(Guid workId, Guid templateId, string idempotencyKey,
        IReadOnlyDictionary<string, string>? extraValues = null, CancellationToken cancellationToken = default);
}
