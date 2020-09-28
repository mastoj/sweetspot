module AzureHelpers

open Pulumi
open Pulumi.FSharp
open KubernetesHelpers
open Pulumi.Kubernetes.Core.V1
open Pulumi.Azure.ServiceBus
open Pulumi.Kubernetes.Types.Inputs.Core.V1
open Pulumi.Azure.CosmosDB
open Pulumi.Azure.CosmosDB.Inputs

let createServiceBusTopic (stack: StackReference) topicName =
    let resourceGroupName = getStackOutput "resourceGroupName" stack

    let serviceBusNamespace =
        getStackOutput "servicebusNamespace" stack

    Topic
        (topicName,
         TopicArgs
             (Name = input topicName, ResourceGroupName = io resourceGroupName, NamespaceName = io serviceBusNamespace))

let createServiceBusSubscription resourceGroupName namespaceName topicName subscriptionName =
    Subscription
        (subscriptionName,
         SubscriptionArgs
             (Name = input subscriptionName,
              ResourceGroupName = resourceGroupName,
              NamespaceName = namespaceName,
              TopicName = topicName,
              MaxDeliveryCount = input 3))

let createCosmosDb stack name (config: AccountArgs -> AccountArgs) =
    let resourceGroupName = getStackOutput "resourceGroupName" stack
    let location = getStackOutput "location" stack

    let args =
        AccountArgs
            (ResourceGroupName = io resourceGroupName,
             ConsistencyPolicy =
                 input
                     (AccountConsistencyPolicyArgs
                         (ConsistencyLevel = input "Session",
                          MaxIntervalInSeconds = input 5,
                          MaxStalenessPrefix = input 100)),
             OfferType = input "standard",
             GeoLocations =
                 inputList [ input (AccountGeoLocationArgs(Location = io location, FailoverPriority = input 0)) ])
        |> config

    Account(name, args)
