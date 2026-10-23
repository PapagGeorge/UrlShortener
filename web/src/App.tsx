import { type FormEvent, useState } from 'react';
import { createShortUrl } from './api';
import { validateAlias, validateExpiry, validateUrl } from './validation';

export function App() {
  const [url, setUrl] = useState('');
  const [alias, setAlias] = useState('');
  const [expiresAt, setExpiresAt] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [shortUrl, setShortUrl] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setShortUrl(null);
    setCopied(false);

    const problem = validateUrl(url) ?? validateAlias(alias) ?? validateExpiry(expiresAt);
    if (problem) {
      setError(problem);
      return;
    }

    setError(null);
    setBusy(true);
    const result = await createShortUrl({
      originalUrl: url.trim(),
      alias: alias.trim() || undefined,
      expiresAt: expiresAt ? new Date(expiresAt).toISOString() : undefined,
    });
    setBusy(false);

    if (result.ok) setShortUrl(result.shortUrl);
    else setError(result.message);
  }

  async function copy() {
    if (!shortUrl) return;
    await navigator.clipboard.writeText(shortUrl);
    setCopied(true);
  }

  return (
    <main>
      <h1>URL Shortener</h1>
      <form onSubmit={submit} noValidate>
        <label>
          Long URL
          <input type="url" value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://example.com/a/very/long/path" autoFocus />
        </label>
        <label>
          Custom alias <span className="hint">(optional, 8–30 letters, digits, _ or -)</span>
          <input type="text" value={alias} onChange={(e) => setAlias(e.target.value)} autoComplete="off" />
        </label>
        <label>
          Expires <span className="hint">(optional)</span>
          <input type="datetime-local" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
        </label>
        <button type="submit" disabled={busy}>
          {busy ? 'Shortening…' : 'Shorten'}
        </button>
      </form>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {shortUrl && (
        <section className="result" aria-live="polite">
          <a href={shortUrl} target="_blank" rel="noreferrer">
            {shortUrl}
          </a>
          <button type="button" onClick={copy}>
            {copied ? 'Copied!' : 'Copy'}
          </button>
        </section>
      )}
    </main>
  );
}
