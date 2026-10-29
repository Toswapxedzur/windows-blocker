# Manuale utente Windows Vault

Windows Vault ha tre pagine: **Vault** blocca le app native, **Classifier** applica tag ai contenuti browser supportati e **Activity** mostra l'uso registrato. L'estensione browser raccoglie i contenuti supportati e applica i blocchi nel browser. Installala e connettila nel browser che usi.

## Avvio rapido

1. In **Vault**, aggiungi un gruppo di blocco e un obiettivo Apps, poi seleziona le app con il selettore +.
2. Scegli il comportamento di blocco del gruppo e attivalo.
3. In **Classifier**, crea un gruppo, scegli le piattaforme e aggiungi tag con descrizioni.
4. Scegli un livello di modello locale e scaricalo se necessario. Attiva l'applicazione dei tag nelle impostazioni di Classifier e riprendi il gruppo.
5. Apri contenuti supportati nel browser collegato. Configura un filtro tag nel gruppo di blocco del browser se vuoi che i tag controllino il blocco.

## Gruppi di blocco

Un **gruppo di blocco** applica una policy di blocco. Un **gruppo Classifier** assegna tag ai contenuti; da solo non blocca nulla.

1. Aggiungi un gruppo di blocco e assegnagli un nome.
2. Scegli gli obiettivi in **Si applica a**.
3. Scegli quando applicare il blocco, poi imposta un programma o un tempo consentito, se necessario.
4. Attiva il gruppo. I suoi obiettivi condividono la policy del gruppo.

Le modifiche ordinarie si salvano automaticamente. Un errore indica che la modifica non è stata accettata; correggi il campo e riprova. Disattiva un gruppo per sospenderne la policy mantenendo la configurazione. **Elimina gruppo** lo rimuove. Trascina i gruppi per riordinarli. Più gruppi possono applicarsi allo stesso obiettivo; posticiparne uno non rimuove il blocco di un altro.

**Esporta** copia la configurazione di un gruppo. **Importa** sostituisce la configurazione del gruppo selezionato dopo la conferma.

### Tempo consentito e programma

**Blocca subito** si applica quando il gruppo attivo corrisponde e il programma è in corso. **Blocca quando si esaurisce il tempo consentito** permette l'uso corrispondente finché il tempo consentito non termina.

Imposta il tempo consentito in minuti e l'intervallo di ripristino in ore. Un limite mobile conteggia l'uso nella finestra temporale precedente. Il ripristino a mezzanotte avvia un nuovo periodo alla mezzanotte locale, anche per un limite mobile.

Scegli i giorni attivi della settimana e gli intervalli di ora locale facoltativi, uno per riga, ad esempio **09:00-12:00**. Senza intervalli, il gruppo si applica per tutti i giorni selezionati. Un intervallo deve terminare più tardi rispetto all'inizio nello stesso giorno; dividi un programma notturno su giorni diversi.

### Posticipa

Configura il posticipo in ogni gruppo di blocco. **Sospendi il blocco** sospende la policy del gruppo per la durata impostata. **Aggiungi al tempo consentito** aggiunge minuti utilizzabili a un gruppo con limite temporale. Solo il tempo aggiuntivo consumato viene conteggiato come posticipo. Il tempo aggiuntivo non utilizzato scade al ripristino successivo; per un limite mobile scade dopo una finestra, o prima a mezzanotte se l'opzione è attiva.

**Ritardo di attivazione** posticipa il rinvio mentre il blocco continua. **Intervallo di attesa** indica quanto attendere dalla fine del posticipo prima di richiederlo di nuovo. **Conferme richieste** imposta il numero di passaggi di conferma. Il posticipo è disponibile per un gruppo congelato solo se consentito prima del congelamento.

### Congelamento e PIN

**Congela** impedisce le modifiche ordinarie. Per scongelare servono dieci conferme a distanza di cinque secondi, oltre all'eventuale attesa configurata e a un PIN di sei cifre. **Attendi prima di scongelare** accetta 0–72 ore; 0 non aggiunge attesa.

Mentre il gruppo è congelato, l'attesa può essere prolungata e si può aggiungere un PIN se non è già presente. Queste condizioni non possono essere allentate finché il gruppo non viene scongelato. Anche l'eliminazione rispetta l'attesa restante e il PIN.

### Gruppi collegati

Usa **Collega** per connettere gruppi selezionati esplicitamente in altri programmi Vault. I gruppi collegati condividono nome, impostazioni di policy supportate, obiettivi, utilizzo e condizioni di congelamento. Ogni programma modifica e applica i tipi di obiettivo supportati; le altre voci restano disponibili ai programmi collegati. Scollegare mantiene ciascun gruppo e le sue impostazioni.

Se un membro collegato è offline, potrebbe non essere possibile modificarlo. Apri Windows Vault e il browser collegato per riconnetterti. Una policy salvata localmente può continuare ad applicarsi quando il membro è offline.

## Assistenza

Fai clic sulla piccola **i** accanto a un campo per leggerne la spiegazione. Fai clic all'esterno o premi Escape per chiuderla. Gli elenchi restano in riquadri scorrevoli; scorri il riquadro per visualizzare altre voci. La ricerca filtra l'elenco visibile senza eliminare voci.

Le regole personalizzate hanno un [Manuale del codice](../code-manual/it.md) dedicato, che spiega l'editor, l'attivazione, i log, l'accesso ai file e l'API supportata.

## App native

