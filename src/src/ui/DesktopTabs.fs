module DesktopTabs

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

[<ReactMemoComponent>]
let RepositoryPanel (tab: RepositoryTab) =
    Html.div
        [
            prop.className "h-full flex flex-col"
            prop.children
                [
                    Html.div
                        [
                            prop.className "border-b border-base-300 px-6 py-4"
                            prop.children
                                [
                                    Html.h1 [ prop.className "font-semibold text-lg"; prop.text tab.name ]
                                    Html.p
                                        [
                                            prop.className "text-sm text-base-content/60 break-all mt-1"
                                            prop.text tab.root
                                        ]
                                ]
                        ]
                    Html.div
                        [
                            prop.className "flex-1 flex flex-col items-center justify-center gap-3 p-8 text-center"
                            prop.children
                                [
                                    svgIcons.git 40
                                    Html.h2 [ prop.className "text-lg font-medium"; prop.text "Repository workspace" ]
                                    Html.p
                                        [
                                            prop.className "text-sm text-base-content/60 max-w-md"
                                            prop.text
                                                "Commit history, graph and file diffs will be available here in a future update."
                                        ]
                                ]
                        ]
                ]
        ]
