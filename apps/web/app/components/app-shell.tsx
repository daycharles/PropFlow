"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import type { ReactNode } from "react";
import { api, type Session } from "../../lib/api";
import { hasCapability } from "../../lib/capabilities";
export function AppShell({
  session,
  children,
  onLogout,
}: {
  session: Session;
  children: ReactNode;
  onLogout?: () => void;
}) {
  const router = useRouter();
  async function logout() {
    try {
      await api.auth.logout();
    } finally {
      onLogout?.();
      router.push("/");
      router.refresh();
    }
  }
  return (
    <main>
      <header>
        <Link className="brand" href="/">
          PropFlow
        </Link>
        <nav aria-label="Primary navigation">
          <Link href="/">Work</Link>
          {hasCapability(session, "Settings.ManageCategories") && (
            <Link href="/settings/categories">Categories</Link>
          )}
        </nav>
        <span className="role">{session.role}</span>
        <button className="secondary" onClick={() => void logout()}>
          Sign out
        </button>
      </header>
      {children}
    </main>
  );
}
