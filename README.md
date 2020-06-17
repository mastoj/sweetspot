# sweetspot
This is a proof of concept on how one could setup a k8s cluster with linkerd on Azure and what it might look like to deploy apps there. 

## Structure

There are three pulumi projects:

* [Core infrastructure](src/infrastructure/Sweetspot.Infrastructure.Core) - no CI setup for this project since the purpose of this project is just to prove a point, this should ideally be in its own repository. Before deploying the app this project needs to deployed first.
* [Publish app](src/infrastructure/Sweetspot.Infrastructure.Publish) - Only purpose is to publish the docker containers for the actual application to azure registry that is configured in the core infrastructure project.
* [Deploy app](src/infrastructure/Sweetspot.Infrastructure.Application) - Deploys the application to k8s that was configured in core using the images published in the publish project.

The actual application is just some random things that is under [application](src/app). No specific purpose with the app more than try different things out as I go.
