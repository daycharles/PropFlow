"use client";
import { FormEvent, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api, ApiError } from "../../../lib/api";

// PF-S01.09: the invited person has no session yet, so this page is deliberately outside
// ProtectedPage/AppShell - it is reached straight from the link a Members-page admin shares.
export default function AcceptInvite() {
  const params = useParams<{ token: string }>();
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState("");
  const [accepted, setAccepted] = useState(false);

  async function accept(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage("");
    if (password !== confirm) {
      setMessage("Passwords do not match.");
      return;
    }
    setSubmitting(true);
    try {
      await api.invitations.accept(params.token, password);
      setAccepted(true);
    } catch (error) {
      setMessage(
        error instanceof ApiError
          ? error.status === 404
            ? "This invitation link is not valid."
            : error.status === 410
              ? "This invitation has expired. Ask your admin to send a new one."
              : error.status === 409
                ? "This invitation has already been accepted — try signing in instead."
                : error.message
          : "Something went wrong. Please try again.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  if (accepted)
    return (
      <main className="centered">
        <h1>You&apos;re all set</h1>
        <p>
          Your account is ready. Ask whoever invited you for your organization&apos;s sign-in
          details, then sign in below.
        </p>
        <Link href="/">Go to sign in</Link>
      </main>
    );

  return (
    <main className="centered">
      <h1>Accept your invitation</h1>
      <p>Choose a password to finish setting up your account.</p>
      <form onSubmit={accept}>
        <label>
          Password
          <input
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
            minLength={12}
          />
        </label>
        <label>
          Confirm password
          <input
            type="password"
            autoComplete="new-password"
            value={confirm}
            onChange={(event) => setConfirm(event.target.value)}
            required
            minLength={12}
          />
        </label>
        <button disabled={submitting}>{submitting ? "Setting up…" : "Accept invitation"}</button>
      </form>
      {message && (
        <p className="message" role="alert">
          {message}
        </p>
      )}
    </main>
  );
}
