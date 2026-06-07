# CS-012: Dockerfile Sample Plugin Inclusion

**Source:** `TODO.md` item 1
**Scope:** Dockerfile, CI/CD
**Complexity:** Small
**Breaking changes:** None

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

- **Do not include sample plugins in the default Docker image.** The image
  should contain only the bot executable and core assembly. Users deploy
  their own plugins via volume mounts to `/app/plugins/`.
- **Provide a build argument** (`INCLUDE_SAMPLES`, default `false`) that
  includes the sample plugins when set to `true`, for users who want a
  quick-start experience.
- **Document the volume mount approach** in a comment in the Dockerfile and
  in the project README.

## Changes

### 1. Add `INCLUDE_SAMPLES` build argument

```dockerfile
ARG INCLUDE_SAMPLES=false
```

### 2. Conditionally build and copy sample plugins

Replace the unconditional plugin build and copy with a conditional block:

```dockerfile
# Build plugins only when INCLUDE_SAMPLES is true
RUN if [ "$INCLUDE_SAMPLES" = "true" ]; then \
        for plugin in src/plugins/*/; do \
            dotnet build "$plugin" -c Release -p:Version="$VERSION"; \
        done && \
        mkdir -p /app/plugins && \
        for plugin in src/plugins/*/; do \
            name=$(basename "$plugin"); \
            cp "$plugin/bin/Release/net10.0/$name.dll" /app/plugins/; \
        done; \
    else \
        mkdir -p /app/plugins; \
    fi
```

### 3. Update the release workflow

The release workflow's Docker job does not need changes — the default
(`INCLUDE_SAMPLES=false`) produces the clean image. If a "batteries
included" image variant is desired later, it can be built with:

```bash
docker build --build-arg INCLUDE_SAMPLES=true -t marv:with-samples .
```

### 4. Remove hardcoded plugin `.csproj` lines from restore layer

The restore layer currently copies every plugin `.csproj` for dependency
caching. When `INCLUDE_SAMPLES` is false, these are unnecessary. However,
`dotnet restore` on the solution file restores all projects regardless.
To avoid breaking the restore cache, keep the `.csproj` copies — they are
small and the restore step is a no-op for unchanged projects. This is a
minor inefficiency that keeps the Dockerfile simpler.

If a leaner restore is desired, a future change could use a separate
solution file or `--no-dependencies` flag, but this is not worth the
complexity now.

## Impact

- **Docker image:** Default image shrinks by the size of the sample plugin
  DLLs. Users who want samples can opt in via `INCLUDE_SAMPLES=true`.
- **Release workflow:** No changes needed. Release images ship without
  sample plugins.
- **Plugin API:** No changes.
