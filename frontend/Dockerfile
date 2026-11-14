# DollarShark web image: builds the React app, then serves it with Caddy,
# which also forwards /api to the API container and handles HTTPS certificates automatically.

FROM node:22-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM caddy:2-alpine
COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/dist /srv
