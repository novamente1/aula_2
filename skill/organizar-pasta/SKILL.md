---
name: organizar-pasta
description: Analisa, deduplica, classifica, renomeia e reorganiza com segurança arquivos de uma pasta local ou sincronizada pelo Google Drive. Use quando o usuário abrir o Codex dentro de um dossiê ou diretório e pedir para organizar arquivos, descobrir duplicados, comparar a estrutura atual com a desejada, visualizar um antes/depois, revisar a proposta em várias mensagens ou aplicar um plano confirmado sem renomear a pasta raiz nem excluir originais automaticamente.
---

# Organizar pasta

Trabalhar sobre a pasta atual, salvo se o usuário indicar outra raiz. Tratar pastas sincronizadas pelo Google Drive como sistema de arquivos local.

## Regras invioláveis

- Nunca renomear, mover ou excluir a pasta raiz.
- Não alterar arquivos de origem na fase de análise e proposta.
- Não excluir duplicatas. Propor mover cópias aprovadas para `98 DUPLICADOS`.
- Não sobrescrever destinos existentes nem decidir colisões depois da aprovação.
- Exigir aprovação explícita da revisão visualizada antes de executar qualquer movimentação ou renomeação.
- Manter todas as origens e destinos dentro da raiz.
- Tratar `98 DUPLICADOS`, `99 ORIGINAIS`, `.organiza-codex`, temporários, locks e partes `_parte-XX.pdf` como áreas especiais, não como arquivos comuns a renomear.
- Avisar sobre destinos com mais de 240 caracteres e bloquear os que excederem 260.
- Usar conteúdo e identidade explícita do documento antes de termos incidentais citados no corpo. Uma sentença que menciona uma matrícula continua sendo sentença.
- Parar antes da aplicação se houver dossiês distintos misturados, arquivos ambíguos relevantes ou sincronização concorrente.

## Fluxo obrigatório por etapas

Conduzir a execução como uma conversa com estado. Em cada resposta, dizer qual etapa terminou e qual decisão é esperada. Nunca atravessar a confirmação na mesma resposta em que a proposta foi criada ou revisada.

### Etapa 1 — Localizar duplicados

Resolver a raiz absoluta e informar que a análise inicial não altera arquivos de origem; somente artefatos internos são gravados em `.organiza-codex`.

Não usar uma pasta-pai que contenha vários dossiês `V###`/`P###`. Nesse caso, listar os dossiês e pedir que o usuário escolha um deles antes de continuar.

Executar:

```powershell
python <skill-dir>/scripts/folder_organizer.py analyze --root . --hash-all --find-similar --output .organiza-codex/analysis.json --markdown .organiza-codex/analysis.md
```

Usar caminhos absolutos para `<skill-dir>`. Ler `analysis.md` e consultar `analysis.json` para metadados, hashes, prévias de conteúdo, processos detectados, duplicados, pastas vazias e alertas de caminho.

Apresentar primeiro dois resultados distintos:

1. duplicatas exatas por tamanho + SHA-256, com principal sugerido, cópias e localização;
2. possíveis duplicatas por conteúdo, com percentual de similaridade e aviso de revisão manual.

Somente o primeiro grupo pode originar proposta para `98 DUPLICADOS`. Nunca tratar semelhança textual como identidade de arquivo. Não mover nada ainda.

Se `pdftotext` estiver disponível, o script extrai uma prévia das cinco primeiras páginas de PDFs. Em Windows, ao falhar por acentos ou caminho longo, ele repete a extração por uma cópia temporária de nome ASCII, sem alterar a origem. HTML deve ser analisado pelo texto visível, não pelo código-fonte. DOCX e outros arquivos textuais são lidos com recursos da biblioteca padrão. Para documentos ainda sem texto ou casos ambíguos, inspecionar o arquivo diretamente com as ferramentas disponíveis antes de classificá-lo. Criar qualquer cópia de inspeção dentro de `.organiza-codex` ou em diretório temporário gerenciado automaticamente; não deixar `tmp` solto na raiz.

### Etapa 2 — Comparar estrutura atual e estrutura desejada

- Mapear a árvore atual e compará-la com a estrutura adequada ao dossiê.
- Identificar o assunto principal da raiz, processos, imóvel/veículo e partes relevantes.
- Apontar arquivos pertencentes a outro dossiê; não incluí-los silenciosamente no plano.
- Revisar grupos duplicados por tamanho e SHA-256. Escolher como principal, nesta ordem: arquivo fora de `98/99`, caminho mais raso, nome mais informativo (preferir o nome descritivo mais longo) e caminho lexical.
- Tratar cópias já em `98/99` como preservadas, não como pendência.
- Não usar partes geradas de PDF, relatórios internos ou temporários como fonte para nomes.
- Usar a estrutura padrão como ponto de partida, mas preservar uma taxonomia personalizada quando ela for coerente.

### Etapa 3 — Propor nomes, destinos e tratamento dos duplicados

Ler [naming-and-categories.md](references/naming-and-categories.md) antes de classificar documentos jurídicos, imobiliários ou administrativos.

Gerar nomes legíveis e discriminantes. Preservar a extensão original. Quando disponível, incluir a identidade documental e um discriminador útil, como partes abreviadas e data, bloco inicial do processo, matrícula, placa ou ID curto correlacionável de guia/pagamento.

Não manter nomes genéricos ou referências `ref <hash>` quando o conteúdo fornecer identificador melhor. Não dar o mesmo nome a conteúdos diferentes.

