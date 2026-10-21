module DesktopTabs

open System
open Feliz
open state

[<ReactComponent>]
let TabBar (tabs: RepositoryTab list) (active: string option) (dispatch: Msg -> unit) =
    let items =
        (None, "Projects", "projects")
        :: (tabs |> List.map (fun tab -> Some tab.id, tab.name, tab.root))

    let buttonId id =
        "tab-" + (id |> Option.defaultValue "projects")

    let select id =
        id
        |> Option.map SelectRepositoryTab
        |> Option.defaultValue ShowProjects
        |> dispatch

    let focus id =
        let element = Browser.Dom.document.getElementById (buttonId id)

        if not (isNull element) then
            element.focus ()

    React.useEffect ((fun () -> focus active), [| box active |])

    Html.div
        [
            prop.role "tablist"
            prop.ariaLabel "Workspace tabs"
            prop.className "flex shrink-0 overflow-x-auto border-b border-base-300 bg-base-200 px-2 pt-2 gap-1"
            prop.children
                [
                    for index, (id, name, path) in items |> List.indexed do
                        let selected = active = id

                        Html.div
                            [
                                prop.key (buttonId id)
                                prop.className (
                                    "group flex shrink-0 items-center rounded-t border border-b-0 "
                                    + if selected then
                                          "bg-base-100 border-base-300"
                                      else
                                          "border-transparent hover:bg-base-300/60"
                                )
                                prop.children
                                    [
                                        Html.button
                                            [
                                                prop.id (buttonId id)
                                                prop.type' "button"
                                                prop.role "tab"
                                                prop.ariaSelected selected
                                                prop.ariaControls ("panel-" + (id |> Option.defaultValue "projects"))
                                                prop.tabIndex (if selected then 0 else -1)
                                                prop.title path
                                                prop.className
                                                    "flex items-center gap-2 px-4 py-2.5 text-sm focus-visible:outline-2 focus-visible:outline-primary outline-offset-[-2px]"
                                                prop.onClick (fun _ -> select id)
                                                prop.onKeyDown (fun event ->
                                                    let target =
                                                        match event.key with
                                                        | "ArrowRight" -> Some((index + 1) % items.Length)
                                                        | "ArrowLeft" ->
                                                            Some((index + items.Length - 1) % items.Length)
                                                        | "Home" -> Some 0
                                                        | "End" -> Some(items.Length - 1)
                                                        | _ -> None

                                                    target
                                                    |> Option.iter (fun i ->
                                                        event.preventDefault ()
                                                        let next, _, _ = items[i]
                                                        select next
                                                        focus next))
                                                prop.children
                                                    [
                                                        if id.IsSome then
                                                            svgIcons.git 16
                                                        Html.span [ prop.className "max-w-48 truncate"; prop.text name ]
                                                    ]
                                            ]
                                        match id with
                                        | Some id ->
                                            Html.button
                                                [
                                                    prop.type' "button"
                                                    prop.title $"Close {name}"
                                                    prop.ariaLabel $"Close repository {name}"
                                                    prop.className
                                                        "mr-1 rounded px-2 py-1 text-base-content/50 hover:bg-base-300 hover:text-base-content focus-visible:outline-2 focus-visible:outline-primary"
                                                    prop.text "×"
                                                    prop.onClick (fun _ ->
                                                        dispatch (CloseRepositoryTab id)
                                                        let previous, _, _ = items[max 0 (index - 1)]
                                                        focus (if selected then previous else active))
                                                ]
                                        | None -> Html.none
                                    ]
                            ]
                ]
        ]

let private commitDate (value: string) =
    match DateTime.TryParse value with
    | true, date -> date.ToString("yyyy-MM-dd HH:mm")
    | _ -> value

