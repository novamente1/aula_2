# ESCOPO INTEGRAL ATUALIZADO — ORGANIZA

## Complemento V2.19.0 — processamento de alto volume

A seleção de uma pasta grande não deve calcular SHA-256 de toda a árvore nem bloquear a interface. O inventário, a validação contextual e a auditoria leve devem executar em segundo plano. A confirmação integral de duplicidade pertence à Etapa 2 e deve calcular hash apenas para arquivos com tamanho repetido, com progresso visível e concorrência limitada. A leitura de conteúdo e a validação do cache podem processar dois documentos simultaneamente, preservando a ordem de entrada. Listas extensas devem usar virtualização e atualização em lote. A enumeração deve ignorar diretórios momentaneamente inacessíveis sem interromper toda a árvore.

## Complemento V2.18.6 — atalhos nativos do Google Workspace

Arquivos `.gdoc`, `.gsheet`, `.gslides` e equivalentes são ponteiros do Google Drive, não documentos físicos locais. O Organiza deve identificá-los, informá-los na auditoria e excluí-los de OCR, hash, duplicidade, renomeação e Livro Mestre. A presença desses ponteiros nunca pode impedir a seleção da pasta. Para análise de conteúdo, o operador deve exportar o item para um formato físico como PDF, DOCX ou XLSX.

## Complemento V2.18.5 — aplicação da estrutura oficial do imóvel

Ao confirmar `APLICAR NOMES APROVADOS` no modo de estrutura padrão, o Organiza deve criar ou reutilizar as quatro áreas operacionais oficiais e as dez pastas numeradas do modelo de imóvel. Essas pastas fazem parte do modelo validado e devem permanecer mesmo quando vazias. A consolidação de nomes equivalentes deve preservar o conteúdo e registrar cada movimentação.

## Complemento V2.18.4 — bloqueio de aplicação explicável

- A aplicação física continua exigindo aprovação individual de todos os documentos não duplicados.
- O comando de aplicação não pode parecer quebrado quando houver itens pendentes: deve informar a quantidade restante e explicar que o ✓ cinza precisa ficar verde.
- Tentar aplicar com conferência incompleta não altera nenhum arquivo.

## Complemento V2.18.3 — identidade semântica do documento

- O título, o cabeçalho e a função principal comprovada do documento prevalecem sobre palavras citadas na narrativa ou em anexos.
- Menções incidentais a sentença, contrato, certidão, Renajud, CRLV, carta ou auto de arrematação não podem definir sozinhas a espécie documental.
- Consultas SENATRAN são pesquisa; demonstrativos de débitos e IPVA são financeiros; peças, notificações e respostas a ofício judicial são jurídicas.
- Quando a identidade principal não puder ser comprovada, o nome atual é preservado e o item segue desmarcado para revisão humana.

## Complemento V2.18.2 — barreira final do nome

- Nenhuma sugestão pode ser considerada pronta se não contiver descrição documental compreensível.
- Número, fragmento de processo e data, isoladamente, não identificam um documento.
- A barreira final valida o resultado depois da classificação, do protocolo oficial e da análise de contexto.
- Sugestão inválida preserva o nome atual, fica desmarcada e recebe `REVISAO-NOME-INCOMPLETO` em `97 REVISAR QUALIDADE`.
- O formato humano e legível pela IA permanece `NN - TIPO/DESCRIÇÃO - ID PRINCIPAL - DATA.ext`.

## Complemento V2.18.1 — identidade principal e prioridade documental

- A ocorrência de `RG`, `CPF`, `IPTU` ou `débito` dentro de uma peça não define sozinha o tipo do documento.
- RG exige cabeçalho inequívoco de carteira de identidade ou combinação de campos próprios, como filiação, naturalidade, nascimento e órgão expedidor.
- Cabeçalhos judiciais, contexto processual e identidade explícita da peça prevalecem sobre qualificações das partes e assuntos apenas mencionados.
- Demonstrativos, consultas de débito, comprovantes e guias mantêm classificação financeira quando essa for a identidade principal comprovada.
- `Refazer` continua sendo uma releitura integral sem cache, com progresso documento a documento; sua duração maior é esperada e evita reaproveitar uma leitura possivelmente reprovada.

## Complemento V2.17 — mesa de conferência humana

- A barra lateral é compacta para priorizar a área útil do documento.
- A grade principal exibe somente: original clicável, nome sugerido editável, ações de conferência, revisão resumida e pasta oficial.
- Aplicar, qualidade, protocolo, regra, tipo, origem e vínculo técnico da cópia continuam no modelo interno, catálogo e histórico, sem poluir a mesa principal.
- Antes da análise, a coluna de sugestão informa `Clique em GERAR NOMES`; o nome original não é apresentado como se fosse sugestão.
- O nome sugerido é azul e editável diretamente; qualquer edição reinicia a aprovação daquela linha e será aplicada somente na confirmação final.
- Abrir é azul, aprovar é verde e refazer é laranja, com controles compactos.
- Descrições automáticas são limitadas a 48 caracteres e termos extensos recebem formas funcionais curtas.
- Documento de veículo prioriza RENAVAM como ID principal e placa como secundário; peça judicial prioriza processo; IPTU prioriza cadastro; documento registral prioriza matrícula.

