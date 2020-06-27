module Program

open Pulumi
open Pulumi.FSharp
open KubernetesHelpers
open Pulumi.Kubernetes.Core.V1
open Pulumi.Azure.ServiceBus
open Pulumi.Kubernetes.Types.Inputs.Core.V1
open Pulumi.Azure.CosmosDB
open Pulumi.Azure.CosmosDB.Inputs
open System

type ApplicationConfigModifier = ApplicationConfig -> ApplicationConfig

let makeSecret = Func<string, Output<string>>(Output.CreateSecret)

let toBase64 (str: string) =
    let bytes = System.Text.Encoding.UTF8.GetBytes(str)
    System.Convert.ToBase64String(bytes)

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
        )

    let envVarArgs' = (input envVarArg)::envVariables
    { 
        appConfig with
            DeploymentConfig = {
                appConfig.DeploymentConfig with
                    EnvVariables = envVarArgs'
            }
    }

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

let createCosmosDb stack (config: AccountArgs -> AccountArgs) =
    let resourceGroupName = getStackOutput "resourceGroupName" stack
    let location = getStackOutput "location" stack
    let args = 
        AccountArgs(
            ResourceGroupName = io resourceGroupName,
            ConsistencyPolicy = input (
                AccountConsistencyPolicyArgs(
                    ConsistencyLevel = input "Session",
                    MaxIntervalInSeconds = input 5,
                    MaxStalenessPrefix = input 100
                )),
            OfferType = input "standard",
            GeoLocations = inputList [
                input (
                    AccountGeoLocationArgs(
                        Location = io location,
                        FailoverPriority = input 0
                    )
                )
            ]
        )
        |> config
    Account("sweetspotdb",
        args
    )

let deployAppInfrastructure (stack: StackReference) =
    let topic = createServiceBusTopic stack "sweetspot-dev-web-topic"
    let subscription = createServiceBusSubscription stack topic "sweetspot-dev-web-topic-worker-sub"
    let cosmosDb = createCosmosDb stack id

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