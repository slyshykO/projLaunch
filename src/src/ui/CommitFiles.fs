module CommitFilesView

open Feliz
open state

let private statusLabel (status: string) =
    match status |> Seq.tryHead with
    | Some 'A' -> "Added"
    | Some 'D' -> "Deleted"
    | Some 'M' -> "Modified"
    | Some 'R' -> "Renamed"
    | Some 'C' -> "Copied"
    | Some 'T' -> "Type changed"
    | _ -> status

[<ReactMemoComponent>]
let Panel (tab: RepositoryTab) (dispatch: Msg -> unit) =
    Html.section
        [
            prop.ariaLabel "Changed files"
            prop.className "h-full flex flex-col min-h-0"
            prop.children
                [
                    Html.div
                        [
                            prop.className "shrink-0 border-b border-base-300 px-4 py-4"
                            prop.children
                                [
                                    Html.h2 [ prop.className "font-semibold"; prop.text "Changed files" ]
                                    match tab.selection with
                                    | None -> Html.none
                                    | Some selection ->
                                        Html.p
                                            [
                                                prop.className "mt-2 text-sm break-words"
                                                prop.text selection.commit.subject
                                            ]

                                        Html.p
                                            [
                                                prop.className "mt-1 font-mono text-xs break-all text-base-content/60"
                                                prop.text selection.commit.oid
                                            ]

                                        if selection.commit.parents.Length > 1 then
                                            Html.label
                                                [
                                                    prop.className "block mt-3 text-sm"
                                                    prop.children
                                                        [
                                                            Html.span [ prop.text "Compare with parent" ]
                                                            Html.select
                                                                [
                                                                    prop.className "select select-sm w-full mt-1"
                                                                    prop.value (
                                                                        selection.parent
                                                                        |> Option.map string
                                                                        |> Option.defaultValue ""
                                                                    )
                                                                    prop.onChange (fun (value: string) ->
                                                                        match System.Int32.TryParse value with
                                                                        | true, parent ->
                                                                            dispatch (
                                                                                SelectCommitParent(tab.id, parent)
                                                                            )
                                                                        | _ -> ())
                                                                    prop.children
                                                                        [
                                                                            Html.option
                                                                                [
                                                                                    prop.value ""
                                                                                    prop.disabled true
                                                                                    prop.text "Choose a parent…"
                                                                                ]
                                                                            for index, oid in
                                                                                selection.commit.parents
                                                                                |> Array.indexed do
                                                                                Html.option
                                                                                    [
                                                                                        prop.key oid
                                                                                        prop.value (string (index + 1))
                                                                                        prop.text
                                                                                            $"Parent {index + 1} — {oid}"
                                                                                    ]
                                                                        ]
                                                                ]
                                                        ]
                                                ]
                                        else
                                            Html.p
                                                [
                                                    prop.className "mt-2 text-xs text-base-content/60"
                                                    prop.text (
                                                        if selection.commit.parents.Length = 0 then
                                                            "Initial commit — compared with an empty tree"
                                                        else
                                                            "Compared with parent 1"
                                                    )
                                                ]
                                ]
                        ]
                    Html.div
                        [
                            prop.className "flex-1 min-h-0 overflow-auto p-4"
                            prop.ariaBusy (tab.selection |> Option.exists (fun selection -> selection.requestId.IsSome))
                            prop.children
                                [
                                    match tab.selection with
                                    | None ->
                                        Html.p
                                            [
                                                prop.className "text-sm text-base-content/60"
                                                prop.text "Select a commit to view changed files."
                                            ]
                                    | Some selection ->
                                        if selection.commit.parents.Length > 1 && selection.parent.IsNone then
                                            Html.p
                                                [
                                                    prop.text
                                                        "Choose a parent to view the changes in this merge commit."
                                                ]
                                        elif
                                            selection.requestId.IsSome
                                            || (not selection.hasLoaded && selection.error.IsNone)
                                        then
                                            Html.p [ prop.role "status"; prop.text "Loading changed files…" ]
                                        else
                                            match selection.error with
                                            | Some error ->
                                                Html.div
                                                    [
                                                        prop.role "alert"
                                                        prop.className "text-sm"
                                                        prop.children
                                                            [
                                                                Html.p [ prop.text "Could not load changed files." ]
                                                                Html.p
                                                                    [
                                                                        prop.className
                                                                            "mt-2 whitespace-pre-wrap break-words text-error"
                                                                        prop.text error
                                                                    ]
                                                                Html.button
                                                                    [
                                                                        prop.type' "button"
                                                                        prop.className "btn btn-sm mt-3"
                                                                        prop.text "Retry"
                                                                        prop.onClick (fun _ ->
                                                                            dispatch (RefreshCommitFiles tab.id))
                                                                    ]
                                                            ]
                                                    ]
                                            | None ->
                                                Html.p
                                                    [
                                                        prop.role "status"
                                                        prop.className "text-sm text-base-content/60 mb-3"
                                                        prop.text (
                                                            if selection.files.Length = 0 then "No changed files."
                                                            elif selection.files.Length = 1 then "1 changed file"
                                                            else $"{selection.files.Length} changed files"
                                                        )
                                                    ]

                                                Html.ul
                                                    [
                                                        prop.className "space-y-2"
                                                        prop.children
                                                            [
                                                                for file in selection.files do
                                                                    Html.li
                                                                        [
                                                                            prop.key file.path
                                                                            prop.className
                                                                                "border-b border-base-300 pb-2 text-sm"
                                                                            prop.children
                                                                                [
                                                                                    Html.span
                                                                                        [
                                                                                            prop.className
                                                                                                "badge badge-sm badge-outline mb-1 mr-2"
                                                                                            prop.title file.status
                                                                                            prop.text (
                                                                                                statusLabel file.status
                                                                                            )
                                                                                        ]
                                                                                    match file.originalPath with
                                                                                    | Some original ->
                                                                                        Html.div
                                                                                            [
                                                                                                prop.className
                                                                                                    "font-mono text-xs whitespace-pre-wrap break-all text-base-content/60"
                                                                                                prop.text original
                                                                                            ]

                                                                                        Html.span
                                                                                            [
                                                                                                prop.ariaLabel "to"
                                                                                                prop.text "→ "
                                                                                            ]
                                                                                    | None -> Html.none
                                                                                    Html.span
                                                                                        [
                                                                                            prop.className
                                                                                                "font-mono text-xs whitespace-pre-wrap break-all"
                                                                                            prop.text file.path
                                                                                        ]
                                                                                ]
                                                                        ]
                                                            ]
                                                    ]
                                ]
                        ]
                ]
        ]
