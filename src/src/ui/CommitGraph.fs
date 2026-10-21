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

let private color index = colors[index % colors.Length]
let private x lane = 24 + lane * 20
let width laneCount = 48 + max 0 (laneCount - 1) * 20

let private path colorIndex (data: string) =
    Svg.path
        [
            svg.d data
            svg.stroke (color colorIndex)
            svg.strokeWidth 2
            svg.fill "none"
            svg.custom ("vectorEffect", "non-scaling-stroke")
        ]

let private edgePath (edge: CommitGraph.Edge) passing =
    let start = x edge.fromLane
    let finish = x edge.toLane

    let prefix =
        if passing then
            $"M {start} 0 L {start} 50"
        else
            $"M {start} 50"

    path edge.color $"{prefix} C {start} 75 {finish} 75 {finish} 100"

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

let Cell laneCount (row: CommitGraph.Row) selected =
    let description =
        match row.parents.Length with
        | 0 -> "Root commit"
        | 1 -> "Commit"
        | count -> $"Merge commit ({count} parents)"

    Html.td
        [
            prop.className "commit-graph-cell"
            prop.style [ style.width (width laneCount); style.minWidth (width laneCount) ]
            prop.title description
            prop.children
                [
                    Html.span [ prop.className "sr-only"; prop.text description ]
                    lines
                        laneCount
                        [
                            for edge in row.passing do
                                edgePath edge true
                            if row.incoming then
                                path row.color $"M {x row.lane} 0 L {x row.lane} 50"
                            for edge in row.parents do
                                edgePath edge false
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
                                                                if row.parents.Length > 1 then
                                                                    "var(--color-base-100)"
                                                                else
                                                                    color row.color
                                                            )
                                                            svg.stroke (color row.color)
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

let Continuation (graph: CommitGraph.Layout) hasMore =
    Html.tr
        [
            prop.className "commit-graph-continuation"
            prop.children
                [
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
