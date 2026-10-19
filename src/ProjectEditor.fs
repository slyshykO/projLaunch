module ProjectEditor

open System
open Feliz
open projectData
open state

[<ReactComponent>]
let Dialog (initial: ProjectData) (isNew: bool) (isSaving: bool) (saveError: string option) (dispatch: Msg -> unit) =
    let draft, setDraft = React.useState initial
    let environment, setEnvironment = React.useState (initial.environment |> Map.toList)
    let validationError, setValidationError = React.useState<string option> None
    let modalId = "project-editor"
    let titleId = "project-editor-title"

    React.useEffectOnce (fun () ->
        let modal =
            Browser.Dom.document.getElementById modalId :?> Browser.HTMLDialogElement

        if not modal.``open`` then
            modal.showModal ()

        { new IDisposable with
            member _.Dispose() =
                if modal.``open`` then
                    modal.close ()
        })

    let fieldLabel (id: string) (text: string) =
        Html.label [ prop.htmlFor id; prop.className "label text-sm"; prop.text text ]

    let input (id: string) (label: string) (value: string) required (onChange: string -> unit) =
        Html.div
            [
                prop.children
                    [
                        fieldLabel id label
                        Html.input
                            [
                                prop.id id
                                prop.className "input input-sm w-full"
                                prop.value value
                                prop.required required
                                prop.onChange (onChange: string -> unit)
                            ]
                    ]
            ]

    let remoteType =
        match draft.remote with
        | Some(Ssh _) -> "ssh"
        | Some(Wsl _) -> "wsl"
        | None -> ""

    let submit () =
        let invalidEnvironment =
            environment
            |> List.exists (fun (key, value) ->
                String.IsNullOrWhiteSpace key
                || key.Contains "="
                || key.Contains '\000'
                || value.Contains '\000')

        let duplicateKeys =
            (environment |> List.map fst |> List.distinct |> List.length)
            <> environment.Length

        let invalidRemote =
            match draft.remote with
            | Some(Ssh(host, _)) -> String.IsNullOrWhiteSpace host
            | Some(Wsl distro) -> String.IsNullOrWhiteSpace distro
            | None -> false

        if String.IsNullOrWhiteSpace draft.name || String.IsNullOrWhiteSpace draft.path then
            setValidationError (Some "Enter a project name and path.")
        elif invalidRemote then
            setValidationError (Some "Enter the SSH host or WSL distribution.")
        elif invalidEnvironment then
            setValidationError (
                Some
                    "Environment names must be nonempty and cannot contain '='. Names and values cannot contain null characters."
            )
        elif duplicateKeys then
            setValidationError (Some "Environment variable names must be unique.")
        else
            setValidationError None

            dispatch (
                SaveProject
                    { draft with
                        environment = Map.ofList environment
                    }
            )

    Html.dialog
        [
            prop.id modalId
            prop.className "modal"
            prop.ariaLabelledBy titleId
            prop.onCancel (fun event ->
                event.preventDefault ()

                if not isSaving then
                    dispatch CloseProjectEditor)
            prop.children
                [
                    Html.div
                        [
                            prop.className "modal-box w-11/12 max-w-xl"
                            prop.children
                                [
                                    Html.button
                                        [
                                            prop.type' "button"
                                            prop.className "btn btn-sm btn-circle btn-ghost absolute right-2 top-2"
                                            prop.ariaLabel "Close project editor"
                                            prop.disabled isSaving
                                            prop.text "✕"
                                            prop.onClick (fun _ -> dispatch CloseProjectEditor)
                                        ]
                                    Html.h3
                                        [
                                            prop.id titleId
                                            prop.className "text-lg font-bold mb-4 pr-8"
                                            prop.text (if isNew then "Add project" else "Edit project")
                                        ]
                                    Html.form
                                        [
                                            prop.onSubmit (fun event ->
                                                event.preventDefault ()

                                                if not isSaving then
                                                    submit ())
                                            prop.children
                                                [
                                                    Html.fieldSet
                                                        [
                                                            prop.disabled isSaving
                                                            prop.className "flex flex-col gap-3"
                                                            prop.children
                                                                [
                                                                    input
                                                                        "project-name"
                                                                        "Name"
                                                                        draft.name
                                                                        true
                                                                        (fun value ->
                                                                            setDraft { draft with name = value })
                                                                    Html.div
                                                                        [
                                                                            prop.children
                                                                                [
                                                                                    fieldLabel
                                                                                        "project-description"
                                                                                        "Description"
                                                                                    Html.textarea
                                                                                        [
                                                                                            prop.id
                                                                                                "project-description"
                                                                                            prop.className
                                                                                                "textarea textarea-sm w-full"
                                                                                            prop.rows 2
                                                                                            prop.value draft.description
                                                                                            prop.onChange (fun value ->
                                                                                                setDraft
                                                                                                    { draft with
                                                                                                        description =
                                                                                                            value
                                                                                                    })
                                                                                        ]
                                                                                ]
                                                                        ]
                                                                    input
                                                                        "project-path"
                                                                        "Solution / workspace / directory"
                                                                        draft.path
                                                                        true
                                                                        (fun value ->
                                                                            setDraft { draft with path = value })
                                                                    Html.div
                                                                        [
                                                                            prop.className "grid grid-cols-2 gap-3"
                                                                            prop.children
                                                                                [
                                                                                    Html.div
                                                                                        [
                                                                                            prop.children
                                                                                                [
                                                                                                    fieldLabel
                                                                                                        "project-ide"
                                                                                                        "IDE"
                                                                                                    Html.select
                                                                                                        [
                                                                                                            prop.id
                                                                                                                "project-ide"
                                                                                                            prop
                                                                                                                .className
                                                                                                                "select select-sm w-full"
                                                                                                            prop.value
                                                                                                                draft
                                                                                                                    .ide
                                                                                                            prop
                                                                                                                .onChange
                                                                                                                    (fun
                                                                                                                        value ->
                                                                                                                        setDraft
                                                                                                                            { draft with
                                                                                                                                ide =
                                                                                                                                    value
                                                                                                                                remote =
                                                                                                                                    if
                                                                                                                                        value
                                                                                                                                            =
                                                                                                                                            "vscode"
                                                                                                                                    then
                                                                                                                                        draft
                                                                                                                                            .remote
                                                                                                                                    else
                                                                                                                                        None
                                                                                                                            })
                                                                                                            prop
                                                                                                                .children
                                                                                                                [
                                                                                                                    let choices =
                                                                                                                        [
                                                                                                                            "vscode",
                                                                                                                            "VS Code"
                                                                                                                            "visualstudio2022",
                                                                                                                            "Visual Studio 2022"
                                                                                                                            "visualstudio2026",
                                                                                                                            "Visual Studio 2026"
                                                                                                                            "rider",
                                                                                                                            "Rider"
                                                                                                                        ]

                                                                                                                    if
                                                                                                                        not (
                                                                                                                            choices
                                                                                                                            |> List.exists
                                                                                                                                (fun
                                                                                                                                    (value,
                                                                                                                                     _) ->
                                                                                                                                    value
                                                                                                                                        =
                                                                                                                                        draft
                                                                                                                                            .ide)
                                                                                                                        )
                                                                                                                    then
                                                                                                                        Html
                                                                                                                            .option
                                                                                                                            [
                                                                                                                                prop
                                                                                                                                    .value
                                                                                                                                    draft
                                                                                                                                        .ide
                                                                                                                                prop
                                                                                                                                    .text
                                                                                                                                    draft
                                                                                                                                        .ide
                                                                                                                            ]

                                                                                                                    for value,
                                                                                                                        label in
                                                                                                                        choices do
                                                                                                                        Html
                                                                                                                            .option
                                                                                                                            [
                                                                                                                                prop
                                                                                                                                    .value
                                                                                                                                    value
                                                                                                                                prop
                                                                                                                                    .text
                                                                                                                                    label
                                                                                                                            ]
                                                                                                                ]
                                                                                                        ]
                                                                                                ]
                                                                                        ]
                                                                                    Html.div
                                                                                        [
                                                                                            prop.children
                                                                                                [
                                                                                                    fieldLabel
                                                                                                        "project-remote"
                                                                                                        "Remote"
                                                                                                    Html.select
                                                                                                        [
                                                                                                            prop.id
                                                                                                                "project-remote"
                                                                                                            prop
                                                                                                                .className
                                                                                                                "select select-sm w-full"
                                                                                                            prop.value
                                                                                                                remoteType
                                                                                                            prop
                                                                                                                .disabled (
                                                                                                                    not (
                                                                                                                        draft
                                                                                                                            .ide
                                                                                                                            .Equals(
                                                                                                                                "vscode",
                                                                                                                                StringComparison
                                                                                                                                    .OrdinalIgnoreCase
                                                                                                                            )
                                                                                                                    )
                                                                                                                )
                                                                                                            prop
                                                                                                                .onChange
                                                                                                                    (fun
                                                                                                                        value ->
                                                                                                                        let remote =
                                                                                                                            match
                                                                                                                                value
                                                                                                                            with
                                                                                                                            | "ssh" ->
                                                                                                                                Some(
                                                                                                                                    Ssh(
                                                                                                                                        "",
                                                                                                                                        ""
                                                                                                                                    )
                                                                                                                                )
                                                                                                                            | "wsl" ->
                                                                                                                                Some(
                                                                                                                                    Wsl
                                                                                                                                        ""
                                                                                                                                )
                                                                                                                            | _ ->
                                                                                                                                None

                                                                                                                        setDraft
                                                                                                                            { draft with
                                                                                                                                remote =
                                                                                                                                    remote
                                                                                                                            })
                                                                                                            prop
                                                                                                                .children
                                                                                                                [
                                                                                                                    Html
                                                                                                                        .option
                                                                                                                        [
                                                                                                                            prop
                                                                                                                                .value
                                                                                                                                ""
                                                                                                                            prop
                                                                                                                                .text
                                                                                                                                "None"
                                                                                                                        ]
                                                                                                                    Html
                                                                                                                        .option
                                                                                                                        [
                                                                                                                            prop
                                                                                                                                .value
                                                                                                                                "ssh"
                                                                                                                            prop
                                                                                                                                .text
                                                                                                                                "SSH"
                                                                                                                        ]
                                                                                                                    Html
                                                                                                                        .option
                                                                                                                        [
                                                                                                                            prop
                                                                                                                                .value
                                                                                                                                "wsl"
                                                                                                                            prop
                                                                                                                                .text
                                                                                                                                "WSL"
                                                                                                                        ]
                                                                                                                ]
                                                                                                        ]
                                                                                                ]
                                                                                        ]
                                                                                ]
                                                                        ]
                                                                    match draft.remote with
                                                                    | Some(Ssh(host, username)) ->
                                                                        input
                                                                            "project-host"
                                                                            "SSH host"
                                                                            host
                                                                            true
                                                                            (fun value ->
                                                                                setDraft
                                                                                    { draft with
                                                                                        remote =
                                                                                            Some(Ssh(value, username))
                                                                                    })

                                                                        input
                                                                            "project-username"
                                                                            "SSH username (optional)"
                                                                            username
                                                                            false
                                                                            (fun value ->
                                                                                setDraft
                                                                                    { draft with
                                                                                        remote = Some(Ssh(host, value))
                                                                                    })
                                                                    | Some(Wsl distro) ->
                                                                        input
                                                                            "project-distro"
                                                                            "WSL distribution"
                                                                            distro
                                                                            true
                                                                            (fun value ->
                                                                                setDraft
                                                                                    { draft with
                                                                                        remote = Some(Wsl value)
                                                                                    })
                                                                    | None -> ()
                                                                    Html.div
                                                                        [
                                                                            prop.className
                                                                                "border-t border-base-300 pt-3"
                                                                            prop.children
                                                                                [
                                                                                    Html.p
                                                                                        [
                                                                                            prop.className
                                                                                                "text-sm font-semibold mb-2"
                                                                                            prop.text
                                                                                                "Environment variables"
                                                                                        ]
                                                                                    for index, (key, value) in
                                                                                        List.indexed environment do
                                                                                        Html.div
                                                                                            [
                                                                                                prop.className
                                                                                                    "flex items-start gap-2 mb-2"
                                                                                                prop.children
                                                                                                    [
                                                                                                        Html.input
                                                                                                            [
                                                                                                                prop
                                                                                                                    .className
                                                                                                                    "input input-sm w-2/5"
                                                                                                                prop
                                                                                                                    .ariaLabel
                                                                                                                    $"Variable name {index + 1}"
                                                                                                                prop
                                                                                                                    .placeholder
                                                                                                                    "NAME"
                                                                                                                prop
                                                                                                                    .required
                                                                                                                    true
                                                                                                                prop
                                                                                                                    .value
                                                                                                                    key
                                                                                                                prop.onChange
                                                                                                                    (fun
                                                                                                                        newKey ->
                                                                                                                        setEnvironment (
                                                                                                                            environment
                                                                                                                            |> List.mapi
                                                                                                                                (fun
                                                                                                                                    i
                                                                                                                                    row ->
                                                                                                                                    if
                                                                                                                                        i
                                                                                                                                            =
                                                                                                                                            index
                                                                                                                                    then
                                                                                                                                        newKey,
                                                                                                                                        value
                                                                                                                                    else
                                                                                                                                        row)
                                                                                                                        ))
                                                                                                            ]
                                                                                                        Html.textarea
                                                                                                            [
                                                                                                                prop
                                                                                                                    .className
                                                                                                                    "textarea textarea-sm flex-1 min-w-0"
                                                                                                                prop
                                                                                                                    .ariaLabel
                                                                                                                    $"Variable value {index + 1}"
                                                                                                                prop
                                                                                                                    .placeholder
                                                                                                                    "Value"
                                                                                                                prop
                                                                                                                    .rows
                                                                                                                    1
                                                                                                                prop
                                                                                                                    .value
                                                                                                                    value
                                                                                                                prop.onChange
                                                                                                                    (fun
                                                                                                                        newValue ->
                                                                                                                        setEnvironment (
                                                                                                                            environment
                                                                                                                            |> List.mapi
                                                                                                                                (fun
                                                                                                                                    i
                                                                                                                                    row ->
                                                                                                                                    if
                                                                                                                                        i
                                                                                                                                            =
                                                                                                                                            index
                                                                                                                                    then
                                                                                                                                        key,
                                                                                                                                        newValue
                                                                                                                                    else
                                                                                                                                        row)
                                                                                                                        ))
                                                                                                            ]
                                                                                                        Html.button
                                                                                                            [
                                                                                                                prop
                                                                                                                    .type'
                                                                                                                    "button"
                                                                                                                prop
                                                                                                                    .className
                                                                                                                    "btn btn-sm btn-ghost btn-square"
                                                                                                                prop
                                                                                                                    .ariaLabel
                                                                                                                    $"Remove variable {index + 1}"
                                                                                                                prop
                                                                                                                    .text
                                                                                                                    "✕"
                                                                                                                prop
                                                                                                                    .onClick
                                                                                                                        (fun
                                                                                                                            _ ->
                                                                                                                            setEnvironment (
                                                                                                                                environment
                                                                                                                                |> List
                                                                                                                                    .indexed
                                                                                                                                |> List.filter
                                                                                                                                    (fun
                                                                                                                                        (i,
                                                                                                                                         _) ->
                                                                                                                                        i
                                                                                                                                        <> index)
                                                                                                                                |> List
                                                                                                                                    .map
                                                                                                                                    snd
                                                                                                                            ))
                                                                                                            ]
                                                                                                    ]
                                                                                            ]
                                                                                    Html.button
                                                                                        [
                                                                                            prop.type' "button"
                                                                                            prop.className
                                                                                                "btn btn-xs btn-ghost"
                                                                                            prop.text "+ Add variable"
                                                                                            prop.onClick (fun _ ->
                                                                                                setEnvironment (
                                                                                                    environment
                                                                                                    @ [ "", "" ]
                                                                                                ))
                                                                                        ]
                                                                                ]
                                                                        ]
                                                                ]
                                                        ]
                                                    match validationError |> Option.orElse saveError with
                                                    | Some error ->
                                                        Html.div
                                                            [
                                                                prop.role "alert"
                                                                prop.className
                                                                    "alert alert-error mt-4 text-sm break-words"
                                                                prop.text error
                                                            ]
                                                    | None -> ()
                                                    Html.div
                                                        [
                                                            prop.className "modal-action"
                                                            prop.children
                                                                [
                                                                    Html.button
                                                                        [
                                                                            prop.type' "button"
                                                                            prop.className "btn btn-sm btn-ghost"
                                                                            prop.disabled isSaving
                                                                            prop.text "Cancel"
                                                                            prop.onClick (fun _ ->
                                                                                dispatch CloseProjectEditor)
                                                                        ]
                                                                    Html.button
                                                                        [
                                                                            prop.type' "submit"
                                                                            prop.className "btn btn-sm btn-primary"
                                                                            prop.disabled isSaving
                                                                            prop.text (
                                                                                if isSaving then "Saving…"
                                                                                elif isNew then "Add project"
                                                                                else "Save changes"
                                                                            )
                                                                        ]
                                                                ]
                                                        ]
                                                ]
                                        ]
                                ]
                        ]
                ]
        ]
