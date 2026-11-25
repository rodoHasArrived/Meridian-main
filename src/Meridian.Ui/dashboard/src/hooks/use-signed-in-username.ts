import { useEffect, useState } from "react";
import { getSignedInUsername } from "@/lib/api/auth.api";

// One read per page load: every screen that needs the operator shares it, and a failed read is retried
// by the next screen that mounts rather than cached as "nobody".
let cached: Promise<string | null> | null = null;

function loadOnce(): Promise<string | null> {
  if (!cached) {
    cached = getSignedInUsername().catch((error: unknown) => {
      cached = null;
      throw error;
    });
  }
  return cached;
}

/** Test seam: forget the cached operator. */
export function resetSignedInUsernameCache(): void {
  cached = null;
}

/**
 * The signed-in operator's username, or null until it is known or when there is no named session.
 * Prefer this over the workstation session's `displayName`, which is a desk label, not the person.
 */
export function useSignedInUsername(): string | null {
  const [username, setUsername] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    loadOnce()
      .then((name) => {
        if (active) setUsername(name);
      })
      .catch(() => {
        // Unknown operator: identity-keyed state (recovery drafts) simply stays off.
      });
    return () => {
      active = false;
    };
  }, []);
  return username;
}
