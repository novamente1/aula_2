#!/usr/bin/env python3
"""Inventário e aplicação segura de planos para a skill organizar-pasta.

Usa somente a biblioteca padrão. Integrações opcionais com pdftotext e
tesseract são descobertas no PATH e nunca são requisitos para aplicar planos.
"""

from __future__ import annotations

import argparse
from difflib import SequenceMatcher
import hashlib
import html
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata
import uuid
import zipfile
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from xml.etree import ElementTree
from html.parser import HTMLParser


INTERNAL_DIR = ".organiza-codex"
APP_INTERNAL_DIR = ".organiza"
PROTECTED_NAMES = {"98 duplicados", "99 originais"}
PLAIN_TEXT_EXTENSIONS = {
    ".txt", ".md", ".json", ".csv", ".tsv", ".xml", ".html", ".htm", ".log", ".rtf"
}
IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp"}
PDF_PART_RE = re.compile(r"_parte-\d+(?:_\d+)?\.pdf$", re.IGNORECASE)
PROCESS_RE = re.compile(r"(?<!\d)(?:\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}|\d{20})(?!\d)")
TEMP_RE = re.compile(r"(?:\.tmp|\.lock)$", re.IGNORECASE)
MAX_PATH_WARNING = 240
MAX_PATH_BLOCK = 260
SIMILARITY_THRESHOLD = 0.86
TEMP_DIRECTORY_NAMES = {"tmp", "temp", "temporario", "temporarios", "temporary"}


def now_iso() -> str:
    return datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")


def normalize_name(value: str) -> str:
    decomposed = unicodedata.normalize("NFKD", value)
    return "".join(ch for ch in decomposed if not unicodedata.combining(ch)).strip().casefold()


def canonical_path(path: Path) -> str:
    return os.path.normcase(str(path.resolve()))


def is_inside(root: Path, candidate: Path) -> bool:
    try:
        return os.path.commonpath([canonical_path(root), canonical_path(candidate)]) == canonical_path(root)
    except ValueError:
        return False


def relative_parts(root: Path, path: Path) -> tuple[str, ...]:
    return path.resolve().relative_to(root.resolve()).parts


