FROM node:20-alpine AS frontend
WORKDIR /app
COPY tauri-app/package.json tauri-app/package-lock.json ./
RUN npm ci
COPY tauri-app/index.html tauri-app/svelte.config.js ./
COPY tauri-app/tsconfig.json tauri-app/tsconfig.node.json ./
COPY tauri-app/vite.config.ts ./
COPY tauri-app/src/ ./src/
COPY tauri-app/public/ ./public/
RUN npm run build

FROM python:3.12-slim
WORKDIR /app

COPY tauri-app/backend/requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY tools/jigsaw_generator/ ./tools/jigsaw_generator/
COPY tauri-app/backend/ ./backend/
COPY --from=frontend /app/dist ./dist

ENV PYTHONPATH=/app:/app/tools/jigsaw_generator
RUN mkdir -p /app/data/outputs

WORKDIR /app/backend
EXPOSE 8000
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
