module Program

open Pulumi
open Pulumi.FSharp
open KubernetesHelpers
open Pulumi.Kubernetes.Core.V1

let infra () =
    let createAppConfig (appName, imageName) =
        createApplicationConfig (ApplicationName appName) (ImageName imageName)

    let apps = 
        [
            "sweetspotcsharpworker", "sweetspot.csharpworker"
            "sweetspotweb", "sweetspot.web"
        ] 
        |> List.map createAppConfig
        |> createApplications

    let getIp (service: Service) =
        service.Status
        |> Outputs.apply(fun status -> status.LoadBalancer.Ingress.[0].Ip)

    apps
    |> List.map (
        fun (appName, app) ->
            appName, (getIp app.Service :> obj)
        ) 
    |> dict

[<EntryPoint>]
let main _ =
    Deployment.run infra
