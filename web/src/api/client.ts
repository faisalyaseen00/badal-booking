import { config } from "../config";

// Error shape returned by the API (RFC 7807 ProblemDetails).
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

export type Role = "Customer" | "Performer";
export interface ApiUser { id: string; email: string; role: Role }
export interface AuthResponse { accessToken: string; expiresAtUtc: string; user: ApiUser }

type Options = { method?: string; body?: unknown; token?: string | null; headers?: Record<string, string> };

// Thin fetch wrapper: JSON in/out, Bearer token, and readable errors.
export async function apiFetch<T>(path: string, { method = "GET", body, token, headers = {} }: Options = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(config.apiBaseUrl + path, {
      method,
      headers: {
        ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...headers,
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    // Render's free plan sleeps when idle; the first request can take ~50 s to wake it.
    throw new ApiError(0, "Can't reach the server. If it was asleep, try again in a few seconds.");
  }

  const text = await response.text();
  const data = text ? JSON.parse(text) : undefined;
  if (!response.ok) {
    throw new ApiError(response.status, data?.title ?? `Request failed (${response.status})`, data?.errors ?? {});
  }
  return data as T;
}