## Complemento V2.16 — protocolo NN e índice de mapeamento

- O nome aprovado segue `NN - DESCRIÇÃO_CURTA - ID_PRINCIPAL - ID_SECUNDÁRIO.ext`.
- `NN` é sequencial por pasta de destino; uma numeração válida já existente é preservada para evitar renomeações repetidas.
- IDs são extraídos somente do conteúdo: processo, CPF, CNPJ, cadastro de IPTU, matrícula ou placa, conforme disponibilidade.
- Linha digitável e código de barras nunca são usados como ID principal.
- ID ausente é omitido e sinalizado como protocolo parcial; `INDEFINIDO` e outros campos artificiais são proibidos.
- Depois da aplicação explicitamente confirmada, o lote atualiza `INDICE_MAPEAMENTO.txt` em UTF-8 com BOM.
- Em uma Nova Raiz, o índice fica em `00 TRIAGEM/06 LOGs`. Em um dossiê isolado, fica em `.organiza`, sem criar uma falsa estrutura de portfólio.
- O índice contém `ID_PRINCIPAL | TIPO | NOME_RENOMEADO | PASTA_ATUAL` e não provoca nova leitura nem movimentação.

## Complemento V2.15.1 — visualização gratuita e isolada

- PDFs, imagens e textos compatíveis abrem em um perfil local exclusivo do navegador, sem extensões, evitando que o Adobe intercepte a consulta e apresente oferta de compra.
- Se não houver navegador compatível, o PDF não será encaminhado automaticamente ao Adobe; o operador receberá uma explicação em português.
- DOC/DOCX e planilhas tentam abrir diretamente no LibreOffice instalado antes da associação padrão do Windows.
- A visualização é somente uma consulta ao original; o Organiza não modifica o documento ao abri-lo.

## Complemento V2.15 — duplicidade, qualidade de leitura e protocolo do nome

- A indicação textual `Cópia` no nome não comprova duplicidade; cópia digital exata exige tamanho igual e SHA-256 integral igual.
- Na Etapa 3, os documentos principais aparecem primeiro; cópias exatas ficam ao final, desmarcadas, dispensadas da revisão de nome e vinculadas ao principal.
- Cópias exatas continuam sendo tratadas exclusivamente na Etapa 2 e nunca são excluídas automaticamente.
- OCR sem evidência documental suficiente não pode inferir tipo ou destino pelo nome legado. O nome existente é preservado e o item recebe `REVISAO-QUALIDADE-OCR`.
- `97 REVISAR QUALIDADE` é uma área operacional opcional: não integra o modelo obrigatório e só é criada após aprovação explícita de um item de baixa legibilidade.
- Cada sugestão informa a qualidade da leitura e o estado do protocolo oficial de nomenclatura.
- O protocolo usa somente dados comprovados: data do conteúdo quando localizada, descrição, identificadores disponíveis e extensão preservada; campos ausentes nunca são inventados.
- Não existe ação de exclusão na conferência.

## Complemento V2.14.1 — consulta rápida independente do Adobe

- O botão `Abrir` usa Microsoft Edge ou Google Chrome para PDFs, imagens e textos compatíveis.
- A consulta não depende do leitor de PDF padrão configurado no Windows.
- O operador pode navegar pelas páginas sem alterar o arquivo original.
- Formatos de escritório continuam usando o aplicativo apropriado instalado no Windows.

## Complemento V2.14 — diagnóstico oficial e catálogo local

- A Etapa 1 apresenta diagnóstico estruturado de encontrado, reconhecido, ausente, divergência e ação possível.
- O catálogo oficial contém as 15 categorias e os cinco estados: confirmado, modelo pronto, esqueleto, vazio e incompleto.
- O diagnóstico é estritamente somente leitura; a criação de pastas exige uma ação separada e confirmação explícita.
- Os modelos de Nova Raiz, imóvel, processo, pessoa física, empresa, animal e arquivo morto só podem ser oferecidos em contextos compatíveis.
- Categorias vazias, incompletas e esqueletos não recebem estruturas inventadas.
- A análise local registra `.organiza/catalogo_documental.json`, com identidade por SHA-256, tipo identificado, destino e código da regra de classificação.
- Duplicados aprovados usam destino contextual: processo em `12 DUPLICADOS`, pessoa física em `12 Documentos duplicados` e demais perfis em `98 DUPLICADOS`.
- Não há integração de IA externa, monitoramento noturno ou processamento em massa nesta entrega.

## Complemento V2.13 — conferência humana e reanálise integral

- Cada linha deve permitir abrir o documento original no aplicativo padrão do Windows.
- O operador pode marcar a sugestão como `Certo` ou acionar `Refazer tudo`.
- `Refazer tudo` relê integralmente todos os documentos, recria todas as sugestões e reinicia as aprovações.
- A aplicação física permanece bloqueada até 100% dos itens da lista serem aprovados.
- Se a sugestão reprovada permanecer idêntica após a reanálise, o aplicativo deve informar isso e mantê-la desmarcada.
- As decisões de revisão são registradas sem alterar fisicamente os documentos.

