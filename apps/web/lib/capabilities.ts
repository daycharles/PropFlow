import type { Session } from "./api";
export function hasCapability(
  session: Pick<Session, "capabilities"> | null | undefined,
  capability: string,
) {
  return Boolean(session?.capabilities.includes(capability));
}
