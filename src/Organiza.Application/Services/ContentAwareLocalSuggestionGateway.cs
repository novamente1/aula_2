using System.Text.RegularExpressions;
using System.Globalization;
using Organiza.Application.Abstractions;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed partial class ContentAwareLocalSuggestionGateway : IContentSuggestionGateway
{
    public Task<IReadOnlyList<RenameSuggestion>> GenerateAsync(
        IReadOnlyList<DocumentAnalysis> documents,
        CancellationToken cancellationToken = default)
    {
        var result = documents.Select(CreateSuggestion).ToArray();
        return Task.FromResult<IReadOnlyList<RenameSuggestion>>(result);
    }

    private static RenameSuggestion CreateSuggestion(DocumentAnalysis document)
    {
        var extension = document.File.Extension;
        var originalStem = CleanLegacyCopyMarkers(Path.GetFileNameWithoutExtension(document.File.Name));
        if (IsImage(extension) && !LooksLikeScannedDocument(document.FullText))
            return CreateImageSuggestion(document, originalStem, extension);
        if (RequiresQualityReview(document))
        {
            var preservedName = NormalizeSuggestionStem(originalStem) + extension;
            return new(document.File.FullPath, preservedName, true,
                "REVISAO-QUALIDADE-OCR: a leitura não forneceu evidência suficiente para identificar o documento com segurança. O nome atual foi preservado; confira visualmente antes de aprovar qualquer alteração.",
                StandardFolders.QualityReview, "REVISAO-QUALIDADE-OCR",
                "Não identificado — qualidade insuficiente",
                "Aguardando leitura humana; nenhum campo foi inventado.",
                "Baixa legibilidade — revisão obrigatória");
        }
        string stem;
        string documentType;
        if (!string.IsNullOrWhiteSpace(document.FullText))
        {
            documentType = DetectDocumentType(document.FullText, originalStem);
            var parts = new List<string>();
            var documentDate = SelectDocumentDate(document, documentType);
            if (!string.IsNullOrWhiteSpace(documentDate)) parts.Add(ToIsoDate(documentDate));
            parts.Add(documentType);
            var parties = DetectAbbreviatedParties(document.FullText, documentType);
            if (!string.IsNullOrWhiteSpace(parties)) parts.Add(parties);
            var subject = DetectSubject(document.FullText, documentType);
            if (string.IsNullOrWhiteSpace(parties) && !string.IsNullOrWhiteSpace(subject) &&
                !documentType.Contains(subject, StringComparison.OrdinalIgnoreCase)) parts.Add(subject);
            if (document.ProcessIdentifiers.Count > 0)
                parts.Add($"proc {ShortenProcessIdentifier(document.ProcessIdentifiers[0])}");
            var trackingId = DetectTrackingId(document.FullText, documentType);
            if (!string.IsNullOrWhiteSpace(trackingId)) parts.Add($"ID {trackingId}");
            var plate = VehiclePlateRegex().Match(document.FullText).Value;
            if (!string.IsNullOrWhiteSpace(plate)) parts.Add(plate.ToUpperInvariant());
            stem = string.Join(" - ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        else
        {
            stem = originalStem;
            documentType = "Não determinado — sem texto reconhecido";
        }

        stem = NormalizeSuggestionStem(stem);

        var method = document.ExtractionMethod switch
        {
            TextExtractionMethod.PdfText => "texto integral do PDF",
            TextExtractionMethod.Ocr => "OCR integral do documento",
            TextExtractionMethod.MixedPdfTextAndOcr => "texto integral e OCR complementar",
            TextExtractionMethod.OfficeOpenXml => "conteúdo integral do documento Office",
            TextExtractionMethod.PlainText => "conteúdo textual integral",
            TextExtractionMethod.NoTextFound => "nome original; nenhum texto reconhecido",
            _ => "metadados locais"
        };
        var destination = DetectDestinationFolder(document, stem);
        var classificationRule = DetectClassificationRule(documentType, destination, document.FullText);
        return new(document.File.FullPath, stem + extension, true,
            $"{classificationRule}: sugestão local baseada em {method}; categoria {destination}. Protocolo oficial: data comprovada, descrição, identificadores disponíveis e extensão preservada, sem campos inventados.",
            destination, classificationRule, documentType,
            BuildProtocolStatus(document, stem), "Leitura suficiente para classificação");
    }

    private static bool RequiresQualityReview(DocumentAnalysis document)
    {
        if (document.ExtractionMethod == TextExtractionMethod.NotApplicable) return false;
        if (!document.ExtractionComplete || document.ExtractionWarnings is { Count: > 0 }) return true;
        if (document.ExtractionMethod == TextExtractionMethod.NoTextFound) return true;
        if (document.ExtractionMethod != TextExtractionMethod.Ocr) return false;

        var usefulCharacters = document.FullText.Count(char.IsLetterOrDigit);
        if (usefulCharacters < 80) return true;
        return !ContainsAny(document.FullText,
            "carteira de identidade", "registro geral", "secretaria de segurança", "cpf",
            "carteira nacional de habilitação", "detran", "senatran", "crlv", "renavam",
            "processo", "poder judiciário", "contrato", "certidão", "matrícula", "iptu",
            "comprovante", "recibo", "nota fiscal", "prefeitura", "cartório", "leilão");
    }

    private static string BuildProtocolStatus(DocumentAnalysis document, string stem)
    {
        var hasDate = IsoDateRegex().IsMatch(stem);
        var hasIdentifier = document.ProcessIdentifiers.Count > 0 ||
                            VehiclePlateRegex().IsMatch(stem) || stem.Contains("ID ", StringComparison.OrdinalIgnoreCase);
        return hasDate
            ? $"Conforme: data + descrição{(hasIdentifier ? " + identificador" : string.Empty)}; extensão preservada."
            : $"Conforme parcialmente: descrição{(hasIdentifier ? " + identificador" : string.Empty)}; data não localizada e não inventada; extensão preservada.";
    }

    private static RenameSuggestion CreateImageSuggestion(
        DocumentAnalysis document,
        string originalStem,
        string extension)
    {
        var context = document.File.FullPath.Contains("exemplo", StringComparison.OrdinalIgnoreCase) ||
                      document.File.FullPath.Contains("jaguare", StringComparison.OrdinalIgnoreCase)
            ? "Foto imóvel Avenida Exemplo 100"
            : "Foto do dossiê";
        if (originalStem.StartsWith("Foto do dossiê - ", StringComparison.OrdinalIgnoreCase) ||
            originalStem.StartsWith("Foto imóvel ", StringComparison.OrdinalIgnoreCase))
        {
            return new(document.File.FullPath, originalStem + extension, true,
                "IMG-FOTO-09: nome de foto já normalizado; mantido sem prefixo repetido.",
                StandardFolders.Photos, "IMG-FOTO-09", "Fotografia");
        }
        var timestamp = ImageTimestampRegex().Match(originalStem);
        string stem;
        if (timestamp.Success)
        {
            var date = timestamp.Groups[1].Value;
            var time = timestamp.Groups[2].Value.Replace('.', '-').Replace(':', '-');
            var hdr = originalStem.Contains("HDR", StringComparison.OrdinalIgnoreCase) ? " - HDR" : string.Empty;
            stem = $"{date} - {context} - {time}{hdr}";
        }
        else if (int.TryParse(originalStem, out var sequence))
        {
            stem = $"{context} - {sequence:000}";
        }
        else
        {
            stem = $"{context} - {originalStem}";
        }

        return new(document.File.FullPath, stem + extension, true,
            "IMG-FOTO-09: sugestão local baseada na data/número da foto e no contexto do dossiê; categoria FOTOS.",
            StandardFolders.Photos, "IMG-FOTO-09", "Fotografia");
    }

    private static string DetectClassificationRule(string documentType, string destination, string text)
    {
        if (ContainsAny(documentType, "RG", "CNH", "CPF", "identidade")) return "DOC-PESSOAL-CONTEUDO";
        if (destination == StandardFolders.Litigation) return "JUR-PECA-CONTEUDO";
        if (destination == StandardFolders.AuctionAndJudicial) return "IMOVEL-ARREMATACAO-CONTEUDO";
        if (destination == StandardFolders.Registry) return "IMOVEL-CARTORIO-CONTEUDO";
        if (destination == StandardFolders.Financial) return "IMOVEL-IPTU-FINANCEIRO-CONTEUDO";
        if (destination == StandardFolders.Contracts) return "IMOVEL-CONTRATO-CONTEUDO";
        if (destination == StandardFolders.Leasing) return "IMOVEL-LOCACAO-CONTEUDO";
        if (destination == StandardFolders.Improvements) return "IMOVEL-REFORMA-CONTEUDO";
        if (destination == StandardFolders.Photos) return "IMG-FOTO-09";
        return string.IsNullOrWhiteSpace(text) ? "FALLBACK-NOME-ORIGINAL" : "PESQUISA-CONTEUDO-GERAL";
    }

    private static string DetectDestinationFolder(DocumentAnalysis document, string proposedStem)
    {
        var evidence = $"{document.File.Name}\n{proposedStem}\n{document.FullText}";
        if (ContainsAny(proposedStem, "foto do dossiê", "foto imóvel", "fotografia", "imagem do imóvel"))
            return StandardFolders.Photos;
        if (ContainsAny(proposedStem, "relatório", "relatorio", "laudo", "parecer", "análise", "analise", "resumo"))
            return StandardFolders.Reports;
        if (ContainsAny(proposedStem, "RG do", "documento de identidade", "carteira de identidade", "CNH do",
                "carteira nacional de habilitação", "CPF do", "documento do veículo", "CRLV", "CRV"))
            return StandardFolders.Research;
        if (ContainsAny(proposedStem, "ficha descritiva do imóvel", "dados do imóvel", "descrição do imóvel"))
            return StandardFolders.Research;
        if (ContainsAny(proposedStem, "consulta de veículo", "documento do veículo", "tabela fipe"))
            return StandardFolders.Research;
        if (ContainsAny(proposedStem, "contrato"))
            return StandardFolders.Contracts;
        if (ContainsAny(proposedStem, "proposta de compra", "comprovante de transferência", "agendamento"))
            return StandardFolders.Research;
        if (ContainsAny(proposedStem, "sentença", "decisão", "despacho", "petição", "contestação",
                "contrarrazões", "recurso", "requerimento de usucapião", "notificação judicial",
                "resposta a ofício judicial"))
            return StandardFolders.Litigation;
        if (ContainsAny(proposedStem, "carta de arrematação", "auto de arrematação", "edital de leilão",
                "depósito judicial", "pagamento da arrematação"))
            return StandardFolders.AuctionAndJudicial;
        if (ContainsAny(proposedStem, "matrícula", "certidão", "cartório", "registro de imóveis"))
            return ContainsAny(proposedStem, "iptu", "condomínio", "débito")
                ? StandardFolders.Financial
                : StandardFolders.Registry;
        if (ContainsAny(proposedStem, "demonstrativo de débitos", "consulta de débitos",
                "comprovante de iptu", "comprovante de ipva", "documento de condomínio", "guia de recolhimento",
                "comprovante de pagamento"))
            return StandardFolders.Financial;
        if (ContainsAny(evidence, "arrematação", "arrematacao", "leilão", "leilao", "edital", "depósito judicial",
                "deposito judicial", "sustação", "sustacao", "carta de arrematação", "auto de arrematação"))
            return StandardFolders.AuctionAndJudicial;
        if (ContainsAny(evidence, "petição", "peticao", "contestação", "contestacao", "contrarrazões",
                "contrarrazoes", "recurso", "apelação", "apelacao", "agravo", "sentença", "sentenca",
                "decisão", "decisao", "processo judicial", "excelentíssimo", "excelentissimo", "protocolo ar"))
            return StandardFolders.Litigation;
        if (ContainsAny(evidence, "iptu", "ppi", "condomínio", "condominio", "despesa", "débito", "debito",
                "tribut", "boleto", "prestação", "prestacao", "parcela", "relatório gerencial"))
            return StandardFolders.Financial;
        if (ContainsAny(evidence, "matrícula", "matricula", "cartório", "cartorio", "registro de imóveis",
                "registro de imoveis", "certidão", "certidao", "itbi", "nota de devolução", "averbação", "averbacao"))
            return StandardFolders.Registry;
        if (ContainsAny(evidence, "reforma", "obra", "material de construção", "materiais de construção", "benfeitoria",
                "arquitet", "engenheir"))
            return StandardFolders.Improvements;
        if (ContainsAny(evidence, "locação", "locacao", "aluguel", "inquilino", "locatário", "locatario",
                "despejo"))
            return StandardFolders.Leasing;
        if (ContainsAny(evidence, "relatório", "relatorio", "laudo", "parecer", "análise", "analise", "resumo"))
            return StandardFolders.Reports;
        return StandardFolders.Research;
    }

    private static bool ContainsAny(string evidence, params string[] terms) =>
        terms.Any(term => evidence.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsImage(string extension) => extension.ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".bmp" or ".heic" or ".webp";

    private static bool LooksLikeScannedDocument(string text) => text.Length >= 30 && ContainsAny(text,
        "carteira de identidade", "registro geral", "carteira nacional de habilitação", "cadastro de pessoas físicas",
        "certificado de registro e licenciamento", "senatran", "detran", "processo", "contrato", "certidão");

    private static string DetectDocumentType(string text, string originalStem)
    {
        var firstPage = text.Length <= 6_000 ? text : text[..6_000];
        var opening = firstPage.Length <= 2_000 ? firstPage : firstPage[..2_000];
        var titleZone = firstPage.Length <= 900 ? firstPage : firstPage[..900];

        // A identidade principal deve vir do cabeçalho/título. Uma palavra citada na
        // narrativa (por exemplo "condenado por sentença" ou "conforme contrato")
        // não transforma o documento inteiro nessa espécie documental.
        if (StartsAsUsucapiaoRequest(opening)) return "Requerimento de usucapião extrajudicial";
        if (StandaloneSentenceTitleRegex().IsMatch(titleZone) ||
            ContainsAny(titleZone[..Math.Min(titleZone.Length, 350)], "PODER JUDICIÁRIO SENTENÇA",
                "PODER JUDICIARIO SENTENCA"))
            return "Sentença";
        var explicitOriginalType = DetectTypeFromOriginalName(originalStem, opening);
        if (explicitOriginalType is "Petição de baixa de averbação" or "Expedição de carta de arrematação" or
            "Depósito judicial da arrematação" or "Resposta a ofício judicial" or "Relatório" or "Laudo" or
            "Parecer" or "Tabela FIPE" or "Carta de arrematação" or "Auto de arrematação")
            return explicitOriginalType;
        if (ContainsAny(opening, "FICA V. SA. NOTIFICADO", "FICA V.SA. NOTIFICADO", "INT/CIT.Nº",
                "INT/CIT.N°"))
            return "Notificação judicial";
        if (LooksLikeLegalPleading(firstPage, originalStem))
            return ContainsAny(originalStem, "obrigação de fazer", "obrigacao de fazer")
                ? "Petição - obrigação de fazer"
                : ContainsAny(originalStem, "usucapião", "usucapiao")
                    ? "Petição - usucapião"
                    : "Petição judicial";
        if (ContainsAny(titleZone, "CARTA DE ARREMATAÇÃO", "CARTA DE ARREMATACAO", "CARTA DE ATREMAT"))
            return "Carta de arrematação";
        if (ContainsAny(titleZone, "AUTO DE ARREMATAÇÃO", "AUTO DE ARREMATACAO") &&
            !ContainsAny(titleZone, "CARTA DE ARREMATAÇÃO", "CARTA DE ARREMATACAO"))
            return "Auto de arrematação";
        if (ContainsAny(opening, "PORTAL DE SERVIÇOS SENATRAN", "PORTAL DE SERVICOS SENATRAN") &&
            ContainsAny(opening, "CONSULTAR VEÍCULO", "CONSULTAR VEICULO"))
            return "Consulta de veículo";
        if (ContainsAny(titleZone, "DEMONSTRATIVO DE DÉBITOS", "DEMONSTRATIVO DE DEBITOS",
                "CONSULTA DE DÉBITOS", "CONSULTA DE DEBITOS", "DÉBITOS VINCULADOS AO VEÍCULO",
                "DEBITOS VINCULADOS AO VEICULO", "CONSULTAR DÉBITO", "CONSULTAR DEBITO"))
            return "Demonstrativo de débitos";
        if (ContainsAny(opening, "IPVA APURADO", "DÉBITO DO EXERCÍCIO ATUAL", "DEBITO DO EXERCICIO ATUAL") &&
            ContainsAny(opening, "DADOS DO VEÍCULO", "DADOS DO VEICULO", "RENAVAM"))
            return "Demonstrativo de IPVA";
        if (ContainsAny(titleZone, "OFÍCIO Nº", "OFICIO Nº", "OFÍCIO N°", "OFICIO N°") &&
            ContainsAny(opening, "OFÍCIO RECEBIDO", "OFICIO RECEBIDO", "INFORMAR QUE"))
            return "Resposta a ofício judicial";
        if (ContainsAny(titleZone, "DECISÃO INTERLOCUTÓRIA", "DECISAO INTERLOCUTORIA"))
            return "Decisão interlocutória";
        if (StandaloneDespachoTitleRegex().IsMatch(titleZone)) return "Despacho judicial";
        if (ContainsAny(titleZone, "PETIÇÃO INICIAL", "PETICAO INICIAL")) return "Petição inicial";
        if (StandaloneContestacaoTitleRegex().IsMatch(titleZone)) return "Contestação";
        if (LooksLikeIdentityCard(firstPage))
            return AddRole("RG", originalStem);
        if (ContainsAny(firstPage, "CARTEIRA NACIONAL DE HABILITAÇÃO", "PERMISSÃO PARA DIRIGIR"))
            return AddRole("CNH", originalStem);
        if (ContainsAny(firstPage, "CADASTRO DE PESSOAS FÍSICAS"))
            return AddRole("CPF", originalStem);
        if (ContainsAny(firstPage, "CERTIFICADO DE REGISTRO E LICENCIAMENTO", "CRLV", "CERTIFICADO DE REGISTRO DE VEÍCULO"))
            return "Documento do veículo";
        if (ContainsAny(firstPage, "CONTRATO DE COMPROMISSO DE VENDA E COMPRA",
                "CONTRATO DE COMPRA E VENDA", "INSTRUMENTO PARTICULAR DE COMPRA E VENDA"))
            return "Contrato de compra e venda";
        if (ContainsAny(firstPage, "DADOS SOBRE O APTO", "DETALHES DO IMÓVEL", "DESCRIÇÃO DO APARTAMENTO"))
            return "Ficha descritiva do imóvel";
        if (ContainsAny(firstPage, "COMPROVANTE DE PAGAMENTO BOLETO", "COMPROVANTE DE PAGAMENTO DE BOLETO"))
            return ContainsAny(firstPage, "IPTU", "PREFEITURA MUNICIPAL")
                ? "Comprovante de pagamento de IPTU"
                : "Comprovante de pagamento";
        if (explicitOriginalType is not null) return explicitOriginalType;
        var types = new (string Name, string[] Terms)[]
        {
            ("Comprovante de pagamento da arrematação", ["pagamento da arrematação", "pagamento de arrematação"]),
            ("Depósito judicial", ["depósito judicial", "deposito judicial"]),
            ("Edital de leilão", ["edital", "leilão"]),
            ("Contrarrazões", ["contrarrazões", "contraminuta"]),
            ("Recurso", ["recurso", "apelação", "agravo"]),
            ("Matrícula do imóvel", ["matrícula", "matricula", "registro de imóveis"]),
            ("Certidão imobiliária", ["certidão imobiliária", "certidao imobiliaria"]),
            ("Proposta de compra", ["proposta de compra"]),
            ("Comprovante de transferência", ["transferência", "transferencia", "ted"]),
            ("Declaração de quitação", ["declaração de quitação", "declaracao de quitacao"]),
            ("Demonstrativo de débitos", ["demonstrativo", "débitos", "debitos"]),
            ("Agendamento", ["agendamento", "pré-agendamento", "pre-agendamento"]),
            ("Contrato", ["instrumento particular", "contrato particular"]),
            ("Comprovante de IPVA", ["ipva"]),
            ("Comprovante de IPTU", ["iptu"]),
            ("Documento de condomínio", ["condomínio", "condominial"]),
            ("Guia de recolhimento", ["guia de recolhimento", "dare"]),
            ("Comprovante de pagamento", ["comprovante de pagamento", "pagamento efetuado"]),
            ("Consulta de veículo", ["detran", "denatran", "renavam"]),
            ("Recibo", ["recibo"]),
            ("Anotações processuais", ["anotações", "andamento processual"])
        };
        foreach (var type in types)
            if (type.Terms.Any(term => opening.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return type.Name;
        return "Não identificado — revisão obrigatória";
    }

    private static bool StartsAsUsucapiaoRequest(string opening)
    {
        var firstMeaningful = WhitespaceRegex().Replace(opening, " ").Trim();
        return firstMeaningful.StartsWith("REQUERIMENTO", StringComparison.OrdinalIgnoreCase) &&
               ContainsAny(firstMeaningful[..Math.Min(firstMeaningful.Length, 500)],
                   "USUCAPIÃO EXTRAJUDICIAL", "USUCAPIAO EXTRAJUDICIAL");
    }

    private static bool LooksLikeIdentityCard(string firstPage)
    {
        if (ContainsAny(firstPage, "CARTEIRA DE IDENTIDADE")) return true;
        if (!ContainsAny(firstPage, "REGISTRO GERAL") && !RgLabelRegex().IsMatch(firstPage)) return false;
        var identityFields = new[] { "FILIAÇÃO", "FILIACAO", "NATURALIDADE", "DATA DE NASCIMENTO",
            "ÓRGÃO EXPEDIDOR", "ORGAO EXPEDIDOR", "ASSINATURA DO TITULAR" };
        return identityFields.Count(field => firstPage.Contains(field, StringComparison.OrdinalIgnoreCase)) >= 2;
    }

    private static bool LooksLikeLegalPleading(string firstPage, string originalStem)
    {
        var judicialOpening = ContainsAny(firstPage, "EXCELENTÍSSIMO", "EXCELENTISSIMO", "MERITÍSSIMO",
            "MERITISSIMO", "AO JUÍZO", "AO JUIZO", "TRIBUNAL DE JUSTIÇA", "TRIBUNAL DE JUSTICA");
        var proceduralEvidence = ContainsAny(firstPage, "PROCESSO", "REQUERENTE", "REQUERIDO", "AUTOR",
            "RÉU", "REU", "ADVOGADO", "REQUER", "PETIÇÃO", "PETICAO");
        var legalLegacyName = LegalLegacyNameRegex().IsMatch(originalStem);
        return judicialOpening && proceduralEvidence || legalLegacyName && proceduralEvidence;
    }

    private static string? DetectTypeFromOriginalName(string originalStem, string opening)
    {
        originalStem = CleanLegacyCopyMarkers(originalStem);
        if (GenericFileNameRegex().IsMatch(originalStem)) return null;
        if (ContainsAny(originalStem, "petição de baixa", "peticao de baixa", "baixa averbação", "baixa de averbação"))
            return "Petição de baixa de averbação";
        if (ContainsAny(originalStem, "ofic cumprido", "ofício cumprido", "oficio cumprido") &&
            ContainsAny(opening, "ofic", "oílcio", "oilcio"))
            return "Resposta a ofício judicial";
        if (ContainsAny(originalStem, "expedição carta", "expedicao carta")) return "Expedição de carta de arrematação";
        if (ContainsAny(originalStem, "carta de arrematação", "carta de arrematacao") &&
            ContainsAny(opening, "carta de arrematação", "carta de arrematacao", "carta de atremat"))
            return "Carta de arrematação";
        if (ContainsAny(originalStem, "auto de arrematação", "auto de arrematacao") &&
            ContainsAny(opening, "auto de arrematação", "auto de arrematacao"))
            return "Auto de arrematação";
        if (ContainsAny(originalStem, "depósito judicial", "deposito judicial")) return "Depósito judicial da arrematação";
        if (ContainsAny(originalStem, "demonstrativo", "débitos prefeitura", "debitos prefeitura"))
            return "Demonstrativo de débitos municipais";
        if (ContainsAny(originalStem, "certidão de objeto", "certidao de objeto")) return "Certidão de objeto e pé";
        if (ContainsAny(originalStem, "certidão para cancelamento", "certidao para cancelamento"))
            return "Certidão para cancelamento de averbação de penhora";
        if (ContainsAny(originalStem, "matrícula", "matricula")) return "Matrícula do imóvel";
        if (ContainsAny(originalStem, "contrato compra e venda", "contrato de compra e venda",
                "contrato de compromisso", "contrato compra"))
            return "Contrato de compra e venda";
        if (ContainsAny(originalStem, "proposta de compra")) return "Proposta de compra";
        if (ContainsAny(originalStem, "comprovante de pagamento da arrematação", "comprovante pagamento arrematação"))
            return "Comprovante de pagamento da arrematação";
        if (ContainsAny(originalStem, "relatório", "relatorio", "relat situac")) return "Relatório";
        if (ContainsAny(originalStem, "tabela fipe") && ContainsAny(opening, "fipe")) return "Tabela FIPE";
        if (ContainsAny(originalStem, "laudo")) return "Laudo";
        if (ContainsAny(originalStem, "parecer")) return "Parecer";
        return null;
    }

    private static string AddRole(string documentType, string originalStem)
    {
        if (originalStem.Contains("arrematante", StringComparison.OrdinalIgnoreCase))
            return $"{documentType} do arrematante";
        if (originalStem.Contains("comprador", StringComparison.OrdinalIgnoreCase))
            return $"{documentType} do comprador";
        if (originalStem.Contains("vendedor", StringComparison.OrdinalIgnoreCase))
            return $"{documentType} do vendedor";
        return documentType;
    }

    private static string CleanLegacyCopyMarkers(string value)
    {
        var cleaned = CopyPrefixRegex().Replace(value, string.Empty);
        cleaned = CopySuffixRegex().Replace(cleaned, string.Empty);
        cleaned = DuplicateNumberSuffixRegex().Replace(cleaned, string.Empty);
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim(' ', '-', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? "Documento digitalizado" : cleaned;
    }

    private static string NormalizeSuggestionStem(string value)
    {
        var parts = value.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unique = new List<string>();
        foreach (var part in parts)
        {
            var clean = CleanLegacyCopyMarkers(part);
            if (unique.Contains(clean, StringComparer.OrdinalIgnoreCase)) continue;
            unique.Add(clean);
        }
        return string.Join(" - ", unique);
    }

    private static string? DetectSubject(string text, string documentType)
    {
        var opening = text.Length <= 4_000 ? text : text[..4_000];
        var acceptsRegistrySubject = ContainsAny(documentType, "sentença", "decisão", "petição", "certidão");
        if (acceptsRegistrySubject && ContainsAny(opening, "cancelamento da penhora", "baixa da averbação", "baixa de averbação"))
        {
            var registration = RegistrationNumberRegex().Match(opening).Value;
            return string.IsNullOrWhiteSpace(registration)
                ? "Cancelamento de penhora"
                : $"Cancelamento de penhora - matrícula {NormalizeRegistrationNumber(registration)}";
        }
        if (ContainsAny(documentType, "declaração", "demonstrativo", "documento de condomínio") &&
            ContainsAny(opening, "quitação de débitos de condomínio", "quitacao de debitos de condominio"))
            return "Quitação de condomínio";
        if (ContainsAny(documentType, "demonstrativo", "comprovante de IPTU", "certidão") &&
            ContainsAny(opening, "débito de IPTU", "debitos de iptu", "débitos de IPTU"))
            return "Débitos de IPTU";
        return null;
    }

    private static string? DetectAbbreviatedParties(string text, string documentType)
    {
        if (!documentType.Contains("Contrato", StringComparison.OrdinalIgnoreCase)) return null;
        var opening = text.Length <= 16_000 ? text : text[..16_000];
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "brasileiro", "brasileira", "inscrito", "inscrita", "portador", "portadora",
            "pessoa", "representado", "representada", "doravante"
        };
        var names = PartyLabelRegex().Matches(opening).Cast<Match>()
            .Select(match => match.Groups[1].Value.Trim())
            .Where(name => name.Length >= 3 && !ignored.Contains(name))
            .Select(ToDisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2).ToArray();
        return names.Length == 2 ? $"{names[0]} x {names[1]}" : names.FirstOrDefault();
    }

    private static string? DetectTrackingId(string text, string documentType)
    {
        if (!ContainsAny(documentType, "pagamento", "guia", "boleto", "depósito", "transferência", "IPTU", "condomínio")) return null;
        var opening = text.Length <= 20_000 ? text : text[..20_000];
        var longCodes = LongNumericCodeRegex().Matches(opening).Cast<Match>()
            .Where(match => !CnjInsideCodeRegex().IsMatch(match.Value))
            .Select(match => Regex.Replace(match.Value, @"\D", string.Empty))
            .Where(code => code.Length is >= 18 and <= 60)
            .OrderByDescending(code => code.Length is 44 or 46 or 47 or 48 ? 2 : 1)
            .ThenByDescending(code => code.Length)
            .ToArray();
        var code = longCodes.FirstOrDefault();
        if (code is null)
        {
            var labelled = LabelledTrackingIdRegex().Match(opening);
            if (labelled.Success) code = Regex.Replace(labelled.Groups[1].Value, @"[^A-Za-z0-9]", string.Empty);
        }
        if (string.IsNullOrWhiteSpace(code)) return null;
        return code[^Math.Min(6, code.Length)..].ToUpperInvariant();
    }

    private static string ShortenProcessIdentifier(string value)
    {
        var match = ShortProcessRegex().Match(value);
        return match.Success ? match.Value : value.Length <= 14 ? value : value[..14].TrimEnd('.', '-');
    }

    private static string ToDisplayName(string value)
    {
        var lower = value.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static string? SelectDocumentDate(DocumentAnalysis document, string documentType)
    {
        if (documentType.Contains("Contrato", StringComparison.OrdinalIgnoreCase))
        {
            var contractDate = ContractDateRegex().Match(document.FullText);
            if (contractDate.Success) return contractDate.Groups[1].Value;
        }
        var signed = SignedDateRegex().Match(document.FullText);
        return signed.Success ? signed.Groups[1].Value : document.DocumentDates.FirstOrDefault();
    }

    private static string NormalizeRegistrationNumber(string value) =>
        Regex.Replace(value, @"\D", string.Empty).TrimStart('0') is { Length: > 0 } digits ? digits : value;

    private static string ToIsoDate(string value)
    {
        var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "yyyy-MM-dd" };
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.Replace('/', '-').Replace('.', '-');
    }

    [GeneratedRegex(@"[^\p{L}\p{N}\s._-]")]
    private static partial Regex NonNameCharactersRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:file|arquivo|documento|scan|digitaliza(?:do|ção))[_ -]*\d*$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericFileNameRegex();

    [GeneratedRegex(@"^(?:(?:c[oó]pia|copy)(?:\s+de|\s+of)?)[\s_-]+", RegexOptions.IgnoreCase)]
    private static partial Regex CopyPrefixRegex();

    [GeneratedRegex(@"[\s_-]+(?:c[oó]pia|copy)(?:\s+\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex CopySuffixRegex();

    [GeneratedRegex(@"\s*\(\d+\)$", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateNumberSuffixRegex();

    [GeneratedRegex(@"\bR\.?\s*G\.?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RgLabelRegex();

    [GeneratedRegex(@"\b[A-Z]{3}[0-9][A-Z0-9][0-9]{2}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VehiclePlateRegex();

    [GeneratedRegex(@"(?:juntado em|assinado (?:eletronicamente )?em)\s*[:\-]?\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex SignedDateRegex();

    [GeneratedRegex(@"(?:matr[ií]cula\s*(?:n[º°o.]*)?\s*)\d[\d .]{2,12}", RegexOptions.IgnoreCase)]
    private static partial Regex RegistrationNumberRegex();

    [GeneratedRegex(@"^((?:19|20)\d{2}-\d{2}-\d{2})[ _-]+(\d{2}[.:]\d{2}[.:]\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex ImageTimestampRegex();

    [GeneratedRegex(@"(?im)\b(?:vendedor(?:a|es)?|comprador(?:a|es)?|contratante|contratado|cedente|cession[aá]ri[oa]|locador(?:a)?|locat[aá]ri[oa]|outorgante|outorgado)\b\s*(?:\(\s*ES\s*\))?\s*(?:[:\-]|é)?\s*(?:sr\.?a?\s+)?([\p{L}]{3,})", RegexOptions.IgnoreCase)]
    private static partial Regex PartyLabelRegex();

    [GeneratedRegex(@"(?<!\d)(?:\d[ .\-]?){18,60}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumericCodeRegex();

    [GeneratedRegex(@"\d{7}-\d{2}\.\d{4}\.\d", RegexOptions.CultureInvariant)]
    private static partial Regex CnjInsideCodeRegex();

    [GeneratedRegex(@"(?:autentica[cç][aã]o|identificador|id(?:\s+da\s+transa[cç][aã]o)?|nosso\s+n[uú]mero)\s*[:\-]?\s*([A-Z0-9.\-]{6,30})", RegexOptions.IgnoreCase)]
    private static partial Regex LabelledTrackingIdRegex();

    [GeneratedRegex(@"\d{7}-\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex ShortProcessRegex();

    [GeneratedRegex(@"(?:contrato|instrumento|celebrado|firmado|assinado).{0,100}?(\d{2}[/.]\d{2}[/.]\d{4})", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ContractDateRegex();

    [GeneratedRegex(@"^(?:19|20)\d{2}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"(?im)^\s*SENTEN[ÇC]A\s*(?:$|[-–—:])", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneSentenceTitleRegex();

    [GeneratedRegex(@"(?im)^\s*DESPACHO\s*(?:$|[-–—:])", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneDespachoTitleRegex();

    [GeneratedRegex(@"(?im)^\s*CONTESTA[ÇC][AÃ]O\s*(?:$|[-–—:])", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneContestacaoTitleRegex();

    [GeneratedRegex(@"(?i)(?:^|\s)(?:a[çc][aã]o|peti[çc][aã]o)(?:\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex LegalLegacyNameRegex();
}
