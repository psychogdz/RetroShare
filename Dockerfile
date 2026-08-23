# --------------------------------------------------------------------------
# RetroShare — build & runtime image
# --------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first for layer caching.
COPY ["src/RetroShare.Domain/RetroShare.Domain.csproj", "src/RetroShare.Domain/"]
COPY ["src/RetroShare.Application/RetroShare.Application.csproj", "src/RetroShare.Application/"]
COPY ["src/RetroShare.Infrastructure/RetroShare.Infrastructure.csproj", "src/RetroShare.Infrastructure/"]
COPY ["src/RetroShare.API/RetroShare.API.csproj", "src/RetroShare.API/"]
COPY ["tests/RetroShare.UnitTests/RetroShare.UnitTests.csproj", "tests/RetroShare.UnitTests/"]
COPY ["tests/RetroShare.IntegrationTests/RetroShare.IntegrationTests.csproj", "tests/RetroShare.IntegrationTests/"]
COPY ["RetroShare.sln", "./"]
RUN dotnet restore

# Build, test and publish. Tests run in the image build so a broken build
# never reaches the runtime stage.
COPY . .
RUN dotnet test RetroShare.sln --no-restore -v q
RUN dotnet publish src/RetroShare.API/RetroShare.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Database and storage live under /data; mount a volume to persist them.
VOLUME ["/data"]
ENV ConnectionStrings__Database="Data Source=/data/retroshare.db"
ENV Storage__Root="/data/storage"

ENTRYPOINT ["dotnet", "RetroShare.API.dll"]
