open System
open b0wter.DocuMonster.Google
open FSharp.Control.Tasks.V2
open Argu

let bucketName = "documonster"

let parseFilename (f: string) =
    if not <| IO.File.Exists(f) then failwith (sprintf "The given file '%s' does not exist." f) 
    else if not <| f.EndsWith(".pdf") then failwith "You may only supply pdf files."
    else f

type Args =
    | [<CliPrefix(CliPrefix.None)>] List //of ParseResults<ListArgs>
    | [<CliPrefix(CliPrefix.None)>] Annotate of string //of ParseResults<AnnotationArgs>
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | List _ -> "List previously annotated files."
            | Annotate _ -> "Annotate a pdf file."

[<EntryPoint>]
let main argv =
    task {
        let annotate filename =
            task {
            match Storage.SClient.Create (), Annotation.AClient.Create () with
            | Ok sclient, Ok aclient ->

                let! document = Core.uploadAndAnnotateAsResult sclient
                                    aclient bucketName
                                    { Core.FileDefinition.LocalFileName = filename
                                      Core.FileDefinition.MimeType = Utilities.MimeType.PDF }

                match document with
                | Ok r ->
                    printfn "%A" (r.Pages |> List.map (fun r -> r.Text))
                    return 0
                | Error e ->
                    printfn "%s" e
                    return 1
            | Error e, _ ->
                do printfn "The Google Storage client could not be initialized because: %s" e
                return 1
            | _, Error e ->
                do printfn "The Google Image Annotation client could not be initialized because: %s" e
                return 1
            }
                
        let errorHandler = ProcessExiter(colorizer = function ErrorCode.HelpText -> None | _ -> None)
        let parser = ArgumentParser.Create<Args>(errorHandler = errorHandler)
        try
            let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
            match results.GetAllResults() with
            | [] ->
                failwith "You need to specify a command."
                return 1
            | [ List ] ->
                failwith "Not implemented."
                return 1
            | [ Annotate _ ] ->
                let filename = results.PostProcessResult(<@ Annotate @>, parseFilename)
                let! result = annotate filename
                return result
            | _ ->
                failwith "You need to supply either the 'list' or the 'annotate' argument not both."
                return 1
        with e ->
            printfn "%s" e.Message
            printfn "%s" (parser.PrintUsage())
            return 1
                
    } |> Async.AwaitTask |> Async.RunSynchronously
        
        


