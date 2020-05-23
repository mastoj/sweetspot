// Learn more about F# at http://fsharp.org

open System
open Giraffe
open Saturn
open Microsoft.AspNetCore.Http
open FSharp.Control.Tasks.ContextInsensitive

type Hello = { Wat: string }

let messagesHandler next (ctx: HttpContext) =
    task {
        let! hello = ctx.BindJsonAsync<Hello>()
        let response = { Wat = (sprintf "Hello again %s" hello.Wat) }
        return! ctx.WriteJsonAsync response
    }

let apiRoutes = router {
    post "/api/messages" messagesHandler    
}

let webRoutes = router {
    get "/" (text "Hello world")
}

let app = application {
    use_router apiRoutes
    use_router webRoutes
}


[<EntryPoint>]
let main argv =
    printfn "Hello World from F#!"
    run app
    0 // return an integer exit code

(*

http://localhost:5000/
http://localhost:5000/api/messages

POST http://localhost:5000/api/messages HTTP/1.1
content-type: application/json

{
    "wat": "sample two"
}

*)