module Debug

#if DEBUG
[<Literal>]
let Debug = true

let projectsJsonStr =
    """
[
    {
        "id": "0",
        "name": "Feliz",
        "lastOpened": "2021-09-01T00:00:00",
        "description": "Feliz is a happy web framework for F#",
        "path": "./Feliz",
        "ide": "VSCode",
        "environment": {"EXTRA": "1"}
    },
    {
        "id": "1",
        "name": "Fable",
        "lastOpened": "2021-09-01T00:00:00",
        "description": "Fable is a compiler that generates JavaScript from F# code",
        "path": "./Fable",
        "ide": "VSCode",
        "environment": {"EXTRA": "2"}
    },
    {
        "id": "2",
        "name": "Thoth",
        "lastOpened": "2021-09-01T00:00:00",
        "description": "Thoth is a JSON decoder and encoder for F#",
        "path": "./Thoth",
        "ide": "VSCode",
        "environment": {"EXTRA": "3"}
    },
    {
        "id": "3",
        "name": "Zafir",
        "lastOpened": "2021-09-01T00:00:00",
        "description": "Zafir is a web framework for F#",
        "path": "./Zafir",
        "ide": "VSCode",
        "environment": {"EXTRA": "4"}
    },
    {
        "id": "4",
        "name": "Daisy",
        "lastOpened": "2021-09-01T00:00:00",
        "description": "Daisy is a UI library for F#",
        "path": "./Daisy",
        "ide": "VSCode",
        "environment": {"EXTRA": "5"}
    },
    {
        "id": "5",
        "name": "Femto",
        "lastOpened": "2021-09-01T00:00:00",
        "description": "Femto is a web framework for F#",
        "path": "./Femto",
        "ide": "VSCode",
        "environment": {"EXTRA": "6"}
    },
    {
        "id": "6",
        "name": "Femto",
        "lastOpened": "2021-09-01T00:00:00",
        "description": "Femto is a web framework for F#",
        "path": "./Femto",
        "ide": "VSCode",
        "environment": {"EXTRA": "7"}
    }
]
"""
#else
[<Literal>]
let Debug = false

let projectsJsonStr = ""
#endif
