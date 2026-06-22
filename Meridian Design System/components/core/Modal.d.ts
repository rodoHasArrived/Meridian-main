import React from "react";

export interface ModalProps {
  open?: boolean;
  onClose?: () => void;
  children: React.ReactNode;
}

export function Modal(props: ModalProps): React.ReactElement | null;

export interface ModalHeaderProps {
  title: string;
  onClose?: () => void;
}

export function ModalHeader(props: ModalHeaderProps): React.ReactElement;

export interface ModalBodyProps {
  children: React.ReactNode;
}

export function ModalBody(props: ModalBodyProps): React.ReactElement;

export interface ModalFooterProps {
  children: React.ReactNode;
}

export function ModalFooter(props: ModalFooterProps): React.ReactElement;

export interface ModalFormProps {
  open?: boolean;
  onClose?: () => void;
  title?: string;
  onSubmit?: () => void | Promise<void>;
  submitLabel?: string;
  cancelLabel?: string;
  loading?: boolean;
  children: React.ReactNode;
}

export function ModalForm(props: ModalFormProps): React.ReactElement | null;
