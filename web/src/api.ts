export interface CreateRequest {
  originalUrl: string;
  alias?: string;
  /** ISO 8601 instant. */
  expiresAt?: string;
}

export type ErrorKind = 'validation' | 'conflict' | 'rate-limit' | 'server' | 'network';

export type CreateResult = { ok: true; shortUrl: string } | { ok: false; kind: ErrorKind; message: string };

// nginx (and the Vite dev server) forward /api/* to the Gateway with the prefix removed, so the browser stays on one origin.
const ENDPOINT = '/api/urls';

async function problemDetail(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { detail?: unknown };
    return typeof body.detail === 'string' && body.detail !== '' ? body.detail : null;
  } catch {
    return null;
  }
}

export async function createShortUrl(request: CreateRequest): Promise<CreateResult> {
  const body: CreateRequest = { originalUrl: request.originalUrl };
  if (request.alias) body.alias = request.alias;
  if (request.expiresAt) body.expiresAt = request.expiresAt;

  let response: Response;
  try {
    response = await fetch(ENDPOINT, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  } catch {
    return { ok: false, kind: 'network', message: 'Could not reach the server. Check your connection and try again.' };
  }

  if (response.status === 201) {
    const created = (await response.json()) as { shortUrl: string };
    return { ok: true, shortUrl: created.shortUrl };
  }

  if (response.status === 400) {
    return { ok: false, kind: 'validation', message: (await problemDetail(response)) ?? 'The request was not valid.' };
  }
  if (response.status === 409) {
    return { ok: false, kind: 'conflict', message: (await problemDetail(response)) ?? 'That alias is already taken.' };
  }
  if (response.status === 429) {
    const seconds = response.headers.get('Retry-After');
    return { ok: false, kind: 'rate-limit', message: seconds ? `Too many requests. Try again in ${seconds} second(s).` : 'Too many requests. Try again shortly.' };
  }
  // 5xx and anything unexpected: never show server-provided text, it may contain internals.
  return { ok: false, kind: 'server', message: 'Something went wrong on our side. Please try again.' };
}
