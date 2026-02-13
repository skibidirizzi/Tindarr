import { useEffect, useMemo, useState } from "react";
import {
  ensureTmdbBulkJobRunner,
  getTmdbBulkJob,
  requestStopTmdbBulkJob,
  subscribeTmdbBulkJob,
  type TmdbBulkJob
} from "../tmdb/tmdbBulkJob";

function titleFor(job: TmdbBulkJob) {
  return job.kind === "details" ? "TMDB: Fetch all details" : "TMDB: Fetch all images";
}

export default function TmdbBulkJobToast() {
  const [job, setJob] = useState<TmdbBulkJob | null>(() => getTmdbBulkJob());

  useEffect(() => {
    ensureTmdbBulkJobRunner();
    return subscribeTmdbBulkJob(setJob);
  }, []);

  const visible = useMemo(() => Boolean(job?.running), [job?.running]);

  if (!visible || !job) {
    return null;
  }

  return (
    <div
      style={{
        position: "fixed",
        right: "1rem",
        bottom: "1rem",
        zIndex: 1000,
        width: "min(420px, calc(100vw - 2rem))"
      }}
    >
      <div className="deck__state" style={{ textAlign: "left" }}>
        <div style={{ display: "flex", justifyContent: "space-between", gap: "0.75rem", alignItems: "flex-start" }}>
          <div style={{ display: "grid", gap: "0.25rem" }}>
            <div style={{ fontWeight: 700 }}>{titleFor(job)}</div>
            <div style={{ opacity: 0.85, fontSize: "0.95rem" }}>{job.status}</div>
            <div style={{ opacity: 0.8, fontSize: "0.9rem" }}>
              Processed: {job.processed}{job.failures ? ` (${job.failures} failed)` : ""}
            </div>
          </div>

          <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", justifyContent: "flex-end" }}>
            <button
              type="button"
              className="button button--neutral"
              onClick={requestStopTmdbBulkJob}
              disabled={!job.running}
            >
              Stop
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
