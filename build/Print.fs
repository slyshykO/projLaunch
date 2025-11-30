module Print

module private Internal =
    let lockObj = obj ()

    let IsColorEnabled () =
        Gapotchenko.FX.Console.ConsoleTraits.IsColorEnabled

let print (str: string) =
    lock Internal.lockObj (fun () ->
        System.Console.Write str
        System.Console.Out.Flush())

let println (str: string) =
    lock Internal.lockObj (fun () ->
        System.Console.WriteLine str
        System.Console.Out.Flush())

let printColor (color: System.ConsoleColor) (str: string) =
    if Internal.IsColorEnabled() then
        lock Internal.lockObj (fun () ->
            let oldColor = System.Console.ForegroundColor
            System.Console.ForegroundColor <- color
            System.Console.Write str
            System.Console.ForegroundColor <- oldColor
            System.Console.Out.Flush())
    else
        print str

let printlnColor (color: System.ConsoleColor) (str: string) =
    if Internal.IsColorEnabled() then
        lock Internal.lockObj (fun () ->
            let oldColor = System.Console.ForegroundColor
            System.Console.ForegroundColor <- color
            System.Console.WriteLine str
            System.Console.ForegroundColor <- oldColor
            System.Console.Out.Flush())
    else
        println str

let printlnRed (str: string) =
    printlnColor System.ConsoleColor.Red str

let printRed (str: string) = printColor System.ConsoleColor.Red str

let printlnGreen (str: string) =
    printlnColor System.ConsoleColor.Green str

let printGreen (str: string) =
    printColor System.ConsoleColor.Green str

let printlnYellow (str: string) =
    printlnColor System.ConsoleColor.Yellow str

let printlnBlue (str: string) =
    printlnColor System.ConsoleColor.Blue str

let printBlue (str: string) = printColor System.ConsoleColor.Blue str
