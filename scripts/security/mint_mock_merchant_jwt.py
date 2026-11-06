#!/usr/bin/env python3
"""Mint one short-lived Development-only JWT for the local MOCK API.

This is not an OAuth2 authorization server, not real merchant onboarding and
not suitable for non-Development, production, or external traffic.
"""
import argparse
import base64
import hashlib
import hmac
import json
import os
import time
import uuid

ISS = "https://merchant-auth.local.test"
AUD = "paymentgateway.mock.api"
SCOPES = {"payments:create", "payments:read", "refunds:create", "refunds:read"}

def b64(raw: bytes) -> str:
    return base64.urlsafe_b64encode(raw).decode("ascii").rstrip("=")

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--merchant", required=True, help="Synthetic merchant UUID")
    parser.add_argument("--scopes", default="payments:create payments:read",
                        help="Space-delimited demo scopes")
    parser.add_argument("--lifetime", type=int, default=600,
                        help="Token lifetime seconds (30–900)")
    a = parser.parse_args()
    merchant = str(uuid.UUID(a.merchant))
    scopes = a.scopes.split()
    if not scopes or any(s not in SCOPES for s in scopes):
        parser.error("Unsupported local demo scope")
    if not 30 <= a.lifetime <= 900:
        parser.error("Lifetime must be between 30 and 900 seconds")
    key = os.environ.get("MERCHANT_DEV_JWT_KEY", "")
    if len(key.encode("utf-8")) < 32:
        parser.error("Set MERCHANT_DEV_JWT_KEY (32+ UTF-8 bytes) in local environment")
    now = int(time.time())
    header = {"alg": "HS256", "typ": "JWT"}
    payload = {"iss": ISS, "aud": AUD, "sub": "local-demo-merchant-client",
               "merchant_id": merchant, "scope": " ".join(scopes),
               "iat": now, "nbf": now, "exp": now + a.lifetime,
               "jti": str(uuid.uuid4())}
    data = b64(json.dumps(header, separators=(",", ":")).encode()) + "." + \
           b64(json.dumps(payload, separators=(",", ":")).encode())
    signature = hmac.new(key.encode(), data.encode(), hashlib.sha256).digest()
    print(data + "." + b64(signature))

if __name__ == "__main__":
    main()
