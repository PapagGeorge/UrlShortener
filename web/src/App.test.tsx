import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';

const created = (shortUrl: string) =>
  new Response(JSON.stringify({ shortUrl }), { status: 201, headers: { 'Content-Type': 'application/json' } });

describe('App', () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it('creates a short link and shows it', async () => {
    fetchMock.mockResolvedValue(created('http://s.test/abc1234'));
    const user = userEvent.setup();
    render(<App />);

    await user.type(screen.getByLabelText(/long url/i), 'https://example.com/some/long/path');
    await user.click(screen.getByRole('button', { name: /shorten/i }));

    expect(await screen.findByRole('link', { name: 'http://s.test/abc1234' })).toHaveAttribute('href', 'http://s.test/abc1234');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('does not call the API while the URL is invalid, and says why', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.type(screen.getByLabelText(/long url/i), 'not-a-url');
    await user.click(screen.getByRole('button', { name: /shorten/i }));

    expect(screen.getByRole('alert')).toHaveTextContent(/http/i);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('validates the alias before calling the API', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.type(screen.getByLabelText(/long url/i), 'https://example.com');
    await user.type(screen.getByLabelText(/custom alias/i), 'short');
    await user.click(screen.getByRole('button', { name: /shorten/i }));

    expect(screen.getByRole('alert')).toHaveTextContent(/8/);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('shows the server error, e.g. a taken alias', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ detail: 'That alias is already taken.' }), { status: 409, headers: { 'Content-Type': 'application/json' } }),
    );
    const user = userEvent.setup();
    render(<App />);

    await user.type(screen.getByLabelText(/long url/i), 'https://example.com');
    await user.type(screen.getByLabelText(/custom alias/i), 'taken-alias');
    await user.click(screen.getByRole('button', { name: /shorten/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent('That alias is already taken.');
  });

  it('disables the button while the request is in flight', async () => {
    let finish!: (r: Response) => void;
    fetchMock.mockReturnValue(new Promise<Response>((resolve) => (finish = resolve)));
    const user = userEvent.setup();
    render(<App />);

    await user.type(screen.getByLabelText(/long url/i), 'https://example.com');
    await user.click(screen.getByRole('button', { name: /shorten/i }));

    expect(screen.getByRole('button', { name: /shortening/i })).toBeDisabled();
    finish(created('http://s.test/abc1234'));
    await screen.findByRole('link', { name: 'http://s.test/abc1234' });
  });

  it('copies the short link to the clipboard', async () => {
    fetchMock.mockResolvedValue(created('http://s.test/abc1234'));
    const user = userEvent.setup();
    render(<App />);
    await user.type(screen.getByLabelText(/long url/i), 'https://example.com');
    await user.click(screen.getByRole('button', { name: /shorten/i }));
    await screen.findByRole('link', { name: 'http://s.test/abc1234' });

    await user.click(screen.getByRole('button', { name: /copy/i }));

    await waitFor(async () => expect(await navigator.clipboard.readText()).toBe('http://s.test/abc1234'));
    expect(screen.getByRole('button', { name: /copied/i })).toBeInTheDocument();
  });

  it('sends the expiry as an ISO instant', async () => {
    fetchMock.mockResolvedValue(created('http://s.test/abc1234'));
    const user = userEvent.setup();
    render(<App />);

    await user.type(screen.getByLabelText(/long url/i), 'https://example.com');
    await user.type(screen.getByLabelText(/expires/i), '2099-01-01T10:30');
    await user.click(screen.getByRole('button', { name: /shorten/i }));

    await screen.findByRole('link');
    const body = JSON.parse(fetchMock.mock.calls[0][1].body);
    expect(body.expiresAt).toBe(new Date('2099-01-01T10:30').toISOString());
  });
});
