#.PHONY: run-quiz
.SILENT: ;
.DEFAULT_GOAL := help

GIT_SHA:=$(shell git rev-parse --short HEAD)

help:
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-30s\033[0m %s\n", $$1, $$2}'

restore-tool: ## Restore tools
	dotnet tool restore

build: restore-tool ## Build apps
	dotnet fake build target BuildApp

publish: restore-tool ## Build and publish apps
	dotnet fake build target Publish

deploy: restore-tool ## Deploys the apps
	dotnet fake build target Deploy

deploy-core-infra: ## Deploys the core infrastructure
	pulumi up -y -C src/infrastructure/Sweetspot.Infrastructure.Core

deploy-root: restore-tool ## Deploys the apps
	pulumi stack select dev
	pulumi up -y -C Sweetspot.Infrastructure.Application