# Esquema do plano

Criar JSON UTF-8 com esta estrutura:

```json
{
  "version": 1,
  "root": "C:\\caminho\\absoluto\\do\\dossie",
  "created_at": "2026-08-04T15:30:00-03:00",
  "revision": 1,
  "status": "awaiting_confirmation",
  "revision_notes": ["Proposta inicial"],
  "unresolved_items": ["Documento sem texto útil mantido no local atual"],
  "operations": [
    {
      "op": "move",
      "source": "arquivo antigo.pdf",
      "destination": "07 PROCESSOS E PEÇAS JUDICIAIS\\Sentença - proc 0001571-37.pdf",
      "expected_sha256": "HASH_SHA256_EM_HEXADECIMAL",
      "reason": "Cabeçalho SENTENÇA e processo identificados no conteúdo"
    }
  ]
}
```

## Regras

- `root` deve ser o caminho absoluto exato analisado.
- `revision` deve começar em 1 e aumentar sempre que as observações mudarem o plano.
- `status` deve permanecer `awaiting_confirmation` no plano exibido e aprovado. Não editar o plano depois da confirmação, pois isso mudaria seu hash; o artefato pode receber o estado visual `applied` após a execução.
- `revision_notes` registra de forma curta o que mudou em cada rodada.
- `unresolved_items` lista ambiguidades mantidas fora das operações.
- `source` e `destination` devem ser caminhos relativos à raiz.
- Usar somente `op: "move"`. Uma movimentação também pode renomear o arquivo.
- `expected_sha256` é obrigatório e deve vir da análise mais recente.
- Cada origem e destino deve aparecer uma única vez.
- O destino não pode existir na pré-validação.
- Não criar operação cujo destino seja idêntico à origem.
- Para duplicados, usar como destino `98 DUPLICADOS\\<nome>` e explicar o grupo SHA-256 em `reason`.
- Resolver colisões no próprio plano, com nome humano ou sufixo revisado. O executor nunca inventa um destino diferente do aprovado.
- Não incluir operações para excluir arquivos, remover pastas ou alterar a raiz.

O hash do plano é SHA-256 sobre o JSON canônico calculado pelo executor. Qualquer mudança no plano exige nova pré-validação e nova aprovação.
