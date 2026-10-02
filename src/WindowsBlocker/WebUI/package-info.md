# Native editor adapters

- `Storage.cs`, `WebStore.cs` manage isolated development/production paths and the shared editor document.
- `NativeEditorContract.cs` validates editor origins and produces the document-start seed.
- `ExternalLinkHandler.cs` opens only user-initiated HTTP(S) source/help links.
- `InstalledAppCatalog.cs`, `AppInventory.cs` resolve exact Windows app identities and installed icons.
- `ActivityNativeIcons.cs` enriches historical Activity replies from stored app identities without replacing browser icons.
