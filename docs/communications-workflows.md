# Communications workflows (FS-S16)

Communications are tenant-scoped in the `communications` schema. Outbound messages remain in
the transactional outbox and use the existing idempotency key, retry, stale-claim recovery, and
HMAC provider-callback flow. Provider callbacks are replay-safe and update delivery state only
when their timestamp is newer than the stored state.

The workflow layer adds:

- `Conversations` and ordered `ConversationMessages` for inbox threading. Each message stores a
  SHA-256 integrity hash of its body and is linked by the tenant composite key.
- `Campaigns` for reusable email/SMS campaign content and explicit publish state.
- `ChannelUnsubscribes` with a unique tenant/channel/address key. Senders must consult this list
  alongside the resident's explicit channel consent before enqueueing campaign or direct messages.

Endpoints are under `/api/communication/conversations` and `/api/communication/campaigns`.
Conversation reads require `Work.Read`; mutation and campaign administration require the existing
communications capabilities. Unsubscribe is intentionally unauthenticated so provider links can
be used, while writes remain tenant-bound by the signed session/provider flow.
