namespace b0wter.DocuMonster.CouchDb

open b0wter.CouchDb.Lib.Server

module Core =
    open b0wter.CouchDb.Lib
    open b0wter.DocuMonster.SharedEntities
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
            match! b0wter.CouchDb.Lib.Server.Authenticate.queryAsResult props with
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
            
    let private storeJson (dbProps: DbProperties.T) (dbName: string) (document: Document.Document) =
        Databases.AddDocument.queryAsResult dbProps dbName document |> Async.map (function | Ok response -> Ok response | Error e -> Error (ErrorRequestResult.textAsString e))
        
    let private storeAttachment (dbProps: DbProperties.T) (dbName: string) (documentId: string) (documentRev: string) (attachment: byte[]) =
        async {
            match! b0wter.CouchDb.Lib.Attachments.PutBinary.queryAsResult dbProps dbName documentId documentRev "pdf" attachment with
            | Ok response ->
                if response.Ok then
                    return Ok ()
                else
                    return Error "Request returned with a success state but the response returned false."
            | Error e ->
                return Error (ErrorRequestResult.textAsString e)
        }
        
    let storeWith ((dbProps: DbProperties.T), (dbName: string)) (document: Document.Document) (filename: string) : Async<Result<unit, string>> =
        async {
            match readLocalFile filename with
            | Ok bytes ->
                return! (storeJson dbProps dbName document |> AsyncResult.bindA (fun response -> storeAttachment dbProps dbName response.Id response.Rev bytes))
            | Error e ->
                return Error (sprintf "Could not read '%s' from the local file system because: %s" filename e)
        }
        