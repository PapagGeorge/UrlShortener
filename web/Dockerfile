# syntax=docker/dockerfile:1
# React UI: built with Node, served by unprivileged nginx. Build context is web/ (docker build -f web/Dockerfile web).
# nginx also proxies /api/* to the Gateway, so the browser talks to a single origin (no CORS).
# Images are built by GitHub Actions (linux/amd64) and never on the dev Mac.

FROM node:22-alpine AS build
WORKDIR /app

# The lockfile first, so the install layer is cached until a dependency changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.31-alpine AS final
# *.template files in this folder are run through envsubst at startup (API_UPSTREAM) and written to conf.d/.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

ENV API_UPSTREAM=gateway:8080
# Port 8080, non-root user (101) provided by the base image.
EXPOSE 8080
