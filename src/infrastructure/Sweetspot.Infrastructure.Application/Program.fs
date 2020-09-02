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

type Infrastructure =
    { Subscription: Subscription
      CosmosDb: Account }

type App = { Secret: Secret }

let getServiceBusInputMap (infrastructure: Infrastructure) stack =
    let topicKey = "sample"

    let sendEndpoint =
        getStackOutput (sprintf "%s_send_endpoint" topicKey) stack

    let listenEndpoint =
        getStackOutput (sprintf "%s_listen_endpoint" topicKey) stack

    let topicName = getStackOutput topicKey stack

    [ (sprintf "SB_%s_TOPIC" topicKey).ToUpper(), io (topicName.Apply(fun s -> s |> toBase64))
      (sprintf "SB_%s_ENDPOINT_LISTEN" topicKey).ToUpper(), io (listenEndpoint.Apply(fun s -> s |> toBase64))
      (sprintf "SB_%s_ENDPOINT_SEND" topicKey).ToUpper(), io (sendEndpoint.Apply(fun s -> s |> toBase64))
      (sprintf "SB_%s_SUBSCRIPTION" topicKey).ToUpper(),
      io (infrastructure.Subscription.Name.Apply(fun s -> s |> toBase64)) ]
    |> inputMap
    |> InputMap

let deployWeb webName (secret: Secret) (k8sProvider: Pulumi.Kubernetes.Provider) (stack: StackReference) =
    let addSampleTopicSecret =
        addSecret "SB_SAMPLE_TOPIC" "SB_SAMPLE_TOPIC" secret

    let addSendEndpointSecret =
        addSecret "SB_SAMPLE_ENDPOINT_SEND" "SB_SAMPLE_ENDPOINT_SEND" secret

    createApplicationConfig (ApplicationName webName) (ImageName "sweetspot.web")
    |> (addSampleTopicSecret >> addSendEndpointSecret)
    |> withLoadbalancer
    |> createApplication stack k8sProvider


let deployWorker workerName (secret: Secret) (k8sProvider: Pulumi.Kubernetes.Provider) (stack: StackReference) =
    let addSampleSubscriptionSecret =
        addSecret "SB_SAMPLE_SUBSCRIPTION" "SB_SAMPLE_SUBSCRIPTION" secret

    let addListenEndpointSecret =
        addSecret "SB_SAMPLE_ENDPOINT_LISTEN" "SB_SAMPLE_ENDPOINT_LISTEN" secret

    createApplicationConfig (ApplicationName workerName) (ImageName "sweetspot.csharpworker")
    |> (addSampleSubscriptionSecret
        >> addListenEndpointSecret)
    |> withLoadbalancer
    |> createApplication stack k8sProvider

let deployApps (infrastructure: Infrastructure) (stack: StackReference) =
    let serviceBusInputMap =
        getServiceBusInputMap infrastructure stack

    let k8sCluster =
        stack
        |> getClusterConfig "kubeconfig"
        |> getK8sProvider "k8s" "app"

    let secret =
        createSecret k8sCluster "sb-sample" serviceBusInputMap

    let webName = "sweetspotweb"
    let workerName = "sweetspotcsharpworker"

    [ webName, deployWeb webName secret k8sCluster stack
      workerName, deployWorker workerName secret k8sCluster stack ]
    |> List.map (fun (appName, app) -> appName, (getServiceIp app.Service :> obj))

let deployAppInfrastructure (stack: StackReference) =
    let resourceGroupName = getStackOutput "resourceGroupName" stack

    let serviceBusNamespace =
        getStackOutput "servicebusNamespace" stack

    let topicName = getStackOutput "sample" stack

    let subscription =
        createServiceBusSubscription (io resourceGroupName) (io serviceBusNamespace) (io topicName) "sample-sub"

    let cosmosDb = createCosmosDb stack "sweetspotdb" id

    { Subscription = subscription
      CosmosDb = cosmosDb }

let infra () =
    let env = "dev"
    let coreStacKName = sprintf "mastoj/Sweetspot.core/%s" env

    let stack = getStackRef coreStacKName
    let infrastructure = deployAppInfrastructure stack
    let appsOutput = deployApps infrastructure stack

    let infrastructureOutput =
        [ "sample_subscription", infrastructure.Subscription.Name :> obj
          "dbMasterKey", (infrastructure.CosmosDb.PrimaryMasterKey.Apply<string>(makeSecret)) :> obj
          "dbEndpoint", infrastructure.CosmosDb.Endpoint :> obj ]

    [ infrastructureOutput; appsOutput ]
    |> List.concat
    |> dict

[<EntryPoint>]
let main _ = Deployment.run infra