## Complemento V2.12 — catálogo real da Nova Raiz

- O levantamento direto no Drive em 18/09/2026 prevalece sobre maquetes históricas quando houver divergência.
- O modelo de imóvel contém 10 módulos, incluindo `07A PROCESSOS ADMINISTRATIVOS`.
- Modelos de processo, pessoa física, empresa, animal e arquivo morto devem ser reconhecidos e preservados.
- Categorias vazias, incompletas ou sem subpastas internas validadas não podem ser completadas por suposição.
- O modo inicial seguro é organizar somente os arquivos; a criação do modelo de imóvel exige escolha explícita.
- A nomenclatura baseada em conteúdo usa data no início e abrevia somente para atender ao limite de 240 caracteres.
- A referência executável completa está em `ESTRUTURA_NOVA_RAIZ_OFICIAL.md`.

## Complemento V2.11 — limite absoluto e proteção de modelos

- Nenhum caminho produzido pelo Organiza pode ultrapassar 240 caracteres. Acima de 220 deve haver alerta; acima de 240 a operação deve ser bloqueada.
- Quando o excesso puder ser resolvido pelo nome do arquivo, o aplicativo deve encurtá-lo antes da confirmação, preservando extensão e identificadores reconhecidos.
- A pasta raiz e pastas personalizadas nunca serão encurtadas silenciosamente. Profundidade estrutural acima do limite deve ser apresentada como divergência crítica para correção humana.
- O Drive possui modelos distintos e incompatíveis entre si: estrutura geral `00 TRIAGEM–14 SISTEMAS`, processo `00 AUTOS–12 DUPLICADOS` e imóvel `01 PESQUISA–09 FOTOS`.
- Prefixos numéricos iguais não tornam duas pastas equivalentes. `01 INICIAL`, por exemplo, jamais pode ser consolidada com `01 PESQUISA`.
- A consolidação automática de aliases fica restrita às categorias diretas do dossiê selecionado; categorias dentro de outros modelos aninhados devem ser preservadas.
- O modelo de imóvel oficial é `01 PESQUISA`, `02 ARREMATAÇÃO`, `03 CARTORIO E REGISTRO`, `04 REFORMA E PUBLICIDADE`, `05 IPTU`, `06 LOCACAO`, `07 ACOES`, `08 CONTRATOS` e `09 FOTOS`.

## Complemento V2.10 — revisão visual e classificação canônica

- O nome original de cada documento deve ser acionável na tabela de revisão, abrindo o arquivo pelo aplicativo padrão do Windows sem realizar alteração física.
- Prefixos e sufixos legados de duplicação, como `Cópia de`, `Copy`, `(2)` e `- Cópia`, não podem permanecer automaticamente no nome sugerido.
- Segmentos idênticos repetidos no nome, inclusive datas, devem ser consolidados sem remover identificadores documentais distintos.
- Documentos pessoais reconhecíveis por conteúdo, como RG, CNH e CPF, devem receber identidade documental específica em vez do nome genérico `Documento`.
- Toda pasta sugerida deve pertencer à estrutura canônica oficial. Sugestões externas fora dessa estrutura devem ser corrigidas antes da revisão.
- A estrutura padrão passa a ser a opção inicial recomendada para preparação do RAG, preservando a opção explícita de trabalhar somente nos arquivos internos.

## Complemento V2.8.3 — reparação de nomes legados

- Nomes produzidos por versões anteriores não podem prevalecer sobre cabeçalhos e conteúdo interno confiáveis.
- Contratos indevidamente classificados como matrícula devem retornar a `08 CONTRATOS`; sentenças indevidamente enviadas ao Cartório devem retornar à categoria processual.
- Referências técnicas `ref <hash>` devem ser substituídas por identificadores documentais extraídos quando disponíveis.
- Pastas nuas `01` a `08` não serão destinos válidos; o novo lote utiliza as categorias descritivas completas e remove a pasta nua quando ela ficar vazia.

## Complemento V2.8.2 — nomes discriminantes e categorias descritivas

- Contratos devem incluir tipo, data e identificação abreviada das partes quando extraíveis.
- Guias e comprovantes correlatos devem compartilhar um ID curto de seis caracteres/dígitos derivado do código de barras, autenticação ou identificador transacional.
- Números CNJ usados no nome devem ser reduzidos ao bloco inicial com dígito verificador, mantendo o número integral no Livro Mestre.
- As categorias têm nomes completos. Somente aliases semanticamente equivalentes serão reutilizados; o prefixo numérico isolado não comprova equivalência.
- A pasta raiz jamais será renomeada; cada arquivo marcado será renomeado e remanejado para sua categoria temática após confirmação física.

## Complemento V2.8.1 — seleção limpa e aplicação física comprovada

- Toda nova abertura inicia sem caminho preenchido; nenhum dossiê anterior é restaurado automaticamente.
- O Histórico pode ser consultado depois da confirmação da pasta, independentemente de a renomeação ter sido executada.
- Um arquivo só recebe estado `Completed` quando o destino físico existe e a origem deixou de existir após a movimentação.
- A aplicação de nomes não consolida pastas preexistentes nem altera o nome da raiz. Somente categorias efetivamente utilizadas podem ser criadas.

