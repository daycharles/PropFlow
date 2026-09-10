"use client";
import { useEffect, useState } from "react";
import { AppShell } from "../../components/app-shell";
import { ProtectedPage } from "../../components/protected-page";
import { api, type AutomationRule, type Session } from "../../../lib/api";

export default function AutomationSettings() {
  return (
    <ProtectedPage capability="Settings.ManageAutomationRules">
      {(session) => <AutomationContent session={session} />}
    </ProtectedPage>
  );
}
function AutomationContent({ session }: { session: Session }) {
  const [rules, setRules] = useState<AutomationRule[]>([]);
  const [error, setError] = useState("");
  const [name, setName] = useState("");
  const [trigger, setTrigger] = useState("WorkCreated");
  const [priority, setPriority] = useState("Critical");
  const load = () =>
    api.automationRules
      .list()
      .then(setRules)
      .catch(() => setError("Unable to load automation rules."));
  useEffect(() => {
    void load();
  }, []);
  async function create() {
    setError("");
    try {
      await api.automationRules.create({
        name,
        trigger,
        conditions: [],
        actions: [{ kind: "SetPriority", priority }],
      });
      setName("");
      await load();
    } catch {
      setError("Unable to create rule. Add a name and action.");
    }
  }
  async function toggle(rule: AutomationRule) {
    try {
      const updated = await api.automationRules.setEnabled(rule.id, !rule.isEnabled);
      setRules((current) => current.map((item) => (item.id === updated.id ? updated : item)));
    } catch {
      setError("Unable to change rule status.");
    }
  }
  return (
    <AppShell session={session}>
      <section className="panel">
        <h1>Automation rules</h1>
        <p>Manage the closed v1 WHEN / IF / THEN rules for this organization.</p>
        {error && (
          <p className="message" role="alert">
            {error}
          </p>
        )}
        <div className="form-grid">
          <label>
            Rule name
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="Emergency work priority"
            />
          </label>
          <label>
            When
            <select value={trigger} onChange={(event) => setTrigger(event.target.value)}>
              <option value="WorkCreated">Work created</option>
              <option value="WorkStatusChanged">Work status changed</option>
            </select>
          </label>
          <label>
            Then
            <select value={priority} onChange={(event) => setPriority(event.target.value)}>
              <option value="Critical">Set priority: Critical</option>
              <option value="High">Set priority: High</option>
              <option value="Normal">Set priority: Normal</option>
              <option value="Low">Set priority: Low</option>
            </select>
          </label>
          <button onClick={() => void create()} disabled={!name.trim()}>
            Create rule
          </button>
        </div>
        <ul className="category-list">
          {rules.map((rule) => (
            <li key={rule.id}>
              <span>
                <strong>{rule.name}</strong>
                <br />
                <small>
                  When: {rule.trigger} · {rule.actions.length} action
                  {rule.actions.length === 1 ? "" : "s"}
                </small>
              </span>
              <button className="secondary" onClick={() => void toggle(rule)}>
                {rule.isEnabled ? "Disable" : "Enable"}
              </button>
            </li>
          ))}
        </ul>
      </section>
    </AppShell>
  );
}
