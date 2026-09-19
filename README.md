# Organiza

## Organiza V2.19.0 — modo de alto volume

- seleção inicial leve: percorre a árvore em segundo plano e deixa o SHA-256 integral para a Etapa 2;
- pastas inacessíveis ou transitórias do Google Drive não derrubam toda a enumeração;
- busca de pastas vazias em passagem única, eliminando releituras exponenciais em árvores profundas;
- leitura documental e validação de cache com paralelismo limitado a dois arquivos, preservando a ordem e sem saturar disco/OCR;
- duplicidades calculadas em segundo plano, com progresso real e dois hashes simultâneos somente para tamanhos repetidos;
- tabelas com virtualização e atualização em lote, evitando milhares de repinturas na interface;
- teste sintético automatizado com 1.000 arquivos distribuídos em 25 subpastas.

## Organiza V2.18.6

- reconhece atalhos nativos do Google Workspace (`.gdoc`, `.gsheet`, `.gslides` e equivalentes) sem tentar tratá-los como arquivos físicos;
- esses ponteiros aparecem na auditoria como informação e não interrompem a seleção da pasta;
- OCR, hash, duplicidade, renomeação e Livro Mestre continuam restritos a arquivos físicos acessíveis.

## Organiza V2.18.5

- o botão `APLICAR NOMES APROVADOS` cria e preserva o modelo completo do imóvel antes de mover os documentos;
- inclui as quatro áreas operacionais oficiais (`_DUPLICADAS...`, `_LOGS_MIGRACAO`, `_NAO_PERTENCE...` e `_REVISAR_ORIGEM...`) e as dez pastas numeradas;
- pastas equivalentes antigas continuam consolidadas sem sobrescrever arquivos, e as pastas vazias oficiais não são removidas pela limpeza final.

## Organiza V2.18.4

- mantém o comando de aplicação acessível depois da geração das sugestões;
- informa quantos documentos ainda precisam receber o ✓ verde quando a conferência estiver incompleta;
- elimina o bloqueio silencioso que fazia o botão parecer sem funcionamento;
- preserva a exigência de aprovação individual antes de qualquer alteração física.

## Organiza V2.18.3

- identifica a espécie documental pelo título, cabeçalho e função principal, sem deixar palavras incidentais do corpo dominarem a classificação;
- corrige conflitos observados entre carta de arrematação, sentença, petição, notificação, consulta SENATRAN, certidão, demonstrativo de débitos e documento de veículo;
- impede que menções a Renajud, contrato, auto de arrematação ou CRLV reescrevam indevidamente a descrição principal;
- envia tipos não comprovados para revisão, preservando o nome atual, em vez de criar uma sugestão convincente porém incorreta.

## Organiza V2.18.2

- adiciona uma barreira final única para impedir sugestões sem identidade documental compreensível;
- recusa nomes formados somente por número, fragmento de processo e data;
- preserva o nome atual, desmarca o item e envia o caso para revisão quando o tipo não puder ser comprovado;
- mantém como formato final `NN - TIPO/DESCRIÇÃO - ID PRINCIPAL - DATA.ext`;
- impede que geradores intermediários transformem uma sugestão incompleta em resultado aparentemente pronto.

## Organiza V2.18.1

- impede que a simples menção ao RG de uma parte transforme uma petição inteira em documento de identidade;
- reconhece peças pelo cabeçalho e contexto processual antes de considerar referências incidentais a débitos ou IPTU;
- preserva a classificação financeira explícita de demonstrativos, consultas de débitos, comprovantes e guias;
- mantém a reanálise integral deliberadamente completa: `Refazer` relê cada documento e pode demorar mais que a geração inicial com cache.

## Organiza V2.18.0

- torna a Etapa 2 autoexplicativa: sem cópias, ela conclui automaticamente; com cópias exatas, elas aparecem pré-selecionadas e o original permanece protegido;
- gera nomes para o lote inteiro em um único comando e permite editar somente a sugestão necessária;
- separa visualmente aprovação, edição e reanálise integral, exigindo nova aprovação depois de qualquer edição;
- diferencia certificado de veículo de demonstrativo de débitos, mesmo quando ambos citam RENAVAM;
- usa RENAVAM e placa somente quando comprovados e envia divergências claras de placa para `96 NÃO PERTENCE À PASTA` como sugestão sujeita à confirmação;
- remove o antigo caminho de IA externa e o renderizador legado que não participavam mais do produto;
- mantém toda leitura e classificação local, sem alterar arquivos antes da confirmação final.

## Organiza V2.13.0

