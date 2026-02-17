export type CastingSessionDto = {
  sessionId: string;
  deviceId: string;
  contentTitle: string;
  contentSubtitle: string;
  sessionState: string;
  contentType: string;
  startedAtUtc: string;
  expiresAtUtc: string;
  contentRuntimeSeconds: number;
};

export type CastingEventDto = {
  eventId: number;
  occurredAtUtc: string;
  eventType: string;
  message: string;
  deviceId?: string | null;
  errorDetails?: string | null;
};

export type CastingDiagnosticsDto = {
  activeSessions: CastingSessionDto[];
  recentEvents: CastingEventDto[];
};