let private CommitRow
    (tab: RepositoryTab)
    (commit: GitProtocol.Commit)
    laneCount
    (graphRow: CommitGraph.Row)
    activeLine
    onHover
    onFocus
    (dispatch: Msg -> unit)
    =
    let selected =
        tab.selection
        |> Option.exists (fun selection -> selection.commit.oid = commit.oid)

    Html.tr
        [
            prop.key commit.oid
            prop.className (
                "commit-history-row "
                + if selected then "bg-primary/10" else "hover:bg-base-200/60"
            )
            prop.custom ("data-selected", selected)
            prop.custom ("data-graph-member", (activeLine = Some graphRow.color))
            prop.style [ style.custom ("--commit-graph-color", CommitGraphView.color graphRow.color) ]
            prop.onMouseEnter (fun _ -> onHover (Some graphRow.color))
            prop.onMouseLeave (fun _ -> onHover None)
            prop.onFocus (fun _ -> onFocus (Some graphRow.color))
            prop.onBlur (fun _ -> onFocus None)
            prop.onClick (fun _ -> dispatch (SelectRepositoryCommit(tab.id, commit.oid)))
            prop.children
                [
                    CommitGraphView.Cell laneCount graphRow selected activeLine
                    Html.td
                        [
                            prop.className "min-w-64"
                            prop.children
                                [
                                    Html.div
                                        [
                                            prop.className "flex flex-wrap items-center gap-1 mb-1 min-h-5"
                                            prop.children
                                                [
                                                    for reference in commit.refs do
                                                        CommitGraphView.Reference graphRow reference
                                                    if commit.refs.Length = 0 then
                                                        CommitGraphView.BranchHint graphRow
                                                ]
                                        ]
                                    Html.button
                                        [
                                            prop.type' "button"
                                            prop.ariaPressed selected
                                            prop.className
                                                "break-words text-left cursor-pointer focus-visible:outline-2 focus-visible:outline-primary"
                                            prop.text commit.subject
                                        ]
                                ]
                        ]
                    Html.td
                        [
                            prop.className "max-w-48 truncate"
                            prop.title $"{commit.authorName} <{commit.authorEmail}>"
                            prop.text commit.authorName
                        ]
                    Html.td
                        [
                            prop.className "whitespace-nowrap text-base-content/70"
                            prop.title commit.commitDate
                            prop.text (commitDate commit.commitDate)
                        ]
                    Html.td
                        [
                            prop.className "font-mono text-base-content/70"
                            prop.title commit.oid
                            prop.text (commit.oid.Substring(0, min 8 commit.oid.Length))
                        ]
                ]
        ]

