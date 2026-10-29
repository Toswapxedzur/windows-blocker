# Manuel utilisateur de Windows Vault

Windows Vault comporte trois pages : **Vault** bloque les applications natives, **Classificateur** attribue des tags au contenu des navigateurs pris en charge et **Activité** affiche l’utilisation enregistrée. L’extension collecte le contenu pris en charge et applique le blocage dans le navigateur. Installez-la et connectez-la dans le navigateur que vous utilisez.

## Démarrage rapide

1. Dans **Vault**, ajoutez un groupe de blocage et une cible Applications, puis choisissez des applications avec le sélecteur +.
2. Choisissez le comportement de blocage du groupe et activez-le.
3. Dans **Classificateur**, créez un groupe, choisissez ses plateformes et ajoutez des tags avec leurs descriptions.
4. Choisissez un niveau de modèle local et téléchargez-le si nécessaire. Activez le tagging dans les paramètres du Classificateur et reprenez le groupe.
5. Ouvrez du contenu pris en charge dans le navigateur connecté. Configurez un filtre de tags dans un groupe de blocage du navigateur pour utiliser les tags dans le blocage.

## Groupes de blocage

Un **groupe de blocage** applique une politique de blocage. Un **groupe du Classificateur** attribue des étiquettes au contenu ; il ne bloque rien à lui seul.

1. Ajoutez un groupe de blocage et donnez-lui un nom.
2. Choisissez les cibles sous **S’applique à**.
3. Choisissez quand le blocage s’applique, puis définissez les horaires ou le temps autorisé nécessaires.
4. Activez le groupe. Ses cibles partagent sa politique.

Les modifications courantes sont enregistrées automatiquement. Une erreur signifie que la modification n’a pas été acceptée ; corrigez le champ et réessayez. Désactivez un groupe pour arrêter sa politique tout en conservant sa configuration. **Supprimer le groupe** le retire. Faites glisser les groupes pour les réordonner. Plusieurs groupes peuvent s’appliquer à une cible ; mettre l’un en pause ne lève pas le blocage d’un autre.

**Exporter** copie la configuration d’un groupe. **Importer** remplace la configuration du groupe sélectionné après confirmation.

### Temps autorisé et horaires

**Bloquer immédiatement** s’applique dès que le groupe activé correspond et que ses horaires sont actifs. **Bloquer lorsque le temps autorisé est épuisé** permet l’utilisation correspondante jusqu’à épuisement du temps disponible.

Définissez le temps autorisé en minutes et l’intervalle de réinitialisation en heures. Une limite glissante compte l’utilisation dans la fenêtre précédente. La réinitialisation à minuit commence une nouvelle période à minuit local, y compris pour une limite glissante.

Choisissez les jours actifs et, éventuellement, des plages horaires locales, une par ligne, par exemple **09:00-12:00**. Une liste vide s’applique pendant toute la journée des jours sélectionnés. Une plage doit se terminer après son début le même jour ; répartissez un horaire nocturne sur des jours distincts.

### Pause

Configurez la pause dans chaque groupe de blocage. **Mettre le blocage en pause** suspend la politique du groupe pendant la durée de pause. **Ajouter au temps autorisé** ajoute des minutes utilisables à un groupe limité dans le temps. Seul le temps supplémentaire consommé compte comme temps de pause. Le temps supplémentaire inutilisé expire à la prochaine réinitialisation ; pour une limite glissante, après une fenêtre, ou plus tôt à minuit si cette option est activée.

**Délai d’activation** reporte la pause tandis que le blocage continue. **Délai après la pause** est l’attente entre la fin d’une pause et une nouvelle demande. **Confirmations requises** définit le nombre d’étapes de confirmation. La pause n’est disponible dans un groupe verrouillé que si elle était autorisée avant le verrouillage.

### Verrouillage et PIN

**Verrouiller les modifications** empêche les modifications courantes. Le déverrouillage exige dix confirmations espacées de cinq secondes, ainsi que l’attente configurée et le PIN à six chiffres, le cas échéant. **Attente avant le déverrouillage** accepte 0–72 heures ; 0 n’ajoute aucune attente.

Pendant le verrouillage, l’attente peut être prolongée et un PIN ajouté s’il n’en existe pas. Ces conditions ne peuvent pas être assouplies avant le déverrouillage du groupe. La suppression respecte également l’attente restante et le PIN.

### Groupes liés

Utilisez **Lier** pour connecter des groupes explicitement sélectionnés dans d’autres programmes Vault. Les groupes liés partagent leur nom, les paramètres de politique pris en charge, les cibles, l’utilisation et les conditions de verrouillage. Chaque programme modifie et applique les types de cibles qu’il prend en charge ; les autres entrées restent accessibles aux programmes liés. Délier conserve chaque groupe et ses paramètres.

Si un membre lié est hors ligne, les modifications peuvent être indisponibles. Ouvrez Windows Vault et le navigateur lié pour les reconnecter. Une politique locale enregistrée peut continuer à s’appliquer pendant qu’un membre est hors ligne.

## Obtenir de l’aide

Cliquez sur le petit **i** près d’un champ pour voir son explication. Cliquez à l’extérieur ou appuyez sur Échap pour la fermer. Les listes restent dans des zones défilantes ; faites défiler la zone pour atteindre d’autres entrées. La recherche filtre la liste visible sans supprimer d’entrées.

Les règles personnalisées ont leur propre [Manuel de code](../code-manual/fr.md). Il explique l’éditeur, l’activation, les journaux, l’accès aux fichiers et l’API prise en charge.

