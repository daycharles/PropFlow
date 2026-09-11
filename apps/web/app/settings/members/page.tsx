"use client";
import { useEffect, useState } from "react";
import { AppShell } from "../../components/app-shell";
import { ProtectedPage } from "../../components/protected-page";
import {
  api,
  ApiError,
  type ActiveMember,
  type CreatedInvitation,
  type Invitation,
  type Session,
} from "../../../lib/api";

export default function Members() {
  return (
    <ProtectedPage capability="Identity.ManageMembers">
      {(session) => <MembersContent session={session} />}
    </ProtectedPage>
  );
}

function MembersContent({ session }: { session: Session }) {
  const [members, setMembers] = useState<ActiveMember[]>([]);
  const [pending, setPending] = useState<Invitation[]>([]);
  const [roles, setRoles] = useState<string[]>([]);
  const [error, setError] = useState("");
  const [email, setEmail] = useState("");
  const [inviteRole, setInviteRole] = useState("Read Only");
  const [created, setCreated] = useState<CreatedInvitation | null>(null);

  const load = () =>
    Promise.all([api.members.list(), api.invitations.list(), api.roles.list()])
      .then(([activeMembers, pendingInvitations, roleRows]) => {
        setMembers(activeMembers);
        setPending(pendingInvitations);
        setRoles(roleRows.map((row) => row.role));
      })
      .catch(() => setError("Unable to load members."));
  useEffect(() => {
    void load();
  }, []);

  async function invite() {
    setError("");
    setCreated(null);
    try {
      const invitation = await api.invitations.create({ email, role: inviteRole });
      setCreated(invitation);
      setEmail("");
      await load();
    } catch {
      setError("Unable to send invitation. Check the email address.");
    }
  }
  async function changeRole(userId: string, role: string) {
    setError("");
    try {
      await api.members.changeRole(userId, role);
      await load();
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 409
          ? "That change would leave no one able to manage members."
          : "Unable to change that member's role.",
      );
    }
  }
  async function remove(userId: string) {
    setError("");
    try {
      await api.members.remove(userId);
      await load();
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 409
          ? "That would leave no one able to manage members."
          : "Unable to remove that member.",
      );
    }
  }

  const inviteLink =
    created && typeof window !== "undefined"
      ? `${window.location.origin}/accept-invite/${created.token}`
      : "";

  return (
    <AppShell session={session}>
      <section className="panel">
        <h1>Members</h1>
        <p>Invite people to this organization and manage existing members&apos; roles.</p>
        {error && (
          <p className="message" role="alert">
            {error}
          </p>
        )}
        <div className="form-grid">
          <label>
            Email
            <input
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              placeholder="new.hire@example.com"
            />
          </label>
          <label>
            Role
            <select value={inviteRole} onChange={(event) => setInviteRole(event.target.value)}>
              {roles.map((role) => (
                <option key={role} value={role}>
                  {role}
                </option>
              ))}
            </select>
          </label>
          <button onClick={() => void invite()} disabled={!email.trim()}>
            Send invitation
          </button>
        </div>

        {created && (
          <p className="message" role="status">
            Invitation sent to {created.email}. Share this link with them — it is shown only once:
            <br />
            <code>{inviteLink}</code>
          </p>
        )}

        <h2>Pending invitations</h2>
        {pending.length === 0 ? (
          <p>No pending invitations.</p>
        ) : (
          <ul className="category-list">
            {pending.map((invitation) => (
              <li key={invitation.id}>
                <span>
                  <strong>{invitation.email}</strong>
                  <br />
                  <small>
                    {invitation.role} · expires{" "}
                    {new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(
                      new Date(invitation.expiresAt),
                    )}
                  </small>
                </span>
              </li>
            ))}
          </ul>
        )}

        <h2>Active members</h2>
        <ul className="category-list">
          {members.map((member) => (
            <li key={member.userId}>
              <span>
                <strong>{member.email}</strong>
              </span>
              <select
                value={member.role}
                onChange={(event) => void changeRole(member.userId, event.target.value)}
                aria-label={`Role for ${member.email}`}
              >
                {roles.map((role) => (
                  <option key={role} value={role}>
                    {role}
                  </option>
                ))}
              </select>
              <button className="secondary" onClick={() => void remove(member.userId)}>
                Remove
              </button>
            </li>
          ))}
        </ul>
      </section>
    </AppShell>
  );
}