[<ReactMemoComponent>]
let private HistoryPanel (tab: RepositoryTab) (dispatch: Msg -> unit) =
    let history = tab.history
    let loading = history.requestId.IsSome
    let hoveredLine, setHoveredLine = React.useState<int option> None
    let focusedLine, setFocusedLine = React.useState<int option> None
    let activeLine = hoveredLine |> Option.orElse focusedLine

    React.useEffect (
        (fun () ->
            setHoveredLine None
            setFocusedLine None),
        [| box history.commits |]
    )

    let graph =
        React.useMemo ((fun () -> CommitGraph.layout history.commits), [| box history.commits |])

    Html.div
        [
            prop.className "h-full flex flex-col min-h-0"
            prop.children
                [
                    Html.div
                        [
                            prop.className
                                "shrink-0 border-b border-base-300 px-6 py-4 flex items-center justify-between gap-4"
                            prop.children
                                [
                                    Html.div
                                        [
                                            prop.className "min-w-0"
                                            prop.children
                                                [
                                                    Html.h1
                                                        [ prop.className "font-semibold text-lg"; prop.text tab.name ]
                                                    Html.p
                                                        [
                                                            prop.className "text-sm text-base-content/60 break-all mt-1"
                                                            prop.text tab.root
                                                        ]
                                                ]
                                        ]
                                    Html.button
                                        [
                                            prop.type' "button"
                                            prop.className "btn btn-sm btn-outline shrink-0"
                                            prop.disabled loading
                                            prop.text "Refresh"
                                            prop.ariaLabel $"Refresh history for {tab.name}"
                                            prop.onClick (fun _ -> dispatch (RefreshRepositoryHistory tab.id))
                                        ]
                                ]
                        ]
                    match history.error with
                    | Some error ->
                        Html.div
                            [
                                prop.role "alert"
                                prop.className "shrink-0 border-b border-error/30 bg-error/10 px-6 py-3 text-sm"
                                prop.children
                                    [
                                        Html.p
                                            [
                                                prop.className "font-medium"
                                                prop.text "Could not load commit history. Refresh to try again."
                                            ]
                                        if history.commits.Length > 0 then
                                            Html.p [ prop.text "Previously loaded commits are shown below." ]
                                        Html.p
                                            [
                                                prop.className
                                                    "break-words whitespace-pre-wrap mt-1 max-h-24 overflow-auto"
                                                prop.text error
                                            ]
                                    ]
                            ]
                    | None -> Html.none
                    Html.div
                        [
                            prop.className "flex-1 min-h-0 overflow-auto"
                            prop.ariaBusy loading
                            prop.children
                                [
                                    if history.commits.Length > 0 then
                                        Html.table
                                            [
                                                prop.className "table table-sm w-full commit-history-table"
                                                prop.children
                                                    [
                                                        Html.caption
                                                            [ prop.className "sr-only"; prop.text "Commit history" ]
                                                        Html.thead
                                                            [
                                                                prop.className "sticky top-0 bg-base-200 z-10"
                                                                prop.children
                                                                    [
                                                                        Html.tr
                                                                            [
                                                                                Html.th [ prop.text "Graph" ]
                                                                                Html.th [ prop.text "Commit" ]
                                                                                Html.th [ prop.text "Author" ]
                                                                                Html.th [ prop.text "Date" ]
                                                                                Html.th [ prop.text "Hash" ]
                                                                            ]
                                                                    ]
                                                            ]
                                                        Html.tbody
                                                            [
                                                                for index, commit in history.commits |> Array.indexed do
                                                                    CommitRow
                                                                        tab
                                                                        commit
                                                                        graph.laneCount
                                                                        graph.rows[index]
                                                                        activeLine
                                                                        setHoveredLine
                                                                        setFocusedLine
                                                                        dispatch
                                                                if graph.continuations.Length > 0 then
                                                                    CommitGraphView.Continuation
                                                                        graph
                                                                        history.nextCursor.IsSome
                                                                        activeLine
                                                            ]
                                                    ]
                                            ]
                                    elif history.error.IsNone then
                                        Html.p
                                            [
                                                prop.className "p-10 text-center text-base-content/60"
                                                prop.text (
                                                    if loading || not history.hasLoaded then
                                                        "Loading commit history…"
                                                    else
                                                        "No commits yet."
                                                )
                                            ]
                                ]
                        ]
                    Html.div
                        [
                            prop.className
                                "shrink-0 border-t border-base-300 px-6 py-3 flex items-center justify-between gap-3 text-sm"
                            prop.children
                                [
                                    Html.span
                                        [
                                            prop.role "status"
                                            prop.text (
                                                if loading then
                                                    $"Loading… {history.commits.Length} commits shown"
                                                elif history.hasLoaded then
                                                    $"{history.commits.Length} commits shown"
                                                else
                                                    "Commit history"
                                            )
                                        ]
                                    if history.nextCursor.IsSome then
                                        Html.button
                                            [
                                                prop.type' "button"
                                                prop.className "btn btn-sm"
                                                prop.disabled loading
                                                prop.text "Load more"
                                                prop.onClick (fun _ -> dispatch (LoadMoreCommits tab.id))
                                            ]
                                ]
                        ]
                ]
        ]

[<ReactMemoComponent>]
let RepositoryPanel (tab: RepositoryTab) (dispatch: Msg -> unit) =
    let diff = tab.selection |> Option.bind (fun selection -> selection.diff)

    Html.div
        [
            prop.className "h-full flex flex-col lg:flex-row min-h-0"
            prop.children
                [
                    Html.div
                        [
                            prop.className "flex-1 min-w-0 min-h-0"
                            prop.children
                                [
                                    Html.div
                                        [
                                            prop.id $"history-panel-{tab.id}"
                                            prop.tabIndex -1
                                            prop.hidden diff.IsSome
                                            prop.className "h-full min-h-0"
                                            prop.children [ HistoryPanel tab dispatch ]
                                        ]
                                    match tab.selection, diff with
                                    | Some selection, Some diff ->
                                        Html.div
                                            [
                                                prop.key $"{selection.commit.oid}-{selection.parent}-{diff.file.path}"
                                                prop.className "h-full min-h-0"
                                                prop.children [ CommitDiffView.Panel tab.id selection diff dispatch ]
                                            ]
                                    | _ -> Html.none
                                ]
                        ]
                    Html.div
                        [
                            prop.className
                                "h-2/5 lg:h-full lg:w-2/5 shrink-0 min-h-0 border-t lg:border-t-0 lg:border-l border-base-300"
                            prop.children [ CommitFilesView.Panel tab dispatch ]
                        ]
                ]
        ]
