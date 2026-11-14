/**
 * `OperatorSurfaceVisitDto` (C3): the signed-in operator's visits to one workstation surface.
 * `previousVisitAt` is the "since your last visit" baseline. Recording a visit returns the one it
 * replaced and `visitedAt`; reading returns the last visit as `previousVisitAt` and a null `visitedAt`.
 */
export interface OperatorSurfaceVisit {
  surface: string;
  previousVisitAt: string | null;
  visitedAt: string | null;
}
