module b0wter.DocuMonster.SharedEntities.Utilities

    open System.Threading.Tasks
    open FSharp.Control.Tasks.V2

    module Result =
        let bindT (f: 'a -> Task<Result<'c, 'b>>) (r: Result<'a, 'b>) : Task<Result<'c, 'b>> =
            task {
                match r with
                | Ok o ->
                    return! f o
                | Error e ->
                    return Error e
            }

        let map2 (r1: Result<'a, 'b>) (r2: Result<'c, 'b>) (f: 'a -> 'c -> 'd) : Result<'d, 'b> =
            match r1, r2 with
            | Ok r1, Ok r2 ->
                Ok (f r1 r2)
            | Error e, _ | _, Error e -> Error e