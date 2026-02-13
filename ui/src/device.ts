export function isMobileDevice(): boolean {
  if (typeof navigator === "undefined") return false;

  // Prefer UA Client Hints when available.
  const uaData = navigator.userAgentData as undefined | { mobile?: boolean };
  if (typeof uaData?.mobile === "boolean") return uaData.mobile;

  const ua = navigator.userAgent ?? "";

  // Broad but practical detection for touch-first mobile browsers.
  return /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini|Mobile/i.test(ua);
}
