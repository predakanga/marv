# CS-012: Dockerfile Sample Plugin Inclusion — COMPLETED

**Source:** `TODO.md` item 1
**Scope:** Dockerfile, CI/CD
**Complexity:** Small
**Breaking changes:** None
**Status:** Completed

---

## Problem

The Dockerfile currently builds and copies all sample plugins
(`Marv.Plugins.Auth`, `Marv.Plugins.AuthConsumer`,
`Marv.Plugins.CannedResponses`, `Marv.Plugins.Greet`,
`Marv.Plugins.Moderation`) into the Docker image's `/app/plugins/`
directory. These plugins exist primarily as examples and test fixtures for
plugin authoring. Including them in the production Docker image has
tradeoffs:

- **Image size:** Each plugin DLL increases the image footprint. For a
  minimal deployment, users may not want any sample plugins.
- **Security surface:** Sample plugins run with the same permissions as the
  bot. A user who forgets to configure the `Plugins` list may accidentally
  load sample plugins, exposing unintended commands.
- **User expectation:** Users pulling the Docker image may expect a
  "batteries included" experience with example plugins ready to try, or
  they may expect a clean image they populate with their own plugins via
  volume mounts.

## Decisions

- **Do not include sample plugins in the default Docker image.** The base
  image should contain only the bot executable and core assembly.
- **Users add plugins by building a derived Docker image** that copies
  plugin DLLs into `/app/plugins/`. This is the standard Docker pattern
  for extensible applications — the base image provides the runtime, and
  derived images add application-specific content.
- **Remove sample plugin build steps from the Dockerfile.** The sample
  plugins are development/documentation artifacts, not production
  components. They should not be built as part of the Docker image.
- **Document the derived-image approach** in the Dockerfile and in the
  project README.

## Changes

### 1. Remove sample plugin build and copy from the Dockerfile

Remove the plugin build loop and the plugin DLL copy step. The Dockerfile
should only publish the main `Marv` application:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
ARG VERSION=0.2.0
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

USER app

ENTRYPOINT ["./Marv"]
```

The restore layer no longer needs plugin `.csproj` files, test `.csproj`
files, or `Marv.Testing` — only the projects required to build the host
application.

### 2. Remove plugin and test `.csproj` copies from the restore layer

The current Dockerfile copies every plugin and test `.csproj` to enable
full solution restore. Since we now only restore and build
`src/Marv/Marv.csproj` (which depends on `Marv.Core`), only those two
`.csproj` files are needed. This shrinks the restore layer and avoids
cache invalidation when unrelated projects change.

### 3. Document the derived-image pattern

Add a comment to the Dockerfile and a section to the README showing how
users deploy their own plugins:

```dockerfile
# To add plugins, create a derived image:
#
#   FROM ghcr.io/predakanga/marv:latest
#   COPY my-plugins/*.dll /app/plugins/
```

Example `Dockerfile` for a deployment with custom plugins:

```dockerfile
FROM ghcr.io/predakanga/marv:latest
COPY plugins/ /app/plugins/
COPY marv.json /app/marv.json
```

### 4. Update the release workflow

The release workflow's Docker job needs no changes beyond what the
Dockerfile change provides — the built image will no longer contain
sample plugins.

The binary release archives (`.tar.gz`) in the `publish-binaries` job
currently include sample plugins. These should also be removed from the
binary archives to be consistent with the Docker image. Update the
publish step to only package the main application and an empty `plugins/`
directory.

## Design decisions

**Why derived images instead of volume mounts?** Volume mounts are a
runtime concern — they couple deployment to the host filesystem and
require the operator to manage plugin files outside of the container
image. A derived image is self-contained, versioned, and deployable to
any container orchestrator without host dependencies. It also enables
multi-stage builds where plugins are compiled from source in the same
`docker build`.

**Why derived images instead of a build argument?** A build argument
(`INCLUDE_SAMPLES=true`) would conditionally include the *sample* plugins,
but real deployments need *their own* plugins, not samples. The derived
image pattern solves the general case. Users who want the samples for
development can build them locally with `dotnet build`.

**Why remove plugins from binary archives too?** Consistency. If the
Docker image ships without sample plugins, the `.tar.gz` downloads should
match. Users who want sample plugins can build them from source — they
are example code, not distributable components.

## Impact

- **Docker image:** Smaller base image containing only the bot runtime.
  Users create derived images to add plugins. No more accidental sample
  plugin loading.
- **Binary releases:** Archives contain only the bot executable. Users
  build plugins separately.
- **Release workflow:** Minor update to remove plugin build from the
  `publish-binaries` job.
- **Plugin API:** No changes.
- **Development workflow:** No changes. `dotnet build` still builds
  everything including sample plugins. The Makefile is unaffected.
