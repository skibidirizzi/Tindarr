type CastDeviceInfo = {
  friendlyName: string;
};

const CAST_SDK_URL = "https://www.gstatic.com/cv/js/sender/v1/cast_sender.js?loadCastFramework=1";
const DEFAULT_MEDIA_RECEIVER_APP_ID = "CC1AD845";

function isIos(): boolean {
  const ua = navigator.userAgent;
  return /iPad|iPhone|iPod/i.test(ua);
}

function isSafari(): boolean {
  const ua = navigator.userAgent;
  return /Safari/i.test(ua) && !/Chrome|Chromium|Edg|OPR/i.test(ua);
}

export function shouldUseGoogleCastSdk(): boolean {
  // Google Cast Web Sender SDK isn't supported on iOS/Safari.
  if (typeof window === "undefined") return false;
  if (isIos()) return false;
  if (isSafari()) return false;
  return true;
}

function isCastFrameworkReady(): boolean {
  return Boolean((window as any).cast?.framework?.CastContext);
}

function ensureCastScriptTag(): HTMLScriptElement {
  const existing = document.querySelector(`script[src^="${CAST_SDK_URL}"]`) as HTMLScriptElement | null;
  if (existing) return existing;

  const script = document.createElement("script");
  script.src = CAST_SDK_URL;
  script.async = true;
  document.head.appendChild(script);
  return script;
}

export async function ensureGoogleCastSdkLoaded(): Promise<boolean> {
  if (!shouldUseGoogleCastSdk()) return false;
  if (isCastFrameworkReady()) return true;

  await new Promise<void>((resolve, reject) => {
    let settled = false;

    const timeout = window.setTimeout(() => {
      if (settled) return;
      settled = true;
      reject(new Error("Cast SDK load timed out"));
    }, 8000);

    (window as any).__onGCastApiAvailable = (isAvailable: boolean) => {
      if (settled) return;
      window.clearTimeout(timeout);
      settled = true;
      if (!isAvailable) {
        reject(new Error("Cast API not available"));
        return;
      }
      resolve();
    };

    const script = ensureCastScriptTag();
    script.addEventListener(
      "error",
      () => {
        if (settled) return;
        window.clearTimeout(timeout);
        settled = true;
        reject(new Error("Cast SDK failed to load"));
      },
      { once: true }
    );
  });

  return isCastFrameworkReady();
}

export function initGoogleCastContext(): void {
  if (!isCastFrameworkReady()) {
    throw new Error("Cast framework not ready");
  }

  const castAny = (window as any).cast as any;
  const chromeAny = (window as any).chrome as any;
  const receiverId = chromeAny?.cast?.media?.DEFAULT_MEDIA_RECEIVER_APP_ID ?? DEFAULT_MEDIA_RECEIVER_APP_ID;

  const context = castAny.framework.CastContext.getInstance();
  context.setOptions({
    receiverApplicationId: receiverId,
    autoJoinPolicy: chromeAny.cast.AutoJoinPolicy.ORIGIN_SCOPED,
  });
}

export async function requestCastSession(): Promise<CastDeviceInfo> {
  const castAny = (window as any).cast as any;
  const context = castAny.framework.CastContext.getInstance();
  await context.requestSession();
  const session = context.getCurrentSession();
  const device = session?.getCastDevice?.();
  const name = (device?.friendlyName ?? "Cast device") as string;
  return { friendlyName: name };
}

export function getCurrentCastDevice(): CastDeviceInfo | null {
  if (!isCastFrameworkReady()) return null;
  const castAny = (window as any).cast as any;
  const context = castAny.framework.CastContext.getInstance();
  const session = context.getCurrentSession();
  const device = session?.getCastDevice?.();
  const name = (device?.friendlyName ?? "") as string;
  return name ? { friendlyName: name } : null;
}

export async function loadMediaToCastSession(args: { url: string; contentType: string; title: string; subTitle?: string | null }): Promise<void> {
  if (!isCastFrameworkReady()) {
    throw new Error("Cast framework not ready");
  }

  const castAny = (window as any).cast as any;
  const chromeAny = (window as any).chrome as any;
  const context = castAny.framework.CastContext.getInstance();

  const session = context.getCurrentSession();
  if (!session) {
    throw new Error("No cast session");
  }

  const mediaInfo = new chromeAny.cast.media.MediaInfo(args.url, args.contentType);
  mediaInfo.metadata = new chromeAny.cast.media.GenericMediaMetadata();
  mediaInfo.metadata.title = args.title;
  if (args.subTitle) {
    mediaInfo.metadata.subtitle = args.subTitle;
  }

  const request = new chromeAny.cast.media.LoadRequest(mediaInfo);
  request.autoplay = true;
  await session.loadMedia(request);
}
