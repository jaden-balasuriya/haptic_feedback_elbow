#!/usr/bin/env python3
"""
UNO Q Linux TCP server for Quest → MCU force commands (Wi-Fi hotspot path).

Quest BraceTcpClient connects to this host (usually 10.42.0.1:7777 on the
UNO Q hotspot) and sends ASCII lines like "9.810\\n" (Newtons). Each line is
forwarded to the MCU sketch via Bridge.call("on_force_line", line).

Run on the UNO Q after setup_hotspot.sh (App Lab Run, or docker / SSH):
  python3 main.py
"""

from __future__ import annotations

import queue
import socket
import threading
import time

from arduino.app_utils import App, Bridge

LISTEN_HOST = "0.0.0.0"
# Not 9000: Quest SELinux labels 9000 as webserver_port_t and blocks untrusted apps.
LISTEN_PORT = 7777

_pending: "queue.Queue[str]" = queue.Queue(maxsize=64)


def _enqueue_line(line: str) -> None:
    line = line.strip()
    if not line:
        return
    try:
        _pending.put_nowait(line)
    except queue.Full:
        try:
            _pending.get_nowait()
        except queue.Empty:
            pass
        try:
            _pending.put_nowait(line)
        except queue.Full:
            pass


def handle_client(conn: socket.socket, addr) -> None:
    print(f"[TorqueTCP] Quest connected: {addr}")
    buf = ""
    try:
        with conn:
            conn.settimeout(1.0)
            while True:
                try:
                    chunk = conn.recv(256)
                except socket.timeout:
                    continue
                if not chunk:
                    break
                buf += chunk.decode("ascii", errors="ignore")
                while True:
                    nl = buf.find("\n")
                    cr = buf.find("\r")
                    if nl < 0 and cr < 0:
                        break
                    if nl < 0:
                        idx = cr
                    elif cr < 0:
                        idx = nl
                    else:
                        idx = min(nl, cr)
                    line, buf = buf[:idx], buf[idx + 1 :]
                    _enqueue_line(line)
    finally:
        print("[TorqueTCP] Quest disconnected")


def tcp_server_loop() -> None:
    while True:
        server = None
        try:
            server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            server.bind((LISTEN_HOST, LISTEN_PORT))
            server.listen(1)
            print(f"[TorqueTCP] Listening on {LISTEN_HOST}:{LISTEN_PORT}")
            conn, addr = server.accept()
            handle_client(conn, addr)
        except OSError as exc:
            print(f"[TorqueTCP] socket error: {exc}; retrying in 2s")
            time.sleep(2.0)
        finally:
            if server is not None:
                try:
                    server.close()
                except OSError:
                    pass


def bridge_pump() -> None:
    """Forward queued TCP lines on the App/Bridge thread."""
    try:
        while True:
            line = _pending.get_nowait()
            try:
                Bridge.call("on_force_line", line)
                print(f"[TorqueTCP] forwarded forceN={line}")
            except Exception as exc:
                print(f"[TorqueTCP] Bridge.call failed: {exc}")
    except queue.Empty:
        pass


def main() -> None:
    thread = threading.Thread(
        target=tcp_server_loop, name="TorqueTcpServer", daemon=True
    )
    thread.start()
    App.run(user_loop=bridge_pump)


if __name__ == "__main__":
    main()
