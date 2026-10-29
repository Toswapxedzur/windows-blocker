# Gebruikershandleiding Windows Vault

Windows Vault heeft drie pagina's: **Vault** blokkeert native apps, **Classifier** tagt ondersteunde browserinhoud en **Activity** toont geregistreerd gebruik. De browserextensie verzamelt ondersteunde inhoud en past browserblokkering toe. Installeer en verbind de extensie in de browser die je gebruikt.

## Snel starten

1. Voeg in **Vault** een blokkeergroep en Apps-doel toe en selecteer apps met de +-kiezer.
2. Kies het blokkeergedrag van de groep en schakel die in.
3. Maak in **Classifier** een groep, kies de platforms en voeg tags met beschrijvingen toe.
4. Kies een lokaal modelniveau en download het zo nodig. Schakel tagging in bij de Classifier-instellingen en hervat de groep.
5. Open ondersteunde inhoud in de verbonden browser. Stel een tagfilter in een browser-blokkeergroep in als tags de blokkering moeten sturen.

## Blokkeergroepen

Een **blokkeergroep** past een blokkeerbeleid toe. Een **Classifier-groep** wijst tags toe aan inhoud; die blokkeert zelf niets.

1. Voeg een blokkeergroep toe en geef deze een naam.
2. Kies doelen onder **Van toepassing op**.
3. Kies wanneer blokkeren geldt en stel eventueel een schema of toegestane tijd in.
4. Schakel de groep in. De doelen delen het beleid van de groep.

Gewone wijzigingen worden automatisch opgeslagen. Een fout betekent dat de wijziging niet is geaccepteerd; corrigeer het veld en probeer opnieuw. Schakel een groep uit om het beleid te stoppen en de configuratie te behouden. **Groep verwijderen** verwijdert de groep. Sleep groepen om ze te herschikken. Meerdere groepen kunnen hetzelfde doel raken; uitstel van één groep heft de blokkering door een andere niet op.

**Exporteren** kopieert een groepsconfiguratie. **Importeren** vervangt na bevestiging de configuratie van de geselecteerde groep.

### Toegestane tijd en schema

**Onmiddellijk blokkeren** geldt zodra de ingeschakelde groep overeenkomt en het schema actief is. **Blokkeren zodra de toegestane tijd is gebruikt** staat overeenkomstig gebruik toe totdat de tijd op is.

Stel de toegestane tijd in minuten in en het herstelinterval in uren. Een rollende limiet telt gebruik in het voorafgaande tijdvenster. Een herstel om middernacht begint een nieuwe periode om lokale middernacht, ook voor een rollende limiet.

Kies actieve weekdagen en optionele lokale tijdvensters, één per regel, zoals **09:00-12:00**. Zonder vensters geldt het schema de hele geselecteerde dagen. Een venster moet op dezelfde dag later eindigen dan het begint; splits een nachtelijk schema over aparte dagen.

### Uitstel

Stel uitstel in voor elke blokkeergroep. **Blokkering pauzeren** schort het beleid van die groep op gedurende de pauzetijd. **Aan de toegestane tijd toevoegen** voegt bruikbare minuten toe aan een groep met tijdslimiet. Alleen gebruikte extra tijd telt als uitstel. Ongebruikte extra tijd vervalt bij de volgende reset; bij een rollende limiet na één venster, of eerder om middernacht als die optie aanstaat.

**Activeringsvertraging** stelt uitstel uit terwijl de blokkering doorgaat. **Afkoelperiode** is de wachttijd na afloop van uitstel voordat je het opnieuw kunt aanvragen. **Vereiste bevestigingen** bepaalt het aantal bevestigingsstappen. Uitstel is bij een bevroren groep alleen beschikbaar als het vóór het bevriezen is toegestaan.

### Bevriezen en PIN

**Bevriezen** voorkomt gewone wijzigingen. Ontdooien vereist tien bevestigingen met vijf seconden ertussen, plus de ingestelde wachttijd en een zescijferige PIN. **Wacht vóór ontdooien** accepteert 0–72 uur; 0 voegt geen wachttijd toe.

Tijdens het bevriezen kan de wachttijd worden verlengd en kan een PIN worden toegevoegd als die ontbreekt. Deze voorwaarden kunnen niet worden versoepeld voordat de groep is ontdooid. Verwijderen vereist ook naleving van de resterende wachttijd en PIN.

### Gekoppelde groepen

Gebruik **Koppelen** om expliciet geselecteerde groepen in andere Vault-programma's te verbinden. Gekoppelde groepen delen hun naam, ondersteunde beleidsinstellingen, doelen, gebruik en bevriesvoorwaarden. Elk programma bewerkt en handhaaft de doeltypen die het ondersteunt; andere doelen blijven beschikbaar voor gekoppelde programma's. Ontkoppelen behoudt elke groep en de instellingen ervan.

Als een gekoppeld lid offline is, kan bewerken niet beschikbaar zijn. Open Windows Vault en de gekoppelde browser om opnieuw te verbinden. Een lokaal opgeslagen beleid kan actief blijven terwijl een lid offline is.

## Hulp krijgen

Klik op de kleine **i** naast een veld voor uitleg. Klik erbuiten of druk op Escape om te sluiten. Lijsten staan in schuifbare vakken; scroll in het vak voor meer items. Zoeken filtert de zichtbare lijst zonder items te verwijderen.

Aangepaste regels hebben een eigen [Codemanual](../code-manual/nl.md). Daarin staan de editor, activering, logboeken, bestandstoegang en de ondersteunde API uitgelegd.

## Native apps

Gebruik de +-kiezer van een Apps-doel om geïnstalleerde apps te selecteren. **Alle apps behalve deze blokkeren** maakt van de lijst een toestemmingslijst. Systeemapps, browsers en Vault zelf worden uitgesloten van blokkering van native apps.

