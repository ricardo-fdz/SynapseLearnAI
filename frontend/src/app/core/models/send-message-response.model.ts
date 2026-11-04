import type { Message } from './message.model';

export interface AppliedMemoryChangeSummary {
  memoryChangeId: number;
  key: string;
  operation: string;
  path: string;
  targetId?: string | null;
  reason: string;
}

export interface SendMessageResponse {
  userMessage: Message;
  assistantMessage: Message;
  appliedMemoryChanges: AppliedMemoryChangeSummary[];
}
