module Tauri

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom

[<Erase>]
type Tauri =

    [<Import("invoke", "@tauri-apps/api/core")>]
    static member invoke(_cmd: string, _invokeParams: obj option) : JS.Promise<_> = jsNative

    [<Import("isTauri", "@tauri-apps/api/core")>]
    static member isTauri() : bool = nativeOnly

    /// <summary>
    /// Returns the path to the suggested directory for your app's config files.
    /// Resolves to <c>${configDir}/${bundleIdentifier}</c>, where <c>bundleIdentifier</c> is the [<c>identifier</c>](https://v2.tauri.app/reference/config/#identifier) value configured in <c>tauri.conf.json</c>.
    /// </summary>
    /// <example>
    /// <code lang="typescript">
    /// import { appConfigDir } from '@tauri-apps/api/path';
    /// const appConfigDirPath = await appConfigDir();
    /// </code>
    /// </example>
    [<Import("appConfigDir", "@tauri-apps/api/path")>]
    static member appConfigDir() : JS.Promise<string> = nativeOnly

    /// <summary>
    /// Returns the path to the suggested directory for your app's data files.
    /// Resolves to <c>${dataDir}/${bundleIdentifier}</c>, where <c>bundleIdentifier</c> is the [<c>identifier</c>](https://v2.tauri.app/reference/config/#identifier) value configured in <c>tauri.conf.json</c>.
    /// </summary>
    /// <example>
    /// <code lang="typescript">
    /// import { appDataDir } from '@tauri-apps/api/path';
    /// const appDataDirPath = await appDataDir();
    /// </code>
    /// </example>
    [<Import("appDataDir", "@tauri-apps/api/path")>]
    static member appDataDir() : JS.Promise<string> = nativeOnly

    /// <summary>
    /// Gets the application version.
    /// </summary>
    /// <example>
    /// <code lang="typescript">
    /// import { getVersion } from '@tauri-apps/api/app';
    /// const appVersion = await getVersion();
    /// </code>
    /// </example>
    [<Import("getVersion", "@tauri-apps/api/app")>]
    static member getVersion() : JS.Promise<string> = nativeOnly
