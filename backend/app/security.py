"""Small local-demo safeguards; not a substitute for production access controls."""

from starlette.responses import JSONResponse
from starlette.types import ASGIApp, Receive, Scope, Send


class RequestBodyLimit:
    """Bound actual bytes before JSON parsing, including requests without Content-Length."""

    def __init__(self, app: ASGIApp, max_bytes: int):
        self.app = app
        self.max_bytes = max_bytes

    async def __call__(self, scope: Scope, receive: Receive, send: Send):
        if scope["type"] != "http":
            return await self.app(scope, receive, send)
        for name, value in scope.get("headers", []):
            if name.lower() == b"content-length":
                try:
                    if not value.strip().isdigit():
                        raise ValueError
                    size = int(value)
                    if size < 0:
                        raise ValueError
                except ValueError:
                    return await JSONResponse(
                        {"detail": "Invalid Content-Length"}, status_code=400
                    )(scope, receive, send)
                if size > self.max_bytes:
                    return await self.too_large(scope, receive, send)
        body = bytearray()
        while True:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            chunk = message.get("body", b"")
            if len(body) + len(chunk) > self.max_bytes:
                return await self.too_large(scope, receive, send)
            body.extend(chunk)
            if not message.get("more_body", False):
                break
        delivered = False

        async def replay():
            nonlocal delivered
            if not delivered:
                delivered = True
                return {"type": "http.request", "body": bytes(body), "more_body": False}
            return await receive()

        return await self.app(scope, replay, send)

    async def too_large(self, scope, receive, send):
        response = JSONResponse({"detail": "Request body too large"}, status_code=413)
        await response(scope, receive, send)