## Applications natives

Utilisez le sélecteur + d’une cible Applications pour choisir les applications installées. **Bloquer toutes les applications sauf celles-ci** transforme la liste en liste d’autorisation. Les applications système, les navigateurs et Vault lui-même sont exclus du blocage des applications natives.

Une application bloquée reçoit une demande de fermeture. **Redemander la fermeture d’une application bloquée toutes les (minutes)** règle les nouvelles tentatives. Les redirections de sites, les pauses de pages et le masquage des fils sont appliqués par l’extension ; ils ne deviennent pas des actions sur les applications natives.

## Étiquetage du contenu

Un groupe Étiquetage du contenu étiquette le contenu de ses plateformes avec son arbre de tags et ses réglages de modèle. Chaque plateforme appartient à un groupe ; choisissez-les à la création car elles ne sont plus modifiables ensuite. Les horaires et filtres de blocage ne contrôlent pas le tagging. Activez-le dans Réglages et mettez chaque groupe en pause/reprise séparément. Désactiver l’enregistrement du flux dans **Activité → Enregistrement** arrête aussi son tagging.

### Tags et réglages du modèle

Créez des tags, décrivez leur sens et définissez ou retirez leurs parents dans l’arbre. Le glisser-déposer déplace une branche. **Confiance minimale du tag** définit le niveau de preuve de 1 à 5 ; les nouveaux groupes commencent à 4 (preuve forte). Aucun nombre de tags n’est imposé ; un élément peut n’en recevoir aucun. **Consignes d’étiquetage** mémorise les préférences du groupe. Les modifications s’enregistrent automatiquement ; chaque groupe utilise connexion, modèle et effort explicitement choisis.

### Connexions IA

**Connexions IA** propose ChatGPT subscription, OpenAI API, Gemini API et Grok API. L’abonnement utilise la connexion Vault et un forfait admissible ; les clés API suivent la facturation et les limites du fournisseur. Choisissez le fournisseur et saisissez la clé sous la liste commune ; elle est enregistrée automatiquement, la vider la supprime. **Récupérer la liste des modèles** récupère les modèles sans en choisir un. **Effort de raisonnement** propose modèle par défaut, Low, Medium ou High selon prise en charge. Les nouveaux choix commencent à Medium ; les anciens restent enregistrés. Un effort plus élevé peut prendre du temps et utiliser plus de jetons. La recherche Web est toujours activée pour le tagging ; le modèle décide quand l’utiliser. En cas d’échec, les résultats valides en cache restent ; les nouveaux éléments restent en attente jusqu’à **Réessayer la classification cloud**. Aucun changement automatique de fournisseur ; chaque connexion a son intervalle et sa limite d’essais. Corrigez les tags dans l’extension avec **+ tag** ; commande de retrait ou Delete une fois pour supprimer. **Sans étiquette** signifie terminé sans tag ; **Étiquetage en cours** signifie en attente. Votre correction fait autorité.


## Activité

Activité enregistre localement l’utilisation des apps activées, les visites de sites et **Contenu consulté** pris en charge. Les graphiques reflètent les données enregistrées ; une zone vide ne prouve pas que le PC était inactif. Choisissez une période. **Chronologie** montre l’utilisation selon l’heure de la journée ; **Totaux** additionne les durées ; **Intervalle de temps** regroupe l’usage de chaque intervalle en blocs verticaux. **Couleurs** est une légende cliquable : choisissez une source pour centrer les graphiques dessus. Choisissez un jour pour afficher l’usage à partir de cette date.

### Groupes Activité

Créez un groupe pour afficher ensemble dans Usage les apps et sites sélectionnés. **Merge** donne un nom et une couleur communs à ses membres dans Activité. Le groupe organise l’utilisation enregistrée ; il est distinct des groupes de blocage et de Étiquetage du contenu. Enregistrez explicitement l’éditeur avec **Save**.

### Enregistrement et conservation

Dans **Enregistrement**, activez ou désactivez l’enregistrement par catégorie ou source individuelle. **Conserver** définit la durée de conservation de l’historique ; **Toujours** évite l’expiration automatique. Les choix individuels peuvent suivre le réglage général. Arrêter l’enregistrement empêche les nouveaux enregistrements ; supprimer l’historique retire les entrées enregistrées. Les flux recueillent le contenu affiché sur les pages prises en charge, ouvertes ou non. Un flux **Étiquetage pris en charge** peut alimenter Étiquetage du contenu lorsque l’enregistrement est actif. Sa conservation est indépendante de l’usage des apps et sites. Mettre en pause un groupe de tagging n’arrête pas l’enregistrement.

## Réglages

Choisissez la langue de l’interface dans Réglages. Les boutons Info affichent les explications dans la langue sélectionnée.

## Enregistrement et dépannage

Les modifications courantes de Vault et du Classificateur sont enregistrées automatiquement. Les groupes d’Activité utilisent **Enregistrer**. L’ajout, la suppression, le téléchargement d’un modèle, le test d’une connexion et la récupération d’une liste de modèles restent des actions explicites.

Si les tags manquent, vérifiez la connexion au navigateur, le commutateur global de tagging, la suspension du groupe, l’enregistrement de la plateforme et le téléchargement du modèle. Si la recherche ne fonctionne pas, vérifiez le consentement, le choix du groupe, les identifiants du fournisseur, le modèle et l’état de la recherche. Si un groupe lié ne peut pas être modifié, reconnectez ses programmes ou déverrouillez-le comme indiqué.
