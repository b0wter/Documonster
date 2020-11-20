namespace b0wter.DocuMonster.Google.Utilities

module MimeType =
    type T = T of string
    let (PDF: T) = T "application/pdf"
    let (TIFF: T) = T "application/tiff"

    let private typeMap = [("tiff", TIFF); ("pdf", PDF)] |> Map.ofList

    let getForExtension (extension: string) =
        typeMap |> Map.tryFind (extension.ToLower())

    let value (t: T) = 
        match t with T s -> s

module BucketUri =
    type T = T of string

    let private creationConditions = [
        fun (s: string) -> s.StartsWith("gs://")
    ]

    let create (s: string) : Result<T, string>  =
        match creationConditions |> List.tryFind (not << ((|>) s)) with
        | None -> Ok <| T s
        | Some condition -> Error "Could not validate Bucket Url."

    let value (t: T) : string =
        match t with T s -> s


module BucketName =
    type T = T of string

module Tasks =
    open FSharp.Control.Tasks.V2
    open System.Threading.Tasks

    let map (f: 'a -> 'b) (t: Task<'a>) : Task<'b> =
        task {
            let! t = t
            return f t
        }

    let bind (f: 'a -> Task<'b>) (t: Task<'a>) : Task<'b> =
        task {
            let! t = t
            let! temp = f t
            return temp
        }

module Result =
    open FSharp.Control.Tasks.V2
    open System.Threading.Tasks

    let mapT (f: 'a -> Task<'b>) (result: Result<'a, 'c>) : Task<Result<'b, 'c>> =
        task {
            match result with
            | Ok a -> 
                let! b = f a
                return Ok b
            | Error e -> return Error e
        }

    let bindT (f: 'a -> Task<Result<'b, 'c>>) (result: Result<'a, 'c>) : Task<Result<'b, 'c>> =
        task {
            match result with
            | Ok a ->
                match! f a with
                | Ok b -> return Ok b
                | Error e -> return Error e
            | Error e ->
                return Error e
        }

    let map2 (a: Result<'a, 'b>) (b: Result<'c, 'b>) (f: 'a -> 'c -> 'd) =
        match a, b with
        | Ok oa, Ok ob ->
            Ok (f oa ob)
        | Error e, _ -> Error e
        | _, Error e -> Error e