module Program

open Pulumi
open Pulumi.FSharp

let getCoreStackRef() =
    let env = "dev" // stackParts[stackParts.Length - 1];
    let stackRef = sprintf "mastoj/Sweetspot.core/%s" env
    StackReference(stackRef)

let getStackOutput key (stack: StackReference) =
    stack.RequireOutput(input key).Apply(fun v -> v.ToString())

let get 

let infra () =
    let stack = getCoreStackRef()
    // let name = 
    //     deployment.Metadata
    //     |> Outputs.apply(fun (metadata) -> metadata.Name)

    dict []

[<EntryPoint>]
let main _ =
    Deployment.run infra
