module CommitGraphView

open Feliz

let private colors =
    [|
        "#3b82f6"
        "#d97706"
        "#10b981"
        "#a855f7"
        "#f43f5e"
        "#06b6d4"
        "#84cc16"
        "#ec4899"
    |]

let color index = colors[index % colors.Length]
let private x lane = 24 + lane * 20
let width laneCount = 48 + max 0 (laneCount - 1) * 20

let private opacity activeLine colorIndex =
    match activeLine with
    | Some active when active <> colorIndex -> 0.2
    | _ -> 1.0

let private branchDescription (branch: CommitGraph.BranchLabel) =
    match branch.reference with
    | Some reference -> $"Branch line: {branch.name} ({reference})"
    | None -> $"Branch line: {branch.name} (inferred from merge message)"

let private Reference (row: CommitGraph.Row) reference =
    let branch = CommitGraph.branchReference reference

    let label =
        match branch with
        | Some branch -> branch.name
        | None when reference.StartsWith "refs/tags/" -> "tag: " + reference.Substring("refs/tags/".Length)
        | None -> reference

    Html.span
        [
            prop.key reference
            prop.className "commit-ref-label badge badge-sm badge-outline"
            if branch.IsSome then
                prop.style [ style.borderColor (color row.color) ]
            prop.title reference
            prop.text label
        ]

let private BranchHint (row: CommitGraph.Row) =
    match row.branch with
    | None -> Html.none
    | Some branch ->
        Html.span
            [
                prop.className "commit-branch-hint commit-ref-label badge badge-sm badge-outline"
                prop.style [ style.borderColor (color row.color) ]
                prop.title (branchDescription branch)
                prop.ariaLabel (branchDescription branch)
                prop.text (
                    if branch.reference.IsSome then
                        branch.name
                    else
                        "~ " + branch.name
                )
            ]

let References (row: CommitGraph.Row) (references: string array) =
    Html.td
        [
            prop.className "commit-refs-cell"
            prop.children
                [
                    if references.Length > 0 || row.branch.IsSome then
                        Html.div
                            [
                                prop.className (
                                    "commit-refs-content"
                                    + if references.Length = 0 then
                                          " commit-branch-context"
                                      else
                                          ""
                                )
                                prop.children
                                    [
                                        Html.div
                                            [
                                                prop.className "commit-ref-labels"
                                                prop.children
                                                    [
                                                        for reference in references do
                                                            Reference row reference
                                                        if references.Length = 0 then
                                                            BranchHint row
                                                    ]
                                            ]
                                        Html.span
                                            [
                                                prop.className "commit-ref-connector"
                                                prop.style [ style.custom ("--commit-graph-node-x", $"{x row.lane}px") ]
                                                prop.ariaHidden true
                                            ]
                                    ]
                            ]
                ]
        ]

let private path dashed activeLine colorIndex (data: string) =
    Svg.path
        [
            svg.d data
            // The presentation-only working-tree node starts route zero.
            if dashed && colorIndex = 0 then
                svg.strokeDasharray [| 4; 4 |]
            svg.stroke (color colorIndex)
            svg.custom ("opacity", opacity activeLine colorIndex)
            svg.strokeWidth 2
            svg.fill "none"
            svg.custom ("vectorEffect", "non-scaling-stroke")
        ]

let private edgePath dashed activeLine (edge: CommitGraph.Edge) passing =
    let start = x edge.fromLane
    let finish = x edge.toLane

    let prefix =
        if passing then
            $"M {start} 0 L {start} 50"
        else
            $"M {start} 50"

    path dashed activeLine edge.color $"{prefix} C {start} 75 {finish} 75 {finish} 100"

let private lines laneCount children =
    Svg.svg
        [
            svg.className "commit-graph-lines"
            svg.width (width laneCount)
            svg.custom ("height", "100%")
            svg.viewBox (0, 0, width laneCount, 100)
            svg.custom ("preserveAspectRatio", "none")
            svg.custom ("aria-hidden", true)
            svg.custom ("focusable", false)
            svg.children children
        ]

