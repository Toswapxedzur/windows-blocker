# Manual do usuário Windows Vault

Windows Vault tem três páginas: **Vault** bloqueia aplicativos native, **Classifier** marca conteúdo compatível do navegador e **Activity** mostra o uso registrado. A extensão coleta conteúdo compatível e aplica bloqueio no navegador. Instale e conecte-a no navegador que você usa.

## Início rápido

1. Em **Vault**, adicione um grupo de bloqueio e um alvo Apps; depois selecione aplicativos com o seletor +.
2. Escolha o comportamento de bloqueio do grupo e ative-o.
3. Em **Classifier**, crie um grupo, escolha as plataformas e adicione tags com descrições.
4. Escolha um nível de modelo local e baixe-o se necessário. Ative a marcação nas configurações do Classifier e retome o grupo.
5. Abra conteúdo compatível no navegador conectado. Configure um filtro de tags em um grupo de bloqueio do navegador se quiser que as tags controlem o bloqueio.

## Grupos de bloqueio

Um **grupo de bloqueio** aplica uma política de bloqueio. Um **grupo Classifier** atribui tags ao conteúdo; ele não bloqueia nada por conta própria.

1. Adicione um grupo de bloqueio e dê um nome a ele.
2. Escolha alvos em **Aplicar a**.
3. Escolha quando o bloqueio se aplica e defina um cronograma ou tempo permitido, se quiser.
4. Ative o grupo. Os alvos compartilham a política do grupo.

As edições comuns são salvas automaticamente. Um erro significa que a edição não foi aceita; corrija o campo e tente novamente. Desative um grupo para suspender sua política sem perder a configuração. **Excluir grupo** o remove. Arraste os grupos para reordená-los. Mais de um grupo pode se aplicar ao mesmo alvo; adiar um não remove o bloqueio de outro.

**Exportar** copia a configuração de um grupo. **Importar** substitui a configuração do grupo selecionado após a confirmação.

### Tempo permitido e cronograma

**Bloquear imediatamente** aplica-se sempre que o grupo ativo corresponder e seu cronograma estiver em vigor. **Bloquear quando o tempo permitido for usado** permite o uso correspondente até o tempo acabar.

Defina o tempo permitido em minutos e o intervalo de redefinição em horas. Um limite móvel conta o uso na janela anterior. A redefinição à meia-noite inicia um novo período à meia-noite local, inclusive para um limite móvel.

Escolha os dias da semana ativos e intervalos opcionais do horário local, um por linha, como **09:00-12:00**. Sem intervalos, aplica-se durante todos os dias selecionados. Um intervalo deve terminar depois de começar no mesmo dia; divida um cronograma noturno em dias separados.

### Adiar

Configure o adiamento em cada grupo de bloqueio. **Pausar bloqueio** suspende a política do grupo durante o período definido. **Adicionar ao tempo permitido** acrescenta minutos utilizáveis a um grupo limitado por tempo. Só o tempo adicional consumido conta como tempo adiado. O tempo adicional não usado expira na próxima redefinição; em um limite móvel, expira após uma janela ou antes, à meia-noite, se a opção estiver ativada.

**Atraso de ativação** adia o pedido de adiamento enquanto o bloqueio continua. **Intervalo de espera** é o tempo após o fim do adiamento até que outro pedido possa ser feito. **Confirmações necessárias** define o número de etapas de confirmação. O adiamento fica disponível para um grupo congelado somente se permitido antes do congelamento.

### Congelar e PIN

**Congelar** impede edições comuns. Para descongelar, são necessárias dez confirmações com cinco segundos de intervalo, além da espera configurada e de um PIN de seis dígitos. **Aguardar antes de descongelar** aceita 0–72 horas; 0 não acrescenta espera.

Enquanto estiver congelado, é possível prolongar a espera e adicionar um PIN se ainda não houver um. Essas condições não podem ser flexibilizadas até o grupo ser descongelado. A exclusão também respeita a espera restante e o PIN.

### Grupos vinculados

Use **Vincular** para conectar grupos explicitamente selecionados em outros programas Vault. Grupos vinculados compartilham o nome, as configurações de política compatíveis, os alvos, o uso e as condições de congelamento. Cada programa edita e aplica os tipos de alvo compatíveis; outras entradas de alvo continuam disponíveis aos programas vinculados. Desvincular mantém cada grupo e suas configurações.

Se um membro vinculado estiver offline, talvez não seja possível editá-lo. Abra Windows Vault e o navegador vinculado para reconectar. Uma política salva localmente pode continuar ativa enquanto o membro estiver offline.

## Ajuda

Clique no pequeno **i** ao lado de um campo para ver a explicação. Clique fora dele ou pressione Escape para fechar. As listas ficam em caixas roláveis; role a caixa para ver mais itens. A pesquisa filtra a lista visível sem excluir itens.

As regras personalizadas têm um [Manual de código](../code-manual/pt.md) próprio. Ele explica o editor, a ativação, os registros, o acesso a arquivos e a API compatível.

## Aplicativos native

