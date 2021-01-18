namespace b0wter.DocuMonster.CouchDb


module Core =
    open b0wter.CouchDb.Lib
    open b0wter.DocuMonster.SharedEntities
    open b0wter.CouchDb.Lib.Server
    open System
    open b0wter.FSharp
    
    let private readLocalFile (filename: string) : Result<byte array, string> =
        if IO.File.Exists filename then
            try
                Ok (IO.File.ReadAllBytes filename)
            with
            | ex -> Error ex.Message
        else
            Error (sprintf "The file '%s' does not exist." filename)
        
    let configurationFromEnvironment () =
        let ip = match Environment.GetEnvironmentVariable("DOCUMONSTER_COUCHDB_IP") with null -> None | s -> Some s 
        let port = Environment.GetEnvironmentVariable("DOCUMONSTER_COUCHDB_PORT") |> Parsers.parseInt
        let user = match Environment.GetEnvironmentVariable("DOCUMONSTER_COUCHDB_USER") with null -> None | s -> Some s
        let password = match Environment.GetEnvironmentVariable("DOCUMONSTER_COUCHDB_PASSWORD") with null -> None | s -> Some s
        let dbName = match Environment.GetEnvironmentVariable("DOCUMONSTER_COUCHDB_DB") with null -> None | s -> Some s 
        
        match ip, port, user, password, dbName with
        | None, _, _, _, _ -> Error "CouchDb host environment variable is not set or invalid."
        | _, None, _, _, _ -> Error "CouchDb port environment variable is not set or invalid."
        | _, _, None, _, _ -> Error "CouchDb username environment variable is not set or invalid."
        | _, _, _, None, _ -> Error "CouchDb password environment variable is not set or invalid."
        | _, _, _, _, None -> Error "CouchDb database name environment variable is not set or invalid."
        | Some ip, Some port, Some user, Some password, Some dbName ->
            let credentials = Credentials.create (user, password)
            match (DbProperties.create (ip, port, credentials, DbProperties.ConnectionType.Http)) with
            | DbProperties.HostIsEmpty -> Error "CouchDb host environment variable is not set or invalid."
            | DbProperties.PortIsInvalid -> Error "CouchDb port environment variable is not set or invalid."
            | DbProperties.Valid c -> Ok (c, dbName)
            
    let authenticate (props, dbName) =
        // dbName is not required but it makes it easier to chain this function
        async {
            match! Authenticate.queryAsResult props with
            | Ok _ -> return Ok (props, dbName)
            | Error e -> return Error (e |> ErrorRequestResult.textAsString)
        }
        
    let private createDatabase (props, dbName) =
        async {
            match! b0wter.CouchDb.Lib.Databases.Exists.queryAsResult props dbName with
            | Ok exists when exists ->
                return Ok (props, dbName)
            | Ok _ ->
                match! b0wter.CouchDb.Lib.Databases.Create.queryAsResult props dbName [] with
                | Ok r when r.Ok -> return Ok (props, dbName)
                | Ok _ -> return Error "Request returned success code but payload said the request failed."
                | Error e -> return Error (e |> ErrorRequestResult.textAsString)
            | Error e -> return Error (e |> ErrorRequestResult.textAsString)
        }
        
    let initFromEnvironment () =
        async {
            return! configurationFromEnvironment () |> Result.bindA authenticate |> AsyncResult.bindA createDatabase
        }
            
    let storeDocument (dbProps: DbProperties.DbProperties) (dbName: string) (document: Document.Document) =
        Databases.AddDocument.queryAsResult dbProps dbName document |> Async.map (function | Ok response -> Ok response | Error e -> Error (ErrorRequestResult.textAsString e))
        
    let storeAttachment ((dbProps: DbProperties.DbProperties), (dbName: string)) (documentId: string) (documentRev: string option) (attachmentName: string) (attachment: byte[]) =
        async {
            match! Attachments.PutBinary.queryAsResult dbProps dbName documentId documentRev attachmentName attachment with
            | Ok response ->
                if response.Ok then
                    return Ok ()
                else
                    return Error "Request returned with a success state but the response returned false."
            | Error e ->
                return Error (ErrorRequestResult.textAsString e)
        }
        
    let retrieveAttachment ((dbProps: DbProperties.DbProperties), (dbName: string)) documentId attachmentName : Async<Result<byte[], string>> =
        async {
            let! result = Attachments.GetBinary.queryAsResult dbProps dbName documentId attachmentName
            return result |> Result.mapError ErrorRequestResult.binaryAsString
        }
        
    let storeDocumentAndFileWith ((dbProps: DbProperties.DbProperties), (dbName: string)) (document: Document.Document) (filename: string) : Async<Result<unit, string>> =
        async {
            match readLocalFile filename with
            | Ok bytes ->
                return! (storeDocument dbProps dbName document |> AsyncResult.bindA (fun response -> storeAttachment (dbProps, dbName) response.Id (Some response.Rev) "pdf" bytes))
            | Error e ->
                return Error (sprintf "Could not read '%s' from the local file system because: %s" filename e)
        }
        
    /// Retrieves a document by using its id.
    let retrieveById<'a> ((dbProps: DbProperties.DbProperties), (dbName: string)) (documentId: string) : Async<Result<'a, string>> =
        async {
            //let! result = b0wter.CouchDb.Lib.Documents.Get.queryAsResult dbProps dbName documentId []
            let documentIdSelector = Mango.condition "_id" (Mango.Equal <| (Mango.Text documentId)) |> Mango.createExpression
            let! result = b0wter.CouchDb.Lib.Databases.Find.queryAsResult<'a> dbProps dbName documentIdSelector
            return result |> Result.mapBoth (fun ok -> ok.Docs.Head) (fun error -> error |> ErrorRequestResult.textAsString)
        }
        
    /// Checks whether a given document id exists.
    let documentIdExists ((dbProps: DbProperties.DbProperties), (dbName: string)) documentId : Async<Result<bool, string>> =
        async {
            let! result = b0wter.CouchDb.Lib.Documents.Head.query dbProps dbName documentId
            return match result with
                    | HttpVerbs.Head.DocumentExists _ | HttpVerbs.Head.NotModified _ -> Ok true
                    | HttpVerbs.Head.NotFound _ -> Ok false
                    | HttpVerbs.Head.Unauthorized e | HttpVerbs.Head.DbNameMissing e | HttpVerbs.Head.DocumentIdMissing e | HttpVerbs.Head.ParameterIsMissing e | HttpVerbs.Head.Unknown e ->
                        ErrorRequestResult.fromRequestResultAndCase(e, result) |> ErrorRequestResult.textAsString |> Error
        }
        
    let attachmentIdExists ((dbProps: DbProperties.DbProperties), (dbName: string)) documentId attachmentId : Async<Result<bool, string>> =
        async {
            let! result = b0wter.CouchDb.Lib.Attachments.Head.query dbProps dbName documentId attachmentId None
            return match result with
                    | HttpVerbs.Head.DocumentExists _ | HttpVerbs.Head.NotModified _ -> Ok true
                    | HttpVerbs.Head.NotFound _ -> Ok false
                    | HttpVerbs.Head.Unauthorized e | HttpVerbs.Head.DbNameMissing e | HttpVerbs.Head.DocumentIdMissing e | HttpVerbs.Head.ParameterIsMissing e | HttpVerbs.Head.Unknown e ->
                        ErrorRequestResult.fromRequestResultAndCase(e, result) |> ErrorRequestResult.textAsString |> Error
        }