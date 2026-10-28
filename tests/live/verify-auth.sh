#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Fr4ctalNoise and Project A contributors.
#
# Project A sign-in test (design doc, verification 1) for YOUR server. Run on Unraid:
#   bash verify-auth.sh endpoints.tsv
#
# What it does:
#   - signs in two test users (an ordinary one, and one you then disable) and reads an
#     API key you create, all typed at hidden prompts;
#   - calls every Project A endpoint with no token, the disabled user, the API key and the
#     ordinary user, and compares each answer with what the design requires;
#   - shows Project A's health check;
#   - signs the test sessions out again.
# What it does NOT do: change any setting, user, library or file. The only server-side
# effects are the sign-ins and sign-outs, which appear under Dashboard > Activity.
# Secrets: passwords, tokens and the API key are never put on a command line, in a file or
# on screen; they go to curl inside the Jellyfin container through standard input.
# The output contains only endpoint paths, status codes and the health check (no user data),
# so it is safe to paste back.

set -euo pipefail

CONTAINER="${CONTAINER:-Jellyfin}"
BASE="http://localhost:8096"
ENDPOINTS="${1:-endpoints.tsv}"
CLIENT='MediaBrowser Client="ProjectA-Verify", Device="Unraid", DeviceId="projecta-verify", Version="1.0"'

fail() { echo "ERROR: $*" >&2; exit 2; }

[ -f "$ENDPOINTS" ] || fail "endpoint list '$ENDPOINTS' not found (download it next to this script)"
docker inspect "$CONTAINER" >/dev/null 2>&1 || fail "no container named '$CONTAINER' (set CONTAINER=...)"
docker exec "$CONTAINER" sh -c 'command -v curl >/dev/null' || fail "curl not found inside the container"

# HTTP status of a request made inside the container. The token (may be empty) arrives on stdin.
status() {  # status METHOD PATH  (token on stdin)
    docker exec -i "$CONTAINER" sh -c '
        IFS= read -r T || true
        if [ -n "$T" ]; then AUTH="$0, Token=\"$T\""; else AUTH="$0"; fi
        curl -s -o /dev/null -w "%{http_code}" -X "$1" -H "Authorization: $AUTH" "$2$3"' \
        "$CLIENT" "$1" "$BASE" "$2"
}

body() {  # body METHOD PATH  (token on stdin)
    docker exec -i "$CONTAINER" sh -c '
        IFS= read -r T || true
        curl -s -X "$1" -H "Authorization: $0, Token=\"$T\"" "$2$3"' \
        "$CLIENT" "$1" "$BASE" "$2"
}

# Signs a user in; prints the access token. Username and password arrive on stdin.
sign_in() {
    docker exec -i "$CONTAINER" sh -c '
        IFS= read -r U; IFS= read -r P
        esc() { printf "%s" "$1" | sed "s/\\\\/\\\\\\\\/g; s/\"/\\\\\"/g"; }
        curl -s -X POST -H "Content-Type: application/json" -H "Authorization: $0" \
             --data "{\"Username\":\"$(esc "$U")\",\"Pw\":\"$(esc "$P")\"}" "$1/Users/AuthenticateByName" \
          | sed -n "s/.*\"AccessToken\":\"\([0-9a-fA-F]*\)\".*/\1/p"' \
        "$CLIENT" "$BASE"
}

ask_secret() { local v; IFS= read -r -s -p "$1" v; echo >&2; printf '%s' "$v"; }

echo "Project A sign-in test against container '$CONTAINER'"
echo
echo "Step 1 of 3: the ordinary test user (for example pa-tester, not an administrator)."
read -r -p "  Username: " USER_NAME
USER_PW=$(ask_secret "  Password (hidden): ")
USER_TOKEN=$(printf '%s\n%s\n' "$USER_NAME" "$USER_PW" | sign_in)
unset USER_PW
[ -n "$USER_TOKEN" ] || fail "could not sign in as '$USER_NAME' (check the name and password)"
echo "  Signed in."

echo
echo "Step 2 of 3: the throwaway user you will disable (for example pa-disabled)."
echo "  It must still be ENABLED right now."
read -r -p "  Username: " DIS_NAME
DIS_PW=$(ask_secret "  Password (hidden): ")
DIS_TOKEN=$(printf '%s\n%s\n' "$DIS_NAME" "$DIS_PW" | sign_in)
unset DIS_PW
[ -n "$DIS_TOKEN" ] || fail "could not sign in as '$DIS_NAME' (is it still enabled?)"
echo "  Signed in. Now, in Jellyfin: Dashboard > Users > $DIS_NAME > tick 'Disable this user' > Save."
read -r -p "  Press Enter once it is disabled... " _

echo
echo "Step 3 of 3: the throwaway API key (Dashboard > API Keys > +, name it pa-verify)."
API_KEY=$(ask_secret "  Paste the key (hidden): ")
[ -n "$API_KEY" ] || fail "no API key entered"

echo
echo "Checking every endpoint..."
pass=0; failed=0
check() {  # check LABEL EXPECTED-REGEX ACTUAL
    if [[ "$3" =~ ^($2)$ ]]; then pass=$((pass+1)); printf '  PASS  %-46s %s\n' "$1" "$3"
    else failed=$((failed+1)); printf '  FAIL  %-46s got %s, expected %s\n' "$1" "$3" "$2"; fi
}

while IFS=$'\t' read -r method path anonymous allowkey adminonly; do
    case "$method" in ''|'#'*) continue ;; esac
    if [ "$anonymous" = "yes" ]; then
        check "$method $path, no token" "200" "$(printf '\n' | status "$method" "$path")"
        continue
    fi
    check "$method $path, no token" "401" "$(printf '\n' | status "$method" "$path")"
    check "$method $path, disabled user" "401|403" "$(printf '%s\n' "$DIS_TOKEN" | status "$method" "$path")"
    if [ "$allowkey" = "yes" ]; then exp=200; else exp=403; fi
    check "$method $path, API key" "$exp" "$(printf '%s\n' "$API_KEY" | status "$method" "$path")"
    if [ "$adminonly" = "yes" ]; then exp=403; else exp=200; fi
    check "$method $path, ordinary user" "$exp" "$(printf '%s\n' "$USER_TOKEN" | status "$method" "$path")"
done < "$ENDPOINTS"

echo
echo "Health check (as the API key):"
printf '%s\n' "$API_KEY" | body GET /Social/Health; echo

echo
echo "Signing the test sessions out..."
printf '%s\n' "$USER_TOKEN" | status POST /Sessions/Logout >/dev/null || true
printf '%s\n' "$DIS_TOKEN" | status POST /Sessions/Logout >/dev/null || true
unset USER_TOKEN DIS_TOKEN API_KEY

echo
echo "Result: $pass passed, $failed failed."
echo "Now delete the API key pa-verify (Dashboard > API Keys) and the user $DIS_NAME (Dashboard > Users)."
[ "$failed" -eq 0 ]