## Complemento V2.8 — fluxo obrigatório, nomes confiáveis e relatório

- As etapas devem operar como assistente sequencial. Navegação posterior permanece bloqueada até a conclusão segura da etapa atual.
- Durante processamento, toda a janela fica indisponível e informa o arquivo/ação corrente; cliques concorrentes não são aceitos.
- “Analisar e gerar sugestões” jamais será apresentado como renomeação concluída. A aplicação física exige revisão e confirmação próprias.
- A identidade explícita do documento prevalece sobre menções incidentais: uma petição que cita carta de arrematação continua sendo petição; uma expedição de carta que cita IPTU não vira relatório de IPTU.
- O relatório de execução deve resumir renomeações, movimentações, itens mantidos, pastas removidas, artefatos gerados e falhas, com origem e destino legíveis.
- Logs, temporários e relatório técnico permanecem na área interna `.organiza`.

## Complemento V2.7 — matrícula e restrições registrárias

- Quando houver matrícula ou certidão imobiliária, o Livro Mestre deve resumir número, cartório, documento mais recente e atos/restrições encontrados.
- Devem ser mapeados penhoras, indisponibilidades, hipotecas, alienações fiduciárias, arrestos, usufrutos e outros ônus, com referência `R.`/`Av.`, data, fonte e trecho de evidência.
- Cada ocorrência será classificada como `baixa/cancelamento com evidência localizada` ou `pendente de confirmação ou baixa`, sem afirmar vigência definitiva sem certidão atualizada.
- Ordens judiciais de cancelamento devem ser diferenciadas da averbação efetiva no Registro de Imóveis.

## Complemento V2.6 — fluxo sem mutação antecipada

- Confirmar a pasta apenas valida e inventaria. Nenhuma pasta ou arquivo pode ser criado, movido ou consolidado na Etapa 1.
- A Etapa 2 mantém um principal por hash e pré-seleciona todas as cópias para `98 DUPLICADOS`, exibindo caminhos relativos distinguíveis.
- A leitura página a página do Livro Mestre é a fonte primária das sugestões de nome e categoria. A base é reutilizada por hash e mantida também em cache interno local.
- A consolidação de aliases e subpastas ocorre uma única vez, na aplicação final dos itens revisados; categorias vazias não utilizadas são eliminadas.
- Um cabeçalho documental explícito, como `SENTENÇA`, `DESPACHO` ou `PETIÇÃO`, prevalece sobre expressões apenas citadas no corpo.

## Complemento V2.5 — seleção, classificação e aplicação física

- A Etapa 1 aceita caminho sincronizado e URL `drive.google.com/drive/folders/...`; a URL é associada uma vez ao caminho local e reutilizada nas próximas execuções.
- A Etapa 3 só permite aplicar depois da geração das sugestões e exibe, para cada item marcado, o nome e a pasta de destino.
- Em modo de estrutura padrão, o arquivo é renomeado e movido para a categoria canônica sugerida conforme seu conteúdo; em modo “apenas arquivos”, a localização atual é preservada.
- Pastas canônicas do modelo de imóvel (`01 PESQUISA` a `09 FOTOS`), `98 DUPLICADOS` e `99 ORIGINAIS` são protegidas, mesmo vazias. Somente diretórios vazios não estruturais são removidos e registrados.

**Versão do escopo:** 2.13

**Data de consolidação:** 03/08/2026

**Plataforma:** Windows 10/11, .NET 8 e WPF
**Situação:** escopo funcional consolidado a partir do aplicativo atual

## 1. Visão do produto

O **Organiza** é um aplicativo desktop para organizar documentos jurídicos, administrativos e imobiliários armazenados em pastas locais do Windows ou em pastas sincronizadas pelo Google Drive, inclusive caminhos como `G:\Meu Drive\...`.

O aplicativo deve localizar duplicidades exatas, preservar originais, dividir PDFs grandes, analisar a primeira página de documentos, sugerir nomes, aplicar renomeações aprovadas e produzir um Livro Mestre 360° com inventário e evidências técnicas.

O Organiza opera diretamente sobre o sistema de arquivos do Windows. A versão atual não acessa a API remota do Google Drive; pastas do Drive são tratadas como pastas locais sincronizadas.

## 2. Princípios inegociáveis

1. Nenhum arquivo deve ser movido, renomeado, dividido ou substituído sem confirmação explícita do usuário.
2. A pasta raiz selecionada nunca deve ser renomeada.
3. Arquivos originais devem ser preservados sempre que uma operação gerar novos arquivos.
4. O aplicativo não deve excluir diretamente duplicatas; cópias aprovadas devem ser movidas para `98 DUPLICADOS`.
5. Nenhuma leitura de conteúdo ou chamada de IA deve ocorrer automaticamente ao selecionar uma pasta.
6. Na Etapa 3, as sugestões de renomeação devem aparecer marcadas por padrão para compor a remessa; duplicatas e PDFs grandes permanecem desmarcados por segurança.
7. Operações não podem escapar da pasta raiz protegida.
8. Falhas isoladas de arquivo, permissão ou sincronização não devem encerrar o aplicativo nem impedir o processamento seguro dos demais itens.
9. Todas as operações materiais devem ser registradas no histórico local.
10. Resultados automáticos de análise documental devem ser tratados como preliminares e sujeitos a revisão humana.

