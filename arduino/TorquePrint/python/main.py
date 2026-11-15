#!/usr/bin/env python3
"""
UNO Q Linux TCP bridge for Quest ↔ MCU (Wi-Fi hotspot path).

Quest BraceTcpClient connects to this host (usually 10.42.0.1:7777 on the
UNO Q hotspot):

  Quest → MCU: ASCII force lines like "9.810\\n" (Newtons)
               forwarded via Bridge.call("on_force_line", line)

  MCU → Quest: elbow angle from pot on A0
               Bridge.call("get_elbow_angle") → "angleDeg=90.0\\n"
               Bridge.call("get_pitch_deg") → "pitchDeg=12.3\\n"

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

ANGLE_HZ = 10.0
ANGLE_SEND_EPS = 0.25  # degrees; also send on change
PITCH_SEND_EPS = 0.25

_pending_force: "queue.Queue[str]" = queue.Queue(maxsize=64)
_outbound: "queue.Queue[str]" = queue.Queue(maxsize=64)
_quest_connected = threading.Event()
_last_sent_angle: float | None = None
_last_sent_pitch: float | None = None
_last_angle_poll_t = 0.0


def _enqueue_force(line: str) -> None:
    line = line.strip()
    if not line:
        return
    try:
        _pending_force.put_nowait(line)
    except queue.Full:
        try:
            _pending_force.get_nowait()
        except queue.Empty:
            pass
        try:
            _pending_force.put_nowait(line)
        except queue.Full:
            pass


def _enqueue_outbound(line: str) -> None:
    line = line if line.endswith("\n") else line + "\n"
    try:
        _outbound.put_nowait(line)
    except queue.Full:
        try:
            _outbound.get_nowait()
        except queue.Empty:
            pass
        try:
            _outbound.put_nowait(line)
        except queue.Full:
            pass


def _flush_outbound(conn: socket.socket) -> None:
    while True:
        try:
            line = _outbound.get_nowait()
        except queue.Empty:
            return
        conn.sendall(line.encode("ascii"))


def handle_client(conn: socket.socket, addr) -> None:
    print(f"[TorqueTCP] Quest connected: {addr}")
    _quest_connected.set()
    buf = ""
    try:
        with conn:
            conn.settimeout(0.05)
            while True:
                try:
                    _flush_outbound(conn)
                except OSError as exc:
                    print(f"[TorqueTCP] send failed: {exc}")
                    break

                try:
                    chunk = conn.recv(256)
                except socket.timeout:
                    continue
                except OSError as exc:
                    print(f"[TorqueTCP] recv failed: {exc}")
                    break

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
                    _enqueue_force(line)
    finally:
        _quest_connected.clear()
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


def _maybe_queue_sensors() -> None:
    global _last_sent_angle, _last_sent_pitch, _last_angle_poll_t

    if not _quest_connected.is_set():
        return

    now = time.monotonic()
    min_interval = 1.0 / max(1.0, ANGLE_HZ)
    if now - _last_angle_poll_t < min_interval:
        return
    _last_angle_poll_t = now

    try:
        angle = float(Bridge.call("get_elbow_angle"))
    except Exception as exc:
        print(f"[TorqueTCP] get_elbow_angle failed: {exc}")
        return

    angle_line = f"angleDeg={angle:.1f}"
    angle_changed = (
        _last_sent_angle is None
        or abs(angle - _last_sent_angle) >= ANGLE_SEND_EPS
    )
    _enqueue_outbound(angle_line)
    if angle_changed:
        print(f"[TorqueTCP] queued {angle_line}")
    _last_sent_angle = angle

    try:
        pitch = float(Bridge.call("get_pitch_deg"))
    except Exception as exc:
        print(f"[TorqueTCP] get_pitch_deg failed: {exc}")
        return

    pitch_line = f"pitchDeg={pitch:.1f}"
    pitch_changed = (
        _last_sent_pitch is None
        or abs(pitch - _last_sent_pitch) >= PITCH_SEND_EPS
    )
    _enqueue_outbound(pitch_line)
    if pitch_changed:
        print(f"[TorqueTCP] queued {pitch_line}")
    _last_sent_pitch = pitch


def bridge_pump() -> None:
    """Forward queued force lines and poll pot angle + pitch for Quest."""
    try:
        while True:
            line = _pending_force.get_nowait()
            try:
                Bridge.call("on_force_line", line)
                print(f"[TorqueTCP] forwarded forceN={line}")
            except Exception as exc:
                print(f"[TorqueTCP] Bridge.call on_force_line failed: {exc}")
    except queue.Empty:
        pass

    _maybe_queue_sensors()


def main() -> None:
    thread = threading.Thread(
        target=tcp_server_loop, name="TorqueTcpServer", daemon=True
    )
    thread.start()
    App.run(user_loop=bridge_pump)


if __name__ == "__main__":
    main()
