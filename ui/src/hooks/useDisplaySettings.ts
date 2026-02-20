import { useEffect, useState } from "react";
import { getDisplaySettings } from "../api/client";
import type { DateTimeDisplaySettings } from "../utils/formatDateTime";
import { DEFAULT_DISPLAY } from "../utils/formatDateTime";

/**
 * Returns display settings for date/time (mode, time zone, date order).
 * Use with formatDateTime(value, displaySettings ?? DEFAULT_DISPLAY).
 */
export function useDisplaySettings(): DateTimeDisplaySettings | null {
  const [displaySettings, setDisplaySettings] = useState<DateTimeDisplaySettings | null>(null);

  useEffect(() => {
    getDisplaySettings()
      .then((s) =>
        setDisplaySettings({
          dateTimeDisplayMode: s.dateTimeDisplayMode || "locale",
          timeZoneId: s.timeZoneId ?? "Local",
          dateOrder: s.dateOrder ?? "locale"
        })
      )
      .catch(() => {});
  }, []);

  return displaySettings;
}

export { DEFAULT_DISPLAY };
