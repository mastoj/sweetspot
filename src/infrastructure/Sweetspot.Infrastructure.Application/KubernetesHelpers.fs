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
        with
            override this.ToString() =
                match this with
                | LoadBalancer -> "LoadBalancer"
   
    type SetCustomResourceOptions =  (CustomResourceOptions -> CustomResourceOptions)

    type ServiceName = ServiceName of string
    type ServiceConfig = {
        Name: ServiceName
        Selector: InputMap
        ServiceType: ServiceType
        SetCustomResourceOptions: SetCustomResourceOptions
        CustomOptions: Map<string, obj>
    }
    with
        static member InitConfig serviceName =
            {
                Name = serviceName
                Selector = (InputMap.List [])
                ServiceType = LoadBalancer
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

let getCoreStackRef() =
    let env = "dev" // stackParts[stackParts.Length - 1];
    let stackRef = sprintf "mastoj/Sweetspot.core/%s" env
    StackReference(stackRef)

let getStackOutput key (stack: StackReference) =
    stack.RequireOutput(input key).Apply(fun v -> v.ToString())

let getClusterConfig = getStackOutput "kubeconfig"
let getAcrRegistryName stack = 
    let fullName = stack |> getStackOutput "registryLoginServer"
    fullName.Apply(lastPart "/")

let getK8sProvider clusterConfig =
    Provider("k8s",
        ProviderArgs(
            KubeConfig = io clusterConfig,
            Namespace = input "app"
        )
    )

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
    let selectorInput = serviceConfig.Selector.ToInputMap()
    let targetPort: InputUnion<int, string> = InputUnion.op_Implicit(80)
    let (ServiceName serviceName) = serviceConfig.Name
    let options = serviceConfig.SetCustomResourceOptions (CustomResourceOptions(Provider = k8sProvider))

    Service(serviceName,
        ServiceArgs(
            Metadata = input (
                ObjectMetaArgs(
                    Name = input serviceName
                )
            ),
            Spec = input (
                ServiceSpecArgs(
                    Type = input (serviceConfig.ServiceType.ToString()),
                    Selector = selectorInput,
                    Ports = inputList [
                        input (ServicePortArgs(
                            Port = input 80,
                            TargetPort = targetPort,
                            Protocol = input "TCP"
                        ))
                    ])
                )),
            options = options
    )

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
            with Selector = (InputMap.List deploymentLabels)
    }
    {
        DeploymentConfig = deploymentConfig
        ServiceConfig = serviceConfig
    }

let createApplication (stack: StackReference) (k8sProvider: Provider) (applicationConfig: ApplicationConfig) =
    let deployment = applicationConfig.DeploymentConfig |> createDeployment stack k8sProvider
    let service = applicationConfig.ServiceConfig |> createService k8sProvider
    { Deployment = deployment; Service = service }

let createApplications (applicationConfigs: ApplicationConfig list) =
    let stack = getCoreStackRef()
    let k8sprovider =
        stack
        |> getClusterConfig
        |> getK8sProvider

    applicationConfigs
    |> List.map (
        fun config ->
            let (DeploymentName deployName) = config.DeploymentConfig.Name
            deployName, createApplication stack k8sprovider config
        )
