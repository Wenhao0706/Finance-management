# syntax=docker/dockerfile:1.7

# ---- build stage ----
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# Use development env so apiUrl is '/api' (relative) — nginx in the runtime
# stage proxies /api to the backend container, giving us a same-origin setup
# (no CORS, no separate api subdomain in the tunnel).
RUN npx ng build --configuration development

# ---- runtime stage ----
FROM nginx:1.27-alpine AS runtime

COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
# Use 127.0.0.1 explicitly — alpine's busybox wget resolves 'localhost' to ::1
# (IPv6) first, but nginx only binds IPv4 in our config.
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD wget -qO- http://127.0.0.1/health || exit 1

CMD ["nginx", "-g", "daemon off;"]
