FROM ghcr.io/astral-sh/uv:0.11.16 AS uv
FROM python:3.12.13-slim-bookworm AS dependencies

COPY --from=uv /uv /usr/local/bin/uv
WORKDIR /app
ENV UV_PYTHON_DOWNLOADS=never UV_LINK_MODE=copy
COPY pyproject.toml uv.lock ./
RUN uv sync --locked --no-dev --no-install-project --no-cache

FROM python:3.12.13-slim-bookworm AS runtime

RUN useradd --uid 10001 --create-home appuser
WORKDIR /app
ENV PATH="/app/.venv/bin:$PATH" \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    VR_RUNTIME_MODE=hosted \
    VR_GEMINI_ENABLED=false
COPY --from=dependencies /app/.venv /app/.venv
COPY app ./app
USER 10001:10001
EXPOSE 8000
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD ["python", "-c", "import os, urllib.request; urllib.request.urlopen('http://127.0.0.1:' + os.environ.get('PORT', '8000') + '/health', timeout=2).close()"]
CMD ["python", "-m", "app.serve"]
