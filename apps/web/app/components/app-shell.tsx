"use client";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import type { ReactNode } from "react";
import { api, type Session } from "../../lib/api";
import { hasCapability } from "../../lib/capabilities";
import { visibleNav } from "../../lib/navigation";
import { CommandSearch } from "./command-search";
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
  const pathname = usePathname();
  const nav = visibleNav(session);
  // Search spans work, assets, people and places — all behind Work.Read.
  const canSearch = hasCapability(session, "Work.Read");
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
        {nav.length > 0 && (
          <nav aria-label="Primary navigation">
            {nav.map((item) => (
              <Link
                key={item.href}
                href={item.href}
                aria-current={pathname === item.href ? "page" : undefined}
              >
                {item.label}
              </Link>
            ))}
          </nav>
        )}
        {canSearch && (
          <button
            className="secondary search-trigger"
            onClick={() => window.dispatchEvent(new Event("propflow:open-search"))}
            aria-keyshortcuts="Meta+K Control+K"
          >
            Search <kbd>⌘K</kbd>
          </button>
        )}
        <span className="role">{session.role}</span>
        <button className="secondary" onClick={() => void logout()}>
          Sign out
        </button>
      </header>
      {children}
      {canSearch && <CommandSearch />}
    </main>
  );
}
