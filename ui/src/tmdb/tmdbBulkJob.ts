import { tmdbFetchMovieImages, tmdbFillMovieDetails, tmdbListStoredMovies } from "../api/client";

export type TmdbBulkJobKind = "details" | "images";

export type TmdbBulkJob = {
  version: 1;
  kind: TmdbBulkJobKind;
  running: boolean;
  cancelRequested: boolean;
  status: string;
  processed: number;
  failures: number;
  startedAtUtc: string;
  updatedAtUtc: string;
  ownerId: string | null;
  // Images require LocalProxy to have any effect.
  localProxyEnabled: boolean;
};

const STORAGE_KEY = "tindarr:tmdbBulkJob:v1";
const EVENT_NAME = "tindarr:tmdbBulkJobChanged";
const INSTANCE_KEY = "tindarr:uiInstanceId";

function getOrCreateInstanceId(): string {
  try {
    const existing = sessionStorage.getItem(INSTANCE_KEY);
    if (existing) {
      return existing;
    }
    const created = (globalThis.crypto && "randomUUID" in globalThis.crypto)
      ? globalThis.crypto.randomUUID()
      : `inst_${Math.random().toString(16).slice(2)}_${Date.now()}`;
    sessionStorage.setItem(INSTANCE_KEY, created);
    return created;
  } catch {
    return `inst_${Math.random().toString(16).slice(2)}_${Date.now()}`;
  }
}

const instanceId = getOrCreateInstanceId();

function nowUtcIso() {
  return new Date().toISOString();
}

function readJob(): TmdbBulkJob | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as TmdbBulkJob;
    if (!parsed || parsed.version !== 1) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

function writeJob(job: TmdbBulkJob | null) {
  if (!job) {
    localStorage.removeItem(STORAGE_KEY);
  } else {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(job));
  }
  window.dispatchEvent(new CustomEvent(EVENT_NAME, { detail: job }));
}

function patchJob(patch: Partial<TmdbBulkJob>) {
  const current = readJob();
  if (!current) {
    return;
  }
  writeJob({ ...current, ...patch, updatedAtUtc: nowUtcIso() });
}

export function getTmdbBulkJob(): TmdbBulkJob | null {
  return readJob();
}

export function subscribeTmdbBulkJob(listener: (job: TmdbBulkJob | null) => void): () => void {
  const handler = (e: Event) => {
    const ce = e as CustomEvent<TmdbBulkJob | null>;
    listener(ce.detail ?? null);
  };
  const storageHandler = (e: StorageEvent) => {
    if (e.key !== STORAGE_KEY) {
      return;
    }
    listener(readJob());
  };

  window.addEventListener(EVENT_NAME, handler);
  window.addEventListener("storage", storageHandler);
  return () => {
    window.removeEventListener(EVENT_NAME, handler);
    window.removeEventListener("storage", storageHandler);
  };
}

export function requestStopTmdbBulkJob() {
  patchJob({ cancelRequested: true, status: "Stopping…" });
}

export function clearTmdbBulkJob() {
  writeJob(null);
}

function delay(ms: number) {
  return new Promise<void>((resolve) => setTimeout(resolve, ms));
}

let runnerPromise: Promise<void> | null = null;

export function ensureTmdbBulkJobRunner() {
  if (runnerPromise) {
    return;
  }

  const job = readJob();
  if (!job || !job.running) {
    return;
  }

  runnerPromise = runExistingJob(job).finally(() => {
    runnerPromise = null;
  });
}

function isHeartbeatStale(job: TmdbBulkJob) {
  const updatedMs = Date.parse(job.updatedAtUtc);
  if (!Number.isFinite(updatedMs)) {
    return true;
  }
  return Date.now() - updatedMs > 15_000;
}

async function runExistingJob(initial: TmdbBulkJob) {
  const current = readJob();
  if (!current || !current.running) {
    return;
  }

  // Claim ownership if this is our job, or if previous owner is stale.
  if (current.ownerId && current.ownerId !== instanceId && !isHeartbeatStale(current)) {
    return;
  }

  writeJob({ ...current, ownerId: instanceId, updatedAtUtc: nowUtcIso() });

  if (current.kind === "images" && !current.localProxyEnabled) {
    patchJob({
      running: false,
      status: "Failed: LocalProxy mode is not enabled.",
      ownerId: null
    });
    return;
  }

  const take = 50;

  let processed = current.processed;
  let failures = current.failures;

  try {
    patchJob({ status: current.status || "Resuming…" });

    for (let loops = 0; loops < 200; loops++) {
      const jobNow = readJob();
      if (!jobNow || !jobNow.running) {
        return;
      }
      if (jobNow.cancelRequested) {
        patchJob({ running: false, status: `Stopped. ${processed} processed${failures ? ` (${failures} failed)` : ""}.`, ownerId: null });
        return;
      }

      const res = await tmdbListStoredMovies({
        skip: 0,
        take,
        missingDetailsOnly: jobNow.kind === "details",
        missingImagesOnly: jobNow.kind === "images"
      });

      if (!res.items || res.items.length === 0) {
        break;
      }

      patchJob({ status: jobNow.kind === "details" ? "Filling details…" : "Fetching images…" });

      for (const m of res.items) {
        const jobLoop = readJob();
        if (!jobLoop || !jobLoop.running) {
          return;
        }
        if (jobLoop.cancelRequested) {
          patchJob({ running: false, status: `Stopped. ${processed} processed${failures ? ` (${failures} failed)` : ""}.`, ownerId: null });
          return;
        }

        try {
          if (jobLoop.kind === "details") {
            await tmdbFillMovieDetails(m.tmdbId, false);
          } else {
            await tmdbFetchMovieImages(m.tmdbId, { includePoster: true, includeBackdrop: true });
          }
        } catch {
          failures++;
        } finally {
          processed++;
          patchJob({ processed, failures });
        }

        await delay(150);
      }
    }

    patchJob({ running: false, status: `Done. ${processed} processed${failures ? ` (${failures} failed)` : ""}.`, ownerId: null, processed, failures });
  } catch {
    patchJob({ running: false, status: `Failed. ${processed} processed${failures ? ` (${failures} failed)` : ""}.`, ownerId: null, processed, failures });
  }
}

async function startNewJob(kind: TmdbBulkJobKind, localProxyEnabled: boolean) {
  const existing = readJob();
  if (existing?.running) {
    return;
  }

  const job: TmdbBulkJob = {
    version: 1,
    kind,
    running: true,
    cancelRequested: false,
    status: kind === "details" ? "Scanning for missing details…" : "Scanning for missing images…",
    processed: 0,
    failures: 0,
    startedAtUtc: nowUtcIso(),
    updatedAtUtc: nowUtcIso(),
    ownerId: instanceId,
    localProxyEnabled
  };

  writeJob(job);

  if (!runnerPromise) {
    runnerPromise = runExistingJob(job).finally(() => {
      runnerPromise = null;
    });
  }
}

export async function startTmdbBulkFetchAllDetails() {
  await startNewJob("details", false);
}

export async function startTmdbBulkFetchAllImages(localProxyEnabled: boolean) {
  await startNewJob("images", localProxyEnabled);
}
