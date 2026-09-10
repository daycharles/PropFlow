"use client";
import { useEffect, useState } from "react";
import { AppShell } from "../../components/app-shell";
import { ProtectedPage } from "../../components/protected-page";
import { api, type Category, type Session } from "../../../lib/api";
export default function Categories() {
  return (
    <ProtectedPage capability="Settings.ManageCategories">
      {(session) => <CategoriesContent session={session} />}
    </ProtectedPage>
  );
}
function CategoriesContent({ session }: { session: Session }) {
  const [categories, setCategories] = useState<Category[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    api.categories
      .list()
      .then(setCategories)
      .catch(() => setError("Unable to load categories."));
  }, []);
  return (
    <AppShell session={session}>
      <section className="panel">
        <h1>Categories</h1>
        <p>Manage the categories available for new and edited work.</p>
        {error ? (
          <p className="message" role="alert">
            {error}
          </p>
        ) : (
          <ul className="category-list">
            {categories.map((category) => (
              <li key={category.id}>
                {category.name}
                {category.isArchived ? " (archived)" : ""}
              </li>
            ))}
          </ul>
        )}
      </section>
    </AppShell>
  );
}
