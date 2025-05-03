namespace Browser

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types

// ─────────────────────────────────────────────────────────────────────────────
//  HTMLDialogElement – temporary shim until it appears in Browser.Types
// ─────────────────────────────────────────────────────────────────────────────

[<AllowNullLiteral; Global>]
type HTMLDialogElement =
    inherit HTMLElement

    /// <summary>
    /// The open property of the HTMLDialogElement interface is a boolean value reflecting the open HTML attribute,
    /// indicating whether the <dialog> is available for interaction.
    /// </summary>
    abstract ``open``: bool with get, set

    /// Any string you pass to `close(value)`.  Empty string by default.
    abstract returnValue: string with get, set

    /// The closedBy property of the HTMLDialogElement interface indicates the types of user actions that can be used to close the associated <dialog> element. It sets or returns the dialog's closedby attribute value.
    abstract closedBy: string with get, set

    /// <dialog>.show() – shows non‑modal
    abstract show: unit -> unit

    /// <dialog>.showModal() – shows modal, focus‑trapped
    abstract showModal: unit -> unit

    /// <dialog>.close([returnValue])
    abstract close: ?returnValue: string -> unit

    /// <dialog>.requestClose([returnValue])
    abstract requestClose: ?returnValue: string -> unit
