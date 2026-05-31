OUTPUT_DIR := build/output
PLUGIN_DIR := $(OUTPUT_DIR)/plugins
CONFIGURATION := Release

.PHONY: all build test publish clean

all: build test

build:
	dotnet build -c $(CONFIGURATION)

test:
	dotnet test -c $(CONFIGURATION) --no-build

publish:
	@mkdir -p $(OUTPUT_DIR) $(PLUGIN_DIR)
	dotnet publish src/Marv.App/Marv.App.csproj -c $(CONFIGURATION) -o $(OUTPUT_DIR)
	@for plugin in src/plugins/*/; do \
		name=$$(basename $$plugin); \
		cp $$plugin/bin/$(CONFIGURATION)/net10.0/$$name.dll $(PLUGIN_DIR)/; \
	done
	@echo ""
	@echo "Build complete. Output in $(OUTPUT_DIR)/"
	@echo "Run with: $(OUTPUT_DIR)/Marv.App"

clean:
	dotnet clean -c $(CONFIGURATION)
	rm -rf build/