Een geblokkeerde app krijgt een verzoek om af te sluiten. **Een geblokkeerde app elke (minuten) opnieuw vragen af te sluiten** regelt de nieuwe pogingen. Websiteomleidingen, paginapauzes en het verbergen van feeds worden door de browserextensie afgedwongen; het zijn geen native app-acties.

## Inhoud taggen

Elke Content taggen-groep tagt inhoud van de toegewezen platforms met een eigen tagboom en modelinstellingen. Elk platform hoort bij één groep; kies platforms bij het maken, later wijzigen kan niet. Planningen en filters van blocking groups bepalen tagging niet. Schakel tagging in Instellingen in en gebruik **Taggen pauzeren / Taggen hervatten** per groep. Het uitschakelen van feedopname onder **Activiteit → Registratie** stopt ook tagging van die feed.

### Tags en modelinstellingen

Maak tags, beschrijf hun betekenis en stel ouders in de boom in of verwijder ze. Sleep een tag om de tak te verplaatsen. Duidelijke beschrijvingen helpen het model vergelijkbare tags onderscheiden. Instellingen zijn per groep onafhankelijk. **Minimale tagzekerheid** stelt het vereiste bewijsniveau in van 1 tot 5; nieuwe groepen beginnen op 4 (sterke steun). Er is geen doel voor het aantal tags; een item kan geen tags krijgen. **Taginstructies** bewaart groepsvoorkeuren. Gewone wijzigingen worden automatisch opgeslagen; elke groep gebruikt één expliciet gekozen verbinding, model en reasoning effort.

### AI-verbindingen

Instellingen biedt ChatGPT subscription, OpenAI API, Gemini API en Grok API. De ChatGPT-subscriptie gebruikt Vault-aanmelding en een geschikt plan. API-sleutels vallen onder facturering en limieten van de gekozen provider. Kies de provider en voer de sleutel direct onder de gedeelde keuzelijst in. Wijzigingen worden automatisch opgeslagen; het veld leegmaken verwijdert de sleutel. **Modellijst ophalen** haalt modellen op, maar kiest er geen; selecteer daarna een model in de Content taggen- of custom-rule-groep.

**Redeneerniveau** bepaalt hoeveel het model nadenkt: Model default, Low, Medium of High waar ondersteund. Nieuwe keuzes beginnen op Medium; bestaande blijven opgeslagen. Meer inspanning kan tijd en tokens kosten. Web search is voor tagging altijd ingeschakeld; het model bepaalt wanneer het die gebruikt. Kies een model dat zoeken en gestructureerde tagging ondersteunt. Mislukte of niet-beschikbare verzoeken behouden geldige cache-resultaten; nieuwe items blijven onopgelost tot **Cloudclassificatie opnieuw proberen**. Vault wisselt niet automatisch van provider. Elke verbinding heeft een eigen retry-interval en pogingslimiet. Corrigeer tags in de browserextensie met **+ tag**; verwijder via de verwijderknop of selecteer een tag en druk eenmaal op Delete. **Geen tag** betekent klaar zonder tags; **Taggen** betekent dat het resultaat wacht. Je correctie is leidend voor dat item.


## Activiteit

Activiteit legt lokaal gebruik van ingeschakelde apps, websitebezoeken en ondersteunde **Bekeken inhoud** vast. Grafieken tonen vastgelegde gegevens; een leeg gebied bewijst niet dat de PC inactief was. Kies een periode. **Tijdlijn** toont gebruik per tijdstip; **Totalen** telt duur op; **Tijdsinterval** bundelt gebruik in verticale blokken per interval. **Kleuren** is een klikbare legenda waarmee je een bron kiest om grafieken daarop te richten. Kies een dag om gebruik vanaf die dag te zien.

### Activiteit-groepen

Maak een groep om gekozen apps en websites samen in Usage te tonen. **Merge** gebruikt in heel Activiteit één naam en kleur voor leden. Een Activiteit-groep ordent vastgelegd gebruik en staat los van blocking- en tagging-groepen. Sla de editor expliciet op met **Save**.

### Registratie en bewaartermijn

Schakel onder **Registratie** opname per categorie of bron in of uit. **Bewaren** bepaalt de bewaartermijn; **Altijd** voorkomt automatische vervaldatum. Individuele keuzes kunnen de bredere instelling volgen. Opname uitschakelen stopt nieuwe opnamen; geschiedenis verwijderen wist vastgelegde items. Platformfeeds verzamelen inhoud die op ondersteunde pagina’s wordt getoond, ook als de pagina niet is geopend. Een feed met **Taggen ondersteund** kan Content taggen voeden zolang opname aanstaat. De bewaartermijn ervan staat los van app- en websitegebruik. Een tagging-groep pauzeren stopt opname niet vanzelf.

## Instellingen

Kies de interfacetaal in Instellingen. Veldverklaringen zijn beschikbaar via Info-knoppen in de gekozen taal.

## Opslaan en problemen oplossen

Gewone Vault- en Classifier-wijzigingen worden automatisch opgeslagen. Een Activity-groep bewerken vereist **Opslaan**. Toevoegen, verwijderen, een model downloaden, verbinding testen en een modellijst ophalen blijven expliciete acties.

Ontbreken tags, controleer dan de browserverbinding, algemene tagging-schakelaar, pauzestatus van de groep, platformregistratie en modeldownload. Werkt onderzoek niet, controleer toestemming, groepskeuze, providergegevens, onderzoeksmodel en onderzoeksstatus. Kan een gekoppelde groep niet worden bewerkt, verbind de programma's opnieuw of hef het bevriezen op zoals aangegeven.
