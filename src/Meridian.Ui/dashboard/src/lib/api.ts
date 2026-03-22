import type { ResearchWorkspaceResponse, SessionInfo } from "@/types";

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    headers: {
      Accept: "application/json"
    }
  });

  if (!response.ok) {
    throw new Error(`Request failed for ${path} (${response.status})`);
  }

  return response.json() as Promise<T>;
}

export function getSession() {
  return getJson<SessionInfo>("/api/workstation/session");
}

export function getResearchWorkspace() {
  return getJson<ResearchWorkspaceResponse>("/api/workstation/research");
}
