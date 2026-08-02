#!/usr/bin/env bash
#
# Mints a VAPID key pair for Web Push (RFC 8292) — a P-256 pair, base64url
# encoded the way both this server and the browser want it.
#
# Set the output as Railway environment variables:
#
#     Push__PublicKey=<public>
#     Push__PrivateKey=<private>
#     Push__Subject=mailto:you@example.com
#
# The private key is a real secret and belongs nowhere else: not in
# appsettings.json, not in this repository, not in a commit message. The public
# key is handed to every browser as `applicationServerKey`, so it is public by
# definition — but it is still worth keeping the pair together, because rotating
# it invalidates every existing subscription (browsers pin the key they
# subscribed with) and every device has to be re-registered by hand.
#
# Needs openssl. Nothing is written to disk except a temporary key that is
# removed on exit.

set -euo pipefail

command -v openssl >/dev/null || { echo "openssl is required." >&2; exit 1; }

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

openssl ecparam -name prime256v1 -genkey -noout -out "$tmp" 2>/dev/null

# base64url: standard base64, + and / swapped, padding dropped.
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }

# The private scalar. A P-256 SEC1 key without its public half is exactly 51 DER
# bytes: a 7-byte prefix (SEQUENCE, version, OCTET STRING tag+length), the 32-byte
# scalar, then the 12-byte curve OID. So the scalar is *not* at the end — taking
# the last 32 bytes silently returns the tail of the key glued to the OID, and the
# result looks plausible right up until every push is rejected.
der="$(openssl ec -in "$tmp" -no_public -outform DER 2>/dev/null | od -An -v -tx1 | tr -d ' \n')"
[ "${#der}" -eq 102 ] || { echo "unexpected EC key encoding (${#der} hex chars)" >&2; exit 1; }
[ "${der:0:14}" = "30310201010420" ] || { echo "unexpected EC key header: ${der:0:14}" >&2; exit 1; }

private="$(printf '%s' "${der:14:64}" | tr -d '\n' | sed 's/../\\x&/g' | xargs -0 printf | b64url)"

# The public point: 65 bytes, uncompressed, 0x04-prefixed — genuinely the tail of
# the SubjectPublicKeyInfo structure.
public="$(openssl ec -in "$tmp" -pubout -outform DER 2>/dev/null | tail -c 65 | b64url)"

echo "Push__PublicKey=${public}"
echo "Push__PrivateKey=${private}"
