# Windows Vault Benutzerhandbuch

Windows Vault hat drei Seiten: **Vault** blockiert native Apps, **Klassifizierung** weist unterstützten Browserinhalten Tags zu und **Aktivität** zeigt aufgezeichnete Nutzung. Die Browsererweiterung erfasst unterstützte Inhalte und setzt die Browserblockierung durch. Installieren und verbinden Sie sie in dem Browser, den Sie verwenden.

## Schnellstart

1. Fügen Sie in **Vault** eine Blockierungsgruppe und ein Apps-Ziel hinzu und wählen Sie mit der + Auswahl Apps aus.
2. Wählen Sie das Blockierungsverhalten der Gruppe und aktivieren Sie sie.
3. Erstellen Sie in **Klassifizierung** eine Gruppe, wählen Sie ihre Plattformen und fügen Sie Tags mit Beschreibungen hinzu.
4. Wählen Sie eine lokale Modellstufe und laden Sie sie bei Bedarf herunter. Aktivieren Sie die Tag-Zuweisung in den Klassifizierungseinstellungen und setzen Sie die Gruppe fort.
5. Öffnen Sie unterstützte Inhalte im verbundenen Browser. Konfigurieren Sie einen Tag-Filter in einer Browser-Blockierungsgruppe, wenn die Tags die Blockierung steuern sollen.

## Blockierungsgruppen

Eine **Blockierungsgruppe** wendet eine Blockierungsrichtlinie an. Eine **Klassifizierungsgruppe** weist Inhalten Tags zu; sie blockiert selbst nichts.

1. Fügen Sie eine Blockierungsgruppe hinzu und geben Sie ihr einen Namen.
2. Wählen Sie unter **Gilt für** die Ziele aus.
3. Legen Sie fest, wann die Blockierung gilt, und stellen Sie gegebenenfalls einen Zeitplan oder ein Zeitkontingent ein.
4. Aktivieren Sie die Gruppe. Ihre Ziele verwenden die gemeinsame Richtlinie der Gruppe.

Gewöhnliche Änderungen werden automatisch gespeichert. Eine Fehlermeldung bedeutet, dass die Änderung nicht angenommen wurde; korrigieren Sie das Feld und versuchen Sie es erneut. Deaktivieren Sie eine Gruppe, um ihre Richtlinie auszusetzen und ihre Konfiguration zu behalten. **Gruppe löschen** entfernt sie. Ziehen Sie Gruppen, um ihre Reihenfolge zu ändern. Mehrere Gruppen können für dasselbe Ziel gelten; das Aufschieben einer Gruppe hebt die Blockierung einer anderen nicht auf.

**Exportieren** kopiert eine Gruppenkonfiguration. **Importieren** ersetzt nach Bestätigung die Konfiguration der ausgewählten Gruppe.

### Zeitkontingent und Zeitplan

**Sofort blockieren** gilt immer dann, wenn die aktivierte Gruppe zutrifft und ihr Zeitplan aktiv ist. **Nach Verbrauch des Zeitkontingents blockieren** erlaubt die betreffende Nutzung, bis das Kontingent aufgebraucht ist.

Stellen Sie das Kontingent in Minuten und das Rücksetzintervall in Stunden ein. Ein gleitendes Limit zählt die Nutzung im vorangegangenen Zeitfenster. Das Zurücksetzen um Mitternacht beginnt zur lokalen Mitternacht einen neuen Zeitraum, auch bei einem gleitenden Limit.

Wählen Sie aktive Wochentage und optional lokale Zeitfenster, eines pro Zeile, beispielsweise **09:00-12:00**. Eine leere Fensterliste gilt während der gesamten ausgewählten Tage. Ein Fenster muss am selben Tag später enden als beginnen; teilen Sie einen Zeitplan über Nacht auf getrennte Tage auf.

### Aufschieben

Konfigurieren Sie das Aufschieben für jede Blockierungsgruppe. **Blockierung pausieren** setzt die Richtlinie dieser Gruppe für die Pausendauer aus. **Zum Zeitkontingent hinzufügen** ergänzt nutzbare Minuten für eine zeitlich begrenzte Gruppe. Nur verbrauchtes Zusatzkontingent zählt als aufgeschobene Zeit. Ungenutztes Zusatzkontingent verfällt beim nächsten Zurücksetzen; bei einem gleitenden Limit nach einem Zeitfenster oder, falls aktiviert, bereits um Mitternacht.