## 3. Público e cenários de uso

O aplicativo atende usuários que precisam:

- organizar dossiês jurídicos, processos e documentos de imóveis;
- trabalhar com arquivos locais ou sincronizados pelo Google Drive;
- encontrar cópias idênticas mesmo quando possuem nomes diferentes;
- adequar PDFs grandes a um limite seguro inferior a 100 MiB;
- extrair números processuais, datas e trechos úteis da primeira página;
- padronizar nomes sem perder extensões ou identificadores relevantes;
- consolidar uma pasta em relatório narrativo e base JSON auditável.

## 4. Navegação e isolamento das telas

A barra lateral deve conter exclusivamente estas seis etapas, nesta ordem:

1. Selecionar pasta
2. Verificar duplicados
3. Organizar e renomear
4. Dividir PDFs grandes
5. Histórico
6. Configurações

Ao trocar de etapa, a área central deve substituir integralmente o conteúdo anterior. O campo de caminho da pasta deve existir somente na Etapa 1.

## 5. Etapa 1 — Selecionar pasta

### 5.1 Entrada

- Permitir colar ou digitar um caminho.
- Permitir escolher a pasta pelo explorador do Windows.
- Validar que a pasta existe antes de confirmar.
- Permitir incluir ou não todas as subpastas nas etapas de verificação.
- Manter a recursividade desativada por padrão.

### 5.2 Modos de trabalho

O usuário deve escolher entre:

- **Somente arquivos internos:** preserva a estrutura existente e não cria a estrutura padrão.
- **Aplicar estrutura padrão:** cria pastas ausentes e consolida pastas equivalentes somente após nova confirmação.

Se a criação da estrutura for recusada, o aplicativo deve continuar em modo somente arquivos. Pastas operacionais indispensáveis, como `98 DUPLICADOS` ou `99 ORIGINAIS`, podem ser criadas posteriormente, mas apenas quando a operação correspondente for aprovada.

### 5.3 Estrutura padrão

A estrutura canônica atual é:

```text
01 PESQUISA
02 ARREMATAÇÃO
03 CARTORIO E REGISTRO
04 REFORMA E PUBLICIDADE
05 IPTU
06 LOCACAO
07 ACOES
08 CONTRATOS
09 FOTOS
98 DUPLICADOS
99 ORIGINAIS
```

Pastas equivalentes devem ser reconhecidas ignorando maiúsculas, minúsculas e acentos. A consolidação deve preservar o conteúdo, evitar sobrescrita e não criar sufixos artificiais como `(1)` apenas por diferença de grafia.

### 5.4 Pré-análise e higienização obrigatória

- Antes da leitura integral ou do Livro Mestre, identificar números processuais e contextos divergentes nos nomes e caminhos dos documentos.
- Determinar o contexto principal pela raiz e pelos arquivos-fonte, sem deixar cópias protegidas ou partes derivadas distorcerem essa escolha.
- Exibir cada arquivo suspeito, o contexto detectado e o contexto esperado.
- Bloquear a Etapa 3 enquanto os documentos divergentes não forem fisicamente separados pelo operador.
- Quando a raiz não declarar um número processual inequívoco, admitir múltiplos processos relacionados no mesmo dossiê e tratar a divergência apenas como informação, sem bloquear a leitura integral.
- Revalidar a pasta imediatamente antes de **GERAR NOMES PARA TODOS** e **Livro Mestre**.
- Antecipar grupos de pastas padrão equivalentes e pastas vazias não estruturais.
- Remover pastas vazias somente após confirmação, preservando raiz, pastas estruturais e áreas `.organiza`.
- Registrar no histórico cada diretório removido ou consolidado.
- Após **Confirmar pasta**, exibir feedback visual verde e inequívoco: “Pasta confirmada com sucesso. Prossiga para a próxima etapa.”
- Depois da confirmação, executar auditoria recursiva automática e somente de leitura sobre a pasta sincronizada: inventário, estrutura canônica, raiz com múltiplos dossiês, arquivos soltos, temporários, locks, duplicidade exata por SHA-256, sequência/tamanho das partes de PDF e integridade temporal do Livro Mestre.
- Se a pasta selecionada for um portfólio com dois ou mais dossiês V/P, interromper a auditoria profunda e orientar a seleção individual de cada dossiê, impedindo cruzamento entre casos.
- Mostrar na própria Etapa 1 um relatório de conformidade com severidade, exemplos e ação corretiva, sem exigir conferência manual no Explorador.

## 6. Etapa 2 — Verificar duplicados

### 6.1 Detecção

- Agrupar arquivos inicialmente pelo tamanho em bytes.
- Confirmar a duplicidade com SHA-256 completo do conteúdo.
- Nunca usar o nome do arquivo como critério de duplicidade.
- Respeitar a opção de incluir subpastas escolhida na Etapa 1.