Use o seletor + de um alvo Apps para escolher aplicativos instalados. **Bloquear todos os aplicativos, exceto estes** transforma a lista em uma allowlist. Aplicativos de sistema, navegadores e o próprio Vault são excluídos do bloqueio native de aplicativos.

Um aplicativo bloqueado recebe um pedido para encerrar. **Pedir que o aplicativo bloqueado encerre novamente a cada (minutos)** controla as tentativas. Redirecionamentos de sites, pausas de páginas e ocultação de feeds são aplicados pela extensão do navegador; não se tornam ações de aplicativos native.

## Marcação de conteúdo

Cada Marcação de conteúdo marca conteúdo das plataformas atribuídas com sua própria árvore de tags e configurações de modelo. Cada plataforma pertence a um grupo; escolha ao criar, pois não poderá mudar depois. Agendas e filtros de bloqueio não controlam tagging. Ative tagging em Configurações e use **Pausar classificação / Retomar classificação** separadamente por grupo. Desligar a gravação de um feed em **Atividade → Gravação** também interrompe seu tagging.

### Tags e configurações do modelo

Crie tags, descreva seus significados e defina/remova pais na árvore. Arrastar move o ramo. Descrições claras ajudam o modelo a distinguir tags parecidas. As configurações são independentes por grupo. **Confiança mínima da tag** define o grau de evidência exigido, de 1 a 5; grupos novos começam em 4 (suporte forte). Não há quantidade-alvo; um item pode ficar sem tags. **Instruções de marcação** guarda preferências do grupo. Edições comuns salvam automaticamente; cada grupo usa conexão, modelo e reasoning effort escolhidos explicitamente.

### Conexões de IA

Configurações oferece ChatGPT subscription, OpenAI API, Gemini API e Grok API. A assinatura usa o login do Vault e um plano elegível. Chaves API seguem a cobrança e os limites do provedor escolhido. Selecione o provedor e digite a chave logo abaixo do menu compartilhado; alterações salvam automaticamente e limpar o campo apaga a chave. **Buscar lista de modelos** busca modelos, mas não escolhe um; selecione-o no grupo Marcação de conteúdo ou custom-rule.

**Nível de raciocínio** controla quanto o modelo pensa: padrão do modelo, Low, Medium ou High quando compatível. Novas escolhas começam em Medium; as existentes permanecem salvas. Mais esforço pode aumentar tempo e tokens. A busca na Web está sempre ativa para tagging; o modelo decide quando usá-la. Escolha modelo compatível com busca e tagging estruturado. Pedidos falhos/indisponíveis preservam resultados válidos em cache; itens novos ficam sem resolução até escolher **Tentar classificação na nuvem novamente**. O Vault não troca de provedor automaticamente; cada conexão tem intervalo e limite de tentativas próprios. Corrija tags na extensão com **+ tag**; use remover ou selecione e pressione Delete uma vez. **Sem tag** significa concluído sem tags; **Classificando…** significa resultado pendente. Sua correção é a referência daquele item.


## Atividade

Atividade registra localmente uso de apps habilitados, visitas a sites e **Conteúdo visualizado** compatível. Os gráficos mostram dados registrados; espaço vazio não prova que o PC estava ocioso. Escolha um intervalo. **Linha do tempo** mostra horários do dia, **Totais** soma duração e **Intervalo de tempo** agrupa uso em blocos verticais por intervalo. **Cores** é legenda clicável para focar uma fonte. Escolha um dia para ver uso a partir dele.

### Grupos Atividade

Crie um grupo para exibir apps e sites selecionados juntos em Usage. **Merge** usa um nome e cor únicos para os membros em Atividade. Um grupo Atividade organiza uso registrado e é separado de grupos de bloqueio e tagging. Salve o editor explicitamente com **Save**.

### Gravação e retenção

Em **Gravação**, ligue/desligue a gravação por categoria ou fonte. **Manter** define quanto tempo o histórico fica guardado; **Sempre** não expira automaticamente. Escolhas individuais podem seguir a configuração mais ampla. Desligar a gravação impede novos registros; apagar o histórico remove entradas gravadas. Feeds de plataforma coletam conteúdo exibido em páginas compatíveis, abertas ou não. Um feed **Marcação compatível** pode alimentar Marcação de conteúdo enquanto gravado. A retenção é separada do uso de apps e sites. Pausar um grupo de tagging não desliga a gravação.

## Configurações

Escolha o idioma da interface em Configurações. Explicações dos campos aparecem nos botões Info no idioma selecionado.

## Salvamento e solução de problemas

Edições comuns do Vault e Classifier são salvas automaticamente. A edição de grupo Activity usa **Salvar**. Adicionar, excluir, baixar um modelo, testar uma conexão e buscar a lista de modelos continuam sendo ações explícitas.

Se faltarem tags, confira a conexão do navegador, o controle global de marcação, o estado de pausa do grupo, o registro da plataforma e o download do modelo. Se a pesquisa não funcionar, confira o consentimento, a escolha do grupo, as credenciais do provedor, o modelo de pesquisa e o status. Se não for possível editar um grupo vinculado, reconecte os programas ou descongele-o conforme indicado.
