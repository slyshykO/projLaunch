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
    | Some '?' -> "Untracked"
    | Some 'U' -> "Conflict"
    | _ -> status

let private FileRow (tab: RepositoryTab) (selection: CommitFiles) target (file: GitProtocol.DiffFile) dispatch =
    let selected =
        selection.diff
        |> Option.exists (fun diff -> diff.file.path = file.path && diff.target = target)

    let fileContent =
        [
            Html.span
                [
                    prop.className "badge badge-sm badge-outline mb-1 mr-2"
                    prop.title $"{statusLabel file.status} ({file.status})"
                    prop.ariaLabel (statusLabel file.status)
                    prop.text (
                        match file.status |> Seq.tryHead with
                        | Some 'A' -> "+"
                        | Some 'D' -> "-"
                        | Some 'M' -> "M"
                        | Some 'R' -> "R"
                        | Some 'C' -> "C"
                        | Some 'T' -> "T"
                        | Some '?' -> "?"
                        | Some 'U' -> "U"
                        | _ -> statusLabel file.status
                    )
                ]
            match file.originalPath with
            | Some original ->
                Html.div
                    [
                        prop.className "font-mono text-xs whitespace-pre-wrap break-all text-base-content/60"
                        prop.text original
                    ]

                Html.span [ prop.ariaLabel "to"; prop.text "→ " ]
            | None -> Html.none
            Html.span
                [
                    prop.className "font-mono text-xs whitespace-pre-wrap break-all"
                    prop.text file.path
                ]
        ]

    let fileButton =
        Html.button
            [
                prop.type' "button"
                prop.className (
                    "flex-1 min-w-0 text-left p-2 text-sm focus-visible:outline-2 focus-visible:outline-primary "
                    + if selected then "bg-primary/10" else "hover:bg-base-200/60"
                )
                prop.ariaPressed selected
                prop.disabled (
                    selection.commit.IsNone
                    && (tab.indexRequestId.IsSome || selection.requestId.IsSome)
                )
                prop.ariaControls $"diff-panel-{tab.id}"
                prop.onClick (fun _ ->
                    if selection.commit.IsNone then
                        dispatch (SelectWorkingTreeFile(tab.id, target, file.path))
                    else
                        dispatch (SelectCommitFile(tab.id, file.path)))
                prop.children fileContent
            ]

    Html.li
        [
            prop.key file.path
            prop.className "flex items-center gap-2 border-b border-base-300"
            prop.children
                [
                    if selection.commit.IsNone then
                        Html.input
                            [
                                prop.type' "checkbox"
                                prop.className "checkbox checkbox-xs shrink-0"
                                prop.ariaLabel $"Select {file.path} in {Workspace.targetName target}"
                                prop.isChecked (Set.contains (target, file.path) tab.checkedFiles)
                                prop.disabled (not (canChangeIndex tab))
                                prop.onChange (fun (value: bool) ->
                                    dispatch (CheckWorkingTreeFile(tab.id, target, file.path, value)))
                            ]
                    fileButton
                    if selection.commit.IsNone then
                        let action = if target = GitProtocol.Staged then "Unstage" else "Stage"

                        Html.button
                            [
                                prop.type' "button"
                                prop.className "btn btn-xs btn-ghost shrink-0"
                                prop.ariaLabel $"{action} {file.path}"
                                prop.title $"{action} whole file: {file.path}"
                                prop.disabled (not (canChangeIndex tab))
                                prop.text action
                                prop.onClick (fun _ -> dispatch (ChangeIndex(tab.id, target, [| file.path |])))
                            ]
                ]
        ]

let ParentLinks tabId (commit: GitProtocol.Commit) dispatch =
    Html.span
        [
            prop.children
                [
                    Html.text "parent: "
                    if commit.parents.Length = 0 then
                        Html.text "—"
                    for index, oid in commit.parents |> Array.indexed do
                        if index > 0 then
                            Html.text ","

                        Html.button
                            [
                                prop.type' "button"
                                prop.className
                                    "font-mono link link-hover text-primary focus-visible:outline-2 focus-visible:outline-primary"
                                prop.title $"Open parent {oid}"
                                prop.ariaLabel $"Open parent {oid}"
                                prop.text (oid.Substring(0, min 6 oid.Length))
                                prop.onClick (fun _ -> dispatch (NavigateToParent(tabId, oid)))
                            ]
                ]
        ]

