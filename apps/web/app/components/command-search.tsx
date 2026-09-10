"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { api, ApiError, type SearchHit, type SearchHitType } from "../../lib/api";

const typeLabels: Record<SearchHitType, string> = {
  Property: "Property",
  Building: "Building",
  Space: "Space",
  Resident: "Resident",
  Vendor: "Vendor",
  Employee: "Employee",
  Category: "Category",
  Asset: "Asset",
  Work: "Work",
};

// Where a result takes you. Work and assets have their own page; the rest filter the work list.
function destination(hit: SearchHit): string {
  switch (hit.type) {
    case "Work":
      return `/work/${hit.id}`;
    case "Asset":
      return `/assets/${hit.id}`;
    case "Property":
      return `/?propertyId=${hit.id}`;
    case "Space":
      return `/?spaceId=${hit.id}`;
    case "Category":
      return `/?categoryId=${hit.id}`;
    default:
      return `/?search=${encodeURIComponent(hit.label)}`;
  }
}

const isTypingTarget = (node: EventTarget | null) =>
  node instanceof HTMLElement &&
  (node.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(node.tagName));

/**
 * Keyboard-first global search (PF-6.09). Opens on Cmd/Ctrl+K anywhere, or "/" when focus is not
 * in a field. Arrow keys move the selection, Enter opens it, Escape closes. Mounted once in the
 * app shell so it is available on every page.
 */
export function CommandSearch() {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [term, setTerm] = useState("");
  const [hits, setHits] = useState<SearchHit[]>([]);
  const [active, setActive] = useState(0);
  const [settledTerm, setSettledTerm] = useState("");
  const [error, setError] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const requestId = useRef(0);

  const close = useCallback(() => {
    setOpen(false);
    setTerm("");
    setHits([]);
    setActive(0);
    setError("");
    setSettledTerm("");
  }, []);

  // Global open shortcuts.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setOpen((current) => !current);
        return;
      }
      if (event.key === "/" && !event.metaKey && !event.ctrlKey && !isTypingTarget(event.target)) {
        event.preventDefault();
        setOpen(true);
      }
    }
    const openOnRequest = () => setOpen(true);
    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("propflow:open-search", openOnRequest);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("propflow:open-search", openOnRequest);
    };
  }, []);

  useEffect(() => {
    if (open) inputRef.current?.focus();
  }, [open]);

  // Debounced query. All state changes happen inside the timer, never in the effect body.
  useEffect(() => {
    const query = term.trim();
    const id = ++requestId.current;
    const timer = window.setTimeout(
      async () => {
        if (id !== requestId.current) return;
        if (query.length < 2) {
          setHits([]);
          setError("");
          setSettledTerm("");
          return;
        }
        try {
          const results = await api.search(query);
          if (id !== requestId.current) return;
          setHits(results);
          setActive(0);
          setError("");
        } catch (cause) {
          if (id !== requestId.current) return;
          setHits([]);
          setError(cause instanceof ApiError ? cause.message : "Search is unavailable right now.");
        } finally {
          if (id === requestId.current) setSettledTerm(query);
        }
      },
      query.length < 2 ? 0 : 180,
    );
    return () => window.clearTimeout(timer);
  }, [term]);

  const go = useCallback(
    (hit: SearchHit | undefined) => {
      if (!hit) return;
      const href = destination(hit);
      close();
      // Work and asset pages fetch on mount, so a client push is enough. The work-list filter
      // links (`/?…`) are read from the URL only when the list mounts, so force a real
      // navigation — otherwise landing on `/` from `/` would not re-apply the filter.
      if (href.startsWith("/?")) window.location.assign(href);
      else router.push(href);
    },
    [close, router],
  );

  function onInputKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      close();
    } else if (event.key === "ArrowDown") {
      event.preventDefault();
      setActive((current) => (hits.length ? (current + 1) % hits.length : 0));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActive((current) => (hits.length ? (current - 1 + hits.length) % hits.length : 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      go(hits[active]);
    }
  }

  useEffect(() => {
    listRef.current
      ?.querySelector<HTMLElement>('[data-active="true"]')
      ?.scrollIntoView({ block: "nearest" });
  }, [active, hits]);

  const status = useMemo(() => {
    const query = term.trim();
    if (query.length < 2) return "Type at least two characters.";
    if (settledTerm !== query) return "Searching…";
    if (error) return error;
    if (hits.length === 0) return "No matches.";
    return `${hits.length} result${hits.length === 1 ? "" : "s"}`;
  }, [term, settledTerm, error, hits.length]);

  if (!open) return null;

  return (
    <div
      className="command-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) close();
      }}
    >
      <div className="command-palette" role="dialog" aria-modal="true" aria-label="Search PropFlow">
        <input
          ref={inputRef}
          className="command-input"
          type="text"
          role="combobox"
          aria-expanded={hits.length > 0}
          aria-controls="command-results"
          aria-activedescendant={hits.length ? `command-result-${active}` : undefined}
          placeholder="Search work, assets, people, places…"
          value={term}
          onChange={(event) => setTerm(event.target.value)}
          onKeyDown={onInputKeyDown}
        />
        <p className="command-status" role="status">
          {status}
        </p>
        {hits.length > 0 && (
          <ul className="command-results" id="command-results" role="listbox" ref={listRef}>
            {hits.map((hit, index) => (
              <li
                key={`${hit.type}-${hit.id}`}
                id={`command-result-${index}`}
                role="option"
                aria-selected={index === active}
                data-active={index === active}
                className="command-result"
                onMouseEnter={() => setActive(index)}
                onMouseDown={(event) => {
                  event.preventDefault();
                  go(hit);
                }}
              >
                <span className="command-type">{typeLabels[hit.type]}</span>
                <span className="command-label">{hit.label}</span>
                {hit.sublabel ? <span className="command-sublabel">{hit.sublabel}</span> : null}
              </li>
            ))}
          </ul>
        )}
        <p className="command-hint">
          <kbd>↑</kbd>
          <kbd>↓</kbd> to navigate · <kbd>↵</kbd> to open · <kbd>esc</kbd> to close
        </p>
      </div>
    </div>
  );
}
