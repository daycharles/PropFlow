"use client";
import { useEffect, useState } from "react";
import { AppShell } from "../../components/app-shell";
import { ProtectedPage } from "../../components/protected-page";
import {
  api,
  type CustomFieldDefinition,
  type CustomFieldType,
  type NotificationEventType,
  type NotificationPreference,
  type NumberingScheme,
  type OrganizationSettings,
  type Session,
} from "../../../lib/api";

// PF-S03.08. One page for the PF-S03 configuration primitives: custom fields (.01), numbering
// (.04), business hours + default time zone (.06), and notification preferences (.07). Every
// one of these is gated on Settings.ManageConfiguration except notification preferences, which
// are personal (Work.Read) - see the note in that section.
export default function Configuration() {
  return (
    <ProtectedPage capability="Settings.ManageConfiguration">
      {(session) => <ConfigurationContent session={session} />}
    </ProtectedPage>
  );
}

const WEEK_ORDER = [
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
  "Sunday",
] as const;
const NOTIFICATION_LABELS: Record<NotificationEventType, string> = {
  WorkAssigned: "Work assigned to me",
  AutomationApplied: "An automation rule applied",
  InvitationReceived: "Invitation received",
};

function ConfigurationContent({ session }: { session: Session }) {
  return (
    <AppShell session={session}>
      <section className="panel">
        <h1>Configuration</h1>
        <p>Custom fields, numbering, business hours, and notification preferences.</p>
      </section>
      <CustomFieldsSection />
      <NumberingSection />
      <BusinessHoursSection />
      <NotificationPreferencesSection />
    </AppShell>
  );
}

