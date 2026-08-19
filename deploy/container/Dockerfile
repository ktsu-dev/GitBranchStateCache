# A hand-written Dockerfile rather than the .NET SDK's PublishContainer target, which is what
# ktsu.GitLfsCache uses. That target composes an image from a base plus the published output and has
# no way to add a native dependency, and this service shells out to the git binary for everything it
# does. An image without git would start, pass its own liveness probe, and fail every request.
#
# The runtime stage is the only place git is needed, so the build stage stays on the plain SDK image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

# No RuntimeIdentifier and no -r flag. The output is IL, and the runtime base image below is a
# multi-architecture manifest, so one publish serves both amd64 and arm64 and buildx only has to
# vary the final stage.
RUN dotnet publish GitBranchStateCache.Service/GitBranchStateCache.Service.csproj \
    --configuration Release \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

# git and its transport, and nothing else. The chiseled variant this project would otherwise use
# carries no package manager and no shell, so it cannot be given git at all.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# The mirror volume is mounted here. Created and owned by the non-root user the runtime image
# provides, so the container does not need a writable root filesystem to hold its own data.
RUN mkdir -p /var/lib/gitbranchstatecache && chown -R $APP_UID:$APP_UID /var/lib/gitbranchstatecache

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "ktsu.GitBranchStateCache.Service.dll"]
