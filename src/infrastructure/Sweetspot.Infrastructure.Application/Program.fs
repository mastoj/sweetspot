module Program

open Pulumi
open Pulumi.FSharp
open KubernetesHelpers
open AzureHelpers
open Pulumi.Kubernetes.Core.V1
open Pulumi.Azure.ServiceBus
open Pulumi.Kubernetes.Types.Inputs.Core.V1
open Pulumi.Azure.CosmosDB
open Pulumi.Azure.CosmosDB.Inputs
open System


let deployApps (stack: StackReference) =
    
    let createAppConfig (appName, imageName, modifier) =
        createApplicationConfig (ApplicationName appName) (ImageName imageName)
        |> modifier

    let sbConnectionString = stack |> getStackOutput "sbConnectionstring"
    let inputMap = 
        ["connectionstring", io (sbConnectionString.Apply(fun s -> s |> toBase64))]
        |> inputMap
        |> InputMap

    let secret = createSecret stack "servicebus" inputMap

    let apps = 
        [
            "sweetspotcsharpworker", "sweetspot.csharpworker", (addSecretModifier secret)
            "sweetspotweb", "sweetspot.web", (addSecretModifier secret)
        ] 
        |> List.map createAppConfig
        |> createApplications stack

    let getIp (service: Service) =
        service.Status
        |> Outputs.apply(fun status -> status.LoadBalancer.Ingress.[0].Ip)

    apps
    |> List.map (
        fun (appName, app) ->
            appName, (getIp app.Service :> obj)
        ) 

let deployAppInfrastructure (stack: StackReference) =
    let topic = createServiceBusTopic stack "sweetspot-dev-web-topic"
    let subscription = createServiceBusSubscription stack topic "sweetspot-dev-web-topic-worker-sub"
    let cosmosDb = createCosmosDb stack "sweetspotdb" id

    [
        "topic", topic.Name :> obj
        "subscription", subscription.Name :> obj
        "dbMasterKey", (cosmosDb.PrimaryMasterKey.Apply<string>(makeSecret)) :> obj
        "dbEndpoint", cosmosDb.Endpoint :> obj
    ]

let infra () =
    let stack = getCoreStackRef()
    let infrastructureOutput = deployAppInfrastructure stack
    let appsOutput = deployApps stack
    [
        infrastructureOutput
        appsOutput
    ]
    |> List.concat
    |> dict


[<EntryPoint>]
let main _ =
    Deployment.run infra
