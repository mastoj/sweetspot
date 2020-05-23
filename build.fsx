#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.DotNet.MSBuild
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators


// Properties
let buildDir = "./.build/"

// *** Define Targets ***
Target.create "Clean" (fun _ ->
    Trace.log " --- Cleaning stuff --- "
    Shell.cleanDir buildDir
)

Target.create "BuildApp" (fun _ ->
    !! "src/app/**/*.fsproj"
        |> MSBuild.runRelease id buildDir "Publish"
        |> Trace.logItems "AppBuild-Output: "
)

Target.create "Publish" (fun _ ->
    Trace.log " --- Publishing app --- "
)

open Fake.Core.TargetOperators

// *** Define Dependencies ***
"Clean"
    ==> "BuildApp"
    ==> "Publish"

// *** Start Build ***
Target.runOrDefault "Publish"