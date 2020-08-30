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

    let sbConnectionString =
        getStackOutput "sample_send_endpoint" stack

    let inputMap =
        [ "sbconnectionstring", io (sbConnectionString.Apply(fun s -> s |> toBase64)) ]
        |> inputMap
        |> InputMap

    let secret =
        createSecret k8sCluster "servicebus" inputMap

    let addSbConnectionString =
        addSecret "SB_CONNECTIONSTRING" "sbconnectionstring" secret

    let workerName = "sweetspotcsharpworker"

    let worker =
        createApplicationConfig (ApplicationName workerName) (ImageName "sweetspot.csharpworker")
        |> addSbConnectionString
        |> withLoadbalancer
        |> createApplication stack k8sCluster

    let webName = "sweetspotweb"

    let web =
        createApplicationConfig (ApplicationName webName) (ImageName "sweetspot.web")
        |> addSbConnectionString
        |> withLoadbalancer
        |> createApplication stack k8sCluster

    let apps = [ (workerName, worker); (webName, web) ]
    apps
    |> List.map (fun (appName, app) -> appName, (getServiceIp app.Service :> obj))

let deployAppInfrastructure (stack: StackReference) =
    let resourceGroupName = getStackOutput "resourceGroupName" stack

    let serviceBusNamespace =
        getStackOutput "servicebusNamespace" stack

    let topicName = getStackOutput "sample" stack

    let subscription =
        createServiceBusSubscription (io resourceGroupName) (io serviceBusNamespace) (io topicName) "sample-sub"

    let cosmosDb = createCosmosDb stack "sweetspotdb" id

    [ "subscription", subscription.Name :> obj
      "dbMasterKey", (cosmosDb.PrimaryMasterKey.Apply<string>(makeSecret)) :> obj
      "dbEndpoint", cosmosDb.Endpoint :> obj ]

let infra () =
    let env = "dev"
    let coreStacKName = sprintf "mastoj/Sweetspot.core/%s" env

    let stack = getStackRef coreStacKName
    let infrastructureOutput = deployAppInfrastructure stack
    let appsOutput = deployApps stack
    [ infrastructureOutput; appsOutput ]
    |> List.concat
    |> dict


[<EntryPoint>]
let main _ = Deployment.run infra
