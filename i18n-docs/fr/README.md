# Coffre Windows

Windows Vault est le membre Windows natif de la famille de produits Vault. Il s'agit d'une application .NET 8 WPF avec un éditeur WebView2, un inventaire d'applications natif, un moteur d'application, un environnement d'exécution de règles personnalisées et un hub de pont d'applications Web local.

Le code est le contrat de produit. Le manuel intégré à l'application est [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Capacités actuelles

- Groupes par défaut pour les applications Windows sélectionnées et groupes personnalisés pour les règles de stratégie avancées.
- Modes immédiat, allocation et compte à rebours ; les horaires ; geler; somnoler; et import/export groupé.
- Inventaire des applications Windows et composants d'application basés sur les fenêtres.
- Un éditeur WebView2 hébergé depuis `src/WindowsBlocker/WebAssets/`.
- Exécution contrôlée de règles personnalisées avec vérification de la syntaxe et flux de journaux.
- Un hub de pont de bouclage pour les groupes compatibles explicitement liés.
- Fenêtres natives de minuterie, de pain grillé et de superposition de panneaux.

## Construire

Utilisez la solution et le projet archivés :

```powershell
dotnet build WindowsBlocker.sln
```

Le projet d'application cible `net8.0-windows` et utilise WPF plus WebView2. Créez-le et exécutez-le sous Windows avec le SDK .NET requis et le runtime WebView2 disponibles.

## Carte du projet

| Zone | Répertoire source |
| --- | --- |
| Modèle de groupe et évaluation des politiques | @@@GARDER0000@@@ |
| Application autochtone | @@@GARDER0000@@@ |
| Inventaire des applications et pont WebView | @@@GARDER0000@@@ |
| Exécution de règles personnalisées | @@@GARDER0000@@@ |
| Moyeu du pont | @@@GARDER0000@@@ |
| Fenêtres et superpositions WPF | @@@GARDER0000@@@ |

## Documentation et traductions

Les documents anglais restent canoniques. Les étiquettes de l'interface utilisateur utilisent les catalogues JSON complets dans `src/WindowsBlocker/WebAssets/translation/` ; les manuels traduits se trouvent à côté de `manual/en.md`, et les copies traduites des documents conservés restants se trouvent sous `i18n-docs/<locale>/`.
