FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
ARG VERSION=0.2.0
WORKDIR /src

# Restore dependencies first for layer caching
COPY Marv.slnx .
COPY src/Marv/Marv.csproj src/Marv/
COPY src/Marv.Core/Marv.Core.csproj src/Marv.Core/
COPY src/plugins/Marv.Plugins.Auth/Marv.Plugins.Auth.csproj src/plugins/Marv.Plugins.Auth/
COPY src/plugins/Marv.Plugins.AuthConsumer/Marv.Plugins.AuthConsumer.csproj src/plugins/Marv.Plugins.AuthConsumer/
COPY src/plugins/Marv.Plugins.CannedResponses/Marv.Plugins.CannedResponses.csproj src/plugins/Marv.Plugins.CannedResponses/
COPY src/plugins/Marv.Plugins.Greet/Marv.Plugins.Greet.csproj src/plugins/Marv.Plugins.Greet/
COPY src/plugins/Marv.Plugins.Moderation/Marv.Plugins.Moderation.csproj src/plugins/Marv.Plugins.Moderation/
COPY src/Marv.Testing/Marv.Testing.csproj src/Marv.Testing/
COPY tests/Marv.Core.Tests/Marv.Core.Tests.csproj tests/Marv.Core.Tests/
COPY tests/Marv.Plugins.Tests/Marv.Plugins.Tests.csproj tests/Marv.Plugins.Tests/
COPY tests/Marv.Testing.Tests/Marv.Testing.Tests.csproj tests/Marv.Testing.Tests/
RUN dotnet restore

# Copy source and build
COPY . .
RUN dotnet publish src/Marv/Marv.csproj -c Release -o /app -p:Version="$VERSION" && \
    for plugin in src/plugins/*/; do \
        dotnet build "$plugin" -c Release -p:Version="$VERSION"; \
    done

# Copy plugin DLLs into the plugins directory
RUN mkdir -p /app/plugins && \
    for plugin in src/plugins/*/; do \
        name=$(basename "$plugin"); \
        cp "$plugin/bin/Release/net10.0/$name.dll" /app/plugins/; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:10.0.8 AS runtime
WORKDIR /app

COPY --from=build /app .

USER app

ENTRYPOINT ["./Marv"]
