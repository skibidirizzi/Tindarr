export const CONFIGURED_SCOPES_UPDATED_EVENT = "tindarr:configuredScopesUpdated";

export function notifyConfiguredScopesUpdated(): void {
  window.dispatchEvent(new Event(CONFIGURED_SCOPES_UPDATED_EVENT));
}
