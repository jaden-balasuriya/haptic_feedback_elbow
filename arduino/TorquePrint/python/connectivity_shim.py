#!/usr/bin/env python3
"""
Tiny HTTP shim so Android/Quest marks the UNO Q hotspot as validated.

Android probes hosts like connectivitycheck.gstatic.com/generate_204.
We answer 204 No Content on :80 so the Quest will allow app TCP on this Wi-Fi.
"""

from __future__ import annotations

from http.server import BaseHTTPRequestHandler, HTTPServer


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):  # noqa: N802
        self.send_response(204)
        self.end_headers()

    def do_HEAD(self):  # noqa: N802
        self.send_response(204)
        self.end_headers()

    def log_message(self, fmt, *args):
        print(f"[ConnShim] {self.address_string()} {fmt % args}")


def main() -> None:
    server = HTTPServer(("0.0.0.0", 80), Handler)
    print("[ConnShim] Listening on 0.0.0.0:80 (Android generate_204)")
    server.serve_forever()


if __name__ == "__main__":
    main()
