export type ServiceScope = {
  serviceType: string;
  serverId: string;
};

const STORAGE_KEY = "tindarr:serviceScope:v1";
export const SERVICE_SCOPE_UPDATED_EVENT = "tindarr:serviceScopeUpdated";

export const DEFAULT_SERVICE_SCOPE: ServiceScope = {
  serviceType: "tmdb",
  serverId: "tmdb"
};

function normalizeServiceType(value: string) {
  return value.trim().toLowerCase();
}

function normalizeServerId(value: string) {
  return value.trim();
}

export function getServiceScope(): ServiceScope {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return DEFAULT_SERVICE_SCOPE;

    const parsed = JSON.parse(raw) as Partial<ServiceScope>;
    const serviceType = typeof parsed.serviceType === "string" ? normalizeServiceType(parsed.serviceType) : "";
    const serverId = typeof parsed.serverId === "string" ? normalizeServerId(parsed.serverId) : "";

    if (!serviceType || !serverId) return DEFAULT_SERVICE_SCOPE;

    return { serviceType, serverId };
  } catch {
    return DEFAULT_SERVICE_SCOPE;
  }
}

export function setServiceScope(scope: ServiceScope) {
  try {
    const normalized: ServiceScope = {
      serviceType: normalizeServiceType(scope.serviceType),
      serverId: normalizeServerId(scope.serverId)
    };

    localStorage.setItem(STORAGE_KEY, JSON.stringify(normalized));
  } catch {
    // Ignore quota / storage disabled.
  }
}

export function setServiceScopeAndNotify(scope: ServiceScope) {
  setServiceScope(scope);
  window.dispatchEvent(new Event(SERVICE_SCOPE_UPDATED_EVENT));
}