Usa il selettore + di un obiettivo Apps per scegliere le app installate. **Blocca tutte le app tranne queste** trasforma l'elenco in una lista consentita. App di sistema, browser e Vault stesso sono esclusi dal blocco delle app native.

A un'app bloccata viene richiesto di chiudersi. **Richiedi nuovamente la chiusura dell'app bloccata ogni (minuti)** controlla i tentativi. Reindirizzamenti dei siti, pause delle pagine e occultamento dei feed sono applicati dall'estensione browser; non diventano azioni dell'app nativa.

## Etichettatura dei contenuti

Il gruppo assegna tag ai contenuti delle piattaforme associate con il proprio albero tag e le impostazioni del modello. Ogni piattaforma appartiene a un gruppo; sceglila alla creazione, non si può cambiare dopo. Calendari e filtri di blocco non controllano il tagging. Attivalo in Impostazioni e usa **Sospendi i tag / Riprendi i tag** separatamente per ogni gruppo. Disattivare la registrazione del feed in **Attività → Registrazione** ferma anche il suo tagging.

### Tag e impostazioni del modello

Crea tag, descrivine il significato e imposta o rimuovi i genitori nell’albero; trascinare sposta il ramo. Descrizioni chiare aiutano il modello a distinguere tag simili. Le impostazioni di ogni gruppo sono indipendenti. **Confidence minima del tag** imposta il grado di prova richiesto per ogni tag previsto da 1 a 5; i nuovi gruppi iniziano a 4 (supporto forte). Non c’è un numero obiettivo; un elemento può non ricevere tag. **Istruzioni per i tag** conserva le preferenze del gruppo. Le modifiche ordinarie si salvano automaticamente; ogni gruppo usa una connessione, un modello e un reasoning effort selezionati esplicitamente.

### Connessioni IA

Impostazioni offre ChatGPT subscription, OpenAI API, Gemini API e Grok API. L’abbonamento usa l’accesso Vault e un piano idoneo. Le chiavi API seguono fatturazione e limiti del provider selezionato. Scegli il provider e inserisci la chiave sotto il menu condiviso; le modifiche si salvano automaticamente e svuotare il campo elimina la chiave. **Recupera elenco modelli** recupera i modelli, ma non ne sceglie uno: selezionalo nel gruppo Tag dei contenuti o custom-rule.

**Livello di ragionamento** regola quanto pensa il modello: Model default, Low, Medium o High se supportati. Le nuove scelte iniziano a Medium; quelle esistenti restano salvate. Un livello maggiore può aumentare tempo e token. La ricerca Web è sempre attiva per il tagging; il modello decide quando usarla. Scegli un modello che supporti ricerca e tagging strutturato. Le richieste fallite o non disponibili mantengono risultati validi in cache; i nuovi elementi restano irrisolti finché non scegli **Riprova la classificazione cloud**. Vault non cambia provider automaticamente; ogni connessione ha intervallo di retry e limite di tentativi propri. Correggi i tag nell’estensione: **+ tag**, ricerca e scelta per aggiungere; controllo di rimozione o Delete una volta per eliminare. **Senza tag** significa tagging completato senza tag; **Assegnazione tag in corso** indica risultato in attesa. La correzione è autorevole per quell’elemento.


## Attività

Attività registra localmente uso delle app abilitate, visite ai siti e **Contenuti visualizzati** supportato. I grafici riflettono i dati registrati; uno spazio vuoto non prova che il PC fosse inattivo. Scegli un intervallo di date. **Sequenza temporale** mostra l’ora del giorno, **Totali** somma le durate e **Intervallo di tempo** raggruppa l’uso di ciascun intervallo in blocchi verticali. **Colori** è una legenda cliccabile; seleziona una fonte per focalizzare i grafici. Scegli un giorno per vedere l’uso da quel giorno.

### Gruppi Attività

Crea un gruppo per mostrare insieme in Usage app e siti scelti. **Merge** usa un solo nome e colore per i membri in Attività. Il gruppo organizza l’uso registrato ed è distinto dai gruppi di blocco e Tag dei contenuti. Salva esplicitamente l’editor con **Save**.

### Registrazione e conservazione

In **Registrazione**, attiva o disattiva la registrazione per categoria o singola fonte. **Conserva** determina per quanto conservare la cronologia; **Per sempre** non prevede scadenza automatica. Le scelte individuali possono seguire l’impostazione più ampia. Disattivare la registrazione ferma i nuovi dati; eliminare la cronologia rimuove le voci registrate. I feed raccolgono contenuti mostrati nelle pagine supportate, aperte o meno. Un feed **Tag supportati** può fornire dati a Tag dei contenuti mentre la registrazione è attiva. La sua conservazione è separata dall’uso di app e siti. Mettere in pausa il gruppo di tagging non arresta la registrazione.

## Impostazioni

Scegli la lingua dell’interfaccia in Impostazioni. I pulsanti Info mostrano le spiegazioni dei campi nella lingua selezionata.

## Salvataggio e risoluzione dei problemi

Le modifiche ordinarie di Vault e Classifier si salvano automaticamente. Modificare un gruppo Activity richiede **Salva**. Aggiungere, eliminare, scaricare un modello, verificare una connessione e recuperare un elenco di modelli sono azioni esplicite.

Se mancano tag, controlla la connessione browser, l'interruttore globale di tagging, lo stato di pausa del gruppo, la registrazione della piattaforma e il download del modello. Se la ricerca non parte, controlla consenso, gruppo scelto, credenziali provider, modello di ricerca e stato. Se non puoi modificare un gruppo collegato, riconnetti i programmi o scongelalo come indicato.