let Cell laneCount (row: CommitGraph.Row) selected activeLine =
    let description =
        match row.uncommitted, row.parents.Length with
        | true, _ -> "Uncommitted changes"
        | false, 0 -> "Root commit"
        | false, 1 -> "Commit"
        | false, count -> $"Merge commit ({count} parents)"

    Html.td
        [
            prop.className "commit-graph-cell"
            prop.style [ style.width (width laneCount); style.minWidth (width laneCount) ]
            prop.title (
                row.branch
                |> Option.map (fun branch -> description + " — " + branchDescription branch)
                |> Option.defaultValue description
            )
            prop.children
                [
                    Html.span [ prop.className "sr-only"; prop.text description ]
                    lines
                        laneCount
                        [
                            for edge in row.passing do
                                edgePath row.dashedBelow activeLine edge true
                            if row.incoming then
                                path row.dashedAbove activeLine row.color $"M {x row.lane} 0 L {x row.lane} 50"
                            for edge in row.joining do
                                path
                                    row.dashedAbove
                                    activeLine
                                    edge.color
                                    $"M {x edge.fromLane} 0 C {x edge.fromLane} 25 {x edge.toLane} 25 {x edge.toLane} 50"
                            for edge in row.parents do
                                edgePath row.dashedBelow activeLine edge false
                        ]
                    Html.div
                        [
                            prop.className "commit-graph-node"
                            prop.style [ style.left (x row.lane) ]
                            prop.ariaHidden true
                            prop.children
                                [
                                    Svg.svg
                                        [
                                            svg.width 22
                                            svg.height 22
                                            svg.viewBox (0, 0, 22, 22)
                                            svg.custom (
                                                "opacity",
                                                if selected then 1.0 else opacity activeLine row.color
                                            )
                                            svg.custom ("focusable", false)
                                            svg.children
                                                [
                                                    if selected then
                                                        Svg.circle
                                                            [
                                                                svg.cx 11
                                                                svg.cy 11
                                                                svg.r 9
                                                                svg.fill "none"
                                                                svg.stroke "currentColor"
                                                                svg.strokeWidth 1.5
                                                            ]
                                                    Svg.circle
                                                        [
                                                            svg.cx 11
                                                            svg.cy 11
                                                            svg.r (if row.parents.Length > 1 then 6 else 4)
                                                            svg.fill (
                                                                if row.uncommitted || row.parents.Length > 1 then
                                                                    "var(--color-base-100)"
                                                                else
                                                                    color row.color
                                                            )
                                                            svg.stroke (color row.color)
                                                            if row.uncommitted then
                                                                svg.strokeDasharray [| 2; 2 |]
                                                            svg.strokeWidth 2
                                                        ]
                                                    if row.parents.Length > 1 then
                                                        Svg.circle
                                                            [
                                                                svg.cx 11
                                                                svg.cy 11
                                                                svg.r 2
                                                                svg.fill (color row.color)
                                                            ]
                                                ]
                                        ]
                                ]
                        ]
                ]
        ]

let Continuation (graph: CommitGraph.Layout) hasMore activeLine =
    Html.tr
        [
            prop.className "commit-graph-continuation"
            prop.children
                [
                    Html.td [ prop.className "commit-refs-cell" ]
                    Html.td
                        [
                            prop.className "commit-graph-cell"
                            prop.children
                                [
                                    lines
                                        graph.laneCount
                                        [
                                            for index, lane in graph.continuations |> Array.indexed do
                                                Svg.path
                                                    [
                                                        svg.d $"M {x index} 0 L {x index} 80"
                                                        svg.stroke (color lane.color)
                                                        svg.custom ("opacity", opacity activeLine lane.color)
                                                        svg.strokeWidth 2
                                                        svg.strokeDasharray [| 3; 3 |]
                                                        svg.custom ("vectorEffect", "non-scaling-stroke")
                                                    ]
                                        ]
                                ]
                        ]
                    Html.td
                        [
                            prop.colSpan 4
                            prop.className "text-xs text-base-content/60 py-3"
                            prop.text (
                                if hasMore then
                                    "History continues below — load more commits."
                                else
                                    "Some parents are outside the loaded history."
                            )
                        ]
                ]
        ]
