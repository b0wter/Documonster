namespace b0wter.DocuMonster.BsaSearch

open System.Collections.Generic
open System.IO
open Bsa.Search.Core
open Bsa.Search.Core.Documents
open Bsa.Search.Core.Indexes
open Bsa.Search.Core.Indexes.Requests
open Bsa.Search.Core.Searcher
open b0wter.DocuMonster.SharedEntities.Document
open b0wter.FSharp

module Core =
    
    /// Creates a new BSA document from an annotated document.
    /// Each block in the document is converted into a separate `TextIndexField` and is given a name based
    /// on the page and block index: $PAGE_INDEX--$BLOCK_INDEX.
    let private createBsaDocument (document: Document) = //(name: string) (fields: (string * string) list) =
        let name = document.Name |> Option.getOrElse document.Filename
        let doc = IndexDocument(name)
        
        let formatBlockName pageIndex blockIndex =
            sprintf "%i--%i" pageIndex blockIndex
            
        let toNameAndContentTuple pageIndex blockIndex block =
            formatBlockName pageIndex blockIndex, block |> b0wter.DocuMonster.SharedEntities.Document.block
        
        let fields = document.Pages
                     |> List.mapi (fun pageIndex p -> p.Blocks
                                                      |> List.mapi (toNameAndContentTuple pageIndex)
                     )
                     |> List.collect id
        
        do fields |> List.iter (fun (name, content) ->
            doc.Add(Bsa.Search.Core.Helpers.TextFieldExtension.GetField(name, content))
        )
        
        doc
        
    /// Contains the details of where a match was found inside a field.
    /// (A document consists of multiple fields (e.g. paragraphs, titles, ...). See document creation for details.)
    type Match = {
        Position: int
        Length: int
        StartIndex: int
        Word: string
    }
    
    let private toMatch (i: IndexFieldTerm) : Match =
        {
            Position = i.Position
            Length = i.Length
            StartIndex = i.StartIndex
            Word = i.Word
        }
    
    type FieldResult = {
        FieldName: string
        Matches: Match seq
    }
    
    let private toFieldResult (kvp: KeyValuePair<string, HighlightFieldCollection>) : FieldResult =
        {
            FieldName = kvp.Key
            Matches = kvp.Value.Items |> Seq.map toMatch
        }
    
    type SearchResult = {
        DocumentName: string
        Fields: FieldResult seq
    }
        
    let private toSearchResult (kvp: KeyValuePair<string, HighlightFieldResult>) : SearchResult =
        {
            DocumentName = kvp.Key
            Fields = kvp.Value.Fields |> Seq.map toFieldResult
        }
        
    /// Contains all information to identify a match.
    /// In contrast to a `Match` this contains the field name as well as the document name.
    type FlatMatch = {
        Position: int
        Length: int
        StartIndex: int
        Word: string
        FieldName: string
        DocumentName: string
    }
    
    let asFlatMatch (searchResults: SearchResult seq) : FlatMatch seq =
        let flattener (searchResult: SearchResult) : FlatMatch seq =
            searchResult.Fields |> Seq.collect (fun s ->
                s.Matches |> Seq.map (fun m ->
                        {
                            Position = m.Position
                            Length = m.Length
                            StartIndex = m.StartIndex
                            Word = m.Word
                            FieldName = s.FieldName
                            DocumentName = searchResult.DocumentName
                        }
                    )    
            )
        searchResults |> Seq.collect flattener

    let extractFromHighlights (results: Dictionary<string, HighlightFieldResult>) : SearchResult seq =
        results |> Seq.map toSearchResult
        
    /// Wrapper type for the BSA internal document index style.
    /// This makes it so that the calling assembly does not have to have a reference
    /// to the BSA package.
    type BsaIndex private (index: MemoryDocumentIndex) =
        let index = index
        let searchService = SearchServiceEngine(index)
        
        /// Creates a new index from a previously serialized index.
        static member FromStream(s: Stream) =
            let newIndex = new MemoryDocumentIndex()
            do newIndex.Import(s)
            BsaIndex(newIndex)
            
        /// Creates a new empty `BsaIndex`.
        static member Empty() =
            let newIndex = new MemoryDocumentIndex()
            BsaIndex(newIndex)
        
        /// Save this index to a stream.
        member this.ToStream(s: Stream) =
            do index.Export(s)
            
        /// Index a new IndexDocument.
        member this.Index(d: IndexDocument) =
            do searchService.Index([| d  |])
            
        member this.IndexDocument (d: Document) =
            d |> createBsaDocument |> this.Index
            
        member this.Search (query: string) : SearchResult seq =
            let field = "*"
            let parsed = Bsa.Search.Core.Queries.Helpers.SearchQueryParserHelper.Parse(query)
            let request = SearchQueryRequest(Field = field, Query = parsed, ShowHighlight = true, Size = 20)
            let result = searchService.Search(request)
            result.Highlights |> extractFromHighlights
    
    let hello () =
        let field = "*"
        let query = """
            large
            """
        let documentIndex = new MemoryDocumentIndex()
        //let content = SampleText.all
        let searchService = SearchServiceEngine(documentIndex)
        let doc = IndexDocument("SampleText")
        //do doc.Add(IndexField("content", content))
        //do doc.Add("content".GetField(content))
        do doc.Add(Bsa.Search.Core.Helpers.TextFieldExtension.GetField("p1", SampleText.p1))
        do doc.Add(Bsa.Search.Core.Helpers.TextFieldExtension.GetField("p2", SampleText.p2))
        do doc.Add(Bsa.Search.Core.Helpers.TextFieldExtension.GetField("p3", SampleText.p3))
        
        do searchService.Index([| doc |])
        
        let parsed = Bsa.Search.Core.Queries.Helpers.SearchQueryParserHelper.Parse(query)
        
        let request = SearchQueryRequest(Field = field, Query = parsed, ShowHighlight = true, Size = 20)
        let result = searchService.Search(request)
        
        // Results are found here:
        // result.Highlights.[XYZ].Fields[ABC]
        // where XYZ is an index over all matching documents while ABC is an index over all text fields that match
        // inside the given document.
        
        0