- permite abrir o documento original diretamente na linha de revisão;
- adiciona as decisões `Certo` e `Refazer tudo` para conferência humana;
- ao reprovar um resultado, relê integralmente todos os documentos e recria toda a lista sem usar o cache anterior;
- zera as aprovações após cada reanálise e só libera a aplicação física quando 100% da lista estiver aprovada;
- registra aprovações e reprovações no histórico sem renomear ou mover arquivos durante a conferência;
- sinaliza quando a reanálise repete a mesma sugestão, mantendo o item desmarcado para nova verificação.

## Organiza V2.12.0

- incorpora o levantamento real da Nova Raiz de 18/09/2026 como catálogo oficial;
- reconhece os modelos de processo, imóvel, pessoa física, empresa, animal e arquivo morto;
- protege categorias vazias, incompletas e esqueletos ainda não validados contra criação indevida;
- inclui `07A PROCESSOS ADMINISTRATIVOS` como o décimo módulo do imóvel;
- usa o modo “somente arquivos” como escolha inicial segura;
- coloca a data extraída do documento no início do nome sugerido;
- aplica o dicionário oficial de abreviações somente quando o caminho precisa ser reduzido.

Consulte [`ESTRUTURA_NOVA_RAIZ_OFICIAL.md`](ESTRUTURA_NOVA_RAIZ_OFICIAL.md) para a matriz de precedência e os modelos confirmados.

## Organiza V2.11.0

- estabelece 240 caracteres como limite máximo absoluto de qualquer destino produzido pelo aplicativo;
- encurta nomes de arquivos antes da confirmação, preservando extensão, processo, data, placa e IDs reconhecidos;
- audita arquivos e pastas já existentes acima de 240 como divergência crítica;
- reconhece e protege separadamente os modelos da raiz do Drive, dos processos e dos dossiês de imóveis;
- impede que pastas de modelos diferentes sejam misturadas apenas por compartilharem o mesmo prefixo numérico;
- limita a consolidação de aliases às categorias diretas do dossiê selecionado;
- alinha o modelo de imóvel às pastas reais `01 PESQUISA–09 FOTOS`.

## Organiza V2.10.0

- permite abrir o documento original diretamente na tabela de revisão antes de confirmar qualquer renomeação;
- remove marcadores legados de cópia e sufixos numéricos artificiais dos nomes sugeridos;
- consolida segmentos repetidos, inclusive datas duplicadas;
- reconhece RG, CNH, CPF e documentos de veículo pelo conteúdo extraído/OCR;
- prioriza a identidade explícita de relatórios e evita classificações causadas por menções incidentais;
- garante que toda pasta sugerida pertença à estrutura oficial;
- recomenda por padrão a estrutura oficial para preparar o dossiê para o RAG, mantendo disponível o modo somente arquivos.

## Organiza V2.9.0

- refaz o Livro Mestre 360° com apresentação jurídico-documental limpa, numeração progressiva formal e apêndices técnicos separados;
- aplica negrito seletivo a valores, processos, datas críticas, gravames e obrigações sem poluir o texto;
- cruza cronologicamente restrições registrais com cartas e autos de arrematação;
- concilia parcelas, valores devidos e recibos/pagamentos em estrutura auditável;
- extrai o histórico de diligências dos oficiais de justiça, com data, ato, resultado, destinatário e endereço quando reconhecidos;
- eleva a base JSON para o esquema 3.0, preservando evidências e rastreabilidade documental.

## Organiza V2.8.4

- valida todos os caminhos antes da primeira alteração física, impedindo lotes parcialmente aplicados;
- na versão 2.10, avisava acima de 240 e bloqueava acima de 260; a regra foi substituída na versão 2.11 pelo limite absoluto de 240;
- mantém a pasta raiz selecionada absolutamente protegida, inclusive quando há caminhos longos;
- orienta a edição manual dos nomes sugeridos ou a escolha de um caminho mais curto, sem compactar pastas automaticamente.

## Organiza V2.8.3

- corrige nomes legados ruins em vez de aceitá-los como identidade documental;
- cabeçalhos internos `SENTENÇA` e `CONTRATO` prevalecem sobre nomes errados produzidos anteriormente;
- comprovantes de IPTU substituem referências de hash pelo ID curto do código de barras;
- pastas apenas numéricas, como `05` e `07`, deixam de ser reutilizadas e são substituídas funcionalmente pelas categorias completas;
- o relatório diferencia alterações físicas de itens que já estavam adequados.

## Organiza V2.8.2

