OUTPUT_DIR := build/output
SBOM_DIR := build/sbom
PLUGIN_DIR := $(OUTPUT_DIR)/plugins
CONFIGURATION := Release
.PHONY: all build test test-integration publish sbom clean

all: build test

build:
	dotnet build -c $(CONFIGURATION)

test:
	dotnet test -c $(CONFIGURATION) --no-build --filter "Category!=Integration"

test-integration:
	dotnet test -c $(CONFIGURATION) --no-build --filter "Category=Integration"

publish: build
	@mkdir -p $(OUTPUT_DIR) $(PLUGIN_DIR)
	dotnet publish src/Marv/Marv.csproj -c $(CONFIGURATION) -o $(OUTPUT_DIR)
	@for plugin in src/plugins/*/; do \
		name=$$(basename $$plugin); \
		cp $$plugin/bin/$(CONFIGURATION)/net10.0/$$name.dll $(PLUGIN_DIR)/; \
	done
	@echo ""
	@echo "Build complete. Output in $(OUTPUT_DIR)/"
	@echo "Run with: $(OUTPUT_DIR)/Marv"

# CycloneDX SBOMs for the host app and the NuGet packages. The release
# workflow generates the same files; see docs/releasing.md.
sbom:
	./scripts/generate-sbom.sh src/Marv/Marv.csproj $(SBOM_DIR)/Marv.cdx.json
	./scripts/generate-sbom.sh src/Marv.Core/Marv.Core.csproj $(SBOM_DIR)/TDW.Marv.Core.cdx.json
	./scripts/generate-sbom.sh src/Marv.Testing/Marv.Testing.csproj $(SBOM_DIR)/TDW.Marv.Testing.cdx.json

clean:
	dotnet clean -c $(CONFIGURATION)
	rm -rf build/
