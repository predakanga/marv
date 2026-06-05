OUTPUT_DIR := build/output
PLUGIN_DIR := $(OUTPUT_DIR)/plugins
CONFIGURATION := Release
IRCD_CONTAINER := marv-ircd
IRCD_IMAGE := linuxserver/ngircd

.PHONY: all build test test-integration ircd-start ircd-stop publish clean

all: build test

build:
	dotnet build -c $(CONFIGURATION)

test:
	dotnet test -c $(CONFIGURATION) --no-build --filter "Category!=Integration"

test-integration: ircd-start
	dotnet test -c $(CONFIGURATION) --no-build --filter "Category=Integration"; \
	status=$$?; $(MAKE) ircd-stop; exit $$status

ircd-start:
	@docker rm -f $(IRCD_CONTAINER) 2>/dev/null || true
	@docker run -d --name $(IRCD_CONTAINER) -p 6667:6667 $(IRCD_IMAGE)
	@echo "Waiting for IRC server..."
	@for i in $$(seq 1 30); do nc -z localhost 6667 2>/dev/null && break; sleep 1; done

ircd-stop:
	@docker stop $(IRCD_CONTAINER) 2>/dev/null || true

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

clean:
	dotnet clean -c $(CONFIGURATION)
	rm -rf build/