### Etapa 4 — Criar e mostrar o artefato antes/depois

Ler [plan-schema.md](references/plan-schema.md). Criar `.organiza-codex/plan.json` com uma operação `move` por arquivo. Renomear e classificar são a mesma operação: a origem e o destino podem ter nome e pasta diferentes.

Definir `revision: 1`, `status: "awaiting_confirmation"`, `revision_notes` e `unresolved_items`. Cada operação deve conter o SHA-256 observado na análise. Não incluir operações para itens que já estão adequados. Não incluir exclusão de arquivo ou pasta.

Executar a pré-validação:

```powershell
python <skill-dir>/scripts/folder_organizer.py apply --root . --plan .organiza-codex/plan.json
```

Corrigir toda colisão, hash ausente, destino inseguro ou caminho excessivo antes de apresentar o plano.

Gerar o artefato visual:

```powershell
python <skill-dir>/scripts/folder_organizer.py render --root . --analysis .organiza-codex/analysis.json --plan .organiza-codex/plan.json --output .organiza-codex/proposta.md --html-output .organiza-codex/proposta.html
```

Os artefatos devem mostrar status/revisão, duplicados, árvore atual, árvore final simulada, tabela origem → destino → motivo, categorias novas, itens intactos ou pendentes e o hash exato do plano. A árvore final deve incluir também as pastas existentes que ficarão ou continuarão vazias; não simular sua exclusão.

Na resposta de revisão, mostrar obrigatoriamente, nesta ordem:

1. um bloco `text` com a árvore completa atual;
2. um bloco `text` com a árvore completa final simulada, incluindo pastas vazias preservadas;
3. uma tabela Markdown completa com `Origem`, `Destino` e `Motivo` para cada operação.

Quando a visualização inline estiver disponível, apresentar esse mesmo conteúdo nela, com os dois blocos antes/depois e a tabela explicativa. Ainda assim, não depender somente de arquivo local ou link para obter aprovação. Gerar e fornecer também `.organiza-codex/proposta.html` e `.organiza-codex/proposta.md` como apoio. Em links locais no Windows, usar caminho absoluto com barras `/` para não perder o separador antes de `.organiza-codex`.

O usuário deve conseguir avaliar toda a proposta diretamente na conversa, sem abrir JSON e sem depender do visualizador de Markdown.

Encerrar a resposta pedindo uma destas duas ações para a próxima mensagem:

1. observações/correções; ou
2. `Confirmo a revisão N`.

Não aplicar o plano nessa mesma resposta.

### Etapa 5 — Incorporar observações e atualizar a visualização

Se o usuário enviar observações:

- atualizar `plan.json`, incrementar `revision` e registrar as observações em `revision_notes`;
- reexecutar a pré-validação;
- regenerar `proposta.md` e `proposta.html` e reapresentar na conversa os dois blocos de árvore e a tabela completa;
- resumir o que mudou desde a revisão anterior;
- pedir nova confirmação e parar novamente.

Observações nunca significam aprovação implícita. Qualquer alteração no plano invalida o hash e a confirmação anteriores.

### Etapa 6 — Aplicar somente a revisão confirmada

Quando o usuário confirmar explicitamente a revisão mais recente, conferir que nenhum arquivo ou plano mudou desde a exibição. Usar internamente o hash exato mostrado no artefato e executar:

```powershell
python <skill-dir>/scripts/folder_organizer.py apply --root . --plan .organiza-codex/plan.json --execute --confirm <PLAN_SHA256>
```

O executor refaz a validação e, imediatamente antes de cada movimento, confere novamente existência e hash da origem e ausência do destino. Assim ele bloqueia sincronização concorrente e registra resultados em `.organiza-codex/history.jsonl`.

Se a pasta mudar entre a exibição, a confirmação e a execução, não recriar o plano silenciosamente. Reanalisar, atualizar o artefato e pedir nova confirmação.

### Etapa 7 — Verificar e mostrar o realizado

Executar novamente `analyze --hash-all` em `after-analysis.json` e `after-analysis.md`. Confirmar:

- destinos existem e preservam os hashes;
- origens planejadas deixaram de existir;
- nenhuma duplicidade ativa permanece fora de `98/99`, salvo exceção explicada;
- nenhum arquivo escapou da raiz;
- a raiz manteve nome e localização;
- itens não aprovados permaneceram intactos.

Entregar resumo antes/depois, link do artefato final atualizado e arquivos deixados para decisão manual.

Depois da verificação, atualizar o mesmo artefato para o estado aplicado, sem alterar o plano aprovado:

```powershell
python <skill-dir>/scripts/folder_organizer.py render --root . --analysis .organiza-codex/analysis.json --plan .organiza-codex/plan.json --output .organiza-codex/proposta.md --html-output .organiza-codex/proposta.html --state applied
```

## Casos especiais

- **Somente diagnóstico:** parar após a auditoria; não criar nem aplicar plano.
- **Somente renomear, sem reorganizar:** manter cada diretório atual no destino.
- **Estrutura personalizada já existente:** preferir pastas descritivas existentes; não criar pastas nuas `01` a `08`.
- **PDF acima de 97 MiB:** apenas relatar. Esta skill não divide PDF na primeira versão.
- **Google Drive ocupado, arquivo bloqueado ou estado oscilando:** interromper a aplicação e aguardar sincronização estável.
