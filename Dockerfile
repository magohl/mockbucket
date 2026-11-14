# ---------- web (Svelte 5 + Vite + Tailwind 4) ----------
FROM node:24-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# ---------- api (publish, SPA copied into wwwroot) ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY src/api/MockBucket.Api.csproj src/api/
RUN dotnet restore src/api/MockBucket.Api.csproj
COPY src/api/ src/api/
COPY --from=web /web/dist src/api/wwwroot
RUN dotnet publish src/api/MockBucket.Api.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---------- runtime (chiseled, non-root) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
# behind a proxy set: ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
USER $APP_UID
ENTRYPOINT ["dotnet", "MockBucket.Api.dll"]
