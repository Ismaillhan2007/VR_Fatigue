"""Provision private Quest connection defaults from a laptop; no headset typing or APK secrets."""

import argparse
import ipaddress
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path
from urllib.parse import urlsplit

import httpx

from app.config import Settings

PACKAGE = "com.vrfatigue.prototype"
REMOTE_FILE = "files/backend-connection.json"


def validate(body):
    if not isinstance(body, dict) or set(body) != {
        "backend_url", "backend_token", "local_development"
    }:
        raise ValueError("invalid_setup")
    url, token, local = body["backend_url"], body["backend_token"], body["local_development"]
    if not isinstance(url, str) or not isinstance(token, str) or type(local) is not bool:
        raise ValueError("invalid_setup")
    if not token or len(token) > 4096 or any(ord(c) <= 32 or ord(c) >= 127 for c in token):
        raise ValueError("invalid_setup")
    parsed = urlsplit(url)
    if (parsed.scheme not in {"https", "http"} or not parsed.hostname or parsed.username
        or parsed.password or parsed.path not in {"", "/"} or parsed.query or parsed.fragment):
        raise ValueError("invalid_setup")
    parsed.port  # Reject malformed ports without echoing the URL.
    host = parsed.hostname
    loopback = host == "localhost"
    lan = False
    try:
        address = ipaddress.ip_address(host)
        loopback |= address.is_loopback
        lan = any(address in ipaddress.ip_network(net) for net in (
            "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10"
        ))
    except ValueError:
        pass
    if (parsed.scheme == "http" or loopback) and not (local and (loopback or lan)):
        raise ValueError("invalid_setup")
    return body


def preflight(body):
    with httpx.Client(timeout=8, trust_env=False, follow_redirects=False) as client:
        response = client.get(
            body["backend_url"].rstrip("/") + "/api/v1/readiness",
            headers={"Authorization": "Bearer " + body["backend_token"]},
        )
    if response.status_code == 401:
        raise ValueError("backend_token_rejected")
    if (response.status_code != 200
        or response.json().get("ready_for_consented_summary") is not True):
        raise ValueError("backend_not_ready")


def export_config(path, body):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", dir=path.parent, delete=False) as file:
            temporary = Path(file.name)
            json.dump(body, file)
        os.replace(temporary, path)
    finally:
        if temporary and temporary.exists():
            temporary.unlink()


def find_adb(explicit=None):
    if explicit:
        return explicit
    found = shutil.which("adb")
    if found:
        return found
    roots = [Path.home() / "Library/Android/sdk", Path.home() / "Android/Sdk"]
    roots += list(Path("/Applications/Unity/Hub/Editor").glob(
        "*/Unity.app/Contents/PlaybackEngines/AndroidPlayer/SDK"
    ))
    for root in roots:
        path = root / "platform-tools/adb"
        if path.is_file():
            return str(path)
    raise ValueError("adb_missing")


def adb_run(adb, args, payload=None):
    result = subprocess.run(
        [adb, *args], input=payload, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        timeout=20, check=False,
    )
    if result.returncode:
        raise ValueError("adb_failed")
    return result.stdout


def device(adb, serial=None):
    lines = adb_run(adb, ["devices"]).decode().splitlines()
    connected = [line.split()[0] for line in lines if len(line.split()) == 2
                 and line.split()[1] == "device"]
    if serial:
        if serial not in connected:
            raise ValueError("device_not_authorized")
        return serial
    if len(connected) != 1:
        raise ValueError("connect_one_authorized_quest")
    return connected[0]


def install_config(adb, serial, body):
    # The token travels only over stdin, never process arguments, logs or a shared APK.
    command = (f"run-as {PACKAGE} sh -c 'umask 077; mkdir -p files; "
               f"cat > {REMOTE_FILE}.tmp && mv {REMOTE_FILE}.tmp {REMOTE_FILE}'")
    adb_run(adb, ["-s", serial, "shell", "-T", command], json.dumps(body).encode())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--export", type=Path, help="Create a private setup file for the operator")
    mode.add_argument("--install", type=Path, help="Install setup into the development APK")
    mode.add_argument("--clear", action="store_true", help="Remove saved connection defaults")
    parser.add_argument("--backend-url", help="Reachable backend origin, not a secret")
    parser.add_argument(
        "--env-file", type=Path, default=Path(__file__).resolve().parents[1] / ".env"
    )
    parser.add_argument("--adb", help="Path to Android platform-tools adb")
    parser.add_argument("--serial", help="Select one authorized USB device")
    args = parser.parse_args()
    try:
        if args.export:
            if not args.backend_url:
                parser.error("--export requires --backend-url")
            token = Settings(_env_file=args.env_file).backend_access_token.get_secret_value()
            body = validate({"backend_url": args.backend_url, "backend_token": token,
                             "local_development": True})
            preflight(body)
            export_config(args.export, body)
            print("Private setup file created. Transfer it privately to the headset operator.")
            return 0
        adb = find_adb(args.adb)
        serial = device(adb, args.serial)
        if args.clear:
            adb_run(adb, ["-s", serial, "shell", "run-as", PACKAGE, "rm", "-f", REMOTE_FILE])
            print("Saved connection defaults removed. Close and reopen the app.")
            return 0
        if args.install.stat().st_size > 8192:
            raise ValueError("invalid_setup")
        body = json.loads(args.install.read_text())
        if args.backend_url:
            body["backend_url"] = args.backend_url
        body = validate(body)
        preflight(body)  # Verify the operator laptop can reach/authenticate the backend.
        install_config(adb, serial, body)
        print("Quest defaults installed in private app storage. Close and reopen the updated app.")
        print("No VR typing required. Each participant still chooses Gemini consent.")
        return 0
    except (OSError, ValueError, TypeError, AttributeError,
            httpx.HTTPError, subprocess.SubprocessError) as exc:
        codes = {"invalid_setup", "backend_token_rejected", "backend_not_ready", "adb_missing",
                 "adb_failed", "device_not_authorized", "connect_one_authorized_quest"}
        reason = str(exc) if str(exc) in codes else "connection_or_setup_failed"
        print(f"Setup failed: {reason}. Check backend reachability/auth/allowance, authorized USB, "
              "adb and the updated development APK. No credentials were printed.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
