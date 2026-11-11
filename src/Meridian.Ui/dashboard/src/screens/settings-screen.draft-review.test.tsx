import { act, renderHook } from "@testing-library/react";
import { useState } from "react";
import { beforeEach, describe, expect, it } from "vitest";
import { synchronizeSettingsDraftActor, useSettingsDraftReview } from "./settings-screen.draft-review";

function useEditor(source: { owner: string; dueDate: string }, revision: string, actorKey?: string, scopeKey = "calendar:fund:period:workflow") {
  const [draft, setDraft] = useState({ owner: "", dueDate: "" });
  return { draft, setDraft, review: useSettingsDraftReview<typeof draft>(draft, source, (fields) => setDraft(fields), revision, actorKey ? { actorKey, scopeKey } : undefined) };
}

describe("governed Settings drafts", () => {
  beforeEach(() => synchronizeSettingsDraftActor(null));

  it("restores an unsaved draft after workspace remount and marks it conflicted against a changed authoritative source", () => {
    const source = { owner: "Operations", dueDate: "2026-06-01" };
    const first = renderHook(() => useEditor(source, "1", "actor-A"));
    act(() => first.result.current.setDraft({ ...source, owner: "Retained draft" }));
    first.unmount();
    const second = renderHook(() => useEditor(source, "1", "actor-A"));
    expect(second.result.current.draft.owner).toBe("Retained draft");
    expect(second.result.current.review.conflict).toBe(false);
    second.unmount();
    const third = renderHook(() => useEditor({ ...source, owner: "New authoritative owner" }, "2", "actor-A"));
    expect(third.result.current.draft.owner).toBe("Retained draft");
    expect(third.result.current.review.conflict).toBe(true);
    third.unmount();
    const changedActor = renderHook(() => useEditor(source, "1", "actor-B"));
    expect(changedActor.result.current.draft.owner).toBe("Operations");
    changedActor.unmount();
    const returnedActor = renderHook(() => useEditor(source, "1", "actor-A"));
    expect(returnedActor.result.current.draft.owner).toBe("Operations");
  });

  it("keeps distinct record scopes separate and removes retained data on discard", () => {
    const source = { owner: "Operations", dueDate: "2026-06-01" };
    const first = renderHook(() => useEditor(source, "1", "actor-A", "record-1"));
    act(() => first.result.current.setDraft({ ...source, owner: "Record one draft" }));
    first.unmount();
    const other = renderHook(() => useEditor(source, "1", "actor-A", "record-2"));
    expect(other.result.current.draft.owner).toBe("Operations");
    other.unmount();
    const restored = renderHook(() => useEditor(source, "1", "actor-A", "record-1"));
    act(() => restored.result.current.review.discard());
    restored.unmount();
    const discarded = renderHook(() => useEditor(source, "1", "actor-A", "record-1"));
    expect(discarded.result.current.draft.owner).toBe("Operations");
  });

  it("retains edited fields when the authoritative source changes and explicitly loads the latest source on discard", () => {
    const { result, rerender } = renderHook(({ source, revision }) => useEditor(source, revision), {
      initialProps: { source: { owner: "Operations", dueDate: "2026-06-01" }, revision: "1" }
    });
    act(() => result.current.setDraft({ owner: "Controller", dueDate: "2026-06-01" }));
    expect(result.current.review.changes).toEqual([{ field: "owner", before: "Operations", after: "Controller" }]);
    rerender({ source: { owner: "Accounting", dueDate: "2026-06-02" }, revision: "2" });
    expect(result.current.draft).toEqual({ owner: "Controller", dueDate: "2026-06-01" });
    expect(result.current.review.conflict).toBe(true);
    act(() => result.current.review.discard());
    expect(result.current.draft).toEqual({ owner: "Accounting", dueDate: "2026-06-02" });
    expect(result.current.review.dirty).toBe(false);
    expect(result.current.review.conflict).toBe(false);
  });

  it("refreshes clean editors and treats only a completed save as a new baseline", () => {
    const { result, rerender } = renderHook(({ source, revision }) => useEditor(source, revision), {
      initialProps: { source: { owner: "Operations", dueDate: "2026-06-01" }, revision: "1" }
    });
    rerender({ source: { owner: "Accounting", dueDate: "2026-06-02" }, revision: "2" });
    expect(result.current.draft.owner).toBe("Accounting");
    expect(result.current.review.conflict).toBe(false);
    act(() => result.current.setDraft({ owner: "Controller", dueDate: "2026-06-02" }));
    expect(result.current.review.dirty).toBe(true);
    act(() => result.current.review.markSaved());
    rerender({ source: { owner: "Controller", dueDate: "2026-06-02" }, revision: "3" });
    expect(result.current.review.dirty).toBe(false);
    expect(result.current.review.conflict).toBe(false);
  });

  it("detects a new authoritative version even when its displayed values are unchanged", () => {
    const source = { owner: "Operations", dueDate: "2026-06-01" };
    const { result, rerender } = renderHook(({ revision }) => useEditor(source, revision), { initialProps: { revision: "1" } });
    act(() => result.current.setDraft({ ...source, owner: "Controller" }));
    rerender({ revision: "2" });
    expect(result.current.review.conflict).toBe(true);
    expect(result.current.draft.owner).toBe("Controller");
  });
});
