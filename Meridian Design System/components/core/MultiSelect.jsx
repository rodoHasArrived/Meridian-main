// Meridian MultiSelect — checkboxes for batch selection in tables + action toolbar.
// Manages a Set of selected IDs; integrates with table rows via row key.
// Toolbar shows count + contextual actions (delete, export, reassign, etc.).

import React from 'react';
const { useState } = React;

export function MultiSelect({
  selectedIds = new Set(),
  onSelectionChange = () => {},
  totalCount = 0,
  actions = [], // [{id, label, icon?, onClick}]
  onClearSelection = () => {},
}) {
  const selectCount = selectedIds.size;
  const isAllSelected = selectCount === totalCount && totalCount > 0;
  const isPartialSelected = selectCount > 0 && selectCount < totalCount;

  const toggleSelectAll = () => {
    if (isAllSelected) {
      onSelectionChange(new Set());
    } else {
      // Note: caller must provide all IDs; this component only tracks selection state.
      // For a real table, this would be passed in as prop.
      onClearSelection();
    }
  };

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '12px',
        padding: '8px 12px',
        borderBottom: selectCount > 0 ? '1px solid var(--border)' : 'none',
        backgroundColor: selectCount > 0 ? 'var(--blue-a10)' : 'transparent',
        transition: 'background-color 0.2s ease',
      }}
    >
      {/* Select-all checkbox */}
      <label
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          cursor: 'pointer',
          userSelect: 'none',
        }}
      >
        <input
          type="checkbox"
          checked={isAllSelected}
          indeterminate={isPartialSelected}
          onChange={toggleSelectAll}
          style={{
            width: '16px',
            height: '16px',
            cursor: 'pointer',
            accentColor: 'var(--accent)',
          }}
        />
        <span style={{ fontSize: '12px', color: 'var(--text-secondary)', fontWeight: 500 }}>
          {selectCount > 0 ? `${selectCount} selected` : 'Select all'}
        </span>
      </label>

      {selectCount > 0 && (
        <>
          {/* Divider */}
          <div style={{ width: '1px', height: '20px', backgroundColor: 'var(--border)' }} />

          {/* Action buttons */}
          <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
            {actions.map((action) => (
              <button
                key={action.id}
                onClick={() => action.onClick(selectedIds)}
                style={{
                  padding: '6px 12px',
                  fontSize: '12px',
                  fontWeight: 500,
                  color: action.dangerous ? 'var(--red)' : 'var(--accent)',
                  backgroundColor: 'transparent',
                  border: action.dangerous ? '1px solid var(--red-a20)' : '1px solid var(--border)',
                  borderRadius: '2px',
                  cursor: 'pointer',
                  transition: 'all 0.15s ease',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '6px',
                }}
                onMouseEnter={(e) => {
                  e.currentTarget.style.backgroundColor = action.dangerous ? 'var(--red-a10)' : 'var(--bg-hover)';
                  e.currentTarget.style.borderColor = action.dangerous ? 'var(--red-a20)' : 'var(--border-hover)';
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.backgroundColor = 'transparent';
                  e.currentTarget.style.borderColor = action.dangerous ? 'var(--red-a20)' : 'var(--border)';
                }}
              >
                {action.icon && <span style={{ fontSize: '14px' }}>{action.icon}</span>}
                {action.label}
              </button>
            ))}
          </div>

          {/* Clear selection */}
          <button
            onClick={() => onSelectionChange(new Set())}
            style={{
              marginLeft: 'auto',
              padding: '4px 8px',
              fontSize: '11px',
              color: 'var(--text-muted)',
              backgroundColor: 'transparent',
              border: 'none',
              cursor: 'pointer',
              textDecoration: 'underline',
              transition: 'color 0.15s ease',
            }}
            onMouseEnter={(e) => (e.currentTarget.style.color = 'var(--text-secondary)')}
            onMouseLeave={(e) => (e.currentTarget.style.color = 'var(--text-muted)')}
          >
            Clear
          </button>
        </>
      )}
    </div>
  );
}

/**
 * Row checkbox — wire into each table row.
 * Pass the row's ID and whether it's selected; MultiSelect manages the Set.
 */
export function SelectCheckbox({ rowId, isSelected, onChange }) {
  return (
    <input
      type="checkbox"
      checked={isSelected}
      onChange={(e) => onChange(rowId, e.target.checked)}
      style={{
        width: '16px',
        height: '16px',
        cursor: 'pointer',
        accentColor: 'var(--accent)',
      }}
    />
  );
}
