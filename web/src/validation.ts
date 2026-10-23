// Mirrors the server rules for fast feedback only. The Write Service stays the authority and re-validates everything.

const MAX_URL_LENGTH = 2048;
const ALIAS_PATTERN = /^[A-Za-z0-9_-]+$/;

export function validateUrl(raw: string): string | null {
  const value = raw.trim();
  if (value === '') return 'A URL is required.';
  if (value.length > MAX_URL_LENGTH) return `The URL must be at most ${MAX_URL_LENGTH} characters.`;

  let parsed: URL;
  try {
    parsed = new URL(value);
  } catch {
    return /^[a-z][a-z0-9+.-]*:/i.test(value) ? 'Enter a valid URL.' : 'The URL must start with http:// or https://.';
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return 'The URL must start with http:// or https://.';
  if (parsed.hostname === '') return 'Enter a valid URL.';
  return null;
}

export function validateAlias(raw: string): string | null {
  const value = raw.trim();
  if (value === '') return null;
  if (value.length < 8 || value.length > 30) return 'The alias must be 8 to 30 characters long.';
  if (!ALIAS_PATTERN.test(value)) return 'The alias may only contain letters, digits, _ and -.';
  return null;
}

/** `value` is the content of a datetime-local input (local time, no zone). */
export function validateExpiry(value: string, now: Date = new Date()): string | null {
  if (value === '') return null;
  const at = new Date(value);
  if (Number.isNaN(at.getTime())) return 'Enter a valid date and time.';
  if (at <= now) return 'The expiry must be in the future.';
  return null;
}