function CustomFieldsSection() {
  const [fields, setFields] = useState<CustomFieldDefinition[]>([]);
  const [error, setError] = useState("");
  const [key, setKey] = useState("");
  const [name, setName] = useState("");
  const [fieldType, setFieldType] = useState<CustomFieldType>("Text");
  const [optionsText, setOptionsText] = useState("");
  const [isRequired, setIsRequired] = useState(false);
  const load = () =>
    api.customFields
      .list()
      .then(setFields)
      .catch(() => setError("Unable to load custom fields."));
  useEffect(() => {
    void load();
  }, []);
  async function create() {
    setError("");
    try {
      await api.customFields.create({
        key,
        name,
        fieldType,
        isRequired,
        sortOrder: fields.length,
        options:
          fieldType === "SingleSelect"
            ? optionsText
                .split(",")
                .map((o) => o.trim())
                .filter(Boolean)
            : null,
      });
      setKey("");
      setName("");
      setOptionsText("");
      setIsRequired(false);
      await load();
    } catch {
      setError("Unable to create field. Check the key format and, for Single select, the options.");
    }
  }
  async function toggleRequired(field: CustomFieldDefinition) {
    setError("");
    try {
      const updated = await api.customFields.update(field.id, {
        name: field.name,
        options: field.options,
        isRequired: !field.isRequired,
        sortOrder: field.sortOrder,
      });
      setFields((current) => current.map((f) => (f.id === updated.id ? updated : f)));
    } catch {
      setError("Unable to change that field.");
    }
  }
  async function archive(field: CustomFieldDefinition) {
    setError("");
    try {
      await api.customFields.archive(field.id);
      await load();
    } catch {
      setError("Unable to archive that field.");
    }
  }
  return (
    <section className="panel">
      <h2>Custom fields</h2>
      <p>Extra fields captured on a work item, in addition to the built-in ones.</p>
      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}
      <div className="form-grid">
        <label>
          Key
          <input
            value={key}
            onChange={(event) => setKey(event.target.value)}
            placeholder="warranty_number"
          />
        </label>
        <label>
          Label
          <input
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="Warranty number"
          />
        </label>
        <label>
          Type
          <select
            value={fieldType}
            onChange={(event) => setFieldType(event.target.value as CustomFieldType)}
          >
            <option value="Text">Text</option>
            <option value="Number">Number</option>
            <option value="Date">Date</option>
            <option value="Boolean">Yes/No</option>
            <option value="SingleSelect">Single select</option>
          </select>
        </label>
        {fieldType === "SingleSelect" && (
          <label>
            Options (comma-separated)
            <input
              value={optionsText}
              onChange={(event) => setOptionsText(event.target.value)}
              placeholder="One, Two, Three"
            />
          </label>
        )}
        <label>
          <input
            type="checkbox"
            checked={isRequired}
            onChange={(event) => setIsRequired(event.target.checked)}
          />{" "}
          Required
        </label>
        <button onClick={() => void create()} disabled={!key.trim() || !name.trim()}>
          Add field
        </button>
      </div>
      <ul className="category-list">
        {fields.map((field) => (
          <li key={field.id}>
            <span>
              <strong>{field.name}</strong> ({field.key})
              <br />
              <small>
                {field.fieldType}
                {field.isRequired ? " · required" : ""}
                {field.isArchived ? " · archived" : ""}
              </small>
            </span>
            {!field.isArchived && (
              <>
                <button className="secondary" onClick={() => void toggleRequired(field)}>
                  {field.isRequired ? "Make optional" : "Make required"}
                </button>
                <button className="secondary" onClick={() => void archive(field)}>
                  Archive
                </button>
              </>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

function NumberingSection() {
  const [scheme, setScheme] = useState<NumberingScheme | null>(null);
  const [prefix, setPrefix] = useState("");
  const [width, setWidth] = useState(4);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    api.numbering
      .list()
      .then((schemes) => {
        const workItem = schemes.find((s) => s.appliesTo === "WorkItem") ?? null;
        setScheme(workItem);
        if (workItem) {
          setPrefix(workItem.prefix);
          setWidth(workItem.width);
        }
      })
      .catch(() => setError("Unable to load numbering configuration."));
  }, []);
  async function save() {
    setError("");
    setSaved(false);
    try {
      const updated = await api.numbering.configure("WorkItem", { prefix, width });
      setScheme(updated);
      setSaved(true);
    } catch {
      setError("Unable to save numbering. Width must be between 1 and 10.");
    }
  }
  return (
    <section className="panel">
      <h2>Work order numbering</h2>
      <p>
        A prefix and zero-padded width for the display number assigned to new work orders.
        {scheme && (
          <>
            {" "}
            Next number: <strong>{scheme.nextFormatted}</strong>.
          </>
        )}
      </p>
      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}
      {saved && !error && (
        <p className="success" role="status">
          Saved.
        </p>
      )}
      <div className="form-grid">
        <label>
          Prefix
          <input
            value={prefix}
            onChange={(event) => setPrefix(event.target.value)}
            placeholder="WO-"
          />
        </label>
        <label>
          Width
          <input
            type="number"
            min={1}
            max={10}
            value={width}
            onChange={(event) => setWidth(Number(event.target.value))}
          />
        </label>
        <button onClick={() => void save()}>Save numbering</button>
      </div>
    </section>
  );
}

type DayRow = { day: string; closed: boolean; open: string; close: string };

function BusinessHoursSection() {
  const [defaultTimeZoneId, setDefaultTimeZoneId] = useState("");
  const [days, setDays] = useState<DayRow[]>(
    WEEK_ORDER.map((day) => ({ day, closed: true, open: "09:00", close: "17:00" })),
  );
  const [error, setError] = useState("");
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    api.organizationSettings
      .get()
      .then((settings) => {
        setDefaultTimeZoneId(settings.defaultTimeZoneId ?? "");
        setDays(
          WEEK_ORDER.map((day) => {
            const window = settings.businessHours.find((w) => w.day === day);
            return {
              day,
              closed: !window?.open,
              open: window?.open ? window.open.slice(0, 5) : "09:00",
              close: window?.close ? window.close.slice(0, 5) : "17:00",
            };
          }),
        );
      })
      .catch(() => setError("Unable to load business hours."));
  }, []);
  function updateDay(day: string, patch: Partial<DayRow>) {
    setDays((current) => current.map((row) => (row.day === day ? { ...row, ...patch } : row)));
  }
  async function save() {
    setError("");
    setSaved(false);
    try {
      const updated = await api.organizationSettings.update({
        defaultTimeZoneId,
        businessHours: days.map((row) => ({
          day: row.day,
          open: row.closed ? null : `${row.open}:00`,
          close: row.closed ? null : `${row.close}:00`,
        })),
      });
      setDefaultTimeZoneId(updated.defaultTimeZoneId ?? "");
      setSaved(true);
    } catch {
      setError(
        "Unable to save. Check the time zone and that every open time is before its close time.",
      );
    }
  }
  return (
    <section className="panel">
      <h2>Business hours</h2>
      <p>The default time zone used for properties that don&apos;t set their own.</p>
      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}
      {saved && !error && (
        <p className="success" role="status">
          Saved.
        </p>
      )}
      <div className="form-grid">
        <label>
          Default time zone
          <input
            value={defaultTimeZoneId}
            onChange={(event) => setDefaultTimeZoneId(event.target.value)}
            placeholder="America/New_York"
          />
        </label>
      </div>
      <ul className="category-list">
        {days.map((row) => (
          <li key={row.day}>
            <span>
              <strong>{row.day}</strong>
            </span>
            <label>
              <input
                type="checkbox"
                checked={!row.closed}
                onChange={(event) => updateDay(row.day, { closed: !event.target.checked })}
              />{" "}
              Open
            </label>
            {!row.closed && (
              <>
                <input
                  type="time"
                  aria-label={`${row.day} opening time`}
                  value={row.open}
                  onChange={(event) => updateDay(row.day, { open: event.target.value })}
                />
                <input
                  type="time"
                  aria-label={`${row.day} closing time`}
                  value={row.close}
                  onChange={(event) => updateDay(row.day, { close: event.target.value })}
                />
              </>
            )}
          </li>
        ))}
      </ul>
      <button onClick={() => void save()} disabled={!defaultTimeZoneId.trim()}>
        Save business hours
      </button>
    </section>
  );
}

function NotificationPreferencesSection() {
  const [preferences, setPreferences] = useState<NotificationPreference[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    api.notificationPreferences
      .list()
      .then(setPreferences)
      .catch(() => setError("Unable to load notification preferences."));
  }, []);
  async function toggle(preference: NotificationPreference) {
    setError("");
    try {
      const updated = await api.notificationPreferences.setEnabled(
        preference.eventType,
        !preference.enabled,
      );
      setPreferences((current) =>
        current.map((p) => (p.eventType === updated.eventType ? updated : p)),
      );
    } catch {
      setError("Unable to change that preference.");
    }
  }
  return (
    <section className="panel">
      <h2>My notification preferences</h2>
      <p>
        Personal to your account — no delivery channel is wired up to these yet, so nothing is sent
        today.
      </p>
      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}
      <ul className="category-list">
        {preferences.map((preference) => (
          <li key={preference.eventType}>
            <label>
              <input
                type="checkbox"
                checked={preference.enabled}
                onChange={() => void toggle(preference)}
              />{" "}
              {NOTIFICATION_LABELS[preference.eventType]}
            </label>
          </li>
        ))}
      </ul>
    </section>
  );
}
