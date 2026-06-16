# Calcpad Server - Secure Docker Architecture

This document describes the secure multi-container Docker architecture for Calcpad Server, designed to isolate JavaScript execution and PDF generation while maintaining full functionality.

## Architecture Overview

The Calcpad Server uses a simplified 2-container architecture for maximum security:

```
┌─────────────────────────────────┐    ┌─────────────────┐
│        calcpad-server           │    │   pdf-service   │
│                                 │    │                 │
│ • Main API + Math Engine        │───▶│ • PDF Generation│
│ • Web Interface                 │    │ • Node.js/TS    │
│ • Read-only Source Files        │    │ • Chromium      │
│ • Isolated Execution            │    │ • Internal Only │
│ • Port 8080                     │    │ • Sandboxed     │
└─────────────────────────────────┘    └─────────────────┘
```

### Security Benefits

- **🔒 JavaScript Isolation**: JS execution happens in sandboxed containers with no host access
- **🚫 Network Isolation**: Only main server exposed externally
- **📁 Read-only Source Files**: Server can only read templates and required files
- **🌐 API Validation**: All API calls validated against configured domains
- **🛡️ Minimal Privileges**: All containers run as non-root with dropped capabilities

## Container Details

### Calcpad Server (`calcpad-server`)
- **Purpose**: Unified API, math engine, and web interface
- **Technology**: .NET 8 ASP.NET Core + Calcpad.Core
- **Access**: Exposed on port 8080
- **Security**: Read-only filesystem, isolated execution, sandboxed temp directory
- **File Access**: Can only read templates and essential source files (read-only)

### PDF Service (`pdf-service`)
- **Purpose**: Generates PDFs using Puppeteer/Chromium
- **Technology**: Node.js/TypeScript
- **Access**: Internal network only
- **Security**: Read-only filesystem, sandboxed temp directory

## Configuration

### Environment Variables

Create a `.env` file in the `Calcpad.Server` directory:

```bash
# Main Server Configuration
ASPNETCORE_ENVIRONMENT=Production
CALCPAD_PORT=8080
CALCPAD_HOST=*

# Service URLs (Docker internal networking)
PDF_SERVICE_URL=http://pdf-service:3001

# PDF Service Configuration
PDF_SERVICE_PORT=3001
NODE_ENV=production

# Calcpad Isolation
CALCPAD_ISOLATED=true

# Security Settings
DOTNET_EnableDiagnostics=0
DOTNET_CLI_TELEMETRY_OPTOUT=1

# Browser Configuration for PDF Service
PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium-browser
PUPPETEER_ARGS=--no-sandbox --disable-setuid-sandbox --disable-dev-shm-usage

# Optional: Custom Network Configuration
# DOCKER_NETWORK=calcpad-network
```

### Production Configuration

For production deployments, add these additional variables:

```bash
# Production Security
ASPNETCORE_HTTPS_PORT=8443
ASPNETCORE_URLS=https://+:8443;http://+:8080

# Logging Configuration
ASPNETCORE_LOGGING__LOGLEVEL__DEFAULT=Warning
ASPNETCORE_LOGGING__LOGLEVEL__MICROSOFT=Warning

# Performance Tuning
DOTNET_gcServer=1
DOTNET_gcConcurrent=1

# Health Check Configuration
HEALTHCHECK_TIMEOUT=30s
HEALTHCHECK_INTERVAL=60s

# Resource Limits (for docker-compose)
CALCPAD_SERVER_MEMORY=1g
PDF_SERVICE_MEMORY=512m
```

## API Domain Security

To configure allowed API domains for the secure proxy, set up AuthSettings in your Calcpad requests:

```json
{
  "content": "your calcpad content",
  "settings": {
    "auth": {
      "jwt": "your-jwt-token",
      "routingConfig": {
        "api-service": {
          "baseUrl": "https://api.example.com",
          "auth": "jwt",
          "endpoints": {
            "getData": "/api/data/:fileName"
          }
        }
      }
    }
  }
}
```

**Security Note**: Only domains specified in `routingConfig` will be accessible via API calls. All other domains are blocked.

## Deployment

### Development

```bash
# Clone and navigate to the project
cd CalcpadVM/Calcpad.Server

# Create your .env file (see above)
cp .env.example .env

# Build and start all services
docker-compose up --build
```

### Production

```bash
# Production deployment with production environment
docker-compose --env-file .env.production up -d --build

# Or rename your production config to .env
cp .env.production .env
docker-compose up -d --build
```

### Health Checks

Each service provides health check endpoints:

- **Calcpad Server**: `http://localhost:8080/health`
- **PDF Service**: `http://pdf-service:3001/health` (internal)

## Security Considerations

### File System Access

- **Calcpad Server**: Read-only access to templates and essential files only, sandboxed `/tmp`
- **PDF Service**: Read-only filesystem with sandboxed `/tmp`

### Network Security

- Services communicate over internal Docker network only
- Only main server exposed to external traffic
- API proxy validates all outbound requests

### JavaScript Execution

- JavaScript is allowed and executed in isolated containers
- No access to host system or sensitive files
- Sandboxed execution environment

### Container Hardening

All containers implement:
- Non-root user execution
- Dropped Linux capabilities
- No new privileges
- Read-only root filesystems
- Minimal base images

## Troubleshooting

### Common Issues

1. **PDF Service Not Found**
   ```
   Error: PDF_SERVICE_URL environment variable is required
   ```
   **Solution**: Ensure `PDF_SERVICE_URL=http://pdf-service:3001` is set

2. **API Domain Blocked**
   ```
   Error: Domain example.com is not in the allowed domains list
   ```
   **Solution**: Add the domain to your AuthSettings routingConfig

3. **Container Communication Issues**
   ```
   Error: Connection refused to pdf-service:3001
   ```
   **Solution**: Ensure all services are on the same Docker network

4. **Read-only Filesystem Issues**
   ```
   Error: Cannot write to /app/somefile
   ```
   **Solution**: The filesystem is read-only by design. Use `/tmp/calcpad-sandbox` for temporary files

### Logs

View logs for specific services:

```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f calcpad-server
docker-compose logs -f pdf-service
```

## Performance Tuning

### Resource Allocation

Adjust container resources in `docker-compose.yml`:

```yaml
services:
  calcpad-server:
    deploy:
      resources:
        limits:
          memory: 1g
          cpus: '0.5'
  pdf-service:
    deploy:
      resources:
        limits:
          memory: 512m
          cpus: '0.3'
```

### Scaling

Scale PDF service for high-load scenarios:

```bash
docker-compose up --scale pdf-service=3
```

## Monitoring

### Health Checks

Monitor service health:

```bash
# Check all container status
docker-compose ps

# Detailed health information
docker inspect --format='{{.State.Health.Status}}' calcpad-server
```

### Metrics

Enable metrics collection by adding to `.env`:

```bash
ENABLE_METRICS=true
METRICS_PORT=9090
```

## Security Updates

Keep containers secure:

```bash
# Update base images
docker-compose pull
docker-compose up --build -d

# Security scan
docker scout cves
```