### 6.2 Apresentação e ação

- Exibir tamanho, SHA-256 e todos os caminhos de cada grupo.
- Sugerir um arquivo principal, que não deve ser selecionável para movimentação.
- Deixar as demais cópias desmarcadas.
- Mover somente as cópias individualmente marcadas e confirmadas.
- Usar como destino a pasta canônica `98 DUPLICADOS`.
- Nunca apagar diretamente uma duplicata.
- Resolver colisões de nome no destino com sufixo numérico incremental, sem sobrescrever arquivos.
- Continuar o lote quando uma cópia isolada falhar e registrar a falha no histórico.
- Remover, após a movimentação, diretórios de origem que tenham ficado vazios, preservando pastas estruturais e protegidas.
- Recalcular os grupos depois da movimentação.

## 7. Etapa 4 — Dividir PDFs grandes

### 7.1 Seleção

- Listar somente PDFs maiores que **97 MiB** (`97 × 1024 × 1024 bytes`).
- Respeitar a configuração de recursividade.
- Exibir nome e tamanho de cada PDF.
- Deixar todos os PDFs desmarcados por padrão.

### 7.2 Divisão

- Priorizar extração estrutural para evitar duplicação de recursos internos do PDF e partes artificiais de uma única lauda.
- Validar a cobertura sequencial integral de todas as páginas antes de retirar o original da área de trabalho.

- Dividir exclusivamente em limites estruturais de página.
- Medir os bytes reais de cada PDF serializado; não usar estimativa por número de páginas.
- Produzir partes com no máximo 97 MiB sempre que estruturalmente possível.
- Aceitar excepcionalmente uma página única acima do limite quando ela for indivisível.
- Nomear as partes como `nome_parte-01.pdf`, `nome_parte-02.pdf` e assim por diante.
- Manter as partes na pasta de trabalho do documento.
- Não criar pastas temporárias com nomes baseados em hash.

### 7.3 Preservação do original

- Copiar o original inteiro para `99 ORIGINAIS`.
- Conferir a cópia por tamanho e SHA-256 antes de retirar o original da pasta de trabalho.
- Nunca considerar a operação concluída se a cópia preservada for diferente.
- Evitar colisões de nomes sem sobrescrever arquivos existentes.

### 7.4 Resiliência

- Exibir progresso por arquivo.
- Exibir painel modal com “Processando... Aguarde a conclusão da etapa”, bloquear cliques concorrentes e liberar automaticamente a interface ao concluir.
- Se o original ainda existir após interrupção anterior, reconhecer e limpar com repetição segura as partes residuais correspondentes antes de nova divisão confirmada.
- Validar antes da conclusão que todas as páginas estejam cobertas, sem lacunas ou sobreposição; nas últimas 128 páginas, testar de forma descendente o maior bloco real que respeite 97 MiB, sem pressupor monotonicidade do tamanho serializado.
- Excluir partes `_parte-XX.pdf` das filas de duplicidade e renomeação.
- Processar o restante do lote quando um PDF falhar.
- Em falha durante a criação de qualquer parte, remover todas as partes geradas naquela tentativa, inclusive a parte corrente incompleta, sem retirar o PDF original.
- Traduzir erros de acesso, permissões ou sincronização do Google Drive em mensagens operacionais compreensíveis.
- Registrar sucessos e falhas no histórico.

## 8. Etapa 3 — Organizar e renomear

### 8.1 Geração sob demanda

- A análise só deve começar depois do clique em **GERAR NOMES PARA TODOS** e de uma confirmação adicional.
- O clique autoriza a leitura da primeira página dos PDFs selecionados pelo escopo da pasta, mas não autoriza renomeações.
- Para PDFs textuais, usar extração integral com `pdftotext` em todas as páginas.
- Para páginas digitalizadas ou sem texto útil, renderizar cada página necessária e usar OCR com Tesseract.
- Extrair DOCX e formatos textuais compatíveis por text-to-text.
- Extrair e preservar no resultado o SHA-256, números processuais, datas, texto integral, cobertura, páginas OCR e alertas.
- Não truncar silenciosamente o conteúdo usado pela análise jurídica ou pela sugestão de nomes.
- Normalizar o texto extraído em Unicode NFC.

### 8.2 Codex e fallback local

- A integração externa de IA deve permanecer isolada atrás de um contrato próprio.
- Sem credencial ou integração disponível, nenhum conteúdo deve sair da máquina.
- Nesse cenário, o aplicativo deve gerar sugestões locais a partir do conteúdo extraído, número de processo, datas, nome original e metadados.
- A interface deve informar a origem ou o motivo de cada sugestão.
- Uma integração externa futura não pode eliminar as confirmações ou proteções existentes.

### 8.3 Regras de nomenclatura

