# OPTIONAL — local container smoke only.
# Production frontend deploy is static: see cloudbuild.web.yaml (Vite → GCS).
#
# Build calcpad-web (Vite) and serve with nginx on port 8080 under /calcpad/.
# Context: Calcpad.Web/frontend (must include calcpad-frontend + calcpad-web).
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
COPY --from=build /src/calcpad-web/dist /usr/share/nginx/html/calcpad
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
