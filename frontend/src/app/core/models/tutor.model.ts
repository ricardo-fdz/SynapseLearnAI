export interface Tutor {
  id: number;
  name: string;
  description: string;
  systemPromptContent: string;
  geminiModel: string;
  llmProfile: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface LlmProfileOption {
  name: string;
  provider: string;
  model: string;
  fallbackProfiles: string[];
}

export interface LlmProfilesCatalog {
  defaultProfile: string;
  profiles: LlmProfileOption[];
}
