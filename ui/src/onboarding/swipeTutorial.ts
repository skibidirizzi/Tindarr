const KEY_PREFIX = "tindarr:tutorial:swipe:v1";

function keyForUser(userId: string) {
  return `${KEY_PREFIX}:${userId}`;
}

export function hasCompletedSwipeTutorial(userId: string): boolean {
  try {
    return localStorage.getItem(keyForUser(userId)) === "1";
  } catch {
    return false;
  }
}

export function setSwipeTutorialCompleted(userId: string) {
  try {
    localStorage.setItem(keyForUser(userId), "1");
  } catch {
    // ignore
  }
}
