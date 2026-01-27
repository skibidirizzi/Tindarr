import { useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate, type Location } from "react-router-dom";
import { ApiError } from "../api/http";
import { getPreferences, updatePreferences } from "../api/client";
import type { UserPreferencesDto } from "../api/contracts";
import { TMDB_LANGUAGES, TMDB_MOVIE_GENRES, TMDB_REGIONS, TMDB_SORT_BY } from "../tmdb/knownValues";

type FormState = {
  includeAdult: boolean;
  minReleaseYear: string;
  maxReleaseYear: string;
  minRating: string;
  maxRating: string;
  sortBy: string;
  preferredGenres: number[];
  excludedGenres: number[];
  preferredOriginalLanguages: string[];
  preferredRegions: string[];
};

function toNullableNumber(value: string): number | null {
  if (!value.trim()) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function toForm(prefs: UserPreferencesDto): FormState {
  return {
    includeAdult: prefs.includeAdult,
    minReleaseYear: prefs.minReleaseYear?.toString() ?? "",
    maxReleaseYear: prefs.maxReleaseYear?.toString() ?? "",
    minRating: prefs.minRating?.toString() ?? "",
    maxRating: prefs.maxRating?.toString() ?? "",
    sortBy: prefs.sortBy ?? "popularity.desc",
    preferredGenres: [...(prefs.preferredGenres ?? [])],
    excludedGenres: [...(prefs.excludedGenres ?? [])],
    preferredOriginalLanguages: [...(prefs.preferredOriginalLanguages ?? [])],
    preferredRegions: [...(prefs.preferredRegions ?? [])]
  };
}

function toggleInList<T extends string | number>(list: readonly T[], value: T) {
  return list.includes(value) ? list.filter((x) => x !== value) : [...list, value];
}

function pillClass(checked: boolean, variant: "neutral" | "good" | "bad") {
  const base = "pill";
  const on = checked ? " is-on" : "";
  const kind = variant === "good" ? " pill--good" : variant === "bad" ? " pill--bad" : " pill--neutral";
  return `${base}${kind}${on}`;
}

type GenreState = "none" | "include" | "exclude";

function getGenreState(form: FormState, genreId: number): GenreState {
  if (form.preferredGenres.includes(genreId)) return "include";
  if (form.excludedGenres.includes(genreId)) return "exclude";
  return "none";
}

function cycleGenre(form: FormState, genreId: number): FormState {
  const state = getGenreState(form, genreId);

  if (state === "none") {
    return {
      ...form,
      preferredGenres: [...form.preferredGenres, genreId],
      excludedGenres: form.excludedGenres.filter((x) => x !== genreId)
    };
  }

  if (state === "include") {
    return {
      ...form,
      preferredGenres: form.preferredGenres.filter((x) => x !== genreId),
      excludedGenres: [...form.excludedGenres, genreId]
    };
  }

  return {
    ...form,
    preferredGenres: form.preferredGenres.filter((x) => x !== genreId),
    excludedGenres: form.excludedGenres.filter((x) => x !== genreId)
  };
}

export default function PreferencesPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const state = location.state as { backgroundLocation?: Location } | null;
  const backgroundLocation = state?.backgroundLocation;

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [prefs, setPrefs] = useState<UserPreferencesDto | null>(null);
  const [form, setForm] = useState<FormState | null>(null);

  const updatedAt = useMemo(() => (prefs?.updatedAtUtc ? new Date(prefs.updatedAtUtc) : null), [prefs?.updatedAtUtc]);

  function handleClose() {
    if (backgroundLocation) {
      navigate(-1);
      return;
    }
    navigate("/swipe", { replace: true });
  }

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        handleClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [backgroundLocation]);

  useEffect(() => {
    (async () => {
      try {
        setLoading(true);
        setError(null);
        const p = await getPreferences();
        setPrefs(p);
        setForm(toForm(p));
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load preferences.");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  async function handleSave() {
    if (!form) return;
    try {
      setSaving(true);
      setError(null);
      const updated = await updatePreferences({
        includeAdult: form.includeAdult,
        minReleaseYear: toNullableNumber(form.minReleaseYear) as number | null,
        maxReleaseYear: toNullableNumber(form.maxReleaseYear) as number | null,
        minRating: toNullableNumber(form.minRating) as number | null,
        maxRating: toNullableNumber(form.maxRating) as number | null,
        preferredGenres: form.preferredGenres,
        excludedGenres: form.excludedGenres,
        preferredOriginalLanguages: form.preferredOriginalLanguages,
        preferredRegions: form.preferredRegions,
        sortBy: form.sortBy?.trim() || "popularity.desc"
      });
      setPrefs(updated);
      setForm(toForm(updated));

      // Let the background swipedeck refresh immediately after a successful save.
      window.dispatchEvent(new Event("tindarr:preferencesUpdated"));
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else if (err instanceof Error) {
        setError(err.message);
      } else {
        setError("Failed to save preferences.");
      }
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal" aria-hidden={false}>
      <div className="modal__backdrop" onClick={handleClose} />
      <div
        className="modal__panel"
        role="dialog"
        aria-modal="true"
        aria-label="Preferences"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal__header">
          <div>
            <h2 className="modal__title">Preferences</h2>
            {updatedAt ? <p className="modal__subtitle">Last updated: {updatedAt.toLocaleString()}</p> : null}
          </div>
          <button type="button" className="button button--ghost modal__close" onClick={handleClose}>
            Close
          </button>
        </div>

        <div className="modal__body">
          {loading ? <div className="deck__state">Loading preferences…</div> : null}

          {!loading && error ? <div className="deck__state deck__state--error">{error}</div> : null}

          {!loading ? (
            form ? (
              <div className="form" style={{ marginTop: 0 }}>
                <label className="field" style={{ flexDirection: "row", alignItems: "center", gap: "0.75rem" }}>
                  <input
                    type="checkbox"
                    checked={form.includeAdult}
                    onChange={(e) => setForm({ ...form, includeAdult: e.target.checked })}
                  />
                  <span className="field__label" style={{ margin: 0 }}>
                    Include adult titles
                  </span>
                </label>

                <div style={{ display: "grid", gridTemplateColumns: "repeat(2, minmax(0, 1fr))", gap: "1rem" }}>
                  <label className="field">
                    <span className="field__label">Min release year</span>
                    <input
                      className="input"
                      value={form.minReleaseYear}
                      onChange={(e) => setForm({ ...form, minReleaseYear: e.target.value })}
                      inputMode="numeric"
                      placeholder="e.g. 1990"
                    />
                  </label>
                  <label className="field">
                    <span className="field__label">Max release year</span>
                    <input
                      className="input"
                      value={form.maxReleaseYear}
                      onChange={(e) => setForm({ ...form, maxReleaseYear: e.target.value })}
                      inputMode="numeric"
                      placeholder="e.g. 2026"
                    />
                  </label>

                  <label className="field">
                    <span className="field__label">Min rating</span>
                    <input
                      className="input"
                      value={form.minRating}
                      onChange={(e) => setForm({ ...form, minRating: e.target.value })}
                      inputMode="decimal"
                      placeholder="e.g. 6.5"
                    />
                  </label>
                  <label className="field">
                    <span className="field__label">Max rating</span>
                    <input
                      className="input"
                      value={form.maxRating}
                      onChange={(e) => setForm({ ...form, maxRating: e.target.value })}
                      inputMode="decimal"
                      placeholder="e.g. 10"
                    />
                  </label>
                </div>

                <label className="field">
                  <span className="field__label">Sort by</span>
                  <select className="input" value={form.sortBy} onChange={(e) => setForm({ ...form, sortBy: e.target.value })}>
                    {TMDB_SORT_BY.map((opt) => (
                      <option key={opt.value} value={opt.value}>
                        {opt.label}
                      </option>
                    ))}
                  </select>
                </label>

                <div className="field">
                  <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "1rem" }}>
                    <span className="field__label">Genres</span>
                    <button
                      type="button"
                      className="button button--ghost"
                      onClick={() => setForm({ ...form, preferredGenres: [], excludedGenres: [] })}
                    >
                      Clear genres
                    </button>
                  </div>

                  <div className="pickerRow" style={{ marginTop: "0.25rem" }}>
                    {TMDB_MOVIE_GENRES.map((g) => {
                      const state = getGenreState(form, g.id);
                      const checked = state !== "none";
                      const variant = state === "include" ? "good" : state === "exclude" ? "bad" : "neutral";

                      return (
                        <label key={g.id} className={pillClass(checked, variant)}>
                          <input
                            className="pill__input"
                            type="checkbox"
                            checked={checked}
                            onChange={() => setForm(cycleGenre(form, g.id))}
                          />
                          <span className="pill__label">{g.name}</span>
                        </label>
                      );
                    })}
                  </div>
                </div>

                <div className="field">
                  <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "1rem" }}>
                    <span className="field__label">Preferred original languages</span>
                    <button
                      type="button"
                      className="button button--ghost"
                      onClick={() => setForm({ ...form, preferredOriginalLanguages: [] })}
                    >
                      Clear
                    </button>
                  </div>
                  <div className="pickerRow">
                    {TMDB_LANGUAGES.map((opt) => (
                      <label key={opt.value} className={pillClass(form.preferredOriginalLanguages.includes(opt.value), "neutral")}>
                        <input
                          className="pill__input"
                          type="checkbox"
                          checked={form.preferredOriginalLanguages.includes(opt.value)}
                          onChange={() =>
                            setForm({
                              ...form,
                              preferredOriginalLanguages: toggleInList(form.preferredOriginalLanguages, opt.value)
                            })
                          }
                        />
                        <span className="pill__label">{opt.label}</span>
                      </label>
                    ))}
                  </div>
                </div>

                <div className="field">
                  <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "1rem" }}>
                    <span className="field__label">Preferred regions</span>
                    <button
                      type="button"
                      className="button button--ghost"
                      onClick={() => setForm({ ...form, preferredRegions: [] })}
                    >
                      Clear
                    </button>
                  </div>
                  <div className="pickerRow">
                    {TMDB_REGIONS.map((opt) => (
                      <label key={opt.value} className={pillClass(form.preferredRegions.includes(opt.value), "neutral")}>
                        <input
                          className="pill__input"
                          type="checkbox"
                          checked={form.preferredRegions.includes(opt.value)}
                          onChange={() =>
                            setForm({
                              ...form,
                              preferredRegions: toggleInList(form.preferredRegions, opt.value)
                            })
                          }
                        />
                        <span className="pill__label">{opt.label}</span>
                      </label>
                    ))}
                  </div>
                </div>

                <div className="form__actions">
                  <button className="button button--like" type="button" onClick={handleSave} disabled={saving}>
                    {saving ? "Saving…" : "Save preferences"}
                  </button>
                </div>
              </div>
            ) : (
              <div className="deck__state deck__state--error">Preferences failed to load.</div>
            )
          ) : null}
        </div>
      </div>
    </div>
  );
}

