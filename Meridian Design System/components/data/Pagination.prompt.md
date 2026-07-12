Page control for server- or client-paged tables — page numbers with prev/next, sibling-count collapsing for long ranges, and an optional item summary.

```jsx
<Pagination currentPage={page} totalPages={pages} onPageChange={setPage}
  totalItems={1840} itemsPerPage={50} />
```

Prefer `VirtualizedList` for very long client-side data (no paging needed). Use Pagination when the source is genuinely paged or the operator benefits from stable page anchors.
