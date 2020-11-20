namespace b0wter.DocuMonster.Google

module Storage =

    open System
    open System.IO
    open Google.Cloud.Storage.V1
    open FSharp.Control.Tasks.V2
    open System.Threading.Tasks
    open FSharp.Control

    let private bucketName = "documonster"

    type StorageObject = {
        Bucket: string
        Id: string
    }

    type ObjectRetrievalParameter = {
        BucketName: string
        ObjectName: string
    }

    let createObjectRetrievalParameter bucketName objectName = { BucketName = bucketName; ObjectName = objectName }

    type RetrievedTextFile = {
        BucketName: string
        ObjectName: string
        Content: string
    }

    let createClient () =
        try
            Ok (StorageClient.Create())
        with
        | ex -> Error (ex.ToString())

    type SClient private (client: StorageClient) =
        member this.Client = client
        static member Create() =
            match createClient () with
            | Ok client ->
                Ok (SClient(client))
            | Error e ->
                Error (sprintf "The Google Storage Client could not be properly initialized. Internal error: %s" e)

    type Destination
        = FullPath of string
        | PathAndName of string * string
        | PathParts of string array

    let private filenameFromDestination (d: Destination) : Result<string, string> =
        match d with
        | FullPath f -> Ok f
        | PathAndName (filename, path) -> Ok (Path.Combine(filename, path))
        | PathParts [||] -> Error "The list of path parts is empty."
        | PathParts [| single |] -> Ok single
        | PathParts many -> Ok (Path.Combine many)

    let bucketExists (client: SClient) (bucketName: string) : Task<Result<bool, string>> =
        task {
            try
                let! _ = client.Client.GetBucketAsync(bucketName)
                return Ok true
            with
            | :? Google.GoogleApiException as ex ->
                if ex.HttpStatusCode = System.Net.HttpStatusCode.NotFound then
                    return Ok false
                else
                    let err = sprintf "Could not determine if bucket exists, reason: %s; Message: %s; Code: %i" ex.Error.Errors.[0].Reason ex.Error.Errors.[0].Message ex.Error.Code  
                    return Error err
            | ex -> 
                let err = sprintf "%s - %s" (ex.GetType().FullName) ex.Message
                return Error err
        }

    let retrieveTextFile (client: SClient) bucketName objectName =
        task {
            try
                let stream = new MemoryStream()
                let! file = client.Client.DownloadObjectAsync(bucketName, objectName, stream)
                return Ok (Text.Encoding.UTF8.GetString(stream.ToArray()))
            with
            | ex -> return Error ex.Message
        }

    let retrieveManyTextFiles (client: SClient) (objects: ObjectRetrievalParameter list) =

        let format ((o: ObjectRetrievalParameter), (t: Task<Result<string, string>>)) =
            match t.Result with
            | Ok content -> Ok { BucketName = o.BucketName; ObjectName = o.ObjectName; Content = content}
            | Error e -> Error e

        task {
            let retriever (o: ObjectRetrievalParameter) = retrieveTextFile client o.BucketName o.ObjectName 
            let tasks = objects |> List.map retriever
            let! x = Task.WhenAll(tasks)
            let zipped = List.zip objects tasks |> List.map format
            return zipped
        }

    let uploadWith (client: SClient) (bucketName: string) (destinationName: string) (contentType: string) (stream: System.IO.Stream) =
        task {
            try
                let! result = (client.Client.UploadObjectAsync(bucketName, destinationName, contentType, stream))
                return Ok { Bucket = result.Bucket; Id = result.Id }
            with
            | ex -> return Error ex.Message
        }

    let uploadLocalFileWith (client: SClient) (bucketName: string) (destination: Destination) (contentType: Utilities.MimeType.T) (localFilename: string) =
        let contentType = contentType |> Utilities.MimeType.value
        if File.Exists localFilename then
            let fileStream = try Ok (File.OpenRead(localFilename)) with | ex -> Error (ex.ToString())
            let destination = destination |> filenameFromDestination
            match fileStream, destination with
            | Ok stream, Ok dest ->
                uploadWith client bucketName dest contentType stream
            | Error e, _ | _, Error e ->  Task.Run(fun () -> Error e)
        else
            Task.Run (fun () -> Error "The local file does not exist.")
        
    let delete (client: SClient) (bucketName: string) (destination: Destination) =
        task {
            match destination |> filenameFromDestination with
            | Ok file -> 
                try
                    do! client.Client.DeleteObjectAsync(bucketName, file)
                    return Ok ()
                with
                | ex -> return Error (ex.ToString())
            | Error e -> return Error e
        }

    let list (client: SClient) (bucketName: string) (prefix: string option) =
        async {
            try
                let prefix = match prefix with | Some s -> s | None -> String.Empty
                let result = client.Client.ListObjectsAsync(bucketName, prefix)
                let asyncSec = AsyncSeq.ofAsyncEnum (result :> Collections.Generic.IAsyncEnumerable<Google.Apis.Storage.v1.Data.Object>)
                let! all = asyncSec |> AsyncSeq.toListAsync
                return Ok all
            with
            | ex -> return Error (ex.ToString())
        }

    let formatFileSize (size: UInt64) : string * string = 
        let abbreviations = [| "B"; "kB"; "MB"; "GB"; "TB" |]
        let maxDepth = abbreviations.Length - 1

        let rec run (rest: string) (depth: int) : (string * int) =
            if depth >= maxDepth then
                rest, depth
            else if rest.Length <= 3 then
                rest, depth
            else if rest.Length <= 5 then
                let decimalPointPosition = rest.Length - 3
                let withDecimalPoint = rest.Substring(0, decimalPointPosition) + "." + rest.Substring(decimalPointPosition)
                withDecimalPoint.Substring(0, 4).TrimStart('.').TrimEnd('.'), depth + 1
            else
                run (rest.Remove(rest.Length - 3)) (depth + 1)

        let trimmedSize, depth = run (size.ToString()) 0
        trimmedSize, abbreviations.[depth]

    let prettyDataObject (o: Google.Apis.Storage.v1.Data.Object) : string =
        let trimmedSize, sizeUnit = formatFileSize (if o.Size.HasValue then o.Size.Value else UInt64.MinValue)
        let creationTime = if o.TimeCreated.HasValue then o.TimeCreated.Value else DateTime.MinValue
        let updateTime = if o.Updated.HasValue then o.Updated.Value else DateTime.MinValue

        sprintf "%s (%s %s) %s (C) %s (U)" o.Name trimmedSize sizeUnit (creationTime.ToString()) (updateTime.ToString())
