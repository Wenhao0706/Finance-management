# syntax=docker/dockerfile:1.7

# ---- build stage ----
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# Production build: AOT, minified, content-hashed filenames (main-XXXX.js,
# styles-XXXX.css). Hashed names are why nginx can safely set
# `Cache-Control: immutable` on assets — every deploy gets new filenames so
# browsers refetch automatically. (Earlier we used `--configuration development`
# which emits unhashed `main.js` and made every redeploy require a manual CF
# cache purge + browser hard-reload.)
RUN npx ng build --configuration production

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
