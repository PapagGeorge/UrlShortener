#!/usr/bin/env bash
# End-to-end check of the running stack through the Gateway: create -> redirect -> cache -> alias -> unknown -> expire.
# Usage: scripts/smoke.sh [base-url]   (default http://127.0.0.1:8080). Needs only bash and curl. Never run on the dev Mac's Docker: there is none.
set -euo pipefail

BASE="${1:-${BASE_URL:-http://127.0.0.1:8080}}"
failures=0

check() { # description, expected, actual
  if [ "$2" = "$3" ]; then echo "ok   - $1"; else echo "FAIL - $1 (expected '$2', got '$3')"; failures=$((failures + 1)); fi
}

status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

create() { # json body -> prints the response body, status on the last line
  curl -s -w '\n%{http_code}' -X POST "$BASE/urls" -H 'Content-Type: application/json' -d "$1"
}

code_of() { sed -n 's/.*"shortUrl":"\([^"]*\)".*/\1/p' | sed 's|.*/||'; }

check "gateway is alive" 200 "$(status "$BASE/health")"

target="https://example.com/smoke?run=$$"
response="$(create "{\"originalUrl\":\"$target\"}")"
check "create returns 201" 201 "$(echo "$response" | tail -n1)"
code="$(echo "$response" | head -n1 | code_of)"
check "generated code has 7 characters" 7 "${#code}"

headers="$(curl -s -D - -o /dev/null "$BASE/$code")"
check "redirect is a 302" 302 "$(echo "$headers" | head -n1 | awk '{print $2}')"
check "redirect target" "$target" "$(echo "$headers" | tr -d '\r' | sed -n 's/^[Ll]ocation: //p')"
check "redirect is never cacheable" "no-store" "$(echo "$headers" | tr -d '\r' | sed -n 's/^[Cc]ache-[Cc]ontrol: //p')"
check "second redirect (served from the cache)" 302 "$(status "$BASE/$code")"
check "HEAD is supported" 302 "$(status -I "$BASE/$code")"

alias="smoke-alias-$(date +%s)-$$"
check "alias create returns 201" 201 "$(create "{\"originalUrl\":\"$target\",\"alias\":\"$alias\"}" | tail -n1)"
check "same alias again returns 409" 409 "$(create "{\"originalUrl\":\"$target\",\"alias\":\"$alias\"}" | tail -n1)"
check "alias redirects" 302 "$(status "$BASE/$alias")"

check "unknown code returns 404" 404 "$(status "$BASE/zzzzzzz")"
check "invalid url returns 400" 400 "$(create '{"originalUrl":"javascript:alert(1)"}' | tail -n1)"

expires="$(date -u -d '+3 seconds' +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -v+3S +%Y-%m-%dT%H:%M:%SZ)"
response="$(create "{\"originalUrl\":\"$target\",\"expiresAt\":\"$expires\"}")"
check "expiring create returns 201" 201 "$(echo "$response" | tail -n1)"
expiring="$(echo "$response" | head -n1 | code_of)"
check "expiring url redirects before it expires" 302 "$(status "$BASE/$expiring")"
sleep 5
check "expired url returns 410" 410 "$(status "$BASE/$expiring")"

if [ "$failures" -gt 0 ]; then echo "$failures check(s) failed"; exit 1; fi
echo "all checks passed"
