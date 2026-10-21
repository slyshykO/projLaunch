module CommitGraph

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
        passing: Edge array
        parents: Edge array
    }

type Layout =
    {
        rows: Row array
        laneCount: int
        continuations: Lane array
    }

/// The server supplies children before parents. Only the processed prefix affects
/// a row, so appending a history page preserves all existing routes and colors.
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

            // Reuse an existing route to a parent instead of drawing duplicate
            // lanes for a shared ancestor. New first parents inherit the color.
            for index, parent in parents |> Array.indexed do
                if not (after |> Seq.exists (fun lane -> lane.target = parent)) then
                    after.Insert(
                        insertAt,
                        {
                            target = parent
                            color = if index = 0 then node.color else newColor ()
                        }
                    )

                    insertAt <- insertAt + 1

            let parentLane target =
                after |> Seq.findIndex (fun lane -> lane.target = target)

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
                                toLane = parentLane lane.target
                                color = lane.color
                            })

            let edges =
                parents
                |> Array.map (fun parent ->
                    let index = parentLane parent

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
                passing = passing
                parents = edges
            })

    {
        rows = rows
        laneCount = laneCount
        continuations = lanes
    }
