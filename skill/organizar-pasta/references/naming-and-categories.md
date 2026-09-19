# Nomes e categorias

## Categorias padrão

Usar estas categorias quando combinarem com o dossiê. Reutilizar uma pasta descritiva já existente com o mesmo prefixo numérico; nunca preferir uma pasta nua como `05`.

| Pasta | Conteúdo típico |
|---|---|
| `01 CONTRATOS E PESQUISA` | contratos, propostas, pesquisas, fichas e comprovantes de transferência |
| `02 ARREMATAÇÃO E JUDICIAL` | edital, auto/carta de arrematação, depósito e pagamento da arrematação |
| `03 CARTÓRIO E REGISTRO` | matrícula, certidão, averbação, ITBI e nota de devolução |
| `04 REFORMA E BENFEITORIAS` | obras, materiais, arquitetura, engenharia e melhorias |
| `05 IPTU CONDOMÍNIO E FINANCEIRO` | IPTU, PPI, condomínio, boletos, guias, despesas e débitos |
| `06 LOCAÇÃO` | locação, aluguel, inquilino, despejo e documentos locatícios |
| `07 PROCESSOS E PEÇAS JUDICIAIS` | sentença, decisão, despacho, petição, contestação, recurso e contrarrazões |
| `08 RELATÓRIOS` | relatórios, laudos, pareceres, análises e resumos |
| `FOTOS` | imagens do imóvel, veículo, obra ou dossiê |
| `98 DUPLICADOS` | cópias exatas confirmadas por SHA-256 |
| `99 ORIGINAIS` | originais preservados por uma operação derivativa |

Se a pasta já tiver uma taxonomia coerente e específica, preservá-la e mapear os documentos nela; a tabela é padrão, não autorização para deformar uma estrutura válida.

## Ordem para identificar o documento

1. Cabeçalho ou identidade explícita nas primeiras páginas: `SENTENÇA`, `PETIÇÃO`, `CONTRATO`, `MATRÍCULA` etc.
2. Finalidade principal e partes envolvidas.
3. Identificadores estruturais: processo, matrícula, placa/Renavam, inscrição, autenticação ou código de barras.
4. Data de assinatura, emissão, juntada ou pagamento.
5. Nome original, apenas como evidência auxiliar.
6. Termos citados incidentalmente no corpo, com menor peso.

## Padrões úteis

- Contrato: `Contrato de compra e venda - Parte A x Parte B - DD-MM-AAAA.ext`
- Sentença: `Sentença - proc 0001571-37 - Cancelamento de penhora.ext`
- Matrícula: `Matrícula 20208 - atualizada - DD-MM-AAAA.ext`
- Guia: `Guia de IPTU - ID 315287 - DD-MM-AAAA.ext`
- Pagamento correlato: `Comprovante de pagamento de IPTU - ID 315287 - DD-MM-AAAA.ext`
- Foto: `AAAA-MM-DD - Foto do imóvel - contexto - HH-MM-SS.ext`

Usar apenas dados realmente presentes. Omitir campos ausentes em vez de escrever `sem data`, `N/A`, tags ou placeholders.

## Normalização

- Preservar a extensão e nunca convertê-la apenas para padronizar caixa.
- Remover caracteres inválidos do Windows: `< > : " / \\ | ? *`.
- Colapsar espaços e evitar repetição de anos ou prefixos.
- Manter o número CNJ integral nos metadados; no nome, o bloco `NNNNNNN-DD` costuma bastar.
- Para guias e comprovantes, usar o mesmo ID curto verificável, preferencialmente os seis últimos dígitos do código correlacionador.
- Se dois conteúdos diferentes ainda colidirem, acrescentar um discriminador humano. Usar hash curto somente como último recurso e deixar o caso explícito para revisão.
