# Production image for the API (Render builds this file).
#
# Stage 1: build with the full .NET SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files first, so "dotnet restore" is cached until dependencies change.
COPY src/BadalBooking.Api/BadalBooking.Api.csproj src/BadalBooking.Api/
RUN dotnet restore src/BadalBooking.Api/BadalBooking.Api.csproj

COPY src/BadalBooking.Api/ src/BadalBooking.Api/
RUN dotnet publish src/BadalBooking.Api/BadalBooking.Api.csproj -c Release -o /app --no-restore

# Stage 2: small runtime image (no SDK, no source code).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# The PostgreSQL driver probes for Kerberos support at startup; without this small library it logs
# a scary-looking (but harmless) "cannot open libgssapi_krb5.so.2" error.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./

# No secrets in the image: every secret comes from Render's environment variables at runtime.
# Workstation GC uses less memory: Render's free plan has 512 MB.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0

# Run as the image's built-in non-root user.
USER $APP_UID

# Render sets PORT; the app listens on it (see HostingSetup.UsePlatformPort). Otherwise 8080.
EXPOSE 8080
ENTRYPOINT ["dotnet", "BadalBooking.Api.dll"]