- categorias passam a ter nomes completos e pastas numericamente equivalentes já existentes são reutilizadas sem alteração;
- contratos recebem data e partes abreviadas (`Alfa x Beta`);
- guias e comprovantes recebem o mesmo identificador curto de rastreio extraído do código de barras/autenticação;
- processos usam somente o bloco inicial CNJ necessário à identificação;
- fotos já normalizadas não recebem prefixos repetidos;
- arquivos aprovados são renomeados e movidos para a pasta temática correspondente, preservando o nome da raiz.

## Organiza V2.8.1

- inicia sempre com o campo de pasta vazio;
- o Histórico fica disponível logo após a confirmação da pasta;
- renomeações usam repetição segura e só são registradas como concluídas após comprovação física no destino;
- a aplicação não consolida nem renomeia pastas existentes; cria somente a categoria necessária para cada arquivo aprovado;
- o comando final de renomeação foi destacado para não ser confundido com a geração de sugestões.

## Organiza V2.8

- o aplicativo agora funciona como fluxo sequencial: uma etapa só é liberada quando a anterior termina;
- toda a janela, inclusive a barra lateral, fica bloqueada durante operações, exibindo o documento/ação corrente;
- a tela diferencia claramente análise, revisão e aplicação física dos nomes;
- o tipo explícito do arquivo e o cabeçalho documental prevalecem sobre assuntos apenas citados no corpo;
- petição de baixa, expedição de carta, depósito judicial, matrícula e contratos recebem classificação prioritária própria;
- o Histórico foi transformado em relatório de execução com resumo, origem, destino, resultado e falhas;
- histórico e relatório técnico ficam na área interna `.organiza`, sem poluir a raiz do dossiê.

## Organiza V2.7

- o Livro Mestre identifica matrículas e aponta o documento registrário mais recente disponível;
- extrai penhoras, indisponibilidades, hipotecas, alienações fiduciárias, arrestos, usufrutos e outros ônus;
- correlaciona referências `R.`/`Av.` com evidências de cancelamento ou baixa;
- separa restrições com baixa localizada das pendentes de confirmação;
- inclui quadro legível no Markdown e estrutura `PropertyRegistryAnalysis` no JSON schema 2.3;
- nunca trata ausência de texto ou simples menção como prova definitiva da situação registrária atual.

## Organiza V2.6

- a Etapa 1 é exclusivamente de validação: não cria, consolida nem movimenta a estrutura;
- em cada grupo SHA-256, o principal aparece como `MANTER` e fica protegido; as cópias começam desmarcadas e só vão para `98 DUPLICADOS` quando forem marcadas e confirmadas;
- o usuário pode concluir a revisão mantendo as cópias onde estão; essa decisão explícita libera a próxima etapa sem mover ou excluir arquivos;
- os caminhos de duplicados são relativos e completos no tooltip, evitando a aparência de linhas repetidas;
- o Livro Mestre alimenta automaticamente as sugestões de nome e pasta, com cache local validado por SHA-256;
- colisões semânticas recebem referência técnica estável e nunca interrompem todo o lote;
- pastas equivalentes aninhadas são consolidadas somente na aplicação final; subpastas são achatadas com preservação de conflitos;
- categorias vazias não utilizadas são removidas; apenas `98 DUPLICADOS` e `99 ORIGINAIS` permanecem protegidas;
- o tipo documental prioriza o cabeçalho da primeira página e a data de assinatura/juntada.

## Organiza V2.5

- aceita caminho local ou link de pasta do Google Drive; no primeiro uso do link, solicita a associação com a pasta sincronizada e memoriza a relação;
- gera nome e pasta de destino com base no conteúdo integral/OCR;
- aplica renomeação e classificação física em um único lote resiliente, sem sobrescrever arquivos;
- mantém pastas canônicas vazias por serem estruturais e remove somente diretórios vazios não protegidos;
- imagens recebem nomes descritivos com data, horário e contexto do dossiê.

Aplicativo desktop Windows em C# + WPF para organizar documentos locais e sincronizados pelo Google Drive, com consentimento explícito antes de qualquer alteração.

## Estrutura

```text
Organiza.sln
├─ src/
│  ├─ Organiza.Domain/          modelos e regras sem dependências externas
│  ├─ Organiza.Application/     casos de uso, contratos e travas de segurança
│  ├─ Organiza.Infrastructure/  disco, SHA-256, JSON, PDF e OCR local
│  └─ Organiza.Wpf/             interface WPF e ViewModels
└─ tests/
   └─ Organiza.Tests/           testes automatizados das regras críticas
```

