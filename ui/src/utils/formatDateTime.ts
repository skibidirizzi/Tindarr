/**
 * Display settings for date/time formatting.
 * - dateTimeDisplayMode: locale | 12h | 24h | relative
 * - timeZoneId: "Local" (browser), "UTC", or IANA timezone (e.g. America/New_York)
 * - dateOrder: "locale" (browser) | "mdy" (02/20/2026) | "dmy" (20/02/2026) | "ymd" (2026-02-20)
 */
export type DateTimeDisplaySettings = {
  dateTimeDisplayMode?: string;
  timeZoneId?: string;
  dateOrder?: string;
};

/** Default display settings when config is not yet loaded. */
export const DEFAULT_DISPLAY: DateTimeDisplaySettings = {
  dateTimeDisplayMode: "locale",
  timeZoneId: "Local",
  dateOrder: "locale"
};

/** IANA timezone options for dropdowns: Local, UTC, then runtime-supported IANA IDs. */
export function getTimeZoneOptions(): string[] {
  try {
    const iana =
      typeof Intl !== "undefined" && "supportedValuesOf" in Intl
        ? (Intl as unknown as { supportedValuesOf(key: string): string[] }).supportedValuesOf("timeZone")
        : [];
    return ["Local", "UTC", ...iana];
  } catch {
    return [
      "Local",
      "UTC",
      "America/New_York",
      "America/Chicago",
      "America/Denver",
      "America/Los_Angeles",
      "Europe/London",
      "Europe/Paris",
      "Europe/Berlin",
      "Asia/Tokyo",
      "Australia/Sydney"
    ];
  }
}

/**
 * Format an ISO date/time string for display.
 * Uses display settings: mode (12h/24h/locale/relative), time zone, and date order (mdy/dmy/ymd/locale).
 * Example: "2026-02-20T08:51:26.4658493+00:00" → "02/20/2026 8:51:26 AM" (mdy, 12h, Local) or "20/02/2026 08:51:26" (dmy, 24h).
 */
export function formatDateTime(
  value: string | null | undefined,
  display: DateTimeDisplaySettings | string
): string {
  if (value == null || value === "") return "—";
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;

  const settings: DateTimeDisplaySettings =
    typeof display === "string" ? { dateTimeDisplayMode: display } : display;
  const mode = (settings.dateTimeDisplayMode ?? "locale").toLowerCase();
  const timeZoneId = (settings.timeZoneId ?? "Local").trim() || "Local";
  const dateOrder = (settings.dateOrder ?? "locale").toLowerCase() || "locale";

  const timeZone: string | undefined = timeZoneId.toLowerCase() === "local" ? undefined : timeZoneId;
  const locale = dateOrderToLocale(dateOrder);

  try {
    if (mode === "relative") {
      return formatRelative(d, timeZone, locale);
    }

    const baseOpts: Intl.DateTimeFormatOptions = {
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      ...(timeZone ? { timeZone } : {})
    };

    if (mode === "12h") {
      return d.toLocaleString(locale, { ...baseOpts, hour12: true });
    }
    if (mode === "24h") {
      return d.toLocaleString(locale, { ...baseOpts, hour12: false });
    }
    return d.toLocaleString(locale, baseOpts);
  } catch {
    // Invalid IANA timezone (e.g. from DB); fall back to browser local time
    const safeOpts: Intl.DateTimeFormatOptions = {
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit"
    };
    if (mode === "12h") return d.toLocaleString(locale, { ...safeOpts, hour12: true });
    if (mode === "24h") return d.toLocaleString(locale, { ...safeOpts, hour12: false });
    return d.toLocaleString(locale, safeOpts);
  }
}

function dateOrderToLocale(dateOrder: string): string | undefined {
  switch (dateOrder) {
    case "mdy":
      return "en-US";
    case "dmy":
      return "en-GB";
    case "ymd":
      return "sv-SE";
    default:
      return undefined;
  }
}

function formatRelative(
  d: Date,
  timeZone: string | undefined,
  locale: string | undefined
): string {
  const now = new Date();
  const ms = now.getTime() - d.getTime();
  const sec = Math.floor(ms / 1000);
  const min = Math.floor(sec / 60);
  const hour = Math.floor(min / 60);
  const day = Math.floor(hour / 24);

  if (day >= 7 || ms < 0) {
    try {
      return d.toLocaleString(locale, {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        ...(timeZone ? { timeZone } : {})
      });
    } catch {
      return d.toLocaleString(locale, {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit"
      });
    }
  }
  if (day >= 1) return `${day} day${day === 1 ? "" : "s"} ago`;
  if (hour >= 1) return `${hour} hour${hour === 1 ? "" : "s"} ago`;
  if (min >= 1) return `${min} minute${min === 1 ? "" : "s"} ago`;
  if (sec >= 1) return `${sec} second${sec === 1 ? "" : "s"} ago`;
  return "just now";
}
