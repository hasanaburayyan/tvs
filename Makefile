# Trench Valkyries: Sacrifice — Project Makefile
#
# Usage:
#   make help          Show all available targets
#   make publish       Build and publish the SpacetimeDB module
#   make docs-dev      Start the docs site in dev mode
#
# Prerequisites:
#   - .NET 8 SDK with wasi-experimental workload
#   - SpacetimeDB CLI (spacetime)
#   - Node.js >= 20 (see docs/.nvmrc)

# ─── Config ───────────────────────────────────────────────────────────────────

DB_NAME       := tvs
MODULE_PATH   := ./spacetimedb
DOCS_DIR      := ./docs
BINDINGS_DIR  := ./client-unity/Assets/SpacetimeDB

# ─── SpacetimeDB ──────────────────────────────────────────────────────────────

.PHONY: build publish publish-clean generate logs server

## Build the SpacetimeDB module (WASM)
build:
	dotnet build $(MODULE_PATH)/StdbModule.csproj

## Publish the module to the configured server
publish:
	spacetime publish $(DB_NAME) --module-path $(MODULE_PATH)

## Clear the database and republish from scratch
publish-clean:
	spacetime publish $(DB_NAME) --clear-database -y --module-path $(MODULE_PATH)

publish-local:
	spacetime publish $(DB_NAME) --module-path $(MODULE_PATH) --server local

publish-local-clean:
	spacetime publish $(DB_NAME) --clear-database -y --module-path $(MODULE_PATH) --server local

## Generate client bindings (C# for Unity by default)
generate:
	@mkdir -p $(BINDINGS_DIR)
	spacetime generate --lang csharp --out-dir $(BINDINGS_DIR) --module-path $(MODULE_PATH)

## Tail the module logs
logs:
	spacetime logs $(DB_NAME)

logs-local:
	spacetime logs $(DB_NAME) --server local

## Start a local SpacetimeDB server
server:
	spacetime start

# ─── Docs (Docusaurus) ───────────────────────────────────────────────────────

.PHONY: docs-install docs-dev docs-build docs-serve docs-clean

## Install docs dependencies
docs-install:
	cd $(DOCS_DIR) && npm install

## Start the docs dev server with hot reload
docs-dev:
	cd $(DOCS_DIR) && npm start

## Build the docs site for production
docs-build:
	cd $(DOCS_DIR) && npm run build

## Serve the production docs build locally
docs-serve:
	cd $(DOCS_DIR) && npm run serve

## Clear the Docusaurus build cache
docs-clean:
	cd $(DOCS_DIR) && npm run clear

# ─── Convenience ──────────────────────────────────────────────────────────────

.PHONY: setup clean help

## First-time setup: install all dependencies
setup: docs-install
	@echo ""
	@echo "Make sure you also have:"
	@echo "  - .NET 8 SDK            https://dotnet.microsoft.com/download/dotnet/8.0"
	@echo "  - WASI workload         dotnet workload install wasi-experimental"
	@echo "  - SpacetimeDB CLI       https://spacetimedb.com/install"
	@echo "  - Node.js >= 20         https://nodejs.org"

## Remove all build artifacts
clean: docs-clean
	rm -rf $(MODULE_PATH)/bin $(MODULE_PATH)/obj

## Show this help message
help:
	@echo "Available targets:"
	@echo ""
	@awk '/^## /{desc=substr($$0,4)} /^[a-zA-Z_-]+:/{if(desc){printf "  \033[36m%-18s\033[0m %s\n", $$1, desc; desc=""}}' $(MAKEFILE_LIST)
	@echo ""

.DEFAULT_GOAL := help
