export interface MemoryChange {
  id: number;
  memoryEntryId: number;
  memoryEntryKey: string;
  messageId: number | null;
  operation: string;
  path: string;
  targetId: string;
  previousValueJson?: string;
  newValueJson?: string;
  reason: string;
  createdAtUtc: string;
}
