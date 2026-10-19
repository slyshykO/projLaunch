# ProjLaunch — продолжение разработки

Обновлено 19 октября 2026 года. Этот файл — общая точка продолжения работы
между ПК и чатами. Он хранится в Git в корне репозитория. Все пути ниже
относительны корню checkout, независимо от диска, имени пользователя и ОС.

Репозиторий: [slyshykO/projLaunch](https://github.com/slyshykO/projLaunch).
Актуальные инструкции по сборке: [README.md](README.md).
Контракт Git API: [crates/git-server/README.md](crates/git-server/README.md).

## С чего начать на другом ПК

1. Получить актуальную ветку репозитория, открыть её корень как рабочую папку.
   Проверить `git status` и последние коммиты. Сохранять локальные изменения
   пользователя; не заменять checkout копией с другого ПК.
2. Прочитать этот файл, применимые AGENTS.md, README и руководство Git-сервера.
   Считать исходники и lock-файлы источником текущего состояния.
3. Установить зависимости, перечисленные в README: Node/npm, Git, Rust,
   .NET SDK из `global.json`, .NET 10 runtime и Windows/Tauri prerequisites.
   Версии пакетов определяют manifest/lock-файлы, а не этот документ.
4. Из корня выполнить `npm ci`, `dotnet tool restore`, `dotnet paket restore`.
   Локальный Paket закреплён в манифесте инструментов; старый Paket 11.0.1
   не восстанавливает net11.0. Не менять версии только ради обхода среды.
5. Для разработки использовать `npm run tauri dev`. Для сборки Windows —
   `build.bat build` (debug) или `build.bat build --release`.
   Для сборки с последующим запуском: `build.bat run` или `build.bat run --release`.
   Команда ждёт закрытия приложения; при ошибке сборки запуск не выполняется.
   `build.bat` при необходимости пересобирает собственный F# helper.
6. Для следующей задачи использовать раздел «Следующий этап» ниже.
   После завершённого этапа обновлять этот файл вместе с кодом.

Для продолжения достаточно checkout и обычного восстановления зависимостей.
Старые папки прототипов, копия SQLite-проекта, патч переноса и кеши из локальной
папки Codex больше не нужны. Не применять старые скрипты переноса повторно.
`target`, `node_modules`, `bin`, `obj` и sidecar-бинарники генерируются на новом ПК.

SQLite-каталог проектов и пользовательские настройки приложения остаются
локальными в app-data и не синхронизируются через Git. Пути launcher-проектов
могут различаться на разных ПК. Этот handoff переносит контекст разработки,
а не пользовательскую базу.

## Что уже готово

Иконка ProjLaunch заменена на сине-фиолетовую папку со стрелкой запуска.
Исходник с прозрачностью сохранён в `src-tauri/app-icon.png`; из него через
Tauri CLI созданы desktop-иконки в `src-tauri/icons/` и favicon 16/32 px в
`src/assets/`. Пути в Tauri-конфигурации и HTML остались прежними.

- `ca2924d` — SQLite-хранилище проектов.
- `78f62bf` — поддержка debug/release в build helper.
- `14349f6` — локальный Git-сервер, реальные status/log/diff, Tauri/Fable API,
  тесты и сборка sidecar через F# helper.

Эти изменения закоммичены. Не нужно заново переносить SQLite или прототип.
Пользователь проверил первый запуск SQLite: база появилась, проекты отображаются.
Новый экран Git пока не создан.

В карточках launcher добавлена кнопка с SVG-карандашом справа вверху.
Она открывает общий для добавления и редактирования `ProjectEditor.fs`:
название, описание, путь, IDE, SSH/WSL и переменные окружения.
Редактирование сохраняет прежние ID и lastOpened через существующий SQLite IPC.
При ошибке диалог остаётся открытым с введёнными данными; отмена не сохраняет.
Фоновые сохранения lastOpened больше не закрывают редактор. Проверка состояния:
после компиляции Fable выполнить `node scripts/tests/test-project-editor.mjs`.
Тест использует отдельную подмену IPC и не обращается к пользовательской БД.

Внизу карточек отображаются Git-иконки с именами репозиториев и статусом
`Clean` / `Uncommitted changes`. Неизвестное состояние и ошибки не считаются чистым.
Статусы обновляются после загрузки/сохранения проектов и при фокусе окна.
Запоздалые ответы после смены пути/проекта игнорируются.

`git_project_status` читает проект из SQLite, определяет связанные локальные
репозитории и вызывает `repo.open` + `git.status` через существующий stdio-клиент.
Для каталога/обычного файла используется содержащий его worktree; для
`.code-workspace` — также папки `folders`, для `.sln`/`.slnx` — перечисленные
проекты. Повторы объединяются по корню worktree, вложенные каталоги не сканируются.
JSONC поддерживает комментарии и завершающие запятые; `.git` может быть файлом.
Лимиты: 2 MiB на определение, 512 путей, 64 репозитория, 4 проверки карточек
одновременно. Относительный путь карточки требует замены на абсолютный;
относительные пути внутри workspace/solution считаются от файла определения.
SSH/WSL показываются как недоступные для локальной проверки. В БД связи не создаются.

Git-сервер запускается автоматически, когда проверка карточки впервые находит
локальный репозиторий. Tauri управляет одним stdio-процессом и завершает его при
отключении/выходе. Каждая проверка открывает репозиторий на своей текущей сессии;
после сбоя следующая проверка переподключается, старые repoId не переиспользуются.

## Цель и принятые решения

Развиваем существующий Tauri + Fable launcher постепенно, сохраняя запуск
проектов в IDE. Итоговая цель — Git-клиент для Windows, WSL и удалённых машин.
Пользователю нужен единый интерфейс с устойчивым расположением графа истории,
списка файлов и diff, без постоянного переключения вкладок и изменения размеров.
Нужны интерактивный граф и сохранение выбранного репозитория/положения панелей.

- Git выполняется отдельным Rust-сервером на машине, где находится репозиторий,
  включая локальный режим.
- Выбран `json-rpc-rs + axum`: один реестр обработчиков для stdio и сетевых
  адаптеров. `jsonrpsee` рассматривался для сравнения, не выбран backend.
- Tauri Rust управляет процессами и подключениями; Fable вызывает типизированные
  обёртки AppTauri. Детали транспорта не должны попадать в UI Git-операций.
- Один launcher-проект может включать несколько Git-репозиториев (например,
  VS Code workspace); один репозиторий может входить в несколько проектов.
- SQLite через rusqlite принадлежит приложению; Git-серверу база проектов не нужна.
- Целевая последовательность: локальный stdio → status/log/diff → UI с графом →
  WSL/SSH stdio → bootstrap и защищённый HTTPS/WSS с настраиваемым портом.
  SSH сможет копировать/запускать сервер; SSH stdio также остаётся транспортом.
  Удалённые этапы пока не реализованы.

## Карта исходников

| Путь | Назначение |
| --- | --- |
| `src/src/domain/ProjectData.fs` | DTO launcher-проекта, ручные Thoth JSON encoder/decoder |
| `src/src/State.fs`, `src/src/ui/View.fs` | Elmish-состояние и существующий launcher UI |
| `src/src/domain/GitProtocol.fs`, `src/src/tauri/AppTauri.fs` | Frontend DTO и типизированные вызовы Git API |
| `src/app.fsproj` | Порядок компиляции F# файлов |
| `src-tauri/src/lib.rs` | Команды launcher, регистрация GitHost и выход приложения |
| `src-tauri/src/git_host.rs` | Ленивый запуск sidecar, IPC-команды и shutdown |
| `src-tauri/src/project_git.rs` | Обнаружение репозиториев workspace/solution и DTO индикаторов |
| `src-tauri/src/project.rs` | Rust DTO, совместимость старого JSON |
| `src-tauri/src/storage/` | SQLite worker, запросы, миграции, legacy import и тесты |
| `src-tauri/migrations/` | Версионированная SQL-схема |
| `crates/git-protocol/` | Общие Rust DTO и константы протокола |
| `crates/git-server/src/git/` | GitRunner, status, history и diff |
| `crates/git-server/src/api.rs` | Единая регистрация RPC-методов |
| `crates/git-server/src/session.rs`, `transport.rs` | Сессии, отмена и stdio/WS pump |
| `crates/git-server/src/network.rs` | Необязательный development HTTP/WS адаптер |
| `crates/git-server/tests/` | Реальные Git fixtures и тесты транспорта |
| `crates/git-client/` | Мультиплексированный stdio-клиент, владеющий процессом сервера |
| `build/GitServer.fs`, `build/Program.fs` | Команда F# helper `build-git-server` |

`crates/Cargo.toml` — отдельный workspace; у Tauri сохраняется собственный
`src-tauri/Cargo.lock`. Проверять обе части. Native launcher Remote пока означает
`code --remote`; это ещё не подключение к Git-серверу WSL/SSH.

## SQLite: текущее поведение

База — `appDataDir/projlaunch.db` (на Windows обычно
`%APPDATA%/com.projlaunch.app/projlaunch.db`). Одним соединением владеет отдельный
поток; очередь Tokio ограничена, ответы идут через oneshot. Включены foreign keys
и busy timeout 5 секунд. Инициализация предшествует выполнению команд.

Схема (`PRAGMA user_version`) и legacy import выполняются отдельными транзакциями.
Импорт читает все обычные файлы непосредственно из старого `projects/`, независимо
от расширения. Поддерживаются serde Remote objects, старые Thoth-массивы,
отсутствующий/null remote и UTF-8 BOM. Исходные строки дат сохраняются.

Записи и маркер `legacy_projects_v1` фиксируются одной транзакцией под write-lock.
Ошибки JSON/чтения, пустые ID, дубли ID и конфликты с БД отменяют весь импорт;
ошибка с путём показывается в UI. После исправления нужен перезапуск. При ошибке
инициализации чтение/запись блокируются, а не возвращают пустой список.
Неизвестная более новая версия схемы отклоняется без понижения.

Исходные файлы не изменяются, не перемещаются и не удаляются. После успеха база —
единственный рабочий источник, старые файлы больше не перечитываются. Без старого
каталога создаётся пустая БД с маркером. База без маркера повторяет попытку импорта.
Не удалять пользовательскую БД ради повторения импорта: в ней могут быть новые данные.

IPC сохранён: `get_projects`, `open_project`, `rewrite_project_file` (сохраняет
теперь в SQLite). F#-обёртка называется `saveProject`; форма передаёт `pd.id`.
Debug-проекты не маскируют ошибку загрузки в Tauri.

## Git API: текущее поведение и границы

Полный контракт и примеры — в [руководстве сервера](crates/git-server/README.md).
Методы: `server.hello`, `repo.open`, `git.status`, `git.log`, `git.diffFiles`,
`git.diff`, `git.cancel`. NDJSON, object params, обязательные request ID и
caller-generated `operationId` для операций. Batch отключён, notifications
не имеют побочных эффектов. Stdout содержит только RPC, диагностика — в stderr.

- `repo.open` принимает абсолютный каталог внутри worktree, определяет root,
  gitDir/commonDir и возвращает временный repoId. Bare repo не поддерживается.
  repoId и cursor принадлежат своей сессии и недействительны после reconnect.
- Status использует porcelain v2/NUL; разделяет index/worktree, renames,
  untracked и conflicts. Unicode поддерживается; non-UTF-8 paths пока отклоняются.
- Log возвращает OID, родителей, автора, даты, subject, refs; порядок топологический.
  Snapshot фиксирует начальные OID и refs, новые коммиты не сдвигают следующие страницы.
  Страница по умолчанию 100, максимум 200; до 256 начальных OID. Курсоры живут
  до 5 минут или вытеснения, максимум 128. Traversal пока использует `--skip`.
- Diff: unstaged index→worktree, staged HEAD→index, commit parent→commit.
  Для merge обязателен явный родитель (нумерация с 1); root использует empty tree
  соответствующего object format. Сначала список файлов, затем patch выбранного
  пути; rename source определяет сервер. Untracked preview и binary flags отдельны.
- Patch/preview ограничен 128 KiB с флагом truncated. Обычный stdout Git — до
  8 MiB, stderr — до 64 KiB; timeout процесса 30 секунд. До 8 Git-процессов.
  Аргументы передаются массивом без shell, pathspec literal; external diff/textconv
  отключены. При отмене/тайм-ауте/лимите сервер останавливает и дожидается процесса.
- На соединение до 32 запросов плюс 4 места для отмены, очередь ответов 16,
  frame до 1 MiB. Client timeout 90 секунд; timeout закрывает соединение.
- HTTP/WS сохранён под feature `network`, не включён в обычную сборку sidecar.
  Это development loopback-адаптер, а не готовый удалённый endpoint.

Синтетические fetchDemo/watch/unwatch из прототипа не перенесены. Нет Git-записей,
watchers, TLS/auth, SSH/WSL deployment или постоянных repository bindings в БД.

## Сборка Git sidecar

Логика находится в `build/GitServer.fs`, включённом в `build/build.fsproj`.
Ранее использовавшийся `scripts/build-git-server.mjs` удалён; не восстанавливать его.

```powershell
npm run build:git-server
npm run build:git-server -- --release
# Альтернатива через Windows helper:
.\build.bat build-git-server
.\build.bat build-git-server --release
```

npm-обёртка запускает `build.bat build-git-server` и передаёт ему аргументы, включая `--release`.
В `build/build.fsproj` остаются оба фреймворка: `net10.0;net11.0`.
Выбор фреймворка выполняет только `build.bat` по версии SDK; выбранное значение используется для публикации и пути к исполняемому файлу.
Target берётся из `TAURI_ENV_TARGET_TRIPLE`, иначе из `rustc -vV`.
Бинарник копируется в `src-tauri/binaries/project-launch-git-server-<target>[.exe]`.
Tauri externalBin размещает его рядом с приложением.

`npm run dev` подготавливает debug sidecar; `npm run build` — release sidecar
перед Fable/Vite. Перед прямой Cargo-сборкой Tauri сначала собрать sidecar.
При переносе готового Windows-приложения сохранять `project-launch-git-server.exe` рядом с ним.

## Проверки и известные особенности среды

Последний успешный прогон реализации: Windows, Rust 1.99.0,
Git 2.55.0.windows.5. Эти версии описывают проверку, не заменяют manifests.

```powershell
cargo test --manifest-path crates/Cargo.toml --workspace --all-features --locked
cargo clippy --manifest-path crates/Cargo.toml --workspace --all-targets --all-features --locked -- -D warnings
cargo fmt --manifest-path crates/Cargo.toml --all --check
npm run build:git-server
cargo test --manifest-path src-tauri/Cargo.toml --lib --locked
cargo clippy --manifest-path src-tauri/Cargo.toml --all-targets --locked -- -D warnings
cargo fmt --manifest-path src-tauri/Cargo.toml --check
npm run build
cargo build --manifest-path src-tauri/Cargo.toml --locked
dotnet build build/build.fsproj
dotnet fsi --exec build/scripts/SelfBuild.Tests.fsx
```

Прошли 16 SQLite-тестов, 5 unit-тестов Git (один — child helper), 7 Git fixture
сценариев через реальный stdio-процесс и 4 теста транспорта. Проверены empty/unborn,
staged+unstaged, rename, root/merge, conflicts, detached HEAD, binary, Unicode,
limits, worktrees, reconnect, snapshot pagination, отмена, timeout и shutdown.
Unix-only тесты имён/симлинков добавлены, но на Windows не запускались.

Прошли Fable/Vite, Cargo build и Clippy, debug/release сборка sidecar через npm,
выбор target и совпадение SHA256 скопированного бинарника. `build.bat` успешно
автоматически пересобрал Native AOT helper и выполнил новую команду.
FSharp.Core при Native AOT выдавал IL3053/IL2104, публикация завершилась успешно.

Собранный sidecar проверен чтением основного repo: hello/open/status/log/diffFiles/
diff и завершение по EOF. Runtime-проверка будущего Git UI ещё не проводилась.

Для индикаторов карточек прошли 25 Rust-тестов (включая 16 SQLite), Clippy и
Fable/Vite. Новые fixtures проверяют JSONC, SLN/SLNX, Unicode, UTF-16, worktree,
дедупликацию, лимиты, ошибки, чистые/новые/staged/изменённые файлы и перезапуск
реального stdio-сервера. После компиляции Fable: `node scripts/tests/test-project-git.mjs`
проверяет гонки ответов, ошибки/повторную проверку и разметку нескольких индикаторов.

В прежней песочнице .NET блокировался при чтении реестра, Git fixture-тесты —
при записи `.git/config` временных репозиториев, сеть Cargo — при DNS.
Соответствующие проверки успешно запускались вне песочницы с разрешением.
Это ограничения среды, не повод менять код. Настройки старых локальных кешей
не требуются на другом ПК. Некоторые исходники используют CRLF; для проверки
whitespace использовался `git -c core.whitespace=cr-at-eol diff --check`.

## Следующий этап

Следующая функциональная работа — UI истории/файлов/diff с интерактивным графом.
Перед реализацией проверить текущий пользовательский запрос и состояние кода.

- Сохранить launcher и возможность открывать проекты в IDE.
- Использовать `AppTauri.Git`, не переносить stdio/процессы в UI.
- Игнорировать устаревшие ответы через repo/selection/generation; сохранять
  выбор и прокрутку отдельно для репозиториев, затем расположение панелей.
- Начать с ручного обновления и обновления по фокусу; watchers — позднее.
- После reconnect открывать repo заново, сбрасывать старые cursor/repoId.
- Явно выбирать родителя merge для diff и учитывать binary/truncated/empty states.

Предлагаемая постоянная модель пока не реализована:

- `Project` — launcher-каталог, solution или workspace.
- `Endpoint` — среда/сервер и настройки подключения.
- `RepositoryBinding` — постоянные id, endpointId и путь worktree на endpoint.
  Разные worktree — разные bindings.
- `ProjectRepository` — связь projectId/repositoryBindingId, необязательное
  отображаемое имя и порядок; один repo может быть связан с разными проектами.
- Будущие SQL-таблицы: endpoints, repositories, project_repositories.
  Удаление проекта удаляет связи, а не репозитории на диске.
- Runtime connectionId/repoId не заменяет постоянный binding.
  Не создавать Git bindings автоматически при legacy SQLite import.
  Обнаружение roots в каталогах/VS Code workspace — отдельная операция.

Открыты: библиотека графа, точная схема endpoints, credentials/rotation,
TLS trust/bootstrap, обнаружение workspace roots, production cancellation/
subscriptions и распространение сервера на удалённые машины.

## Происхождение и зависимость JSON-RPC

Начальные SQLite и JSON-RPC эксперименты выполнялись вне основного репозитория.
Их необходимые реализации уже перенесены; исторические sandbox-копии и локальная
история baseline-коммитов не нужны для дальнейшей работы и не включены в Git.
Сравнение jsonrpsee и json-rpc-rs было функциональным, не бенчмарком производительности.

Выбранный форк: [slyshykO/json-rpc-rs](https://github.com/slyshykO/json-rpc-rs),
ветка `fix-rpc-protocol-and-axum`, закреплённый commit
`226e48afcc9557adf9b2ce04c72a27a37858be15`.
[Upstream PR #2](https://github.com/pyk/json-rpc-rs/pull/2).
Перед сменой зависимости проверить актуальный upstream статус; не считать PR
слитым по этому handoff. Исправления: неверные параметры → -32602, notification-only
batch без пустого ответа, комментарии feature axum и примеры документации.
В старом эксперименте прошли 84 интеграционных теста и 9 doctests форка с локальными
Windows-обходами; это историческая проверка, не текущая CI-гарантия.

Документ фиксирует решения и выполненную работу, а не дословную историю чатов.
Для нового чата достаточно просьбы: «Прочитай CODEX-HANDOFF.md и продолжи
следующий этап, сохранив мои текущие изменения». После этапа обновлять текущий
статус и оставшиеся задачи здесь; Git сохраняет предыдущие версии документа.
