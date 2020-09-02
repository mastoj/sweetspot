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

let getTopicConfigInputMap topicKey stack =
    let sendEndpoint =
        getStackOutput (sprintf "%s_send_endpoint" topicKey) stack

    let listenEndpoint =
        getStackOutput (sprintf "%s_listen_endpoint" topicKey) stack

    let topicName =
        getStackOutput topicKey stack

    [
        (sprintf "SB_%s_TOPIC" topicKey).ToUpper(), io (sendEndpoint.Apply(fun s -> s |> toBase64))
        (sprintf "SB_%s_ENDPOINT_LISTEN" topicKey).ToUpper(), io (listenEndpoint.Apply(fun s -> s |> toBase64))
        (sprintf "SB_%s_ENDPOINT_SEND" topicKey).ToUpper(), io (topicName.Apply(fun s -> s |> toBase64))
    ]
    |> inputMap
    |> InputMap

let deployApps (stack: StackReference) =

    let k8sCluster =
        stack
        |> getClusterConfig "kubeconfig"
        |> getK8sProvider "k8s" "app"

    let sampleSendEndpoint =
        getStackOutput "sample_send_endpoint" stack

    let sampleSendEndpoint =
        getStackOutput "sample_listen_endpoint" stack

    let sampleTopicInputMap = getTopicConfigInputMap "sample" stack

    let secret =
        createSecret k8sCluster "sb-sample" sampleTopicInputMap

    let addSampleTopicSecret =
        addSecret "SB_SAMPLE_TOPIC" "SB_SAMPLE_TOPIC" secret

    let addSendEndpointSecret =
        addSecret "SB_SAMPLE_ENDPOINT_LISTEN" "SB_SAMPLE_ENDPOINT_LISTEN" secret

    let addListenEndpointSecret =
        addSecret "SB_SAMPLE_ENDPOINT_SEND" "SB_SAMPLE_ENDPOINT_SEND" secret

    let workerName = "sweetspotcsharpworker"

    let worker =
        createApplicationConfig (ApplicationName workerName) (ImageName "sweetspot.csharpworker")
        |> (addSampleTopicSecret >> addListenEndpointSecret)
        |> withLoadbalancer
        |> createApplication stack k8sCluster

    let webName = "sweetspotweb"

    let web =
        createApplicationConfig (ApplicationName webName) (ImageName "sweetspot.web")
        |> (addSampleTopicSecret >> addSendEndpointSecret)
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
