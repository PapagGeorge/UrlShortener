import { createShortUrl } from './api';

const respond = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });

describe('createShortUrl', () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it('posts the camelCase body to /api/urls and returns the short URL', async () => {
    fetchMock.mockResolvedValue(respond(201, { shortUrl: 'http://s.test/abc1234' }));

    const result = await createShortUrl({ originalUrl: 'https://example.com', alias: 'my-alias-1', expiresAt: '2030-01-01T00:00:00.000Z' });

    expect(result).toEqual({ ok: true, shortUrl: 'http://s.test/abc1234' });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/urls');
    expect(init.method).toBe('POST');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(JSON.parse(init.body)).toEqual({ originalUrl: 'https://example.com', alias: 'my-alias-1', expiresAt: '2030-01-01T00:00:00.000Z' });
  });

  it('omits the optional fields that are empty', async () => {
    fetchMock.mockResolvedValue(respond(201, { shortUrl: 'http://s.test/abc1234' }));

    await createShortUrl({ originalUrl: 'https://example.com' });

    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ originalUrl: 'https://example.com' });
  });

  it('turns a 409 into a readable "alias taken" error', async () => {
    fetchMock.mockResolvedValue(respond(409, { detail: 'Alias already exists.', code: 'Alias.Taken' }));

    const result = await createShortUrl({ originalUrl: 'https://example.com', alias: 'taken-alias' });

    expect(result).toEqual({ ok: false, kind: 'conflict', message: 'Alias already exists.' });
  });

  it('turns a 400 into a validation error with the server message', async () => {
    fetchMock.mockResolvedValue(respond(400, { detail: 'The URL is not valid.', code: 'OriginalUrl.Invalid' }));

    const result = await createShortUrl({ originalUrl: 'https://example.com' });

    expect(result).toEqual({ ok: false, kind: 'validation', message: 'The URL is not valid.' });
  });

  it('tells the user how long to wait on a 429', async () => {
    fetchMock.mockResolvedValue(respond(429, { detail: 'Rate limit exceeded.' }, { 'Retry-After': '17' }));

    const result = await createShortUrl({ originalUrl: 'https://example.com' });

    expect(result).toMatchObject({ ok: false, kind: 'rate-limit' });
    expect((result as { message: string }).message).toContain('17');
  });

  it('does not leak server internals on a 500', async () => {
    fetchMock.mockResolvedValue(respond(500, { detail: 'NpgsqlException: password for user urlshortener_write' }));

    const result = await createShortUrl({ originalUrl: 'https://example.com' });

    expect(result).toMatchObject({ ok: false, kind: 'server' });
    expect((result as { message: string }).message).not.toContain('Npgsql');
  });

  it('survives a non-JSON error body (e.g. a proxy error page)', async () => {
    fetchMock.mockResolvedValue(new Response('<html>Bad gateway</html>', { status: 502 }));

    const result = await createShortUrl({ originalUrl: 'https://example.com' });

    expect(result).toMatchObject({ ok: false, kind: 'server' });
  });

  it('reports a network failure', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    const result = await createShortUrl({ originalUrl: 'https://example.com' });

    expect(result).toMatchObject({ ok: false, kind: 'network' });
  });
});
