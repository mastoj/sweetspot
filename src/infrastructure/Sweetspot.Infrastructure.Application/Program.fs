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

    let k8sCluster =
        stack
        |> getClusterConfig "kubeconfig"
        |> getK8sProvider "k8s" "app"

    let sbConnectionString = stack |> getStackOutput "sbConnectionstring"
    let inputMap = 
        ["connectionstring", io (sbConnectionString.Apply(fun s -> s |> toBase64))]
        |> inputMap
        |> InputMap

    let secret = createSecret k8sCluster "servicebus" inputMap
    let addSbConnectionString = addSecret "SB_CONNECTIONSTRING" "connectionstring" secret

    let workerName = "sweetspotcsharpworker"
    let worker =
        createApplicationConfig (ApplicationName workerName) (ImageName "sweetspot.csharpworker")
        |> addSbConnectionString
        |> createApplication stack k8sCluster

    let webName = "sweetspotweb"
    let web =
        createApplicationConfig (ApplicationName webName) (ImageName "sweetspot.web")
        |> addSbConnectionString
        |> withLoadbalancer
        |> createApplication stack k8sCluster

    let apps = [
        (workerName, worker)
        (webName, web)
    ]
    apps
    |> List.map (
        fun (appName, app) ->
            appName, (getServiceIp app.Service :> obj)
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
    let env = "dev"
    let coreStacKName = sprintf "mastoj/Sweetspot.core/%s" env

    let stack = getStackRef coreStacKName
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