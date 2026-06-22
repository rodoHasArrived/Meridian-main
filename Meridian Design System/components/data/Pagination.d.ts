import React from "react";

export interface PaginationProps {
  currentPage?: number;
  totalPages?: number;
  onPageChange?: (page: number) => void;
  totalItems?: number;
  itemsPerPage?: number;
  siblingCount?: number;
}

export function Pagination(props: PaginationProps): React.ReactElement;
