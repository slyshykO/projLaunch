module Icons

open Fable.Core
open Fable.Core.JsInterop
open Feliz

// Explicit imports let the bundler include only the icons used by the UI.
[<Import("Pencil", "lucide-react")>]
let private pencil: obj = jsNative

[<Import("Trash2", "lucide-react")>]
let private trash2: obj = jsNative

[<Import("X", "lucide-react")>]
let private x: obj = jsNative

[<Import("ChevronDown", "lucide-react")>]
let private down: obj = jsNative

[<Import("ChevronLeft", "lucide-react")>]
let private left: obj = jsNative

[<Import("ChevronRight", "lucide-react")>]
let private right: obj = jsNative

[<Import("RefreshCw", "lucide-react")>]
let private refreshCw: obj = jsNative

[<Import("ListPlus", "lucide-react")>]
let private listPlus: obj = jsNative

[<Import("createElement", "react")>]
let private createElement (icon: obj, props: obj) : ReactElement = jsNative

let private render icon (size: int) =
    createElement (
        icon,
        createObj
            [
                "size" ==> size
                "strokeWidth" ==> 1.75
                "color" ==> "currentColor"
                "className" ==> "shrink-0"
                // Accessible names belong to the button/link containing the icon.
                "aria-hidden" ==> true
                "focusable" ==> false
            ]
    )

let edit size = render pencil size
let trash size = render trash2 size
let close size = render x size
let chevronDown size = render down size
let chevronLeft size = render left size
let chevronRight size = render right size
let refresh size = render refreshCw size
let loadMore size = render listPlus size
