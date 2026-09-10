"use client";
import { useEffect, useState, type ReactNode } from "react";
import Link from "next/link";
import { api, type Session } from "../../lib/api";
import { hasCapability } from "../../lib/capabilities";
export function ProtectedPage({
  capability,
  children,
}: {
  capability: string;
  children: (session: Session) => ReactNode;
}) {
  const [session, setSession] = useState<Session | null | undefined>();
  useEffect(() => {
    api
      .session()
      .then(setSession)
      .catch(() => setSession(null));
  }, []);
  if (session === undefined)
    return (
      <main className="centered">
        <p>Loading…</p>
      </main>
    );
  if (!session)
    return (
      <main className="centered">
        <h1>Sign in required</h1>
        <Link href="/">Return to sign in</Link>
      </main>
    );
  if (!hasCapability(session, capability))
    return (
      <main className="centered">
        <h1>Access denied</h1>
        <p>You do not have permission to view this page.</p>
        <Link href="/">Return to Work</Link>
      </main>
    );
  return <>{children(session)}</>;
}
