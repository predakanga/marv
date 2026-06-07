FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
ARG VERSION=0.3.0
WORKDIR /src

# Restore dependencies first for layer caching
COPY Marv.slnx .
COPY src/Marv/Marv.csproj src/Marv/
COPY src/Marv.Core/Marv.Core.csproj src/Marv.Core/
RUN dotnet restore src/Marv/Marv.csproj

# Copy source and build
COPY src/Marv/ src/Marv/
COPY src/Marv.Core/ src/Marv.Core/
RUN dotnet publish src/Marv/Marv.csproj -c Release -o /app -p:Version="$VERSION"

FROM mcr.microsoft.com/dotnet/aspnet:10.0.8 AS runtime
WORKDIR /app

COPY --from=build /app .
RUN mkdir -p plugins

# To add plugins, create a derived image:
#
#   FROM ghcr.io/predakanga/marv:latest
#   COPY my-plugins/*.dll /app/plugins/
#   COPY marv.json /app/marv.json

USER app

ENTRYPOINT ["./Marv"]
