module DesktopTabs

open System
open Feliz
open state
open Fable.Core

[<Fable.Core.Emit("$0.scrollIntoView({block: 'nearest', inline: 'nearest'})")>]
let private revealRow (element: Browser.Types.Element) : unit = jsNative

type private HistoryScrollController =
    abstract update: Workspace.Scroll * string option * bool -> unit
    abstract dispose: unit -> unit

[<Import("attachHistoryScroll", "../interop/_HistoryScroll.js")>]
let private attachHistoryScroll
    (element: Browser.Types.HTMLElement)
    (onScroll: Workspace.Scroll -> unit)
    (onRestored: string -> Workspace.Scroll -> unit)
    (onCancel: unit -> unit)
    : HistoryScrollController =
    jsNative

[<ReactComponent>]
let TabBar (tabs: RepositoryTab list) (active: string option) (dispatch: Msg -> unit) =
    let items =
        (None, "Projects", "projects")
        :: (tabs
            |> List.map (fun tab ->
                Some tab.id,
                GitProtocol.repositoryLabel tab.endpoint tab.name,
                GitProtocol.endpointLabel tab.endpoint + " · " + tab.root))

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

    let tabItem index (id: string option, name: string, path: string) =
        let selected = active = id

        let tabButton =
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
                            | "ArrowLeft" -> Some((index + items.Length - 1) % items.Length)
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

        let tabContent =
            [
                tabButton
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
                prop.children tabContent
            ]

    let tabItems = items |> List.mapi tabItem

    Html.div
        [
            prop.role "tablist"
            prop.ariaLabel "Workspace tabs"
            prop.className "flex shrink-0 overflow-x-auto border-b border-base-300 bg-base-200 px-2 pt-2 gap-1"
            prop.children tabItems
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
        |> Option.exists (fun selection ->
            if graphRow.uncommitted then
                selection.commit.IsNone
            else
                selection.commit |> Option.exists (fun selected -> selected.oid = commit.oid))

    let subjectButton =
        Html.button
            [
                prop.type' "button"
                prop.ariaPressed selected
                prop.disabled (graphRow.uncommitted && tab.history.requestId.IsSome)
                prop.className
                    "break-words text-left cursor-pointer focus-visible:outline-2 focus-visible:outline-primary"
                prop.text commit.subject
            ]

    Html.tr
        [
            prop.key commit.oid
            prop.id $"commit-{tab.id}-{commit.oid}"
            prop.className (
                "commit-history-row "
                + if selected then "bg-primary/10" else "hover:bg-base-200/60"
            )
            prop.custom ("data-selected", selected)
            if not graphRow.uncommitted then
                prop.custom ("data-history-oid", commit.oid)
            prop.custom ("data-graph-member", (activeLine = Some graphRow.color))
            prop.style [ style.custom ("--commit-graph-color", CommitGraphView.color graphRow.color) ]
            prop.onMouseEnter (fun _ -> onHover (Some graphRow.color))
            prop.onMouseLeave (fun _ -> onHover None)
            prop.onFocus (fun _ -> onFocus (Some graphRow.color))
            prop.onBlur (fun _ -> onFocus None)
            prop.onClick (fun _ ->
                dispatch (
                    if graphRow.uncommitted then
                        SelectWorkingTree tab.id
                    else
                        SelectRepositoryCommit(tab.id, commit.oid)
                ))
            prop.children
                [
                    CommitGraphView.References graphRow commit.refs
                    CommitGraphView.Cell laneCount graphRow selected activeLine
                    Html.td
                        [
                            prop.className "commit-subject-cell min-w-64"
                            prop.children [ subjectButton ]
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
                            prop.text (
                                if graphRow.uncommitted then
                                    ""
                                else
                                    commit.oid.Substring(0, min 8 commit.oid.Length)
                            )
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
    let scrollElement = React.useRef<Browser.Types.HTMLElement option> None
    let scrollController = React.useRef<HistoryScrollController option> None

    React.useLayoutEffect (
        (fun () ->
            scrollController.current <-
                scrollElement.current
                |> Option.map (fun element ->
                    attachHistoryScroll
                        element
                        (fun scroll -> dispatch (HistoryScrolled(tab.id, scroll)))
                        (fun token scroll -> dispatch (HistoryScrollRestored(tab.id, token, scroll)))
                        (fun () -> dispatch (CancelHistoryRestore tab.id)))

            { new IDisposable with
                member _.Dispose() =
                    scrollController.current
                    |> Option.iter (fun controller -> controller.dispose ())

                    scrollController.current <- None
            }),
        [| box tab.id |]
    )

    React.useLayoutEffect (
        (fun () ->
            scrollController.current
            |> Option.iter (fun controller ->
                controller.update (
                    tab.historyView.scroll,
                    tab.scrollRestoreId,
                    history.hasLoaded
                    && not loading
                    && not tab.restoringHistory
                    && history.error.IsNone
                ))),
        [|
            box tab.historyView
            box tab.scrollRestoreId
            box tab.restoringHistory
            box history.hasLoaded
            box loading
            box history.error
        |]
    )

    let selectedOid =
        tab.selection
        |> Option.bind (fun selection -> selection.commit)
        |> Option.map (fun commit -> commit.oid)

    React.useEffect (
        (fun () ->
            (if tab.scrollRestoreId.IsSome then None else selectedOid)
            |> Option.iter (fun oid ->
                let row = Browser.Dom.document.getElementById $"commit-{tab.id}-{oid}"

                if not (isNull row) then
                    revealRow row)),
        [| box selectedOid; box tab.revealRevision |]
    )

    React.useEffect (
        (fun () ->
            setHoveredLine None
            setFocusedLine None),
        [| box history.commits |]
    )

    let status =
        tab.workingTree
        |> Option.bind (function
            | Ok status -> Some status
            | Error _ -> None)

    let displayed, graph =
        React.useMemo (
            (fun () -> CommitGraph.withWorkingTree status history.commits),
            [| box history.commits; box tab.workingTree |]
        )

    let repositoryHeading =
        Html.div
            [
                prop.className "min-w-0"
                prop.children
                    [
                        Html.h1
                            [
                                prop.className "font-semibold text-lg"
                                prop.text (GitProtocol.repositoryLabel tab.endpoint tab.name)
                            ]
                        Html.p
                            [
                                prop.className "text-sm text-base-content/60 break-all mt-1"
                                prop.text tab.root
                            ]
                    ]
            ]

    let header =
        Html.div
            [
                prop.className "shrink-0 border-b border-base-300 px-6 py-4 flex items-center justify-between gap-4"
                prop.children
                    [
                        repositoryHeading
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

    let tableHeader =
        Html.thead
            [
                prop.className "sticky top-0 bg-base-200 z-10"
                prop.children
                    [
                        Html.tr
                            [
                                Html.th [ prop.text "Branch / Tag" ]
                                Html.th [ prop.text "Graph" ]
                                Html.th [ prop.text "Commit" ]
                                Html.th [ prop.text "Author" ]
                                Html.th [ prop.text "Date" ]
                                Html.th [ prop.text "Hash" ]
                            ]
                    ]
            ]

    let historyTable () =
        let tableBody =
            Html.tbody
                [
                    for index, commit in displayed |> Array.indexed do
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
                        CommitGraphView.Continuation graph history.nextCursor.IsSome activeLine
                ]

        Html.table
            [
                prop.className "table table-sm w-full commit-history-table"
                prop.children
                    [
                        Html.caption [ prop.className "sr-only"; prop.text "Commit history" ]
                        tableHeader
                        tableBody
                    ]
            ]

    let footer =
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
                                    if tab.restoringHistory then
                                        $"Restoring history… {history.commits.Length} / {tab.historyView.count} commits"
                                    elif loading then
                                        $"Loading… {history.commits.Length} commits shown"
                                    elif history.hasLoaded then
                                        $"{history.commits.Length} commits shown"
                                    else
                                        "Commit history"
                                )
                            ]
                        if tab.restoringHistory then
                            Html.button
                                [
                                    prop.type' "button"
                                    prop.className "btn btn-sm"
                                    prop.text "Cancel"
                                    prop.ariaLabel "Cancel history restoration"
                                    prop.onClick (fun _ -> dispatch (CancelHistoryRestore tab.id))
                                ]
                        elif history.nextCursor.IsSome then
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

    let historyContent =
        [
            if displayed.Length > 0 then
                historyTable ()
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

    let historyError =
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
                                    prop.className "break-words whitespace-pre-wrap mt-1 max-h-24 overflow-auto"
                                    prop.text error
                                ]
                        ]
                ]
        | None -> Html.none

    let workingTreeStatus =
        match tab.workingTree with
        | Some(Error error) ->
            Html.div
                [
                    prop.role "alert"
                    prop.className "px-6 py-3 bg-error/10 text-sm break-words"
                    prop.text $"Could not load uncommitted changes. Refresh to try again. {error}"
                ]
        | Some(Ok status) when status.entries.Length = 0 ->
            Html.p
                [
                    prop.className "px-6 py-2 text-sm text-base-content/60"
                    prop.text "Working tree clean"
                ]
        | _ -> Html.none

    Html.div
        [
            prop.className "h-full flex flex-col min-h-0"
            prop.children
                [
                    header
                    historyError
                    workingTreeStatus
                    Html.div
                        [
                            prop.className "flex-1 min-h-0 overflow-auto"
                            prop.ref scrollElement
                            prop.custom ("data-history-scroll", tab.id)
                            prop.ariaBusy loading
                            prop.children historyContent
                        ]
                    footer
                ]
        ]

[<ReactMemoComponent>]
let private RepositoryContent (tab: RepositoryTab) (dispatch: Msg -> unit) =
    let diff = tab.selection |> Option.bind (fun selection -> selection.diff)

    let historyContent =
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
                        prop.key
                            $"{selection.commit |> Option.map (fun commit -> commit.oid)}-{diff.target}-{diff.file.path}"
                        prop.className "h-full min-h-0"
                        prop.children [ CommitDiffView.Panel tab.id selection diff dispatch ]
                    ]
            | _ -> Html.none
        ]

    Html.div
        [
            prop.className "repository-split h-full min-h-0"
            prop.style
                [
                    style.custom ("--files-width", string tab.filesWidth + "%")
                    style.custom ("--files-height", string tab.filesHeight + "%")
                ]
            prop.children
                [
                    Html.div [ prop.className "flex-1 min-w-0 min-h-0"; prop.children historyContent ]
                    RepositorySplitter.Handle tab.id tab.filesWidth tab.filesHeight dispatch
                    Html.div
                        [
                            prop.id $"files-panel-{tab.id}"
                            prop.className "repository-files-panel min-w-0 min-h-0"
                            prop.children [ CommitFilesView.Panel tab dispatch ]
                        ]
                ]
        ]

[<ReactMemoComponent>]
let RepositoryPanel (tab: RepositoryTab) (dispatch: Msg -> unit) =
    Html.div
        [
            prop.className "repository-layout h-full min-h-0"
            prop.children
                [
                    BranchesView.Panel tab dispatch
                    Html.div
                        [
                            prop.className "repository-content min-w-0 min-h-0"
                            prop.children [ RepositoryContent tab dispatch ]
                        ]
                ]
        ]
