module KubernetesHelpers
open Pulumi
open Pulumi.FSharp.Ops
open Pulumi.Kubernetes.Core.V1
open Pulumi.Kubernetes.Apps.V1
open Pulumi.Kubernetes.Types.Inputs.Core.V1
open Pulumi.Kubernetes.Types.Inputs.Apps.V1
open Pulumi.Kubernetes.Types.Inputs.Meta.V1
open Pulumi.Kubernetes
open LibGit2Sharp
open System
open Pulumi.FSharp

[<AutoOpen>]
module Types =
    type InputMap =
        | List of (string * string) list
        | InputMap of InputMap<string>
        with 
            member this.ToInputMap() =
                let toInputMap xs =
                    xs
                    |> List.map (fun (x, y) -> x, input y)
                    |> inputMap
                match this with
                | InputMap im -> im
                | List lst -> lst |> toInputMap

    type ServiceType =
        | LoadBalancer
        | NodePort
        | ClusterIP
        | ExternalName of string
        with
            member this.TypeString() =
                match this with
                | LoadBalancer -> "LoadBalancer"
                | NodePort -> "NodePort"
                | ClusterIP -> "ClusterIP"
                | ExternalName _ -> "ExternalName"
            member this.Apply (serviceArgs: ServiceArgs) =
                match this with
                | ExternalName name ->
                    serviceArgs.Spec <- io (
                        serviceArgs.Spec.Apply(
                            fun spec ->
                                spec.Type <- input (this.TypeString())
                                spec.ExternalName <- input name
                                spec))
                | _ ->
                    serviceArgs.Spec <- io (
                        serviceArgs.Spec.Apply(
                            fun spec ->
                                spec.Type <- input (this.TypeString())
                                spec))
                serviceArgs
    
    type TransportProtocol =
        | UDP
        | TCP
        with member this.ProtocolString() =
            match this with
            | UDP -> "UDP"
            | TCP -> "TCP"
    
    type ServicePort = 
        {
            Port: int
            TargetPort: int
            Protocol: TransportProtocol
        }
        with
            member this.Apply(serviceArgs: ServiceArgs) =
                let specArgs = serviceArgs.Spec.Apply(fun spec ->
                    spec.Ports.Add(
                        input (
                            ServicePortArgs(
                                Port = input this.Port,
                                TargetPort = inputUnion1Of2 this.TargetPort,
                                Protocol = input (this.Protocol.ProtocolString())
                            )
                        )
                    )
                    spec
                )
                serviceArgs.Spec <- io specArgs
                serviceArgs
   
    type SetCustomResourceOptions =  (CustomResourceOptions -> CustomResourceOptions)

    type ServiceName = ServiceName of string
    type AppSelector = AppSelector of InputMap
        with 
            member this.Apply(serviceArgs: ServiceArgs) =
                let (AppSelector im) = this
                let specArgs = 
                    serviceArgs.Spec.Apply(fun spec ->
                        spec.Selector <- (im.ToInputMap())
                        spec
                    )
                serviceArgs.Spec <- io specArgs
                serviceArgs

    type ServiceConfig = {
        Name: ServiceName
        Selector: AppSelector
        ServiceType: ServiceType
        ServicePort: ServicePort
        SetCustomResourceOptions: SetCustomResourceOptions
        CustomOptions: Map<string, obj>
    }
    with
        static member InitConfig serviceName =
            {
                Name = serviceName
                Selector = (AppSelector (InputMap.List []))
                ServiceType = NodePort
                ServicePort = { Port = 80; TargetPort = 80; Protocol = TCP }
                SetCustomResourceOptions = id
                CustomOptions = Map.empty
            }

    type ImageName = ImageName of string
    type DeploytmentName = DeploymentName of string
    type Replicas = Replicas of int
    type DeploymentConfig = {
        Name: DeploytmentName
        Image: ImageName
        Labels: InputMap
        Replicas: Replicas
        EnvVariables: Input<EnvVarArgs> list
        SetCustomResourceOptions: SetCustomResourceOptions
        CustomOptions: Map<string, obj>
    }
    with
        static member InitConfig deploymentName imageName replicas = 
            {
                DeploymentConfig.Name = deploymentName
                Image = imageName
                Labels = (InputMap.List [])
                Replicas = replicas
                EnvVariables = []
                SetCustomResourceOptions = id
                CustomOptions = Map.empty
            }

    type ApplicationName = ApplicationName of string
    type ApplicationConfig = {
        DeploymentConfig: DeploymentConfig
        ServiceConfig: ServiceConfig
    }
    type Application = {
        Deployment: Deployment
        Service: Service
    }

let lastPart (delimeter: string) (value: string) =
    let parts = value.Split(delimeter)
    parts.[parts.Length - 1]

let mutable private mutableStackMap = Map.empty
let getStackRef stackName =
    if Map.containsKey stackName mutableStackMap |> not
    then 
        mutableStackMap <- mutableStackMap |> Map.add stackName (StackReference(stackName))

    mutableStackMap.[stackName]

let getStackOutput key (stack: StackReference) =
    stack.RequireOutput(input key).Apply(fun v -> v.ToString())

let getClusterConfig configName stack = getStackOutput configName stack
let getAcrRegistryName stack = 
    let fullName = stack |> getStackOutput "registryLoginServer"
    fullName.Apply(lastPart "/")

