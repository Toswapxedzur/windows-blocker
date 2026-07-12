# Cofre do Windows

O Windows Vault é o membro nativo do Windows da família de produtos Vault. É um aplicativo .NET 8 WPF com um editor WebView2, inventário de aplicativos nativos, mecanismo de aplicação, tempo de execução de regras personalizadas e um hub de ponte de aplicativo da web local.

O código é o contrato do produto. O manual no aplicativo mantido é [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Capacidades atuais

- Grupos padrão para aplicativos Windows selecionados e grupos personalizados para regras de política avançadas.
- Modos imediato, subsídio e contagem regressiva; horários; congelar; soneca; e importação/exportação de grupo.
- Inventário de aplicativos do Windows e componentes de aplicação baseados em janelas.
- Um editor WebView2 hospedado em `src/WindowsBlocker/WebAssets/`.
- Execução controlada de regras personalizadas com verificação de sintaxe e feed de log.
- Um hub de ponte de loopback para grupos compatíveis explicitamente vinculados.
- Temporizador nativo, brinde e janelas de sobreposição de painel.

## Construir

Use a solução e o projeto com check-in:

```powershell
dotnet build WindowsBlocker.sln
```

O projeto do aplicativo tem como alvo `net8.0-windows` e usa WPF mais WebView2. Crie e execute-o no Windows com o SDK .NET necessário e o tempo de execução WebView2 disponível.

## Mapa do projeto

| Área | Diretório de origem |
| --- | --- |
| Modelo de grupo e avaliação de políticas | `src/WindowsBlocker/Core/` |
| Aplicação nativa | `src/WindowsBlocker/Enforcement/` |
| Inventário de aplicativos e ponte WebView | `src/WindowsBlocker/WebUI/` |
| Tempo de execução de regra personalizada | `src/WindowsBlocker/Rules/` |
| Centro de ponte | `src/WindowsBlocker/Bridge/` |
| Janelas e sobreposições WPF | `src/WindowsBlocker/` |

## Documentação e traduções

Os documentos em inglês permanecem canônicos. Os rótulos da UI usam os catálogos JSON completos em `src/WindowsBlocker/WebAssets/translation/`; os manuais traduzidos estão ao lado de `manual/en.md`, e as cópias traduzidas dos documentos mantidos restantes estão em `i18n-docs/<locale>/`.
