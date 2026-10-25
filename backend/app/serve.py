"""Container entry point: platform PORT, one process, no request access logs."""

import os
import sys

import uvicorn


def main() -> int:
    value = os.environ.get("PORT", "8000")
    if (
        not value.isascii() or not value.isdecimal() or len(value) > 5
        or not 1 <= int(value) <= 65535
    ):
        # Do not echo environment values: a mistaken value might contain a credential.
        print("PORT must be an integer between 1 and 65535", file=sys.stderr)
        return 1
    uvicorn.run(
        "app.main:app",
        host="0.0.0.0",
        port=int(value),
        workers=1,
        access_log=False,
        proxy_headers=False,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