let mutable private k8sProvider = None
let getK8sProvider providerName namespaceName clusterConfig =
    if k8sProvider.IsNone
    then
        k8sProvider <- Some(
            Provider(providerName,
                ProviderArgs(
                    KubeConfig = io clusterConfig,
                    Namespace = input namespaceName
                )
            )
        )
    k8sProvider.Value

let getSha() =
    let repoPath = Repository.Discover(System.Environment.CurrentDirectory)
    use repo = new Repository(repoPath)
    let latestCommit = repo.Head.Tip
    latestCommit.Sha.Substring(0,6)

let createDeployment (stack: StackReference) (k8sProvider: Provider) (deploymentConfig: DeploymentConfig) =
    let inputMapLabels = deploymentConfig.Labels.ToInputMap()
    let (ImageName imageName) = deploymentConfig.Image
    let acrRegistry = stack |> getAcrRegistryName
    let sha = getSha()
    let fullImageName = acrRegistry.Apply(fun acr -> sprintf "%s/%s:%s" acr imageName sha)
    let (DeploymentName deploymentName) = deploymentConfig.Name
    let (Replicas replicas) = deploymentConfig.Replicas
    let options = deploymentConfig.SetCustomResourceOptions (CustomResourceOptions(Provider = k8sProvider))

    Deployment(deploymentName,
        DeploymentArgs(
            Spec = input (
                DeploymentSpecArgs(
                    Selector = input (LabelSelectorArgs(MatchLabels = inputMapLabels)),
                    Replicas = input replicas,
                    Template = input (
                        PodTemplateSpecArgs(
                            Metadata = input (ObjectMetaArgs(Labels = inputMapLabels)),
                            Spec = input (
                                PodSpecArgs(
                                    Containers = 
                                        inputList [
                                            input (
                                                ContainerArgs(
                                                    Name = input deploymentName,
                                                    Image = io fullImageName,
                                                    ImagePullPolicy = input "Always",
                                                    Env = inputList deploymentConfig.EnvVariables,
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
        ), options = options)

let createService (k8sProvider: Provider) (serviceConfig: ServiceConfig) =
    let (ServiceName serviceName) = serviceConfig.Name
    let options = serviceConfig.SetCustomResourceOptions (CustomResourceOptions(Provider = k8sProvider))

    let serviceArgs =
        ServiceArgs(
            Metadata = input (
                ObjectMetaArgs(
                    Name = input serviceName
                )
            ))
        |> serviceConfig.ServiceType.Apply
        |> serviceConfig.ServicePort.Apply
        |> serviceConfig.Selector.Apply

    Service(serviceName, serviceArgs, options = options)

let createApplicationConfig (ApplicationName applicationName) imageName =
    let deploymentLabels = [ "app", applicationName ]
    let deploymentConfig = { 
        DeploymentConfig.InitConfig 
            (DeploymentName applicationName)
            (imageName)
            (Replicas 1) 
            with Labels = (InputMap.List deploymentLabels) }
    let serviceConfig = {
        ServiceConfig.InitConfig
            (ServiceName applicationName)
            with Selector = AppSelector (InputMap.List deploymentLabels)
    }
    {
        DeploymentConfig = deploymentConfig
        ServiceConfig = serviceConfig
    }

let createApplication (stack: StackReference) (k8sProvider: Provider) (applicationConfig: ApplicationConfig) =
    let deployment = applicationConfig.DeploymentConfig |> createDeployment stack k8sProvider
    let service = applicationConfig.ServiceConfig |> createService k8sProvider
    { Deployment = deployment; Service = service }

let createSecret (k8sProvider: Provider) name (inputMap: InputMap) =
    let options = CustomResourceOptions(Provider = k8sProvider)

    Secret(
        name,
        SecretArgs(
            Data = inputMap.ToInputMap()
        ),
        options = options
    )

let getServiceIp (service: Service) =
    service.Status
    |> Outputs.apply(fun status -> status.LoadBalancer.Ingress.[0].Ip)

let makeSecret = Func<string, Output<string>>(Output.CreateSecret)

let toBase64 (str: string) =
    let bytes = System.Text.Encoding.UTF8.GetBytes(str)
    System.Convert.ToBase64String(bytes)

let addEnvVariable envVariable (appConfig: ApplicationConfig) =
    {
        appConfig with
            DeploymentConfig = {
                appConfig.DeploymentConfig with
                    EnvVariables = envVariable :: appConfig.DeploymentConfig.EnvVariables
            }
    }

let addEnvVariables envVariables (appConfig: ApplicationConfig) =
    envVariables
    |> List.fold (fun ac ev -> addEnvVariable ev ac) appConfig

let addSecret name key (secret: Secret) (appConfig: ApplicationConfig) =
    let envVarArg =
        input (
            EnvVarArgs(
                Name = input name,
                ValueFrom = input (
                    EnvVarSourceArgs(
                        SecretKeyRef = input (
                            SecretKeySelectorArgs(
                                Name = io (secret.Metadata.Apply(fun m -> m.Name)),
                                Key = input key
                            ))
                    ))
            )
        )
    addEnvVariable envVarArg appConfig

let withLoadbalancer (appConfig: ApplicationConfig) =
    {
        appConfig with
            ServiceConfig = {
                appConfig.ServiceConfig with
                    ServiceType = LoadBalancer
            }
    }