- Preservar sempre a extensão original.
- Usar frase legível, preferencialmente em minúsculas com apenas a primeira letra em maiúscula.
- Remover caracteres inválidos do Windows.
- Evitar espaços excedentes, anos repetidos, campos vazios e tags artificiais.
- Preservar identificadores processuais claros presentes no nome ou no conteúdo.
- Não permitir que dois conteúdos diferentes recebam silenciosamente a mesma sugestão genérica.
- Permitir edição manual do nome sugerido antes da aplicação.
- Deixar todos os itens marcados por padrão, permitindo que o operador desmarque apenas o que deseja excluir da remessa.
- Manter **Aplicar itens marcados** indisponível até que sugestões baseadas no conteúdo tenham sido geradas; a listagem inicial não deve ser confundida com renomeação analítica.

### 8.4 Aplicação

- Renomear apenas itens marcados e novamente confirmados.
- Bloquear qualquer destino fora da raiz protegida.
- Impedir sobrescrita de arquivo existente.
- Quando o nome de destino já existir, aplicar sufixo numérico incremental e registrar a resolução no histórico.
- Alterações apenas de capitalização devem ser executadas fisicamente por transição temporária segura e registradas como concluídas.
- Ao final do lote, informar quantos itens foram concluídos, permaneceram inalterados ou falharam.
- Tratar a falha de um item sem interromper os demais itens da remessa.
- Consolidar pastas padrão equivalentes somente dentro do modo autorizado.
- Registrar origem, destino, data e resultado.

## 9. Livro Mestre 360°

### 9.1 Acionamento e saídas

A geração deve ocorrer somente pelo botão **Gerar Livro Mestre 360°**, após confirmação. Deve fazer uma varredura recursiva da pasta protegida e criar ou atualizar:

```text
LIVRO MESTRE 360.md
.organiza_livro_mestre_360.json
```

Os próprios arquivos gerados, o histórico e temporários não devem entrar novamente como fontes da análise.

O schema 2.2 deve separar explicitamente no Markdown e no JSON: síntese executiva; bem e leilão/arrematação; fase processual; auditoria dos patronos anteriores; consolidação financeira por documento; matriz de teses; matriz de riscos; plano estratégico; evidências integrais e apêndice técnico. Caminhos completos não devem dominar o corpo narrativo, que usará referências `DOC-000`.

### 9.2 Conteúdo narrativo

O Markdown deve incluir visão executiva, processos identificados e estes 12 blocos:

1. Identificação dos processos analisados
2. Fase de conhecimento e evolução processual
3. Leilão, arrematação, parcelamentos e cancelamentos
4. Decisões interlocutórias, sentenças e cumprimento
5. Recursos, razões e contrarrazões
6. Atuação dos patronos anteriores
7. Prejuízos financeiros e processuais
8. Inventário financeiro detalhado
9. Matriz de tese jurídica
10. Matriz de risco processual
11. Plano de ação, lacunas e controle documental
12. Resumo executivo para o cliente

O corpo principal deve ser legível e narrativo. Inventário, tamanhos, métodos de extração e hashes devem ficar exclusivamente no **Apêndice Técnico e Integridade**.

### 9.3 Base estruturada

O JSON deve preservar, para cada fonte:

- caminho relativo;
- tamanho em bytes;
- SHA-256 completo;
- método de extração;
- números processuais;
- datas identificadas;
- texto integral extraído sem truncamento silencioso;
- total de páginas, páginas com texto, páginas submetidas a OCR e alertas de cobertura;
- inventário financeiro estruturado com categoria, valor, data, fonte e trecho de evidência.

### 9.4 Compatibilidade textual

- Gravar Markdown e JSON em UTF-8 válido com BOM.
- Normalizar caracteres em Unicode NFC.
- Usar finais de linha CRLF para compatibilidade com editores do Windows e Word.
- Remover caracteres de controle inválidos e hifenizações introduzidas por quebra artificial.
- Preservar hífens legítimos em nomes, termos e números processuais.

## 10. Etapa 5 — Histórico

- Persistir operações no arquivo `.organiza_log.json`, dentro da raiz selecionada.
- Registrar data e hora, operação, origem, destino, status e detalhes úteis.
- Exibir o histórico em tabela somente leitura.
- Registrar também falhas relevantes e operações de criação, consolidação, movimentação, divisão, renomeação e geração do Livro Mestre.
- Serializar gravações concorrentes por pasta, usar troca atômica e limitar o tempo de espera para impedir travamentos prolongados.
- Manter temporários e locks exclusivamente na área interna oculta `.organiza`, removendo resíduos temporários após cada tentativa.
- Ignorar em todos os fluxos interativos arquivos `.tmp`, `.lock` e nomes iniciados por `.$` ou `~$`.

## 11. Etapa 6 — Configurações

A tela deve apresentar, no mínimo:

- limite de divisão de PDF;
- limites de caminhos longos;
- disponibilidade de `pdftotext`;
- disponibilidade do OCR Tesseract;
- estado da integração Codex e do fallback local.

Alterações configuráveis futuras devem manter as mesmas confirmações de segurança.

## 12. Proteção contra caminhos longos