let targetLabel =
    function
    | GitProtocol.Staged -> "Staged · HEAD → index"
    | GitProtocol.Unstaged -> "Unstaged · index → working tree"
    | GitProtocol.Untracked -> "Untracked · new files"
    | GitProtocol.Commit _ -> "Commit"

[<ReactMemoComponent>]
let Panel (tab: RepositoryTab) (dispatch: Msg -> unit) =
    let headerContent =
        [
            Html.h2 [ prop.className "font-semibold"; prop.text "Changed files" ]
            match tab.selection with
            | None -> Html.none
            | Some selection when selection.commit.IsSome ->
                let commit = selection.commit.Value

                let parentOptions =
                    [
                        for index, oid in commit.parents |> Array.indexed do
                            Html.option
                                [
                                    prop.key (string index)
                                    prop.value (string (index + 1))
                                    prop.title oid
                                    prop.text (oid.Substring(0, min 6 oid.Length))
                                ]
                    ]

                let parentSelect =
                    Html.select
                        [
                            prop.ariaLabel "Diff parent"
                            prop.className "select select-xs font-mono"
                            prop.value (selection.parent |> Option.map string |> Option.defaultValue "1")
                            prop.onChange (fun (value: string) ->
                                match System.Int32.TryParse value with
                                | true, parent -> dispatch (SelectCommitParent(tab.id, parent))
                                | _ -> ())
                            prop.children parentOptions
                        ]

                Html.p [ prop.className "mt-2 text-sm break-words"; prop.text commit.subject ]

                Html.p
                    [
                        prop.className "mt-1 font-mono text-xs break-all text-base-content/60"
                        prop.text commit.oid
                    ]

                Html.p
                    [
                        prop.className "mt-2 text-xs text-base-content/60"
                        prop.children [ ParentLinks tab.id commit dispatch ]
                    ]

                if commit.parents.Length > 1 then
                    Html.label
                        [
                            prop.className "flex items-center gap-2 mt-2 text-xs text-base-content/60"
                            prop.children [ Html.span [ prop.text "diff:" ]; parentSelect ]
                        ]
            | Some _ ->
                Html.p [ prop.className "mt-2 text-sm"; prop.text "Uncommitted changes" ]

                Html.button
                    [
                        prop.type' "button"
                        prop.className "btn btn-xs btn-outline mt-2"
                        prop.disabled (tab.history.requestId.IsSome || tab.indexRequestId.IsSome)
                        prop.text "Refresh changes"
                        prop.onClick (fun _ -> dispatch (RefreshRepositoryHistory tab.id))
                    ]

                Html.p
                    [
                        prop.className "mt-2 text-xs text-base-content/60"
                        prop.text "Stage / unstage whole files. Unstage keeps working files intact."
                    ]
            if tab.indexRequestId.IsSome then
                Html.p
                    [
                        prop.role "status"
                        prop.className "mt-2 text-sm"
                        prop.text "Updating index…"
                    ]
            match tab.indexError with
            | Some error ->
                Html.p
                    [
                        prop.role "alert"
                        prop.className "mt-2 text-sm text-error whitespace-pre-wrap break-words"
                        prop.text error
                    ]
            | None -> Html.none
        ]

    let fileError (error: string) =
        Html.div
            [
                prop.role "alert"
                prop.className "text-sm"
                prop.children
                    [
                        Html.p [ prop.text "Could not load changed files." ]
                        Html.p
                            [
                                prop.className "mt-2 whitespace-pre-wrap break-words text-error"
                                prop.text error
                            ]
                        Html.button
                            [
                                prop.type' "button"
                                prop.className "btn btn-sm mt-3"
                                prop.text "Retry"
                                prop.onClick (fun _ -> dispatch (RefreshCommitFiles tab.id))
                            ]
                    ]
            ]

    let fileListContent =
        [
            match tab.selection with
            | None ->
                Html.p
                    [
                        prop.className "text-sm text-base-content/60"
                        prop.text "Select a commit or uncommitted changes to view changed files."
                    ]
            | Some selection ->
                let isLoading =
                    selection.requestId.IsSome
                    || (not selection.hasLoaded && selection.error.IsNone)

                if isLoading then
                    Html.p [ prop.role "status"; prop.text "Loading changed files…" ]

                if not isLoading || selection.hasLoaded then
                    match selection.error with
                    | Some error -> fileError error
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

                        for target, files in selection.groups do
                            if files.Length > 0 then
                                if selection.commit.IsNone then
                                    Html.h3
                                        [
                                            prop.className "font-semibold text-sm mt-4 mb-2"
                                            prop.text (targetLabel target)
                                        ]

                                    let action = if target = GitProtocol.Staged then "Unstage" else "Stage"

                                    let selected =
                                        files
                                        |> Array.filter (fun file -> Set.contains (target, file.path) tab.checkedFiles)
                                        |> Array.map (fun file -> file.path)

                                    Html.div
                                        [
                                            prop.className "flex flex-wrap gap-2 mb-2"
                                            prop.children
                                                [
                                                    Html.button
                                                        [
                                                            prop.type' "button"
                                                            prop.className "btn btn-xs btn-outline"
                                                            prop.disabled (
                                                                not (canChangeIndex tab) || selected.Length = 0
                                                            )
                                                            prop.ariaLabel
                                                                $"{action} selected in {Workspace.targetName target}"
                                                            prop.text $"{action} selected ({selected.Length})"
                                                            prop.onClick (fun _ ->
                                                                dispatch (ChangeIndex(tab.id, target, selected)))
                                                        ]
                                                    Html.button
                                                        [
                                                            prop.type' "button"
                                                            prop.className "btn btn-xs btn-ghost"
                                                            prop.disabled (not (canChangeIndex tab))
                                                            prop.ariaLabel
                                                                $"{action} all in {Workspace.targetName target}"
                                                            prop.text $"{action} all ({files.Length})"
                                                            prop.onClick (fun _ ->
                                                                dispatch (
                                                                    ChangeIndex(
                                                                        tab.id,
                                                                        target,
                                                                        files |> Array.map (fun file -> file.path)
                                                                    )
                                                                ))
                                                        ]
                                                ]
                                        ]

                                    if
                                        target <> GitProtocol.Staged
                                        && (files |> Array.exists (fun file -> file.status.StartsWith "U"))
                                    then
                                        Html.p
                                            [
                                                prop.className "text-xs text-warning mb-2"
                                                prop.text "Staging conflict files marks them resolved."
                                            ]

                                let fileRows =
                                    [
                                        for file in files do
                                            FileRow tab selection target file dispatch
                                    ]

                                Html.ul [ prop.className "space-y-2"; prop.children fileRows ]
        ]

    let navigationStatus =
        match tab.commitNavigation with
        | Some oid ->
            Html.div
                [
                    prop.className "shrink-0 px-4 py-2 text-sm border-b border-base-300"
                    prop.children
                        [
                            Html.span
                                [
                                    prop.role "status"
                                    prop.text $"Finding {tab.navigationLabel} · {oid.Substring(0, min 8 oid.Length)}…"
                                ]
                            Html.button
                                [
                                    prop.type' "button"
                                    prop.className "btn btn-xs ml-2"
                                    prop.text "Cancel"
                                    prop.onClick (fun _ -> dispatch (CancelCommitNavigation tab.id))
                                ]
                        ]
                ]
        | None -> Html.none

    Html.section
        [
            prop.ariaLabel "Changed files"
            prop.className "h-full flex flex-col min-h-0"
            prop.children
                [
                    Html.div
                        [
                            prop.className "shrink-0 border-b border-base-300 px-4 py-4"
                            prop.children headerContent
                        ]
                    navigationStatus
                    match tab.commitNavigationError with
                    | Some error ->
                        Html.p
                            [
                                prop.role "alert"
                                prop.className "shrink-0 p-4 text-sm text-error break-words"
                                prop.text error
                            ]
                    | None -> Html.none
                    Html.div
                        [
                            prop.className "flex-1 min-h-0 overflow-auto p-4"
                            prop.ariaBusy (tab.selection |> Option.exists (fun selection -> selection.requestId.IsSome))
                            prop.children fileListContent
                        ]
                ]
        ]
