module Program

open Pulumi.AzureAD
open Pulumi.Azure.Core
open Pulumi.Azure.ContainerService
open Pulumi.Azure.ContainerService.Inputs
open Pulumi.Azure.Network
open Pulumi.Azure.Role
open Pulumi.FSharp
open Pulumi.Kubernetes.Yaml
open Pulumi.Random
open Pulumi.Tls
open Pulumi

[<RequireQualifiedAccess>]
module Helpers =
    let createResourceGroup name = ResourceGroup(name)

    let createPassword name =
        RandomPassword(name, 
            RandomPasswordArgs(
                Length = input 20,
                Special = input true
            )
        )

    let createPrivateKey name =
        PrivateKey(name,
            PrivateKeyArgs(
                Algorithm = input "RSA",
                RsaBits = input 4096
            ))

    let createApplication name = Application(name)

    let createServicePrincipal name (app: Application) =
        ServicePrincipal(name,
            ServicePrincipalArgs(ApplicationId = io app.ApplicationId))

    let createServicePrincipalPassword name (password: RandomPassword) (servicePrincipal: ServicePrincipal) =
        ServicePrincipalPassword(name,
            ServicePrincipalPasswordArgs(
                ServicePrincipalId = io servicePrincipal.Id,
                Value = io password.Result,
                EndDate = input "2099-01-01T00:00:00Z"
            ))

    let assignNetworkContributorRole name (servicePrincipal: ServicePrincipal) (resourceGroup: ResourceGroup) =
        Assignment(name,
            AssignmentArgs(
                PrincipalId = io servicePrincipal.Id,
                Scope = io resourceGroup.Id,
                RoleDefinitionName = input "Network Contributor"))

    let createVnet name (resourceGroup: ResourceGroup) =
        VirtualNetwork(name,
            VirtualNetworkArgs(
                ResourceGroupName = io resourceGroup.Name,
                AddressSpaces = inputList [ input "10.2.0.0/16" ]
            ))

    let createSubnet name (vnet: VirtualNetwork) (resourceGroup: ResourceGroup) =
        Subnet(name, 
            SubnetArgs(
                ResourceGroupName = io resourceGroup.Name,
                VirtualNetworkName = io vnet.Name,
                AddressPrefixes = inputList [ input "10.2.1.0/24" ]
            ))

    let createContainerRegistry name (resourceGroup: ResourceGroup) =
        Registry(name, 
            RegistryArgs(
                Sku = input "basic",
                ResourceGroupName = io resourceGroup.Name
            ))

    let private createAssignment 
                    name 
                    roleDefintion 
                    (principalId: Output<string>)
                    (scope: Output<string>) =
        Assignment(name, 
            AssignmentArgs(
                PrincipalId = io principalId,
                RoleDefinitionName = input roleDefintion,
                Scope = io scope
            ))

    let createContainerRegistryAssignment name (containerRegistry: Registry) (sp: ServicePrincipal) =
        createAssignment name "AcrPull" sp.Id containerRegistry.Id 

    let createNetworkAssignment name (subnet: Subnet) (sp: ServicePrincipal) =
        createAssignment name "Network Contributor" sp.Id subnet.Id 

    let createCluster
            name
            (subnet: Subnet)
            (privateKey: PrivateKey)
            (app: Application)
            (servicePrincipalPassword: ServicePrincipalPassword)
            (acrAssignment: Assignment)
            (networkAssignment: Assignment)
            (resourceGroup: ResourceGroup)
            kubernetesVersion
            nodeCount =
        let defaultNodePoolArgs =
            KubernetesClusterDefaultNodePoolArgs(
                Name = input "aksagentpool",
                NodeCount = input nodeCount,
                VmSize = input "Standard_B2s",
                OsDiskSizeGb = input 30,
                VnetSubnetId = io subnet.Id
            )

        let linuxProfileArgs = 
            let keyArgs = KubernetesClusterLinuxProfileSshKeyArgs(KeyData = io privateKey.PublicKeyOpenssh)
            KubernetesClusterLinuxProfileArgs(
                AdminUsername = input "aksuser",
                SshKey = input keyArgs
            )

        let servicePrincipalArgs =
            KubernetesClusterServicePrincipalArgs(
                ClientId = io app.ApplicationId,
                ClientSecret = io servicePrincipalPassword.Value
            )

        let rbacArgs =
            KubernetesClusterRoleBasedAccessControlArgs(Enabled = input true)

        let networkProfileArgs =
            KubernetesClusterNetworkProfileArgs(
                NetworkPlugin = input "azure",
                DnsServiceIp = input "10.2.2.254",
                ServiceCidr = input "10.2.2.0/24",
                DockerBridgeCidr = input "172.17.0.1/16"
            )

        KubernetesCluster(name,
            KubernetesClusterArgs(
                ResourceGroupName = io resourceGroup.Name,
                DefaultNodePool = input defaultNodePoolArgs,
                DnsPrefix = input "fsaks",
                LinuxProfile = input linuxProfileArgs,
                ServicePrincipal = input servicePrincipalArgs,
                KubernetesVersion = input kubernetesVersion,
                RoleBasedAccessControl = input rbacArgs,
                NetworkProfile = input networkProfileArgs
            ),
            CustomResourceOptions(
                DependsOn = inputList [ input (acrAssignment :> Resource) ; input (networkAssignment :> Resource) ]
            ))

let infra () =
    let resourceGroupName = "sweetspot-rg"
    let resourceGroup = Helpers.createResourceGroup resourceGroupName
    let password = Helpers.createPassword "sweetspot-sp-password"
    let privateKey = Helpers.createPrivateKey "sweetspot-sshkey"
    let app = Helpers.createApplication "sweetspot"
    let servicePrincipal = Helpers.createServicePrincipal "sweetspot-sp" app
    let servicePrincipalPassword = Helpers.createServicePrincipalPassword "sweetspot-sp-password" password servicePrincipal
    let networkRole = Helpers.assignNetworkContributorRole "role-assignment" servicePrincipal resourceGroup
    let vnet = Helpers.createVnet "fsaksvnet" resourceGroup
    let subnet = Helpers.createSubnet "fsakssubnet" vnet resourceGroup
    let containerRegistry = Helpers.createContainerRegistry "sweetspotacr" resourceGroup
    let containerRegistryAssignment = Helpers.createContainerRegistryAssignment "sweetspotacrassignment" containerRegistry servicePrincipal
    let networkAssignment = Helpers.createNetworkAssignment "subnetassignment" subnet servicePrincipal

    let nodeCount = 3
    let kubernetesVersion = "1.16.7"

    let cluster =
        Helpers.createCluster
            "sweetspot"
            subnet
            privateKey
            app
            servicePrincipalPassword
            containerRegistryAssignment
            networkAssignment
            resourceGroup
            kubernetesVersion
            nodeCount

    ConfigFile("linkerd",
        ConfigFileArgs(
            File = input "manifests/linkerd.yaml"
        )) |> ignore

    ConfigFile("k8sdashboard",
        ConfigFileArgs(
            File = input "manifests/dashboard.yaml"
        )) |> ignore


    // ConfigFile("proxy_inject",
    //     ConfigFileArgs(
    //         File = input "manifests/proxy_inject.yaml"
    //     )) |> ignore

    // Export the kubeconfig string for the storage account
    dict [
        ("kubeconfig", cluster.KubeConfigRaw :> obj)
        ("registryId", containerRegistry.Id :> obj)
        ("registryName", containerRegistry.Name :> obj)
    ]

[<EntryPoint>]
let main _ =
    Deployment.run infra