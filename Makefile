OUTPUT_DIR := build/output
PLUGIN_DIR := $(OUTPUT_DIR)/plugins
CONFIGURATION := Release

.PHONY: all build test publish clean

all: build test

build:
	dotnet build -c $(CONFIGURATION)

test:
	dotnet test -c $(CONFIGURATION) --no-build

publish: build
	@mkdir -p $(OUTPUT_DIR) $(PLUGIN_DIR)
	dotnet publish src/Marv.App/Marv.App.csproj -c $(CONFIGURATION) --no-build -o $(OUTPUT_DIR)
	@for plugin in src/plugins/*/; do \
		name=$$(basename $$plugin); \
		dotnet publish $$plugin$$name.csproj -c $(CONFIGURATION) --no-build -o $(PLUGIN_DIR)/$$name; \
	done
	@echo ""
	@echo "Build complete. Output in $(OUTPUT_DIR)/"
	@echo "Run with: dotnet $(OUTPUT_DIR)/Marv.App.dll"

clean:
	dotnet clean -c $(CONFIGURATION)
	rm -rf build/
