export interface Message {
  id: number;
  sessionId: number;
  role: 'user' | 'assistant' | 'system' | 'tool';
  content: string;
  createdAtUtc: string;
}
