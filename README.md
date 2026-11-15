# MockBucket

Self-hosted HTTP capture sink + mock endpoint. Fire any request at a bucket URL, watch it arrive live,
control what the caller gets back. One Docker container, no database.

This was heavily inspired on my old HttpBucket https://github.com/magohl/httpbucket

See how client react to a specific Status Code? Just add `.{statuscode}` to the bucket

# Hosting
## Docker
```bash
docker run -p 8080:8080 ghcr.io/magohl/mockbucket:latest   # → http://localhost:8080
```

## Docker Compose
```bash
docker compose up --build       # same thing, from source
```

# Development

```bash
./scripts/mb test          # build + tests, fully containerized (host needs only docker + git)
./scripts/mb web           # vite dev server on :5173 (proxies /api → :8080; run `mb app` for the backend)
./scripts/mb app           # the single container, built and run via compose → http://localhost:8080
```

```bash
# try the three response-control mechanisms (precedence: mb_* > url suffix > rule)
ID=$(curl -s -X POST localhost:8080/api/buckets -d '{"name":"demo"}' -H 'content-type: application/json' | jq -r .id)
curl "localhost:8080/b/$ID/ping?mb_status=418&mb_body=teapot"          # per-request override
curl "localhost:8080/b/$ID.503/whatever"                              # URL config — for apps where you only set a base URL
curl -X PUT "localhost:8080/api/buckets/$ID/rules" -H 'content-type: application/json' \
  -d '[{"match":{"method":"POST","path":"/api/*"},"respond":{"status":401,"body":"{\"error\":\"token\"}"}}]'
curl -X POST "localhost:8080/b/$ID/api/thing"                          # stored rule answers
```

# Screenshots
<img width="1421" height="1029" alt="image" src="https://github.com/user-attachments/assets/081165ff-d744-49bf-b5fe-1e074cfb3bb8" />

## Kubernetes
### AuthN/AuthZ
Using Ingress or HttpRoute you can easily add AuthN/AuthZ on the admin stuff but keep the buckets anonymous 

Keep `/b` anonymous followed by `/` with auth.

```yaml
# example of HttpRoute rule using Traefik and OAuth2Proxy
  rules:
    # Anonymous
    - matches:
        - path:
            type: PathPrefix
            value: /b
      backendRefs:
        - name: mockbucket
          port: 8080

    # Everything else requires oauth2-proxy
    - matches:
        - path:
            type: PathPrefix
            value: /
      filters:
        - type: ExtensionRef
          extensionRef:
            group: traefik.io
            kind: Middleware
            name: oauth2-auth
      backendRefs:
        - name: mockbucket
          port: 8080
```
