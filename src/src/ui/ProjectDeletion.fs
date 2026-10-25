module ProjectDeletion

open System
open Feliz
open projectData
open state

[<ReactComponent>]
let Dialog (project: ProjectData) (isDeleting: bool) (error: string option) (dispatch: Msg -> unit) =
    let modalId = "project-deletion"
    let titleId = "project-deletion-title"
    let descriptionId = "project-deletion-description"

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

    Html.dialog
        [
            prop.id modalId
            prop.className "modal"
            prop.ariaLabelledBy titleId
            prop.ariaDescribedBy descriptionId
            prop.onCancel (fun event ->
                event.preventDefault ()

                if not isDeleting then
                    dispatch CancelProjectDeletion)
            prop.children
                [
                    Html.div
                        [
                            prop.className "modal-box max-w-lg"
                            prop.children
                                [
                                    Html.h3
                                        [
                                            prop.id titleId
                                            prop.className "text-lg font-bold"
                                            prop.text "Remove project?"
                                        ]
                                    Html.p [ prop.className "mt-3 font-semibold break-words"; prop.text project.name ]
                                    Html.p
                                        [
                                            prop.className "mt-1 text-sm text-base-content/70 break-all"
                                            prop.text project.path
                                        ]
                                    Html.p
                                        [
                                            prop.id descriptionId
                                            prop.className "mt-4 text-sm"
                                            prop.text
                                                "This removes the project from ProjLaunch. Files and Git repositories stay on disk. Open repository tabs stay open."
                                        ]
                                    match error with
                                    | Some message ->
                                        Html.div
                                            [
                                                prop.role "alert"
                                                prop.className "alert alert-error mt-4 text-sm break-words"
                                                prop.text message
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
                                                            prop.autoFocus true
                                                            prop.disabled isDeleting
                                                            prop.text "Cancel"
                                                            prop.onClick (fun _ -> dispatch CancelProjectDeletion)
                                                        ]
                                                    Html.button
                                                        [
                                                            prop.type' "button"
                                                            prop.className "btn btn-sm btn-error"
                                                            prop.disabled isDeleting
                                                            prop.text (
                                                                if isDeleting then "Removing…" else "Remove project"
                                                            )
                                                            prop.onClick (fun _ -> dispatch DeleteProject)
                                                        ]
                                                ]
                                        ]
                                ]
                        ]
                ]
        ]
