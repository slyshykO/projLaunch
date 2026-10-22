module BranchesView

open Feliz
open Fable.Core
open state

[<Import("attachBranchSplitter", "../interop/_Splitter.js")>]
let private attach (element: Browser.Types.HTMLElement) (onResize: float -> unit) : (unit -> unit) = jsNative

let private shortOid (oid: string) = oid.Substring(0, min 8 oid.Length)

[<ReactMemoComponent>]
let Panel (tab: RepositoryTab) (dispatch: Msg -> unit) =
    let filter, setFilter = React.useState ""
    let tree, setTree = React.useState true
    let collapsed, setCollapsed = React.useState Set.empty<string>
    let hidden, setHidden = React.useState false
    let width, setWidth = React.useState 240.0
    let selectedRef, setSelectedRef = React.useState<string option> None
    let handle = React.useRef<Browser.Types.HTMLElement option> None
    let query = filter.Trim().ToLowerInvariant()
    let filtering = query <> ""
    let loading = tab.history.requestId.IsSome

    let branches =
        tab.branches
        |> Option.map (fun data -> data.branches)
        |> Option.defaultValue [||]

    let matched =
        branches
        |> Array.filter (fun branch -> branch.name.ToLowerInvariant().Contains query)

    let remotes =
        tab.branches
        |> Option.bind (fun data -> data.remotes)
        |> Option.defaultValue [||]

    let folderTitle section prefix =
        if section = "Remotes" then
            remotes
            |> Array.tryFind (fun remote -> remote.name = prefix)
            |> Option.map (fun remote -> prefix + "\n" + remote.url)
            |> Option.defaultValue prefix
        else
            prefix

    let selectedOid =
        tab.selection
        |> Option.bind (fun selection -> selection.commit)
        |> Option.map (fun commit -> commit.oid)

    React.useEffect (
        (fun () ->
            let dispose =
                handle.current
                |> Option.map (fun element -> attach element setWidth)
                |> Option.defaultValue ignore

            { new System.IDisposable with
                member _.Dispose() = dispose ()
            }),
        [| box hidden |]
    )

    let toggle key =
        if not filtering then
            setCollapsed (
                if Set.contains key collapsed then
                    Set.remove key collapsed
                else
                    Set.add key collapsed
            )

    let row (branch: GitProtocol.Branch) (label: string) (depth: int) =
        let selected = selectedRef = Some branch.refName && selectedOid = Some branch.oid

        Html.button
            [
                prop.key branch.refName
                prop.type' "button"
                prop.className "branch-row"
                prop.style [ style.paddingLeft (12 + depth * 14) ]
                prop.custom ("data-current", branch.isCurrent)
                prop.ariaPressed selected
                if branch.isCurrent then
                    prop.custom ("aria-current", "true")
                prop.ariaLabel $"Show branch {branch.name} in history"
                prop.title (
                    branch.refName
                    + "\n"
                    + branch.oid
                    + (branch.upstream
                       |> Option.map (fun upstream -> "\nTracks " + upstream)
                       |> Option.defaultValue "")
                    + (branch.tracking
                       |> Option.map (fun tracking -> " · " + tracking)
                       |> Option.defaultValue "")
                )
                prop.onClick (fun _ ->
                    setSelectedRef (Some branch.refName)
                    dispatch (NavigateToBranch(tab.id, branch.refName)))
                prop.children
                    [
                        Html.span [ prop.className "branch-dot"; prop.ariaHidden true ]
                        Html.span [ prop.className "branch-name"; prop.text label ]
                        if branch.isCurrent then
                            Html.span [ prop.className "branch-current-label"; prop.text "current" ]
                        elif branch.tracking.IsSome then
                            Html.span [ prop.className "branch-tracking"; prop.text branch.tracking.Value ]
                    ]
            ]

    let rec rows (section: string) (path: string) (depth: int) (entries: GitProtocol.Branch array) =
        entries
        |> Array.groupBy (fun branch -> branch.name.Substring(path.Length).Split('/')[0])
        |> Array.sortBy (fun (name, _) -> name.ToLowerInvariant(), name)
        |> Array.map (fun (name, children) ->
            let prefix = path + name
            let nested = children |> Array.filter (fun branch -> branch.name <> prefix)

            if nested.Length = 0 then
                row children[0] name depth
            else
                let key = section + "/" + prefix
                let expanded = filtering || not (Set.contains key collapsed)

                Html.div
                    [
                        prop.key key
                        prop.children
                            [
                                Html.button
                                    [
                                        prop.type' "button"
                                        prop.className "branch-folder"
                                        prop.style [ style.paddingLeft (12 + depth * 14) ]
                                        prop.ariaExpanded expanded
                                        prop.title (folderTitle section prefix)
                                        prop.onClick (fun _ -> toggle key)
                                        prop.children
                                            [
                                                Html.span
                                                    [ prop.ariaHidden true; prop.text (if expanded then "▾" else "▸") ]
                                                Html.span [ prop.className "branch-name"; prop.text name ]
                                                if children |> Array.exists (fun branch -> branch.isCurrent) then
                                                    Html.span
                                                        [
                                                            prop.className "branch-dot"
                                                            prop.title "Contains current branch"
                                                        ]
                                                Html.span [ prop.className "branch-count"; prop.text children.Length ]
                                            ]
                                    ]
                                if expanded then
                                    yield! rows section (prefix + "/") (depth + 1) nested
                            ]
                    ])

    let section remote title =
        let all = branches |> Array.filter (fun branch -> branch.isRemote = remote)
        let visible = matched |> Array.filter (fun branch -> branch.isRemote = remote)
        let expanded = filtering || not (Set.contains title collapsed)

        Html.section
            [
                prop.ariaLabel title
                prop.children
                    [
                        Html.button
                            [
                                prop.type' "button"
                                prop.className "branch-section-heading"
                                prop.ariaExpanded expanded
                                prop.onClick (fun _ -> toggle title)
                                prop.children
                                    [
                                        Html.span [ prop.ariaHidden true; prop.text (if expanded then "▾" else "▸") ]
                                        Html.span [ prop.text title ]
                                        Html.span [ prop.className "branch-count"; prop.text all.Length ]
                                    ]
                            ]
                        if expanded then
                            if visible.Length = 0 then
                                Html.p
                                    [
                                        prop.className "branch-empty"
                                        prop.text (
                                            if filtering then
                                                "No matching branches."
                                            else
                                                "No branches yet."
                                        )
                                    ]
                            elif tree then
                                yield! rows title "" 0 visible
                            else
                                for branch in
                                    visible
                                    |> Array.sortBy (fun branch ->
                                        not branch.isCurrent, branch.name.ToLowerInvariant(), branch.name) do
                                    row branch branch.name 0
                    ]
            ]

    Html.aside
        [
            prop.className "repository-branches"
            prop.custom ("data-collapsed", hidden)
            prop.style [ style.custom ("--branches-width", string width + "px") ]
            prop.ariaLabel "Repository branches"
            prop.children
                [
                    if not hidden then
                        Html.div
                            [
                                prop.ref handle
                                prop.role "separator"
                                prop.tabIndex 0
                                prop.className "branch-splitter"
                                prop.ariaLabel "Resize branches panel"
                                prop.custom ("aria-orientation", "vertical")
                                prop.ariaValueMin 180
                                prop.ariaValueMax 400
                                prop.ariaValueNow width
                            ]
                    Html.div
                        [
                            prop.className "branch-panel-heading"
                            prop.children
                                [
                                    if not hidden then
                                        svgIcons.git 16
                                        Html.h2 [ prop.text "Branches" ]
                                    Html.button
                                        [
                                            prop.type' "button"
                                            prop.className "branch-icon-button"
                                            prop.ariaLabel (
                                                if hidden then
                                                    "Show branches panel"
                                                else
                                                    "Hide branches panel"
                                            )
                                            prop.title (if hidden then "Show branches" else "Hide branches")
                                            prop.ariaExpanded (not hidden)
                                            prop.onClick (fun _ -> setHidden (not hidden))
                                            prop.text (if hidden then "›" else "‹")
                                        ]
                                ]
                        ]
                    if not hidden then
                        Html.div
                            [
                                prop.className "branch-filter"
                                prop.children
                                    [
                                        Html.input
                                            [
                                                prop.type' "search"
                                                prop.ariaLabel "Filter branches"
                                                prop.placeholder "Filter branches…"
                                                prop.value filter
                                                prop.onChange setFilter
                                                prop.onKeyDown (fun event ->
                                                    if event.key = "Escape" then
                                                        setFilter "")
                                            ]
                                        Html.button
                                            [
                                                prop.type' "button"
                                                prop.className "branch-icon-button"
                                                prop.ariaLabel (
                                                    if tree then
                                                        "Switch to list view"
                                                    else
                                                        "Switch to tree view"
                                                )
                                                prop.title (
                                                    if tree then
                                                        "Switch to list view"
                                                    else
                                                        "Switch to tree view"
                                                )
                                                prop.onClick (fun _ -> setTree (not tree))
                                                prop.text (if tree then "List" else "Tree")
                                            ]
                                    ]
                            ]

                        Html.div
                            [
                                prop.className "branch-scroll"
                                prop.ariaBusy loading
                                prop.children
                                    [
                                        match tab.branchesError with
                                        | Some error ->
                                            Html.div
                                                [
                                                    prop.role "alert"
                                                    prop.className "branch-error"
                                                    prop.children
                                                        [
                                                            Html.p [ prop.text "Could not load branches." ]
                                                            Html.p [ prop.className "break-words"; prop.text error ]
                                                            if tab.branches.IsSome then
                                                                Html.p
                                                                    [
                                                                        prop.text
                                                                            "Previously loaded branches are shown."
                                                                    ]
                                                            Html.button
                                                                [
                                                                    prop.type' "button"
                                                                    prop.className "btn btn-xs mt-2"
                                                                    prop.disabled loading
                                                                    prop.text "Retry"
                                                                    prop.onClick (fun _ ->
                                                                        dispatch (RefreshRepositoryHistory tab.id))
                                                                ]
                                                        ]
                                                ]
                                        | None -> ()
                                        if tab.branches.IsNone && tab.branchesError.IsNone then
                                            Html.p
                                                [
                                                    prop.role "status"
                                                    prop.className "branch-empty"
                                                    prop.text "Loading branches…"
                                                ]
                                        match tab.branches with
                                        | Some data ->
                                            match data.currentBranch, data.head with
                                            | None, Some oid ->
                                                Html.p
                                                    [
                                                        prop.className "branch-head"
                                                        prop.text $"Detached HEAD · {shortOid oid}"
                                                    ]
                                            | Some branch, None ->
                                                Html.p
                                                    [
                                                        prop.className "branch-head"
                                                        prop.text $"{branch} · No commits yet"
                                                    ]
                                            | _ -> ()

                                            section false "Local"
                                            section true "Remotes"
                                        | None -> ()
                                    ]
                            ]

                        Html.p
                            [
                                prop.className "branch-panel-footer"
                                prop.text (
                                    if loading then
                                        "Refreshing branches…"
                                    else
                                        "Select a branch to reveal its commit"
                                )
                            ]
                ]
        ]
