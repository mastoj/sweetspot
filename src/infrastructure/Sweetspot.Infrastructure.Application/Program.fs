module Program

open Pulumi
open Pulumi.FSharp
open KubernetesHelpers
open Pulumi.Kubernetes.Core.V1
open Pulumi.Azure.ServiceBus
open Pulumi.Kubernetes.Types.Inputs.Core.V1

type ApplicationConfigModifier = ApplicationConfig -> ApplicationConfig

let toBase64 (str: string) =
    let bytes = System.Text.Encoding.UTF8.GetBytes(str)
    System.Convert.ToBase64String(bytes)

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

    let addSecretModifier (secret: Secret) (appConfig: ApplicationConfig) =
        let envVariables = appConfig.DeploymentConfig.EnvVariables
        let envVarArg =
            EnvVarArgs(
                Name = input "SB_CONNECTIONSTRING",
                ValueFrom = input (
                    EnvVarSourceArgs(
                        SecretKeyRef = input (
                            SecretKeySelectorArgs(
                                Name = io (secret.Metadata.Apply(fun m -> m.Name)),
                                Key = input "connectionstring"
                            ))
                    ))
//                ValueFrom
            )
        let envVarArgs' = (input envVarArg)::envVariables
        { 
            appConfig with
                DeploymentConfig = {
                    appConfig.DeploymentConfig with
                        EnvVariables = envVarArgs'
                }
        }


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
            TopicName = io (topic.Name),
            MaxDeliveryCount = input 3
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