def classify_role(root: Path, path: Path) -> str:
    parts = relative_parts(root, path)
    if not parts:
        return "root"
    first = normalize_name(parts[0])
    name = path.name
    if first in {normalize_name(INTERNAL_DIR), normalize_name(APP_INTERNAL_DIR)}:
        return "internal"
    if first in TEMP_DIRECTORY_NAMES:
        return "temporary"
    if first in PROTECTED_NAMES:
        return "protected"
    if name.startswith(".$") or name.startswith("~$") or TEMP_RE.search(name):
        return "temporary"
    if PDF_PART_RE.search(name):
        return "generated_pdf_part"
    if name.casefold() in {
        ".organiza_log.json",
        "livro mestre 360.md",
        "livro mestre 360 - atualizado.md",
        ".organiza_livro_mestre_360.json",
    }:
        return "generated_artifact"
    return "candidate"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def clean_preview(text: str, limit: int) -> str:
    text = unicodedata.normalize("NFC", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text[:limit]


def extract_docx(path: Path) -> str:
    with zipfile.ZipFile(path) as archive:
        with archive.open("word/document.xml") as stream:
            root = ElementTree.parse(stream).getroot()
    return " ".join(node.text or "" for node in root.iter() if node.tag.endswith("}t"))


class VisibleHTMLText(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.parts: list[str] = []
        self.hidden_depth = 0

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag.casefold() in {"script", "style", "noscript"}:
            self.hidden_depth += 1

    def handle_endtag(self, tag: str) -> None:
        if tag.casefold() in {"script", "style", "noscript"} and self.hidden_depth:
            self.hidden_depth -= 1

    def handle_data(self, data: str) -> None:
        if not self.hidden_depth and data.strip():
            self.parts.append(data)


def extract_html(path: Path) -> str:
    parser = VisibleHTMLText()
    parser.feed(path.read_text(encoding="utf-8", errors="replace"))
    parser.close()
    return " ".join(parser.parts)


def run_with_safe_copy(
    path: Path,
    command_for: Any,
    timeout: int = 120,
) -> tuple[subprocess.CompletedProcess[bytes] | None, bool]:
    try:
        direct = subprocess.run(command_for(path), capture_output=True, timeout=timeout, check=False)
        if direct.returncode == 0:
            return direct, False
    except (OSError, subprocess.TimeoutExpired):
        direct = None

    try:
        with tempfile.TemporaryDirectory(prefix="organiza-codex-tool-") as directory:
            safe_path = Path(directory) / f"input{path.suffix.casefold()}"
            shutil.copy2(path, safe_path)
            fallback = subprocess.run(command_for(safe_path), capture_output=True, timeout=timeout, check=False)
            return fallback, True
    except (OSError, subprocess.TimeoutExpired):
        return direct, True


def extract_pdf(path: Path) -> tuple[str, str]:
    executable = shutil.which("pdftotext")
    if not executable:
        return "", "pdf_without_pdftotext"
    result, used_copy = run_with_safe_copy(
        path,
        lambda source: [executable, "-f", "1", "-l", "5", "-enc", "UTF-8", str(source), "-"],
    )
    if result is None:
        return "", "pdftotext_failed"
    if result.returncode != 0:
        return "", f"pdftotext_error_{result.returncode}"
    method = "pdftotext_pages_1_5_temp_copy" if used_copy else "pdftotext_pages_1_5"
    return result.stdout.decode("utf-8", errors="replace"), method


def extract_image(path: Path) -> tuple[str, str]:
    executable = shutil.which("tesseract")
    if not executable:
        return "", "image_without_tesseract"
    result, used_copy = run_with_safe_copy(
        path,
        lambda source: [executable, str(source), "stdout", "-l", "por+eng"],
    )
    if result is None:
        return "", "tesseract_failed"
    if result.returncode != 0:
        return "", f"tesseract_error_{result.returncode}"
    method = "tesseract_por_eng_temp_copy" if used_copy else "tesseract_por_eng"
    return result.stdout.decode("utf-8", errors="replace"), method


def extract_preview(path: Path, limit: int, ocr_images: bool) -> tuple[str, str]:
    extension = path.suffix.casefold()
    try:
        if extension in {".html", ".htm"}:
            return clean_preview(extract_html(path), limit), "html_visible_text"
        if extension in PLAIN_TEXT_EXTENSIONS:
            return clean_preview(path.read_text(encoding="utf-8", errors="replace"), limit), "plain_text"
        if extension == ".docx":
            return clean_preview(extract_docx(path), limit), "docx_xml"
        if extension == ".pdf":
            text, method = extract_pdf(path)
            return clean_preview(text, limit), method
        if extension in IMAGE_EXTENSIONS and ocr_images:
            text, method = extract_image(path)
            return clean_preview(text, limit), method
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, ElementTree.ParseError) as exc:
        return "", f"extract_error:{type(exc).__name__}"
    return "", "not_extracted"


def normalize_process(value: str) -> str:
    digits = "".join(ch for ch in value if ch.isdigit())
    if len(digits) == 20:
        return f"{digits[:7]}-{digits[7:9]}.{digits[9:13]}.{digits[13]}.{digits[14:16]}.{digits[16:]}"
    return value


def choose_principal(records: list[dict[str, Any]]) -> dict[str, Any]:
    def score(record: dict[str, Any]) -> tuple[int, int, int, str]:
        role_penalty = 1 if record["role"] in {"protected", "generated_pdf_part"} else 0
        depth = len(Path(record["relative_path"]).parts)
        name_length = len(Path(record["relative_path"]).name)
        return role_penalty, depth, -name_length, record["relative_path"].casefold()
    return min(records, key=score)


def find_similar_documents(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    candidates = [
        item for item in records
        if item.get("role") == "candidate"
        and len(item.get("preview", "")) >= 200
        and item.get("extension")
    ]
    pairs: list[dict[str, Any]] = []
    for index, left in enumerate(candidates):
        left_size = int(left.get("size_bytes", 0))
        left_preview = str(left["preview"]).casefold()
        for right in candidates[index + 1:]:
            if str(left["extension"]).casefold() != str(right["extension"]).casefold():
                continue
            if left.get("sha256") and left.get("sha256") == right.get("sha256"):
                continue
            right_size = int(right.get("size_bytes", 0))
            if not left_size or not right_size or min(left_size, right_size) / max(left_size, right_size) < 0.85:
                continue
            right_preview = str(right["preview"]).casefold()
            if min(len(left_preview), len(right_preview)) / max(len(left_preview), len(right_preview)) < 0.75:
                continue
            score = SequenceMatcher(None, left_preview, right_preview, autojunk=False).ratio()
            if score >= SIMILARITY_THRESHOLD:
                pairs.append({
                    "similarity": round(score, 4),
                    "paths": [left["relative_path"], right["relative_path"]],
                    "reason": "Conteúdo textual muito semelhante, mas SHA-256 diferente; revisar manualmente.",
                })
    return sorted(pairs, key=lambda item: (-item["similarity"], [path.casefold() for path in item["paths"]]))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f"{path.name}.{uuid.uuid4().hex}.tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def render_markdown(analysis: dict[str, Any]) -> str:
    lines = [
        "# Análise da pasta",
        "",
        f"- Raiz: `{analysis['root']}`",
        f"- Gerada em: {analysis['generated_at']}",
        f"- Arquivos: {analysis['summary']['files']}",
        f"- Pastas: {analysis['summary']['directories']}",
        f"- Candidatos à organização: {analysis['summary']['candidates']}",
        f"- Grupos duplicados: {analysis['summary']['duplicate_groups']}",
        f"- Possíveis duplicatas por conteúdo: {analysis['summary'].get('similar_document_pairs', 0)}",
        f"- Pastas vazias: {analysis['summary']['empty_directories']}",
        "",
    ]
    if analysis["duplicate_groups"]:
        lines.extend(["## Duplicados exatos", ""])
        for group in analysis["duplicate_groups"]:
            lines.append(f"- `{group['sha256']}` ({group['size_bytes']} bytes); manter `{group['suggested_principal']}`")
            for path in group["paths"]:
                lines.append(f"  - `{path}`")
        lines.append("")
    if analysis.get("similar_document_pairs"):
        lines.extend(["## Possíveis duplicatas por conteúdo", ""])
        lines.append("Apenas para revisão manual; nenhum arquivo deste grupo deve ser movido automaticamente para `98 DUPLICADOS`.")
        lines.append("")
        for pair in analysis["similar_document_pairs"]:
            lines.append(f"- Similaridade {pair['similarity']:.1%}: `{pair['paths'][0]}`")
            lines.append(f"  - Comparar com: `{pair['paths'][1]}`")
        lines.append("")
    if analysis["path_warnings"]:
        lines.extend(["## Caminhos longos", ""])
        for warning in analysis["path_warnings"]:
            lines.append(f"- {warning['length']} caracteres: `{warning['relative_path']}`")
        lines.append("")
    if analysis["empty_directories"]:
        lines.extend(["## Pastas vazias", ""])
        lines.extend(f"- `{path}`" for path in analysis["empty_directories"])
        lines.append("")
    lines.extend(["## Arquivos candidatos", ""])
    for item in analysis["files"]:
        if item["role"] != "candidate":
            continue
        identity = ", ".join(item["process_ids"]) or "sem processo detectado"
        preview = item["preview"][:240]
        lines.append(f"- `{item['relative_path']}` — {item['size_bytes']} bytes; {identity}; {item['extraction_method']}")
        if preview:
            lines.append(f"  - Prévia: {preview}")
    return "\n".join(lines).rstrip() + "\n"


def tree_lines(paths: list[str], empty_directories: list[str] | None = None) -> list[str]:
    tree: dict[str, Any] = {}
    for relative in paths:
        node = tree
        for part in Path(relative).parts:
            node = node.setdefault(part, {})
        node["__file__"] = True
    for relative in empty_directories or []:
        node = tree
        for part in Path(relative).parts:
            node = node.setdefault(part, {})
        node["__empty__"] = True

    lines: list[str] = []

    def walk(node: dict[str, Any], prefix: str) -> None:
        names = sorted((name for name in node if not name.startswith("__")), key=str.casefold)
        for index, name in enumerate(names):
            child = node[name]
            last = index == len(names) - 1
            connector = "└── " if last else "├── "
            has_children = any(not key.startswith("__") for key in child)
            suffix = "/" if has_children else ""
            if child.get("__empty__") and not child.get("__file__"):
                suffix = "/ (vazia)"
            lines.append(f"{prefix}{connector}{name}{suffix}")
            walk(child, prefix + ("    " if last else "│   "))

    walk(tree, "")
    return lines


def parent_directories(paths: list[str]) -> set[str]:
    directories: set[str] = set()
    for relative in paths:
        parent = Path(relative).parent
        while str(parent) not in {"", "."}:
            directories.add(str(parent))
            parent = parent.parent
    return directories


def proposal_paths(analysis: dict[str, Any], plan: dict[str, Any]) -> tuple[list[str], list[str], list[str]]:
    current_paths = [
        item["relative_path"] for item in analysis.get("files", [])
        if item.get("role") != "internal" and "relative_path" in item
    ]
    source_map = {str(operation["source"]): str(operation["destination"]) for operation in plan["operations"]}
    proposed_paths = [source_map.get(path, path) for path in current_paths]

    existing_directories = parent_directories(current_paths)
    existing_directories.update(str(path) for path in analysis.get("empty_directories", []))
    final_directories = existing_directories | parent_directories(proposed_paths)
    nonempty_directories = parent_directories(proposed_paths)
    directories_with_children = {
        str(Path(directory).parent)
        for directory in final_directories
        if str(Path(directory).parent) not in {"", "."}
    }
    proposed_empty = sorted(
        final_directories - nonempty_directories - directories_with_children,
        key=str.casefold,
    )
    return current_paths, proposed_paths, proposed_empty


def render_proposal(
    analysis: dict[str, Any],
    plan: dict[str, Any],
    digest: str,
    state_override: str | None = None,
) -> str:
    operations = plan["operations"]
    current_paths, proposed_paths, proposed_empty = proposal_paths(analysis, plan)
    current_parents = {str(Path(path).parts[0]).casefold() for path in current_paths if Path(path).parts}
    destination_parents = {
        str(Path(operation["destination"]).parts[0])
        for operation in operations if Path(operation["destination"]).parts
    }
    created_categories = sorted(
        (name for name in destination_parents if name.casefold() not in current_parents), key=str.casefold
    )

    renamed = moved = moved_and_renamed = duplicates = 0
    for operation in operations:
        source, destination = Path(operation["source"]), Path(operation["destination"])
        parent_changed = source.parent != destination.parent
        name_changed = source.name != destination.name
        if destination.parts and normalize_name(destination.parts[0]) == "98 duplicados":
            duplicates += 1
        elif parent_changed and name_changed:
            moved_and_renamed += 1
        elif parent_changed:
            moved += 1
        elif name_changed:
            renamed += 1

    revision = plan.get("revision", 1)
    status = state_override or plan.get("status", "awaiting_confirmation")
    lines = [
        f"# Proposta de organização — revisão {revision}",
        "",
        f"**Status:** `{status}`  ",
        f"**Raiz protegida:** `{analysis['root']}`  ",
        f"**Hash do plano:** `{digest}`",
        "",
        "## Resumo da proposta",
        "",
        "| Ação | Quantidade |",
        "|---|---:|",
        f"| Apenas renomear | {renamed} |",
        f"| Apenas mover | {moved} |",
        f"| Renomear e mover | {moved_and_renamed} |",
        f"| Mover cópia para 98 DUPLICADOS | {duplicates} |",
        f"| Categorias novas | {len(created_categories)} |",
        "",
    ]
    if created_categories:
        lines.extend([
            "**Categorias que serão criadas:** " + ", ".join(f"`{name}`" for name in created_categories),
            "",
        ])

    duplicate_groups = analysis.get("duplicate_groups", [])
    lines.extend(["## Etapa 1 — Duplicados", ""])
    if not duplicate_groups:
        lines.extend(["Nenhuma duplicidade exata foi encontrada por tamanho + SHA-256.", ""])
    else:
        for group in duplicate_groups:
            lines.append(f"- `{group['sha256']}` — manter `{group['suggested_principal']}`")
            for path in group["paths"]:
                lines.append(f"  - `{path}`")
        lines.append("")
    similar_pairs = analysis.get("similar_document_pairs", [])
    lines.extend(["### Possíveis duplicatas por conteúdo", ""])
    if not similar_pairs:
        lines.extend(["Nenhum par de conteúdo muito semelhante foi detectado.", ""])
    else:
        lines.extend([
            "Revisão manual obrigatória: os hashes são diferentes e estes itens não podem ser tratados como duplicatas exatas.",
            "",
        ])
        for pair in similar_pairs:
            lines.append(f"- Similaridade {pair['similarity']:.1%}: `{pair['paths'][0]}`")
            lines.append(f"  - Comparar com: `{pair['paths'][1]}`")
        lines.append("")

    lines.extend(["## Estrutura atual", "", "```text"])
    lines.extend(tree_lines(current_paths, analysis.get("empty_directories", [])) or ["(pasta vazia)"])
    lines.extend(["```", "", "## Estrutura proposta", "", "```text"])
    lines.extend(tree_lines(sorted(set(proposed_paths), key=str.casefold), proposed_empty) or ["(pasta vazia)"])
    lines.extend([
        "```", "", "## Alterações propostas", "",
        "| Origem | Destino | Motivo |", "|---|---|---|",
    ])
    for operation in operations:
        reason = str(operation.get("reason", "")).replace("|", "\\|")
        source = str(operation["source"]).replace("|", "\\|")
        destination = str(operation["destination"]).replace("|", "\\|")
        lines.append(f"| `{source}` | `{destination}` | {reason} |")
    lines.append("")

    notes = plan.get("revision_notes") or []
    if notes:
        lines.extend(["## Histórico desta revisão", ""])
        lines.extend(f"- {note}" for note in notes)
        lines.append("")
    unresolved = plan.get("unresolved_items") or []
    lines.extend(["## Itens sem alteração ou pendentes", ""])
    lines.extend(f"- {item}" for item in unresolved)
    if not unresolved:
        lines.append("Nenhuma pendência declarada.")
    if status == "applied":
        lines.extend([
            "", "## Resultado", "",
            f"A revisão {revision} foi aplicada. Consulte `after-analysis.md` e `history.jsonl` para a verificação detalhada.",
            "",
        ])
    else:
        lines.extend([
            "", "## Próxima decisão", "",
            f"Envie observações para gerar a revisão {revision + 1} ou responda **Confirmo a revisão {revision}**.",
            "Nenhuma alteração física foi executada por este artefato.", "",
        ])
    return "\n".join(lines)


def render_proposal_html(
    analysis: dict[str, Any],
    plan: dict[str, Any],
    digest: str,
    state_override: str | None = None,
) -> str:
    current_paths, proposed_paths, proposed_empty = proposal_paths(analysis, plan)
    current_tree = "\n".join(tree_lines(current_paths, analysis.get("empty_directories", [])) or ["(pasta vazia)"])
    proposed_tree = "\n".join(
        tree_lines(sorted(set(proposed_paths), key=str.casefold), proposed_empty) or ["(pasta vazia)"]
    )
    revision = plan.get("revision", 1)
    status = state_override or plan.get("status", "awaiting_confirmation")
    operations = plan["operations"]
    rows = []
    for operation in operations:
        rows.append(
            "<tr>"
            f"<td><code>{html.escape(str(operation['source']))}</code></td>"
            f"<td><code>{html.escape(str(operation['destination']))}</code></td>"
            f"<td>{html.escape(str(operation.get('reason', '')))}</td>"
            "</tr>"
        )
    unresolved = plan.get("unresolved_items") or []
    unresolved_html = "".join(f"<li>{html.escape(str(item))}</li>" for item in unresolved)
    duplicate_count = len(analysis.get("duplicate_groups", []))
    similar_count = len(analysis.get("similar_document_pairs", []))
    return f"""<!doctype html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Proposta de organização — revisão {revision}</title>
<style>
:root{{--bg:#f5f7fa;--paper:#fff;--ink:#172033;--muted:#647086;--line:#d9dee8;--accent:#1769aa;--code:#eef2f6}}
@media(prefers-color-scheme:dark){{:root{{--bg:#11151c;--paper:#1a202a;--ink:#edf2f7;--muted:#aeb8c8;--line:#394354;--accent:#71b7ee;--code:#111821}}}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--bg);color:var(--ink);font:16px/1.5 Segoe UI,Arial,sans-serif}}
main{{max-width:1400px;margin:24px auto;padding:28px;background:var(--paper)}}h1{{margin-top:0}}h2{{margin-top:28px;border-bottom:1px solid var(--line);padding-bottom:8px}}
.meta{{color:var(--muted)}}.summary{{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin:20px 0}}
.metric{{border:1px solid var(--line);padding:14px;border-radius:8px}}.metric strong{{display:block;font-size:1.5rem}}
.compare{{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px}}pre{{margin:0;background:var(--code);border:1px solid var(--line);border-radius:8px;padding:14px;white-space:pre-wrap;overflow-wrap:anywhere}}
.table-wrap{{overflow:auto;border:1px solid var(--line);border-radius:8px}}table{{width:100%;border-collapse:collapse;min-width:800px}}th,td{{padding:10px 12px;text-align:left;vertical-align:top;border-bottom:1px solid var(--line)}}th{{background:var(--code);position:sticky;top:0}}code{{overflow-wrap:anywhere}}.notice{{margin-top:20px;padding:14px;border-left:4px solid var(--accent);background:var(--code)}}
@media(max-width:800px){{main{{margin:0;padding:18px}}.summary,.compare{{grid-template-columns:1fr}}}}
</style>
</head>
<body><main>
<h1>Proposta de organização — revisão {revision}</h1>
<p class="meta"><strong>Status:</strong> {html.escape(str(status))}<br><strong>Hash do plano:</strong> <code>{digest}</code></p>
<div class="summary">
  <div class="metric"><span>Duplicatas exatas</span><strong>{duplicate_count}</strong></div>
  <div class="metric"><span>Possíveis duplicatas</span><strong>{similar_count}</strong></div>
  <div class="metric"><span>Arquivos alterados</span><strong>{len(operations)}</strong></div>
  <div class="metric"><span>Arquivos intactos</span><strong>{len(current_paths) - len(operations)}</strong></div>
</div>
<div class="compare">
  <section><h2>Antes — estrutura atual</h2><pre><code>{html.escape(current_tree)}</code></pre></section>
  <section><h2>Depois — estrutura simulada</h2><pre><code>{html.escape(proposed_tree)}</code></pre></section>
</div>
<h2>Alterações propostas e motivos</h2>
<div class="table-wrap"><table><thead><tr><th>Origem</th><th>Destino</th><th>Motivo</th></tr></thead><tbody>{''.join(rows)}</tbody></table></div>
<h2>Itens intactos ou pendentes</h2>
<ul>{unresolved_html or '<li>Nenhuma pendência declarada.</li>'}</ul>
<div class="notice"><strong>Nenhuma alteração física é executada por esta visualização.</strong> Revise a árvore e cada linha da tabela antes de confirmar.</div>
</main></body></html>"""


def analyze(args: argparse.Namespace) -> int:
    root = Path(args.root).resolve()
    if not root.is_dir():
        raise SystemExit(f"Raiz inexistente ou inválida: {root}")

    directories = [
        path for path in root.rglob("*")
        if path.is_dir() and classify_role(root, path) in {"candidate", "protected"}
    ]
    all_files = [path for path in root.rglob("*") if path.is_file()]
    visible_files = [
        path for path in all_files
        if classify_role(root, path) in {"candidate", "protected"}
    ]
    size_counts: dict[int, int] = defaultdict(int)
    for path in visible_files:
        try:
            size_counts[path.stat().st_size] += 1
        except OSError:
            pass

    records: list[dict[str, Any]] = []
    for index, path in enumerate(sorted(visible_files, key=lambda p: str(p).casefold()), start=1):
        try:
            stat = path.stat()
            role = classify_role(root, path)
            should_hash = args.hash_all or size_counts[stat.st_size] > 1
            digest = sha256_file(path) if should_hash else None
            preview, method = extract_preview(path, args.max_preview_chars, args.ocr_images)
            relative = str(path.relative_to(root))
            records.append({
                "relative_path": relative,
                "size_bytes": stat.st_size,
                "modified_at": datetime.fromtimestamp(stat.st_mtime, timezone.utc).astimezone().isoformat(timespec="seconds"),
                "extension": path.suffix,
                "sha256": digest,
                "role": role,
                "candidate": role == "candidate",
                "extraction_method": method,
                "preview": preview,
                "process_ids": sorted({normalize_process(match.group(0)) for match in PROCESS_RE.finditer(f"{relative} {preview}")}),
            })
            if not args.quiet:
                print(f"[{index}/{len(visible_files)}] {relative}", file=sys.stderr)
        except OSError as exc:
            records.append({"relative_path": str(path.relative_to(root)), "role": "unreadable", "error": str(exc)})

    by_key: dict[tuple[int, str], list[dict[str, Any]]] = defaultdict(list)
    for record in records:
        if record.get("role") in {"candidate", "protected"} and record.get("sha256") and "size_bytes" in record:
            by_key[(record["size_bytes"], record["sha256"])].append(record)
    duplicate_groups = []
    for (size, digest), members in by_key.items():
        if len(members) < 2:
            continue
        principal = choose_principal(members)
        duplicate_groups.append({
            "size_bytes": size,
            "sha256": digest,
            "suggested_principal": principal["relative_path"],
            "paths": [member["relative_path"] for member in members],
        })
    duplicate_groups.sort(key=lambda group: (-group["size_bytes"], group["sha256"]))
    similar_document_pairs = find_similar_documents(records) if args.find_similar else []

    empty_directories = []
    for path in sorted(directories, key=lambda p: len(p.parts), reverse=True):
        try:
            if not any(path.iterdir()):
                empty_directories.append(str(path.relative_to(root)))
        except OSError:
            pass
    path_warnings = [
        {"relative_path": record["relative_path"], "length": len(str(root / record["relative_path"]))}
        for record in records if "relative_path" in record and len(str(root / record["relative_path"])) > MAX_PATH_WARNING
    ]

    analysis = {
        "schema_version": 1,
        "root": str(root),
        "generated_at": now_iso(),
        "summary": {
            "files": len(records),
            "directories": len(directories),
            "candidates": sum(1 for item in records if item.get("role") == "candidate"),
            "duplicate_groups": len(duplicate_groups),
            "similar_document_pairs": len(similar_document_pairs),
            "empty_directories": len(empty_directories),
        },
        "duplicate_groups": duplicate_groups,
        "similar_document_pairs": similar_document_pairs,
        "empty_directories": sorted(empty_directories, key=str.casefold),
        "path_warnings": sorted(path_warnings, key=lambda item: -item["length"]),
        "files": records,
    }
    if args.output:
        output = (root / args.output).resolve() if not Path(args.output).is_absolute() else Path(args.output)
        if not is_inside(root, output):
            raise SystemExit("O relatório JSON deve ficar dentro da raiz analisada.")
        write_json(output, analysis)
    if args.markdown:
        markdown = (root / args.markdown).resolve() if not Path(args.markdown).is_absolute() else Path(args.markdown)
        if not is_inside(root, markdown):
            raise SystemExit("O relatório Markdown deve ficar dentro da raiz analisada.")
        markdown.parent.mkdir(parents=True, exist_ok=True)
        markdown.write_text(render_markdown(analysis), encoding="utf-8")
    if not args.output:
        print(json.dumps(analysis, ensure_ascii=False, indent=2))
    else:
        print(json.dumps({"status": "ok", "output": str(output), "summary": analysis["summary"]}, ensure_ascii=False))
    return 0


def load_plan(path: Path) -> dict[str, Any]:
    plan = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(plan, dict):
        raise ValueError("O plano deve ser um objeto JSON.")
    return plan


def plan_hash(plan: dict[str, Any]) -> str:
    canonical = json.dumps(plan, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest().upper()


def validate_plan(root: Path, plan: dict[str, Any]) -> list[dict[str, Any]]:
    if plan.get("version") != 1:
        raise ValueError("Versão do plano não suportada; esperado version=1.")
    if canonical_path(Path(plan.get("root", ""))) != canonical_path(root):
        raise ValueError("A raiz declarada no plano difere da raiz informada.")
    operations = plan.get("operations")
    if not isinstance(operations, list) or not operations:
        raise ValueError("O plano precisa conter ao menos uma operação.")

    seen_sources: set[str] = set()
    seen_destinations: set[str] = set()
    validated: list[dict[str, Any]] = []
    for index, operation in enumerate(operations, start=1):
        if not isinstance(operation, dict) or operation.get("op") != "move":
            raise ValueError(f"Operação {index}: somente op='move' é permitida.")
        source_rel = operation.get("source")
        destination_rel = operation.get("destination")
        expected = str(operation.get("expected_sha256", "")).upper()
        if not source_rel or not destination_rel or not re.fullmatch(r"[A-F0-9]{64}", expected):
            raise ValueError(f"Operação {index}: source, destination e expected_sha256 válido são obrigatórios.")
        source = (root / source_rel).resolve()
        destination = (root / destination_rel).resolve()
        if not is_inside(root, source) or not is_inside(root, destination) or source == root or destination == root:
            raise ValueError(f"Operação {index}: origem ou destino fora da raiz protegida.")
        source_key, destination_key = canonical_path(source), canonical_path(destination)
        if source_key in seen_sources or destination_key in seen_destinations:
            raise ValueError(f"Operação {index}: origem ou destino repetido no plano.")
        seen_sources.add(source_key)
        seen_destinations.add(destination_key)
        if source_key == destination_key and str(source) == str(destination):
            raise ValueError(f"Operação {index}: origem e destino são idênticos.")
        if not source.is_file():
            raise ValueError(f"Operação {index}: origem não existe ou não é arquivo: {source_rel}")
        if destination.exists() and canonical_path(source) != canonical_path(destination):
            raise ValueError(f"Operação {index}: destino já existe: {destination_rel}")
        if len(str(destination)) > MAX_PATH_BLOCK:
            raise ValueError(f"Operação {index}: destino excede {MAX_PATH_BLOCK} caracteres.")
        actual = sha256_file(source)
        if actual != expected:
            raise ValueError(f"Operação {index}: SHA-256 mudou para {source_rel}; reanalise a pasta.")
        validated.append({
            "index": index,
            "source": source,
            "destination": destination,
            "expected_sha256": expected,
            "reason": operation.get("reason", ""),
            "warning": len(str(destination)) > MAX_PATH_WARNING,
        })
    return validated


def render(args: argparse.Namespace) -> int:
    root = Path(args.root).resolve()
    if not root.is_dir():
        raise ValueError(f"Raiz inexistente ou inválida: {root}")
    analysis_path = Path(args.analysis)
    plan_path = Path(args.plan)
    output_path = Path(args.output)
    html_output_path = Path(args.html_output) if args.html_output else None
    if not analysis_path.is_absolute():
        analysis_path = (root / analysis_path).resolve()
    if not plan_path.is_absolute():
        plan_path = (root / plan_path).resolve()
    if not output_path.is_absolute():
        output_path = (root / output_path).resolve()
    if html_output_path is not None and not html_output_path.is_absolute():
        html_output_path = (root / html_output_path).resolve()
    checked_paths = [analysis_path, plan_path, output_path]
    if html_output_path is not None:
        checked_paths.append(html_output_path)
    if not all(is_inside(root, path) for path in checked_paths):
        raise ValueError("Análise, plano e artefato devem ficar dentro da raiz protegida.")
    analysis = json.loads(analysis_path.read_text(encoding="utf-8-sig"))
    plan = load_plan(plan_path)
    if canonical_path(Path(analysis.get("root", ""))) != canonical_path(root):
        raise ValueError("A análise pertence a outra raiz.")
    if args.state != "applied":
        validate_plan(root, plan)
    digest = plan_hash(plan)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(render_proposal(analysis, plan, digest, args.state), encoding="utf-8")
    if html_output_path is not None:
        html_output_path.parent.mkdir(parents=True, exist_ok=True)
        html_output_path.write_text(
            render_proposal_html(analysis, plan, digest, args.state),
            encoding="utf-8-sig",
        )
    print(json.dumps({
        "status": "rendered",
        "revision": plan.get("revision", 1),
        "plan_sha256": digest,
        "output": str(output_path),
        "html_output": str(html_output_path) if html_output_path is not None else None,
    }, ensure_ascii=False, indent=2))
    return 0


def move_case_safe(root: Path, source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    if canonical_path(source) == canonical_path(destination):
        internal = root / INTERNAL_DIR
        internal.mkdir(parents=True, exist_ok=True)
        temporary = internal / f"rename-{uuid.uuid4().hex}.tmp"
        shutil.move(str(source), str(temporary))
        try:
            shutil.move(str(temporary), str(destination))
        except Exception:
            if temporary.exists() and not source.exists():
                shutil.move(str(temporary), str(source))
            raise
    else:
        shutil.move(str(source), str(destination))


def append_history(root: Path, entry: dict[str, Any]) -> None:
    directory = root / INTERNAL_DIR
    directory.mkdir(parents=True, exist_ok=True)
    with (directory / "history.jsonl").open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(entry, ensure_ascii=False) + "\n")
        stream.flush()
        os.fsync(stream.fileno())


def verify_move_ready(item: dict[str, Any]) -> None:
    source, destination = item["source"], item["destination"]
    if not source.is_file():
        raise OSError("A origem deixou de existir antes da movimentação.")
    if sha256_file(source) != item["expected_sha256"]:
        raise OSError("A origem mudou após a validação; sincronização concorrente detectada.")
    if destination.exists() and canonical_path(source) != canonical_path(destination):
        raise FileExistsError("O destino passou a existir após a validação.")


def apply_plan(args: argparse.Namespace) -> int:
    root = Path(args.root).resolve()
    if not root.is_dir():
        raise SystemExit(f"Raiz inexistente ou inválida: {root}")
    plan_path = Path(args.plan)
    if not plan_path.is_absolute():
        plan_path = (root / plan_path).resolve()
    plan = load_plan(plan_path)
    digest = plan_hash(plan)
    validated = validate_plan(root, plan)
    report = {
        "status": "validated" if not args.execute else "ready",
        "plan_sha256": digest,
        "operations": len(validated),
        "path_warnings": [item["index"] for item in validated if item["warning"]],
    }
    if not args.execute:
        print(json.dumps(report, ensure_ascii=False, indent=2))
        return 0
    if not args.confirm or args.confirm.upper() != digest:
        raise SystemExit("Execução bloqueada: --confirm deve ser o SHA-256 exato do plano validado.")

    results = []
    for item in validated:
        source, destination = item["source"], item["destination"]
        entry = {
            "timestamp": now_iso(),
            "plan_sha256": digest,
            "operation": "move",
            "source": str(source.relative_to(root)),
            "destination": str(destination.relative_to(root)),
            "expected_sha256": item["expected_sha256"],
            "reason": item["reason"],
        }
        try:
            verify_move_ready(item)
            move_case_safe(root, source, destination)
            if source.exists() or not destination.is_file() or sha256_file(destination) != item["expected_sha256"]:
                raise OSError("A verificação física após a movimentação falhou.")
            entry["status"] = "completed"
        except Exception as exc:
            entry["status"] = "failed"
            entry["error"] = f"{type(exc).__name__}: {exc}"
            append_history(root, entry)
            results.append(entry)
            print(json.dumps({"status": "failed", "plan_sha256": digest, "results": results}, ensure_ascii=False, indent=2))
            return 2
        append_history(root, entry)
        results.append(entry)
    print(json.dumps({"status": "completed", "plan_sha256": digest, "results": results}, ensure_ascii=False, indent=2))
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Inventário e aplicação segura para organizar-pasta")
    subparsers = parser.add_subparsers(dest="command", required=True)

    analyze_parser = subparsers.add_parser("analyze", help="Inventariar sem alterar arquivos de origem")
    analyze_parser.add_argument("--root", default=".")
    analyze_parser.add_argument("--hash-all", action="store_true", help="Calcular SHA-256 de todos os arquivos")
    analyze_parser.add_argument(
        "--find-similar",
        action="store_true",
        help="Sinalizar documentos textuais muito semelhantes com hashes diferentes",
    )
    analyze_parser.add_argument("--ocr-images", action="store_true", help="Usar Tesseract em imagens quando disponível")
    analyze_parser.add_argument("--max-preview-chars", type=int, default=6000)
    analyze_parser.add_argument("--output", help="Caminho JSON relativo à raiz")
    analyze_parser.add_argument("--markdown", help="Caminho Markdown relativo à raiz")
    analyze_parser.add_argument("--quiet", action="store_true")
    analyze_parser.set_defaults(handler=analyze)

    apply_parser = subparsers.add_parser("apply", help="Validar ou aplicar um plano aprovado")
    apply_parser.add_argument("--root", default=".")
    apply_parser.add_argument("--plan", required=True)
    apply_parser.add_argument("--execute", action="store_true")
    apply_parser.add_argument("--confirm")
    apply_parser.set_defaults(handler=apply_plan)

    render_parser = subparsers.add_parser("render", help="Gerar artefatos Markdown e HTML antes/depois")
    render_parser.add_argument("--root", default=".")
    render_parser.add_argument("--analysis", required=True)
    render_parser.add_argument("--plan", required=True)
    render_parser.add_argument("--output", required=True)
    render_parser.add_argument("--html-output", help="Caminho opcional para a visualização HTML")
    render_parser.add_argument("--state", choices=["awaiting_confirmation", "applied"])
    render_parser.set_defaults(handler=render)
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    try:
        return args.handler(args)
    except (ValueError, OSError, json.JSONDecodeError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
