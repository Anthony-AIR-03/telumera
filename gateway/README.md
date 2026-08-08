# gateway/

The API Gateway / BFF (`Telumera.Gateway`) — the single authenticated public API surface. Composes
responses across services; holds no module data of its own (see
[docs/architecture/c4-container.md](../docs/architecture/c4-container.md)).

Not yet scaffolded. Will be built as an instance of the ASP.NET Core service template (M00.2), wired up
as part of M00.4 (Identity, workspaces and sites).
