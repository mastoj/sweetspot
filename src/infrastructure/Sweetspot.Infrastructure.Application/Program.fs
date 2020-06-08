module Program

open Pulumi
open Pulumi.FSharp
open Pulumi.Kubernetes
open Pulumi.Kubernetes.Core.V1
open Pulumi.Kubernetes.Apps.V1
open Pulumi.Kubernetes.Types.Inputs.Core.V1
open Pulumi.Kubernetes.Types.Inputs.Apps.V1
open Pulumi.Kubernetes.Types.Inputs.Meta.V1

let lastPart (delimeter: string) (value: string) =
  let parts = value.Split(delimeter)
  parts.[parts.Length - 1]

let getCoreStackRef() =
  let env =  "dev" // stackParts[stackParts.Length - 1];
  let stackRef = sprintf "mastoj/Sweetspot.core/%s" env
  StackReference(stackRef)

let getStackOutput key (stack: StackReference) =
  stack.RequireOutput(input key).Apply(fun v -> v.ToString())

let getClusterConfig = getStackOutput "kubeconfig"
let getAcrRegistryName stack = 
  let fullName = stack |> getStackOutput "registryName"
  fullName.Apply(lastPart "/")

let getK8sProvider clusterConfig =
  Provider("k8s",
    ProviderArgs(
      KubeConfig = io clusterConfig,
      Namespace = input "app"
    )
  )

let infra () =
  let coreStack = getCoreStackRef()
  let provider = coreStack |> getClusterConfig |> getK8sProvider
  let customResourceOptions = CustomResourceOptions(Provider = provider)

  let toInputMap xs =
    xs
    |> List.map (fun (x, y) -> x, input y)
    |> inputMap

  let createDeployment appName imageName appLabels (envVariables: Input<EnvVarArgs> list) =
    let inputMapLabels = appLabels |> toInputMap

    Deployment(appName,
      DeploymentArgs(
        Spec = input (
          DeploymentSpecArgs(
            Selector = input (LabelSelectorArgs(MatchLabels = inputMapLabels)),
            Replicas = input 1,
            Template = input (
              PodTemplateSpecArgs(
                Metadata = input (ObjectMetaArgs(Labels = inputMapLabels)),
                Spec = input (
                  PodSpecArgs(
                    Containers = 
                      inputList [
                        input (
                          ContainerArgs(
                            Name = input appName,
                            Image = io imageName,
                            ImagePullPolicy = input "Always",
                            Env = inputList envVariables,
                            Ports = 
                              inputList [
                                input (
                                  ContainerPortArgs(ContainerPortValue = input 80)
                                )
                              ]
                          )
                        )
                      ]
                  )
                )
              )
            )
          )
        )
      ), options = customResourceOptions)

  let createService serviceName serviceType selector =
    let selectorInput = selector |> toInputMap
    let targetPort: InputUnion<int, string> = InputUnion.op_Implicit(80)

    Service(serviceName,
      ServiceArgs(
        Metadata = input (
          ObjectMetaArgs(
            Name = input serviceName
          )
        ),
        Spec = input (
          ServiceSpecArgs(
            Type = input serviceType,
            Selector = selectorInput,
            Ports = inputList [
              input (ServicePortArgs(
                Port = input 80,
                TargetPort = targetPort,
                Protocol = input "TCP"
              ))
            ])
          )),
        options = customResourceOptions
    )


  let acrRegistry = coreStack |> getAcrRegistryName

  let csharpWorkerName = "sweetspotcsharpworker"
  let csharpWorkerLabels = [ "app", csharpWorkerName ]
  let csharpWorkerImageName = acrRegistry.Apply(fun acr -> sprintf "%s.azurecr.io/%s:latest" acr "sweetspot.csharpworker")
  let csharpWorkerWeb = createDeployment "sweetspotcsharpworker" csharpWorkerImageName csharpWorkerLabels []
  let csharpWorkerService = createService "sweetspotcsharpworker" "LoadBalancer" csharpWorkerLabels

  let csharpWorkerWebName = 
    csharpWorkerWeb.Metadata
    |> Outputs.apply(fun (metadata) -> metadata.Name)
  let csharpWorkerServiceIp = 
    csharpWorkerService.Status
    |> Outputs.apply(fun status -> status.LoadBalancer.Ingress.[0].Ip)
  let csharpWorkerServiceName = 
    csharpWorkerService.Metadata
    |> Outputs.apply(fun metadata -> metadata.Name)

  let webAppName = "sweetspotweb"
  let webImageName = acrRegistry.Apply(fun acr -> sprintf "%s.azurecr.io/%s:latest" acr "sweetspot.web")
  let webAppLabels = ["app", webAppName ]
  let envVariables = [
    input (EnvVarArgs(
      Name = input "service__csharpworker__host", 
      Value = io (csharpWorkerServiceName)))
    input (EnvVarArgs(
      Name = input "service__csharpworker__port", 
      Value = input "80"))
  ]
  let webDeployment = createDeployment webAppName webImageName webAppLabels envVariables
  let webService = createService webAppName "LoadBalancer" webAppLabels

  let webName = 
    webDeployment.Metadata
    |> Outputs.apply(fun (metadata) -> metadata.Name)

  let webServiceIp = 
    webService.Status
    |> Outputs.apply(fun status -> status.LoadBalancer.Ingress.[0].Ip)

  dict [
    ("webName", webName :> obj)
    ("webServiceIp", webServiceIp :> obj)
    ("csharpWorkerWebName", csharpWorkerWebName :> obj)
    ("csharpWorkerServiceIp", csharpWorkerServiceIp :> obj)
  ]

[<EntryPoint>]
let main _ =
  Deployment.run infra
