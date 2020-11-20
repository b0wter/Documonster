open System
open b0wter.DocuMonster.Google
open FSharp.Control.Tasks.V2
open System.Threading.Tasks
open Argu

type CliCommand
    = Annotate
    | List
    
type CommandArgs =
    | [<MainCommand; ExactlyOnce; First>] Annotate of string
    | [<MainCommand; ExactlyOnce; First>] List 
    
[<EntryPoint>]
let main argv =
    task {
        match Storage.SClient.Create (), Annotation.AClient.Create () with
        | Ok sclient, Ok aclient ->

            let! document = Core.uploadAndAnnotateAsResult sclient
                                aclient "documonster"
                                { Core.FileDefinition.LocalFileName = "/home/b0wter/downloads/sample.pdf"
                                  Core.FileDefinition.MimeType = Utilities.MimeType.PDF }

            match document with
            | Ok r ->
                printfn "%A" (r.Pages |> List.map (fun r -> r.Text))
                return 0
            | Error e ->
                printfn "%s" e
                return -1
        | Error e, _ ->
            do printfn "The Google Storage client could not be initialized because: %s" e
            return 1
        | _, Error e ->
            do printfn "The Google Image Annotation client could not be initialized because: %s" e
            return 1
    } |> Async.AwaitTask |> Async.RunSynchronously


