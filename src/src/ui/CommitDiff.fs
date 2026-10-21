module CommitDiffView

open Feliz
open state

let private lineClass (line: string) =
    if line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("diff ") then
        "text-base-content/60"
    elif line.StartsWith("@@") then
        "bg-info/15"
    elif line.StartsWith("+") then
        "bg-success/15"
    elif line.StartsWith("-") then
        "bg-error/15"
    else
        ""

let private PatchText (text: string) =
    let lines = text.Split('\n')

    Html.pre
        [
            prop.className "m-0 min-w-max font-mono text-xs leading-5 p-4"
            prop.children
                [
                    for index, line in lines |> Array.indexed do
                        Html.span
                            [
                                prop.key index
                                prop.className (lineClass line)
                                prop.text (line + if index < lines.Length - 1 then "\n" else "")
                            ]
                ]
        ]

[<ReactMemoComponent>]
let Panel (tabId: string) (selection: CommitFiles) (diff: CommitDiff) dispatch =
    Html.section
        [
            prop.id $"diff-panel-{tabId}"
            prop.ariaLabel "File diff"
            prop.className "h-full min-h-0 flex flex-col"
            prop.children
                [
                    Html.div
                        [
                            prop.className
                                "shrink-0 border-b border-base-300 px-4 py-3 flex items-start justify-between gap-3"
                            prop.children
                                [
                                    Html.div
                                        [
                                            prop.className "min-w-0"
                                            prop.children
                                                [
                                                    Html.h2
                                                        [
                                                            prop.className "font-semibold break-all"
                                                            prop.text diff.file.path
                                                        ]
                                                    match diff.file.originalPath with
                                                    | Some original ->
                                                        Html.p
                                                            [
                                                                prop.className
                                                                    "text-xs text-base-content/60 break-all mt-1"
                                                                prop.text $"Renamed/copied from {original}"
                                                            ]
                                                    | None -> Html.none
                                                    Html.p
                                                        [
                                                            prop.className "text-xs text-base-content/60 mt-1 break-all"
                                                            prop.text (
                                                                match selection.commit with
                                                                | None -> CommitFilesView.targetLabel diff.target
                                                                | Some commit ->
                                                                    CommitFilesView.parentLabel commit
                                                                    + (if commit.parents.Length > 1 then
                                                                           let index =
                                                                               (selection.parent
                                                                                |> Option.defaultValue 1)
                                                                               - 1

                                                                           let oid = commit.parents[index]
                                                                           $" · diff: {oid.Substring(0, min 6 oid.Length)}"
                                                                       else
                                                                           "")
                                                            )
                                                        ]
                                                ]
                                        ]
                                    Html.button
                                        [
                                            prop.type' "button"
                                            prop.className "btn btn-sm shrink-0"
                                            prop.autoFocus true
                                            prop.ariaLabel "Close diff and return to commit history"
                                            prop.title "Close diff"
                                            prop.text "✕"
                                            prop.onClick (fun _ ->
                                                dispatch (CloseCommitDiff tabId)

                                                let panel =
                                                    Browser.Dom.document.getElementById $"history-panel-{tabId}"

                                                if not (isNull panel) then
                                                    // Focus after React has revealed the history panel.
                                                    Browser.Dom.window.requestAnimationFrame (fun _ -> panel.focus ())
                                                    |> ignore)
                                        ]
                                ]
                        ]
                    Html.div
                        [
                            prop.className "flex-1 min-h-0 overflow-auto"
                            prop.ariaBusy diff.requestId.IsSome
                            prop.children
                                [
                                    if diff.requestId.IsSome then
                                        Html.p [ prop.role "status"; prop.className "p-4"; prop.text "Loading diff…" ]
                                    else
                                        match diff.error, diff.patch with
                                        | Some error, _ ->
                                            Html.div
                                                [
                                                    prop.role "alert"
                                                    prop.className "p-4 text-sm"
                                                    prop.children
                                                        [
                                                            Html.p [ prop.text "Could not load diff." ]
                                                            Html.p
                                                                [
                                                                    prop.className
                                                                        "text-error whitespace-pre-wrap break-words mt-2"
                                                                    prop.text error
                                                                ]
                                                            Html.button
                                                                [
                                                                    prop.type' "button"
                                                                    prop.className "btn btn-sm mt-3"
                                                                    prop.text "Retry"
                                                                    prop.onClick (fun _ ->
                                                                        dispatch (RetryCommitDiff tabId))
                                                                ]
                                                        ]
                                                ]
                                        | None, Some patch ->
                                            if patch.truncated then
                                                Html.p
                                                    [
                                                        prop.role "status"
                                                        prop.className "px-4 py-2 bg-warning/15 text-sm"
                                                        prop.text
                                                            "Diff is truncated by the server; only part of the changes is shown."
                                                    ]

                                            if patch.binary then
                                                Html.p
                                                    [
                                                        prop.role "status"
                                                        prop.className "p-4 text-sm"
                                                        prop.text "Binary file — text diff is not available."
                                                    ]
                                            else
                                                match patch.text with
                                                | Some text when text.Length > 0 ->
                                                    if patch.untracked then
                                                        Html.p
                                                            [
                                                                prop.className "px-4 py-2 bg-info/10 text-sm"
                                                                prop.text "New file preview (not tracked by Git)."
                                                            ]

                                                    PatchText text
                                                | _ ->
                                                    Html.p
                                                        [
                                                            prop.role "status"
                                                            prop.className "p-4 text-sm"
                                                            prop.text "No textual changes."
                                                        ]
                                        | _ -> Html.none
                                ]
                        ]
                ]
        ]
