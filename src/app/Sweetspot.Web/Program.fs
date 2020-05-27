// Learn more about F# at http://fsharp.org

open System
open Giraffe
open Saturn
open Microsoft.AspNetCore.Http
open FSharp.Control.Tasks.ContextInsensitive
open System.Net.Http
open System.Text.Json

[<CLIMutable>]
type WeatherForecast = {
    Date: DateTime
    TemperatureC: int
    Summary: string
//        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

type WeatherClient = {
    GetWeather: unit -> Async<WeatherForecast []>
}

module WeatherClient =
    let client (baseAddress: Uri) =
        let client = 
            new HttpClient(
                BaseAddress = baseAddress
            )
        let options = 
            JsonSerializerOptions(
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            )

        let getWeather() =
            async {
                let! responseMessage = client.GetAsync("/weatherforecast") |> Async.AwaitTask
                let! stream = responseMessage.Content.ReadAsStringAsync() |> Async.AwaitTask
                return JsonSerializer.Deserialize<WeatherForecast[]>(stream, options)
            }
        {
            GetWeather = getWeather
        }

type Hello = { Wat: string }

let messagesHandler next (ctx: HttpContext) =
    let weatherClient: WeatherClient = ctx.GetService<WeatherClient>()
    task {
        let! response = weatherClient.GetWeather()
        let! hello = ctx.BindJsonAsync<Hello>()
        let response = { Wat = (sprintf "Hello again %s: %A" hello.Wat response) }
        return! ctx.WriteJsonAsync response
    }

open Microsoft.Extensions.Configuration
let helloHandler next (ctx: HttpContext) =
    let weatherClient: WeatherClient = ctx.GetService<WeatherClient>()
    let baseUri = TyeConfigurationExtensions.GetServiceUri(ctx.GetService<IConfiguration>(), "sweetspot.csharpworker", null)
    printfn "==> Wat: %A" baseUri
    task {
        let! response = weatherClient.GetWeather()
        let response = { Wat = (sprintf "Hello again wat: %A" response) }
        return! ctx.WriteJsonAsync response
    }

let apiRoutes = router {
    post "/api/messages" messagesHandler    
}

let webRoutes = router {
    get "/" helloHandler
}

open Microsoft.Extensions.DependencyInjection
let app =

    let configureServices (services: IServiceCollection) =
        
        services.AddScoped<WeatherClient>(fun sp -> 
            let configuration = sp.GetService<IConfiguration>()
            let baseUri = TyeConfigurationExtensions.GetServiceUri(sp.GetService<IConfiguration>(), "csharpworker", "https")
            let hostUri = 
                if isNull baseUri
                then Uri(configuration.["csharpworker"])
                else baseUri
            printfn "==> BackendUri: %A" baseUri
            printfn "==> hostUri: %A" hostUri
            WeatherClient.client hostUri)

    //     services.Add<WeatherClient>(() => { services.Get<HttpClient>() with BaseAddress = Configuration.GetServiceUri("sweetspot.csharpworker")})
    // let httpClient = 
    //     HttpClient(
    //         BaseAddress = Configuration.GetServiceUri("sweetspot.csharpworker")
    //     )

    application {
        service_config configureServices
        // use_config (fun c -> 
        //     printfn "==> FROM ENVIRON: %s" (c.GetValue("sweetspot.csharpworker")))
        use_router (apiRoutes)
        use_router webRoutes
    }


[<EntryPoint>]
let main argv =
    printfn "Hello World from F#!"
    run (app)
    0

(*

http://localhost:61670/
http://localhost:5000/api/messages

POST http://localhost:61451/api/messages HTTP/1.1
content-type: application/json

{
    "wat": "sample two"
}

*)