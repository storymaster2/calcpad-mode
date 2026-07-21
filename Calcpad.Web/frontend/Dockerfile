# Build calcpad-web (Vite) and serve with nginx on Cloud Run (port 8080).
# Context: Calcpad.Web/frontend (must include calcpad-frontend + calcpad-web).
#
# Hosted under /calcpad/ (Detail Library path). Override with build-args:
#   VITE_BASE_PATH=/calcpad/
#   VITE_SERVER_URL=https://calcpad-server-….run.app
FROM node:22-alpine AS build
WORKDIR /src

ARG VITE_BASE_PATH=/calcpad/
ARG VITE_SERVER_URL
ENV VITE_BASE_PATH=$VITE_BASE_PATH
ENV VITE_SERVER_URL=$VITE_SERVER_URL

COPY calcpad-frontend ./calcpad-frontend
COPY calcpad-web ./calcpad-web

WORKDIR /src/calcpad-web
RUN npm install && npm run build

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
# Place dist under /calcpad/ so public URLs match Vite base.
COPY --from=build /src/calcpad-web/dist /usr/share/nginx/html/calcpad
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