**Aktivierungsverzögerung** verzögert das Aufschieben, während die Blockierung weiter gilt. **Wartezeit** ist die Zeit nach dem Ende des Aufschiebens bis zur nächsten Anfrage. **Erforderliche Bestätigungen** legt die Anzahl der Bestätigungsschritte fest. Für eine eingefrorene Gruppe ist Aufschieben nur verfügbar, wenn es vor dem Einfrieren erlaubt wurde.

### Einfrieren und PIN

**Einfrieren** verhindert gewöhnliche Änderungen. Zum Auftauen sind zehn Bestätigungen im Abstand von fünf Sekunden sowie eine gegebenenfalls konfigurierte Wartezeit und sechsstellige PIN erforderlich. **Wartezeit vor dem Auftauen** akzeptiert 0–72 Stunden; 0 fügt keine Wartezeit hinzu.

Im eingefrorenen Zustand kann die Wartezeit verlängert und eine PIN hinzugefügt werden, sofern noch keine vorhanden ist. Diese Bedingungen lassen sich erst nach dem Auftauen der Gruppe abschwächen. Auch das Löschen berücksichtigt die verbleibende Wartezeit und die PIN.

### Verknüpfte Gruppen

Verwenden Sie **Verknüpfen**, um ausdrücklich ausgewählte Gruppen in anderen Vault-Programmen zu verbinden. Verknüpfte Gruppen teilen ihren Namen, unterstützte Richtlinieneinstellungen, Ziele, Nutzung und Einfrierbedingungen. Jedes Programm bearbeitet und setzt die von ihm unterstützten Zieltypen durch; andere Zieleinträge bleiben für verknüpfte Programme verfügbar. Beim Aufheben der Verknüpfung bleiben jede Gruppe und ihre Einstellungen erhalten.

Ist ein verknüpftes Mitglied offline, kann die Bearbeitung nicht verfügbar sein. Öffnen Sie Windows Vault und den verknüpften Browser, um die Verbindung wiederherzustellen. Eine lokal gespeicherte Richtlinie kann weiter gelten, während ein Mitglied offline ist.

## Hilfe erhalten

Klicken Sie auf das kleine **i** neben einem Feld, um dessen Erklärung anzuzeigen. Klicken Sie außerhalb der Erklärung oder drücken Sie Escape, um sie zu schließen. Listen bleiben in scrollbaren Bereichen; scrollen Sie innerhalb des Bereichs, um weitere Einträge zu erreichen. Die Suche filtert die sichtbare Liste, ohne Einträge zu löschen.

Benutzerdefinierte Regeln haben ein eigenes [Codehandbuch](../code-manual/de.md). Es erklärt den Editor, die Aktivierung, Protokolle, den Dateizugriff und die unterstützte API.

## Native Apps

Verwenden Sie die + Auswahl eines Apps-Ziels, um installierte Apps auszuwählen. **Alle Apps außer diesen blockieren** macht die Liste zu einer Freigabeliste. System-Apps, Browser und Vault selbst sind von der nativen App-Blockierung ausgeschlossen.

Eine blockierte App wird zum Beenden aufgefordert. **Eine blockierte App erneut zum Beenden auffordern alle (Minuten)** steuert Wiederholungen. Websiteweiterleitungen, Seitenpausen und das Verbergen von Feed-Inhalten werden von der Browsererweiterung durchgesetzt; sie werden nicht zu nativen App-Aktionen.

## Inhaltskennzeichnung

Eine Content-tagging-Gruppe versieht Inhalte ihrer Plattformen mit einem eigenen Tag-Baum und Modelleinstellungen. Jede Plattform gehört zu einer Gruppe; wählen Sie sie bei der Erstellung, später kann sie nicht geändert werden. Blocking-Zeitpläne und Filter steuern kein Tagging. Aktivieren Sie Tagging in Einstellungen und pausieren/fortsetzen Sie jede Gruppe separat. Wird die Feed-Aufzeichnung unter **Aktivität → Aufzeichnung** ausgeschaltet, stoppt auch das Tagging.

### Tags und Modelleinstellungen

Erstellen Sie Tags, beschreiben Sie ihre Bedeutung und setzen oder entfernen Sie Eltern im Baum. Ziehen verschiebt einen Zweig. Klare Beschreibungen helfen dem Modell, ähnliche Tags zu unterscheiden. **Mindestkonfidenz für Tags** setzt die Evidenzstufe von 1 bis 5; neue Gruppen beginnen bei 4 (starke Unterstützung). Es gibt keine Zielanzahl; ein Element kann ohne Tags bleiben. **Anweisungen zum Taggen** speichert Gruppenpräferenzen. Änderungen werden automatisch gespeichert; jede Gruppe verwendet ausdrücklich eine Verbindung, ein Modell und einen Reasoning-Aufwand.

