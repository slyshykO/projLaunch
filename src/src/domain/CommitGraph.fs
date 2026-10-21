module CommitGraph

open System.Text.RegularExpressions

/// A visual line's name, not the set of branches containing a commit.
/// Missing reference means a hint recovered from a conventional merge subject.
type BranchLabel =
    {
        name: string
        reference: string option
    }

let branchReference (reference: string) : BranchLabel option =
    if reference.StartsWith "refs/heads/" then
        Some
            {
                name = reference.Substring("refs/heads/".Length)
                reference = Some reference
            }
    elif reference.StartsWith "refs/remotes/" && not (reference.EndsWith "/HEAD") then
        Some
            {
                name = reference.Substring("refs/remotes/".Length)
                reference = Some reference
            }
    else
        None

let private labelFromRefs refs =
    let branches = refs |> Array.choose branchReference

    branches
    |> Array.tryFind (fun branch ->
        branch.reference
        |> Option.exists (fun reference -> reference.StartsWith "refs/heads/"))
    |> Option.orElseWith (fun () -> Array.tryHead branches)

type Lane = { target: string; color: int }

type Edge =
    {
        fromLane: int
        toLane: int
        color: int
    }

type Row =
    {
        oid: string
        lane: int
        color: int
        incoming: bool
        joining: Edge array
        passing: Edge array
        parents: Edge array
        branch: BranchLabel option
    }

type Layout =
    {
        rows: Row array
        laneCount: int
        continuations: Lane array
    }

let private labelRows (commits: GitProtocol.Commit array) (rows: Row array) =
    // Colors are unique route identities even when a route moves to a different
    // column. Palette reuse must never join unrelated lines or their labels.
    let labels = System.Collections.Generic.Dictionary<int, BranchLabel>()
    let hints = System.Collections.Generic.Dictionary<int, BranchLabel>()

    let addHint color name =
        if not (hints.ContainsKey color) then
            hints[color] <- { name = name; reference = None }

    for index, row in rows |> Array.indexed do
        let commit = commits[index]

        if not (labels.ContainsKey row.color) then
            labelFromRefs commit.refs
            |> Option.iter (fun label -> labels[row.color] <- label)

        if row.parents.Length > 1 then
            let merge =
                Regex.Match(commit.subject, "^Merge (?:remote-tracking )?branch '([^']+)'(?: into (.+))?$")

            if merge.Success then
                if merge.Groups[2].Success then
                    addHint row.color merge.Groups[2].Value

                // An octopus subject cannot identify which parent it names.
                if row.parents.Length = 2 && row.parents[1].color <> row.color then
                    addHint row.parents[1].color merge.Groups[1].Value

    rows
    |> Array.map (fun row ->
        let branch =
            match labels.TryGetValue row.color, hints.TryGetValue row.color with
            | (true, label), _ -> Some label
            | _, (true, hint) -> Some hint
            | _ -> None

        { row with branch = branch })

/// The server supplies children before parents. Only the processed prefix affects
/// a row, so appending a history page preserves all existing routes and colors.
/// Names can become more precise when a later page contains a branch ref.
let layout (commits: GitProtocol.Commit array) : Layout =
    let mutable lanes: Lane array = [||]
    let mutable nextColor = 0
    let mutable laneCount = 0

    let newColor () =
        let color = nextColor
        nextColor <- nextColor + 1
        color

    let rows =
        commits
        |> Array.map (fun commit ->
            let incoming = lanes |> Array.tryFindIndex (fun lane -> lane.target = commit.oid)

            let nodeIndex, node =
                match incoming with
                | Some index -> index, lanes[index]
                | None ->
                    lanes.Length,
                    {
                        target = commit.oid
                        color = newColor ()
                    }

            let after =
                ResizeArray<Lane>(lanes |> Array.filter (fun lane -> lane.target <> commit.oid))

            let parents = commit.parents |> Array.distinct
            let mutable insertAt = min nodeIndex after.Count

            // Keep the first-parent line until the shared ancestor is emitted.
            // Collapsing it earlier would give that ancestor the other branch's
            // identity depending on which side of a merge was traversed first.
            for index, parent in parents |> Array.indexed do
                if index = 0 || not (after |> Seq.exists (fun lane -> lane.target = parent)) then
                    after.Insert(
                        insertAt,
                        {
                            target = parent
                            color = if index = 0 then node.color else newColor ()
                        }
                    )

                    insertAt <- insertAt + 1

            let laneByColor = after |> Seq.mapi (fun index lane -> lane.color, index) |> dict

            let joining =
                lanes
                |> Array.mapi (fun index lane -> index, lane)
                |> Array.choose (fun (index, lane) ->
                    if lane.target = commit.oid && index <> nodeIndex then
                        Some
                            {
                                fromLane = index
                                toLane = nodeIndex
                                color = lane.color
                            }
                    else
                        None)

            let passing =
                lanes
                |> Array.mapi (fun index lane -> index, lane)
                |> Array.choose (fun (index, lane) ->
                    if lane.target = commit.oid then
                        None
                    else
                        Some
                            {
                                fromLane = index
                                toLane = laneByColor[lane.color]
                                color = lane.color
                            })

            let edges =
                parents
                |> Array.mapi (fun parentIndex parent ->
                    let index =
                        if parentIndex = 0 then
                            laneByColor[node.color]
                        else
                            after |> Seq.findIndex (fun lane -> lane.target = parent)

                    {
                        fromLane = nodeIndex
                        toLane = index
                        color = after[index].color
                    })

            laneCount <- max laneCount (max (nodeIndex + 1) after.Count)
            lanes <- after.ToArray()

            {
                oid = commit.oid
                lane = nodeIndex
                color = node.color
                incoming = incoming.IsSome
                joining = joining
                passing = passing
                parents = edges
                branch = None
            })

    {
        rows = labelRows commits rows
        laneCount = laneCount
        continuations = lanes
    }
