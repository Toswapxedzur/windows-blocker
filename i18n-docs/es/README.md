# Bóveda de Windows

Windows Vault es el miembro nativo de Windows de la familia de productos Vault. Es una aplicación .NET 8 WPF con un editor WebView2, inventario de aplicaciones nativas, motor de aplicación, tiempo de ejecución de reglas personalizadas y un centro de puente de aplicaciones web local.

El código es el contrato del producto. El manual mantenido en la aplicación es [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Capacidades actuales

- Grupos predeterminados para aplicaciones de Windows seleccionadas y grupos personalizados para reglas de políticas avanzadas.
- Modos inmediato, de asignación y de cuenta regresiva; horarios; congelar; siesta; e importación/exportación grupal.
- Inventario de aplicaciones de Windows y componentes de aplicación basados en ventanas.
- Un editor WebView2 alojado en `src/WindowsBlocker/WebAssets/`.
- Ejecución controlada de reglas personalizadas con verificación de sintaxis y alimentación de registros.
- Un centro de puente loopback para grupos compatibles vinculados explícitamente.
- Ventanas nativas de superposición de paneles, brindis y temporizador.

## Construir

Utilice la solución y el proyecto registrados:

```powershell
dotnet build WindowsBlocker.sln
```

El proyecto de la aplicación tiene como objetivo `net8.0-windows` y utiliza WPF más WebView2. Compílelo y ejecútelo en Windows con el SDK de .NET y el tiempo de ejecución WebView2 necesarios disponibles.

## Mapa del proyecto

| Área | Directorio fuente |
| --- | --- |
| Evaluación de políticas y modelos de grupo | `src/WindowsBlocker/Core/` |
| Aplicación nativa | `src/WindowsBlocker/Enforcement/` |
| Inventario de aplicaciones y puente WebView | `src/WindowsBlocker/WebUI/` |
| Tiempo de ejecución de reglas personalizadas | `src/WindowsBlocker/Rules/` |
| Centro del puente | `src/WindowsBlocker/Bridge/` |
| Ventanas y superposiciones de WPF | `src/WindowsBlocker/` |

## Documentación y traducciones

Los documentos ingleses siguen siendo canónicos. Las etiquetas de la interfaz de usuario utilizan los catálogos JSON completos en `src/WindowsBlocker/WebAssets/translation/`; Los manuales traducidos se encuentran junto a `manual/en.md`, y las copias traducidas de los documentos mantenidos restantes se encuentran en `i18n-docs/<locale>/`.
