import { validateAlias, validateExpiry, validateUrl } from './validation';

describe('validateUrl', () => {
  it.each(['https://example.com', 'http://example.com/a?b=c', '  https://example.com  '])('accepts %s', (value) => {
    expect(validateUrl(value)).toBeNull();
  });

  it.each([
    ['', 'required'],
    ['   ', 'required'],
    ['example.com', 'http'],
    ['ftp://example.com', 'http'],
    ['javascript:alert(1)', 'http'],
    ['https://', 'valid'],
  ])('rejects "%s"', (value, expected) => {
    expect(validateUrl(value)).toContain(expected);
  });

  it('rejects a URL longer than 2048 characters', () => {
    expect(validateUrl('https://example.com/' + 'a'.repeat(2048))).toContain('2048');
  });
});

describe('validateAlias', () => {
  it('treats an empty alias as "generate one for me"', () => {
    expect(validateAlias('')).toBeNull();
    expect(validateAlias('   ')).toBeNull();
  });

  it.each(['abcdefgh', 'My_Alias-01', 'a'.repeat(30)])('accepts %s', (value) => {
    expect(validateAlias(value)).toBeNull();
  });

  it.each([
    ['short', '8'],
    ['a'.repeat(31), '30'],
    ['has space1', 'letters'],
    ['ελληνικά12', 'letters'],
    ['semi;colon', 'letters'],
  ])('rejects "%s"', (value, expected) => {
    expect(validateAlias(value)).toContain(expected);
  });
});

describe('validateExpiry', () => {
  // datetime-local values are local time, so build "now" the same way: the test must not depend on the machine's time zone.
  const now = new Date('2026-10-23T12:00');

  it('treats an empty value as "never expires"', () => {
    expect(validateExpiry('', now)).toBeNull();
  });

  it('accepts a moment in the future', () => {
    expect(validateExpiry('2026-10-23T12:01', now)).toBeNull();
  });

  it('rejects the past', () => {
    expect(validateExpiry('2020-01-01T00:00', now)).toContain('future');
  });

  it('rejects an unparseable value', () => {
    expect(validateExpiry('not a date', now)).toContain('valid');
  });
});
