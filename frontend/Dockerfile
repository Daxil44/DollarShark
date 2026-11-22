# DollarShark web image: builds the React app, then serves it with Caddy,
# which also forwards /api to the API container and handles HTTPS certificates automatically.

FROM node:22-slim AS build
# Small servers (512 MB-1 GB) need Node to be allowed past its default heap limit; swap covers the rest.
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN NODE_OPTIONS=--max-old-space-size=1536 npm run build

FROM caddy:2-alpine
COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/dist /srv
