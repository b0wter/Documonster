open System
open b0wter.DocuMonster.Google
open FSharp.Control.Tasks.V2
open Argu
open b0wter.DocuMonster.SharedEntities.Utilities

let bucketName = "documonster"

let parseFilename (f: string) =
    if not <| IO.File.Exists(f) then failwith (sprintf "The given file '%s' does not exist." f) 
    else if not <| f.EndsWith(".pdf") then failwith "You may only supply pdf files."
    else f

type AnnotateArgs =
    | [<MainCommand; First; CliPrefix(CliPrefix.None)>] Filename of string
    | [<AltCommandLine("-n")>] Name of string 
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Filename _ -> "File to annotate"
            | Name _ -> "Name of the document in the database."
            
type Args =
    | [<CliPrefix(CliPrefix.None)>] List //of ParseResults<ListArgs>
    | [<CliPrefix(CliPrefix.None)>] Annotate of ParseResults<AnnotateArgs>
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | List _ -> "List previously annotated files."
            | Annotate _ -> "Annotate a pdf file."
    (*
    | [<CliPrefix(CliPrefix.None)>] Annotate of string //of ParseResults<AnnotationArgs>
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | List _ -> "List previously annotated files."
            | Annotate _ -> "Annotate a pdf file."
    *)

[<EntryPoint>]
let main argv =
    task {
        let annotate filename name =
            task {
            match Storage.SClient.Create (), Annotation.AClient.Create () with
            | Ok sClient, Ok aClient ->
                let! document = Core.uploadAndAnnotateAsResult sClient
                                    aClient bucketName
                                    { Core.FileDefinition.LocalFileName = filename
                                      Core.FileDefinition.MimeType = Utilities.MimeType.PDF }
                                    name
                match document with
                | Ok r ->
                    return Ok r
                | Error e ->
                    return Error e
            | Error e, _ ->
                return Error (sprintf "The Google Storage client could not be initialized because: %s" e)
            | _, Error e ->
                return Error (sprintf "The Google Image Annotation client could not be initialized because: %s" e)
            }
            
        let store couchDb filename document =
            task {
                return! b0wter.DocuMonster.CouchDb.Core.storeWith couchDb document filename |> Async.StartAsTask
            }
                
        let errorHandler = ProcessExiter(colorizer = function ErrorCode.HelpText -> None | _ -> None)
        let parser = ArgumentParser.Create<Args>(errorHandler = errorHandler)
        //try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
        let! couchDbConnection = b0wter.DocuMonster.CouchDb.Core.initFromEnvironment ()
        match results.GetAllResults(), couchDbConnection with
        | [], _ ->
            printfn "You need to specify a command."
            printfn "%s" (parser.PrintUsage())
            return 1
        | [ List ], Ok couchDb ->
            
            return 1
        | _, Error e ->
            printfn "Could not initialize CouchDb Connection because: %s" e
            return 1
        | [ Annotate _ ], Ok couchDb ->
            let annotationParameter = results.GetResult(<@ Annotate @>)
            let filename = annotationParameter.PostProcessResult(<@ Filename @>, parseFilename)
            let name = annotationParameter.TryGetResult(<@ Name @>)
            let! annotateResult = annotate filename name
            let! storeResult = annotateResult |> (Result.bindT (store couchDb filename))
            match storeResult with
            | Ok _ ->
                printfn "File was annotated successfully and stored in CouchDb."
                return 0
            | Error e ->
                printfn "There was an error trying to annotate and store the file:"
                printfn "%s" e
                return 1
        | _ ->
            printfn "You need to supply either the 'list' or the 'annotate' argument not both."
            printfn "%s" (parser.PrintUsage())
            return 1
                
    } |> Async.AwaitTask |> Async.RunSynchronously
        
        


