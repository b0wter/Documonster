namespace b0wter.DocuMonster.SharedEntities

module Document =
    type Point = {
        X: float
        Y: float
    }

    type BoundingBox = {
        NormalizedVertices: Point list
    }

    type Symbol = {
        BoundingBox: BoundingBox
        Text: string
        Confidence: float
    }

    type Word = {
        BoundingBox: BoundingBox
        Symbols: Symbol list
        Confidence: float
    }

    type Paragraph = {
        BoundingBox: BoundingBox
        Words: Word list
    }

    type Block = {
        BoundingBox: BoundingBox
        Paragraphs: Paragraph list
    }
    
    type Page = {
        Width: int
        Height: int
        Blocks: Block list
        Text: string
    }

    type Document = {
        Pages: Page list
    }
    