### AI-Verbindungen

Unter **KI-Verbindungen** stehen ChatGPT subscription, OpenAI API, Gemini API und Grok API. Das Abo verwendet Vault-Anmeldung und einen berechtigten Tarif. API-Schlüssel folgen Abrechnung und Limits des Anbieters. Anbieter wählen und Schlüssel unter der gemeinsamen Liste eingeben; Änderungen speichern automatisch, Leeren löscht den Schlüssel. **Modellliste abrufen** und danach ein Modell in der Tagging- oder Regelgruppe wählen; der Abruf wählt keines aus. **Denkaufwand** bietet Standard, Low, Medium oder High, soweit unterstützt. Neue Auswahl beginnt mit Medium, vorhandene bleibt gespeichert. Höherer Aufwand kann Zeit und Tokens erhöhen. Websuche ist immer aktiviert; das Modell wählt ihren Einsatz. Bei Fehlern bleiben gültige Cache-Ergebnisse erhalten; neue Inhalte bleiben ungelöst bis **Cloud-Klassifizierung erneut versuchen**. Vault wechselt den Anbieter nicht automatisch; jede Verbindung hat eigenes Intervall und Versuchslimit. Korrigieren Sie Tags in der Erweiterung mit **+ tag**; Entfernen-Steuerung oder einmal Delete entfernt sie. **Ohne Tag** bedeutet abgeschlossen ohne Tags, **Tag-Zuweisung läuft** bedeutet ausstehend. Ihre Korrektur bleibt maßgeblich.


## Aktivität

Aktivität zeichnet die Nutzung aktivierter Apps, Websitebesuche und unterstützte **Angesehene Inhalte** lokal auf. Die Diagramme zeigen aufgezeichnete Daten; ein leerer Bereich beweist nicht, dass der PC untätig war. Wählen Sie einen Zeitraum. **Zeitachse** zeigt Nutzung nach Tageszeit, **Summen** summiert Dauer, **Zeitintervall** bündelt die Nutzung jedes Intervalls in vertikalen Blöcken. **Farben** ist eine anklickbare Legende: Wählen Sie eine Quelle, um die Diagramme darauf zu fokussieren. Wählen Sie einen Tag, um Nutzung ab diesem Tag anzuzeigen.

### Aktivität-Gruppen

Erstellen Sie eine Gruppe, um ausgewählte Apps und Websites gemeinsam unter Usage anzuzeigen. **Merge** verwendet für Mitglieder überall in Aktivität einen Namen und eine Farbe. Aktivität-Gruppen ordnen aufgezeichnete Nutzung und sind von Blocking- und Content-tagging-Gruppen getrennt. Speichern Sie den Editor ausdrücklich mit **Save**.

### Aufzeichnung und Aufbewahrung

Unter **Aufzeichnung** Aufzeichnung je Kategorie oder Quelle schalten. **Aufbewahren** bestimmt die Aufbewahrung, **Für immer** läuft nicht ab. Ausschalten stoppt neue Aufzeichnungen; Löschen entfernt Historie. Plattform-Feeds sammeln angezeigte Inhalte unterstützter Seiten, unabhängig davon, ob sie geöffnet wurden. **Tagging unterstützt**-Feeds können bei aktiver Aufzeichnung Tagging speisen; die Aufbewahrung ist von App- und Webnutzung getrennt. Eine pausierte Tagging-Gruppe stoppt die Aufzeichnung nicht.

## Einstellungen

Wählen Sie unter Einstellungen die Oberflächensprache; Info-Schaltflächen zeigen lokalisierte Erklärungen.

## Speichern und Fehlerbehebung

Gewöhnliche Vault- und Klassifizierungsänderungen werden automatisch gespeichert. Die Bearbeitung von Aktivitätsgruppen verwendet **Speichern**. Hinzufügen, Löschen, Modelldownload, Verbindungstest und Abruf einer Modellliste bleiben ausdrückliche Aktionen.

Fehlen Tags, prüfen Sie die Browserverbindung, den globalen Tag-Schalter, den Pausenstatus der Gruppe, die Plattformaufzeichnung und den Modelldownload. Läuft Recherche nicht, prüfen Sie Einwilligung, Gruppenauswahl, Anbieteranmeldedaten, Recherchemodell und Recherchestatus. Lässt sich eine verknüpfte Gruppe nicht bearbeiten, verbinden Sie ihre Programme erneut oder tauen Sie sie wie angezeigt auf.