- Calcular o caminho final antes de mover, renomear, dividir ou criar arquivos.
- Classificar caminhos com até 220 caracteres como normais.
- Exibir aviso e exigir confirmação adicional acima de 220 caracteres.
- Encurtar automaticamente o nome do arquivo quando isso bastar para manter o caminho em até 240 caracteres, preservando extensão e identificadores reconhecidos.
- Bloquear preventivamente qualquer caminho acima de 240 caracteres.
- Permitir que o usuário edite o nome ou reduza manualmente a profundidade das pastas antes de tentar novamente.

## 13. Limpeza de pastas vazias

Depois de operações aprovadas, o aplicativo pode remover automaticamente pastas vazias não estruturais, desde que:

- nunca remova a raiz selecionada;
- preserve todas as pastas padrão;
- preserve pastas de log ou nomes internos iniciados por `.organiza`;
- não remova pasta que contenha qualquer arquivo ou subpasta válida;
- registre as remoções relevantes no histórico.

## 14. Arquitetura técnica

A solução deve manter separação em quatro camadas:

```text
Organiza.Domain          modelos e regras puras
Organiza.Application     casos de uso, contratos e proteções
Organiza.Infrastructure disco, hash, JSON, PDF, OCR e adaptadores externos
Organiza.Wpf             interface, navegação e ViewModels
```

As dependências devem apontar para dentro: WPF e Infrastructure dependem de Application; Application depende de Domain; Domain não conhece WPF, disco, PDFSharp, Poppler, Tesseract ou serviços externos.

## 15. Requisitos não funcionais

- Interface responsiva durante operações demoradas.
- Processamento assíncrono onde houver leitura intensiva de disco ou hash.
- Mensagens em português e orientadas à ação.
- Zero perda silenciosa de dados.
- Nenhuma sobrescrita não confirmada.
- Resultados determinísticos para hash e detecção de duplicidade.
- Compatibilidade com Windows 10 e Windows 11 em arquitetura x64.
- Distribuição oficial autocontida em arquivo único.

## 16. Publicação

O comando oficial deve ser:

```powershell
.\Publicar-Organiza.cmd
```

A saída esperada é:

```text
publish\Organiza.Wpf.exe
```

Também deve existir opção dependente do .NET Desktop Runtime 8:

```powershell
.\Publicar-Organiza.cmd -FrameworkDependent
```

O processo de publicação deve limpar exclusivamente a pasta `publish` do projeto.

## 17. Testes e critérios de aceite

O escopo só é considerado atendido quando:

- a solução restaura, compila e publica sem erro;
- todos os testes automatizados passam;
- duplicatas são comprovadas por tamanho e SHA-256 integral;
- propriedades apenas de leitura exibidas pelo WPF usam binding `OneWay` quando necessário;
- a raiz nunca é renomeada;
- alterações sem `ExplicitApproval` são bloqueadas;
- caminhos acima de 220 geram aviso, nomes são ajustados quando possível e qualquer destino acima de 240 é bloqueado;
- pastas equivalentes são reutilizadas ignorando caixa e acentos;
- a divisão respeita o tamanho real e valida a cópia do original;
- PDFs textuais e digitalizados possuem caminhos de extração testáveis;
- Markdown é gravado sem corrupção de acentos;
- o Livro Mestre separa narrativa e apêndice técnico;
- o executável publicado abre e permanece responsivo.

## 18. Limites do produto atual

Não fazem parte da versão atual:

- acesso direto à API do Google Drive;
- exclusão permanente de duplicatas;
- análise jurídica conclusiva sem revisão humana;
- leitura automática de conteúdo ao selecionar uma pasta;
- renomeação ou reorganização em massa sem seleção individual;
- sincronização em nuvem própria;
- edição interna do conteúdo de documentos;
- divisão de uma página PDF em fragmentos visuais.

## 19. Resultado esperado

O Organiza deve oferecer um fluxo seguro e auditável no qual o usuário seleciona uma pasta, resolve duplicidades, trata PDFs grandes, revisa sugestões de nomes, aplica somente as mudanças desejadas e, opcionalmente, gera uma visão documental consolidada — sem perder originais, sem alterações silenciosas e sem sair da raiz escolhida.

## 20. Complemento funcional V2.18.0

- A Etapa 2 é apresentada ao operador como **Verificar documentos** e informa claramente o resultado da comparação de cópias exatas.
- Na existência de duplicatas, as cópias ficam pré-selecionadas, o principal permanece protegido e o operador escolhe entre mover as cópias para a pasta contextual de duplicados ou mantê-las no lugar.
- Sem duplicatas, a etapa é concluída automaticamente e libera a etapa seguinte.
- Um único comando **GERAR NOMES PARA TODOS** processa o lote; nunca se exige geração linha a linha.
- O nome original abre o documento para conferência e o comando **Editar** permite corrigir manualmente apenas a sugestão daquela linha.
- O protocolo diferencia certificado do veículo de demonstrativo de débitos: mera ocorrência da palavra RENAVAM não transforma o documento em `Doc Renavam`.
- Quando houver uma placa claramente predominante no lote, documentos que contenham somente outra placa recebem destino sugerido `96 NÃO PERTENCE À PASTA` e regra `REVISAO-CONTEXTO-PLACA`.
- A separação por contexto permanece apenas como sugestão até a confirmação final; não ocorre movimento automático.
