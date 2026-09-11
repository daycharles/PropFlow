import type { Session } from "./api";
import { hasCapability } from "./capabilities";

export type NavItem = {
  href: string;
  label: string;
  /** The capability the destination needs. Omit for a page any signed-in user can use. */
  capability?: string;
};

/**
 * The primary navigation (PF-6.12). An entry belongs here ONLY when its destination is a
 * finished feature the signed-in user can actually use — see `.claude/rules/web.md`. Add a
 * route the day it ships, not before: a "coming soon" nav link is a bug, and a link the current
 * user's role cannot follow is noise. Detail pages (`/work/[id]`, `/assets/[id]`) are reached by
 * links from their list, never from here.
 */
export const PRIMARY_NAV: readonly NavItem[] = [
  { href: "/", label: "Work", capability: "Work.Read" },
  { href: "/attention", label: "Needs attention", capability: "Work.Read" },
  { href: "/settings/categories", label: "Categories", capability: "Settings.ManageCategories" },
  {
    href: "/settings/automation",
    label: "Automation",
    capability: "Settings.ManageAutomationRules",
  },
  { href: "/integrations", label: "Integrations", capability: "Integrations.Manage" },
  { href: "/settings/members", label: "Members", capability: "Identity.ManageMembers" },
];

export function visibleNav(session: Session): NavItem[] {
  return PRIMARY_NAV.filter((item) => !item.capability || hasCapability(session, item.capability));
}
