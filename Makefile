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

publish-docker: restore-tool ## Build and publish apps
	dotnet fake build target PublishDocker

deploy: restore-tool ## Deploys the apps
	dotnet fake build target Deploy

format: restore-tool ## Format code
	dotnet fake build target Format

deploy-core-infra: ## Deploys the core infrastructure
	pulumi up -y -C src/infrastructure/Sweetspot.Infrastructure.Core

kubedev: ## Get dev kubeconfig and export config variable
	pulumi stack output kubeconfig --show-secrets -C src/infrastructure/Sweetspot.Infrastructure.Core > kubeconfig.yaml
	echo "Run > export KUBECONFIG=`pwd`/kubeconfig.yaml"