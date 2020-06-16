module Program

open Pulumi
open Pulumi.FSharp
open KubernetesHelpers
open Pulumi.Kubernetes.Core.V1
open Pulumi.Azure.ServiceBus

let deployApps (stack: StackReference) =
    let createAppConfig (appName, imageName) =
        createApplicationConfig (ApplicationName appName) (ImageName imageName)

    let apps = 
        [
            "sweetspotcsharpworker", "sweetspot.csharpworker"
            "sweetspotweb", "sweetspot.web"
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

let createServiceBusTopic (stack: StackReference) topicName =
    let resourceGroupName = getStackOutput "resourceGroupName" stack
    let serviceBusNamespace = getStackOutput "servicebusNamespace" stack
    Topic(topicName,
        TopicArgs(
            Name = input topicName,
            ResourceGroupName = io resourceGroupName,
            NamespaceName = io serviceBusNamespace
        )
    )

let createServiceBusSubscription (stack: StackReference) (topic: Topic) subscriptionName =
    let resourceGroupName = getStackOutput "resourceGroupName" stack
    let serviceBusNamespace = getStackOutput "servicebusNamespace" stack
    Subscription(subscriptionName,
        SubscriptionArgs(
            Name = input subscriptionName,
            ResourceGroupName = io resourceGroupName,
            NamespaceName = io serviceBusNamespace,
            TopicName = io (topic.Name)
        )
    )


let infra () =
    let stack = getCoreStackRef()
    let appsOutput = deployApps stack
    let topic = createServiceBusTopic stack "sweetspot-dev-web-topic"
    let subscription = createServiceBusSubscription stack topic "sweetspot-dev-web-topic-worker-sub"
    [
        "topic", topic.Name :> obj
        "subscription", subscription.Name :> obj
    ] @ appsOutput
    |> dict


[<EntryPoint>]
let main _ =
    Deployment.run infra