As dependências apontam para dentro: `Wpf` e `Infrastructure` usam `Application`; `Application` usa `Domain`. O domínio não conhece WPF, disco, PDFSharp ou serviços externos.

## Requisitos para executar

- Windows 10/11
- Visual Studio 2022 17.8+ com a carga **Desenvolvimento para desktop com .NET**, ou SDK .NET 8

```powershell
dotnet restore Organiza.sln
dotnet test Organiza.sln
dotnet run --project src/Organiza.Wpf/Organiza.Wpf.csproj
```

## Gerar a distribuição oficial

Na raiz do projeto, execute:

```powershell
.\Publicar-Organiza.cmd
```

O script limpa exclusivamente a pasta `publish/` e gera nela a distribuição Release `win-x64`, autocontida e em arquivo único. O executável final fica sempre em:

```text
publish\Organiza.Wpf.exe
```

Para produzir uma versão menor que exige o .NET Desktop Runtime 8 instalado na máquina de destino:

```powershell
.\Publicar-Organiza.cmd -FrameworkDependent
```

## Garantias implementadas

- A navegação usa um único conteúdo central: cada etapa substitui integralmente a anterior.
- A barra lateral contém somente as seis etapas, na ordem definida.
- A Etapa 1 oferece dois modos explícitos: somente arquivos internos (padrão seguro) ou aplicação confirmada da estrutura padrão.
- A Etapa 1 executa uma pré-análise por identificadores processuais, aponta cruzamentos de dossiês e bloqueia a leitura integral até a separação física dos arquivos divergentes.
- Após a confirmação da Etapa 1, a interface mostra um aviso verde explícito orientando o operador a avançar.
- Pastas padrão equivalentes e diretórios vazios não estruturais são diagnosticados antes da análise; consolidações e remoções confirmadas ficam no histórico.
- A pasta raiz selecionada nunca é renomeada; operações de arquivo também são bloqueadas se tentarem sair dessa raiz.
- Criações, movimentos, renomeações e divisões exigem `ExplicitApproval`.
- Duplicados são agrupados por tamanho e SHA-256 integral, independentemente do nome.
- Cópias selecionadas vão para `98 DUPLICADOS`; nunca são apagadas diretamente. Colisões recebem sufixo incremental e pastas de origem vazias são limpas com registro no histórico.
- PDFs são divididos por bytes efetivamente serializados, com teto padrão de 97 MiB; uma página indivisível maior é aceita excepcionalmente como parte unitária.
- PDFs acima de 50 MiB usam contagem externa protegida (`pdfinfo` ou `pikepdf`); sem uma dessas ferramentas, o lote é bloqueado com aviso em vez de carregar o documento gigante na memória do WPF.
- PDFs textuais de até 50 MiB possuem extração local gerenciada incorporada, sem depender da instalação de `pdftotext`; documentos maiores continuam exigindo a ferramenta externa para preservar a estabilidade.
- Páginas que importam recursos globais gigantes tentam uma rasterização segura com Poppler antes da exceção, preservando o original e registrando o método no histórico.
- `98 DUPLICADOS`, `99 ORIGINAIS`, artefatos internos e partes já geradas ficam fora das filas interativas para impedir reprocessamento em ciclo.
- Falhas de leitura, permissão ou bloqueio pelo Google Drive são convertidas em mensagens operacionais, registradas no histórico e não interrompem os demais PDFs do lote; partes produzidas por uma tentativa incompleta são removidas da raiz.
- O PDF original é copiado para `99 ORIGINAIS` e validado novamente por tamanho e SHA-256 antes da retirada da pasta de trabalho.
- Pastas padrão são reutilizadas ignorando caixa e acentos.
- Nomes são encurtados de forma determinística quando necessário; caminhos acima de 220 recebem alerta e nenhum destino acima de 240 é permitido.
- Operações são persistidas em `.organiza_log.json` com data, origem, destino e status.
- Temporários e locks (`.tmp`, `.$...`, `~$...`, `.lock`) são ignorados pelos fluxos e mantidos na área interna oculta `.organiza`, nunca na listagem operacional da raiz.
- O histórico usa gravação atômica assíncrona, trava por pasta e espera limitada para evitar congelamentos e conflitos concorrentes.
- Sugestões da Etapa 3 aparecem marcadas por padrão, para o operador desmarcar apenas as exclusões; PDFs para divisão continuam desmarcados.
- Conflitos de renomeação usam sufixo numérico incremental, ficam registrados e não interrompem os demais itens do lote.
- Renomeações que alteram somente maiúsculas/minúsculas usam uma transição física segura e são registradas como `Completed`, com resumo final de concluídos, inalterados e falhas.
- Antes de cada movimentação ou renomeação, a V2.9 valida acesso exclusivo de escrita. Arquivos abertos ou bloqueados são mantidos como pendentes, recebem alerta visual com nome e caminho exatos e são registrados em `.organiza/historico.json`, enquanto os demais itens livres continuam normalmente.
- A leitura integral só pode partir de **GERAR NOMES PARA TODOS** ou **Livro Mestre** e ainda exige confirmação explícita.
- PDFs com texto usam `pdftotext` em todas as páginas; páginas sem texto usam renderização Poppler + OCR Tesseract como fallback complementar.
- DOCX e formatos textuais compatíveis são extraídos text-to-text sem o antigo truncamento de 12.000 caracteres.
- Cada análise inclui SHA-256, identificadores processuais, datas, cobertura de páginas, páginas OCR, alertas e texto integral extraído.
- Conteúdos diferentes não conservam silenciosamente a mesma sugestão genérica.
- A Etapa 3 gera, sob confirmação, `LIVRO MESTRE 360.md` e `.organiza_livro_mestre_360.json` na raiz protegida.
- Os arquivos do Livro Mestre são substituídos por gravação atômica: uma falha ou bloqueio do Google Drive preserva integralmente a versão anterior.
- O Livro Mestre schema 3.0 adota estrutura formal numerada e limpa, com destaque estratégico de dados críticos, cronologia cruzada entre restrições registrais e cartas de arrematação, conciliação rigorosa de parcelas e recibos e histórico de diligências dos oficiais de justiça.
- O inventário financeiro estrutura valores e evidências de IPTU, PPI, condomínio, despesas mensais, IPVA, custas e parcelamentos de arrematação.
- Caminhos longos e hashes ficam no apêndice; o corpo usa referências `DOC-000`, narrativa executiva e tabelas legíveis apenas para finanças, teses, riscos e ações.
- Etapas longas exibem um painel modal “Processando... Aguarde a conclusão da etapa”, bloqueiam cliques concorrentes e liberam automaticamente a interface ao terminar.
- A última pasta confirmada é restaurada após reiniciar ou atualizar o aplicativo.
- Se uma divisão anterior tiver sido interrompida com o original ainda presente, as partes residuais correspondentes são limpas com tentativas adicionais antes de um reinício confirmado e seguro.
- A V2.3 valida cobertura contínua de todas as páginas, prioriza extração estrutural com `pikepdf` para manter o tamanho previsível, usa busca descendente nas últimas 128 páginas e nunca considera concluída uma divisão com páginas faltantes.
- A Etapa 1 da V2.4 executa auditoria recursiva de conformidade após a confirmação: detecta pasta-pai com múltiplos dossiês, estrutura ausente, arquivos soltos, temporários, locks, duplicidades SHA-256, partes de PDF fora do limite e Livro Mestre ausente ou desatualizado.
- Pastas sem processo único declarado podem reunir processos relacionados; o bloqueio por contaminação só é aplicado quando o nome da raiz declara expressamente o processo esperado.
- Partes de PDF geradas não aparecem na renomeação. O botão Aplicar fica indisponível até a geração de sugestões descritivas baseadas no conteúdo.
- A verificação de duplicidade permanece visível por tempo mínimo e informa horário e quantidade de arquivos efetivamente examinados.
- A base JSON preserva caminhos relativos, tamanhos, SHA-256, método de extração, processos, datas e texto extraído para consultas futuras de IA.

## Análise local

O ponto de extensão `IContentSuggestionGateway` permanece isolado na camada de aplicação, mas a versão atual usa exclusivamente o analisador local baseado no texto extraído, OCR, identificadores comprovados e estrutura oficial. Não há credencial externa, envio de documentos ou integração de IA externa nesta versão.

## Integração com RAG

O dossiê deve ser auditado e revisado no Organiza antes de entrar em qualquer pipeline de RAG. O fluxo obrigatório, os artefatos de integração e as exclusões de ingestão estão descritos em [GUIA_DO_DESENVOLVEDOR.md](GUIA_DO_DESENVOLVEDOR.md).

## Observação sobre PDF

Um PDF válido só pode ser separado em documentos válidos em limites estruturais de página. O Organiza não usa uma quantidade fixa de páginas ou laudas: ele serializa intervalos candidatos e aceita cada parte exclusivamente após medir o tamanho real do arquivo resultante.
