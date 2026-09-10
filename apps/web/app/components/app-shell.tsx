"use client";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import type { ReactNode } from "react";
import { api, type Session } from "../../lib/api";
import { hasCapability } from "../../lib/capabilities";
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
          <Link href="/" aria-current={pathname === "/" ? "page" : undefined}>
            Work
          </Link>
          <Link href="/attention" aria-current={pathname === "/attention" ? "page" : undefined}>
            Needs attention
          </Link>
          {hasCapability(session, "Settings.ManageCategories") && (
            <Link
              href="/settings/categories"
              aria-current={pathname === "/settings/categories" ? "page" : undefined}
            >
              Categories
            </Link>
          )}
        </nav>
        <button
          className="secondary search-trigger"
          onClick={() => window.dispatchEvent(new Event("propflow:open-search"))}
          aria-keyshortcuts="Meta+K Control+K"
        >
          Search <kbd>⌘K</kbd>
        </button>
        <span className="role">{session.role}</span>
        <button className="secondary" onClick={() => void logout()}>
          Sign out
        </button>
      </header>
      {children}
      <CommandSearch />
    </main>
  );
}
