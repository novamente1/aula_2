using Organiza.Application.Services;
using Organiza.Application.Common;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;
using Organiza.Infrastructure.Files;
using Organiza.Infrastructure.History;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class FolderAndNamingTests
{
    [Fact]
    public void EmptyCleaner_PreservesValidatedDriveCategoriesAndTemplates()
    {
        using var directory = new TemporaryDirectory();
        var financial = Directory.CreateDirectory(directory.PathFor("01 FINANCEIRO")).FullName;
        var courses = Directory.CreateDirectory(directory.PathFor("12 CURSOS")).FullName;
        var legalModule = Directory.CreateDirectory(directory.PathFor("00 AUTOS")).FullName;
        var disposable = Directory.CreateDirectory(directory.PathFor("Pasta temporária sem uso")).FullName;
        var cleaner = new EmptyFolderCleaner(new PhysicalFileSystem());

        var removed = cleaner.RemoveEmptyFolders(directory.FullPath);

        Assert.True(Directory.Exists(financial));
        Assert.True(Directory.Exists(courses));
        Assert.True(Directory.Exists(legalModule));
        Assert.False(Directory.Exists(disposable));
        Assert.Contains(disposable, removed);
    }

    [Fact]
    public void EnforceNamingRules_RemovesLegacyCopyMarkersAndDuplicateSuffix()
    {
        var result = RenameService.EnforceNamingRules(
            "Cópia de Documento do arrematante (2).pdf", "original.pdf");

        Assert.Equal("Documento do arrematante.pdf", result);
    }

    [Fact]
    public void ReusesStandardFolder_IgnoringCaseAndAccents()
    {
        using var directory = new TemporaryDirectory();
        var existing = Directory.CreateDirectory(directory.PathFor("98 duplicádos")).FullName;
        var manager = new StandardFolderManager(new PhysicalFileSystem());

        var resolved = manager.ResolveOrCreate(directory.FullPath, StandardFolders.Duplicates, ExplicitApproval.Grant("teste"));

        Assert.Equal(existing, resolved);
        Assert.Single(Directory.EnumerateDirectories(directory.FullPath));
    }

    [Fact]
    public void NamingRules_PreserveExtensionAndClearProcessIdentifier()
    {
        var result = RenameService.EnforceNamingRules(
            "PETIÇÃO INICIAL 2025 2025.PDF",
            "ATOrd_1001307-42 documento.PDF");

        Assert.Equal("ATOrd_1001307-42 Petição inicial 2025.PDF", result);
    }

    [Fact]
    public void RootProtection_BlocksRenamingTheSelectedRoot()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.FullPath + " renomeada";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RootProtectionGuard().EnsureInternalFileOperation(
                directory.FullPath, directory.FullPath, destination));

        Assert.Contains("pasta raiz", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(directory.FullPath));
    }

    [Fact]
    public void OptionalStructure_OrganizeOnly_CreatesNoFolders()
    {
        using var directory = new TemporaryDirectory();
        var manager = new StandardFolderManager(new PhysicalFileSystem());

        var created = manager.PrepareOptionalStructure(directory.FullPath,
            Organiza.Domain.Selection.FolderOrganizationMode.OrganizeFilesOnly,
            ExplicitApproval.Denied("usuário escolheu apenas arquivos"));

        Assert.Empty(created);
        Assert.Empty(Directory.EnumerateDirectories(directory.FullPath));
    }

    [Fact]
    public async Task Consolidation_UnifiesAccentedAliases_PreservesConflicts_AndRemovesEmptyFolders()
    {
        using var directory = new TemporaryDirectory();
        var canonical02 = Directory.CreateDirectory(directory.PathFor("02 ARREMATAÇÃO E JUDICIAL")).FullName;
        var alias02 = Directory.CreateDirectory(directory.PathFor("02 ARREMATACAO")).FullName;
        var alias03 = Directory.CreateDirectory(directory.PathFor("03 CARTORIO")).FullName;
        var empty = Directory.CreateDirectory(directory.PathFor("pasta vazia")).FullName;
        await File.WriteAllTextAsync(Path.Combine(canonical02, "documento.pdf"), "canônico");
        await File.WriteAllTextAsync(Path.Combine(alias02, "documento.pdf"), "variação preservada");
        await File.WriteAllTextAsync(Path.Combine(alias03, "registro.pdf"), "registro");
        var manager = new StandardFolderManager(new PhysicalFileSystem());

        var result = manager.ConsolidateSimilar(
            directory.FullPath, ExplicitApproval.Grant("teste de consolidação"));

        Assert.False(Directory.Exists(alias02));
        Assert.False(Directory.Exists(alias03));
        Assert.False(Directory.Exists(empty));
        Assert.True(Directory.Exists(directory.PathFor(StandardFolders.Registry)));
        var official02 = directory.PathFor(StandardFolders.AuctionAndJudicial);
        Assert.Equal("canônico", await File.ReadAllTextAsync(Path.Combine(official02, "documento.pdf")));
        Assert.Equal("variação preservada", await File.ReadAllTextAsync(Path.Combine(official02, "documento_2.pdf")));
        Assert.Equal("registro", await File.ReadAllTextAsync(
            Path.Combine(directory.PathFor(StandardFolders.Registry), "registro.pdf")));
        Assert.Equal(3, result.MovedFiles.Count);
        Assert.Contains(empty, result.RemovedFolders, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Consolidation_DoesNotFlattenNestedTemplateCategories()
    {
        using var directory = new TemporaryDirectory();
        var wrapper = Directory.CreateDirectory(directory.PathFor("DOSSIÊ ANTIGO")).FullName;
        var category = Directory.CreateDirectory(Path.Combine(wrapper, "02 ARREMATAÇÃO")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(category, "ARREMAT EXP PROCESSO")).FullName;
        var source = Path.Combine(nested, "carta.pdf");
        await File.WriteAllTextAsync(source, "carta de arrematação");
        var manager = new StandardFolderManager(new PhysicalFileSystem());

        var result = manager.ConsolidateSimilar(directory.FullPath, ExplicitApproval.Grant("teste"));

        Assert.True(File.Exists(source));
        Assert.True(Directory.Exists(wrapper));
        Assert.DoesNotContain(result.MovedFiles, move => move.SourcePath == source);
    }

    [Fact]
    public void Consolidation_WithoutApproval_DoesNotMoveAnything()
    {
        using var directory = new TemporaryDirectory();
        var alias = Directory.CreateDirectory(directory.PathFor("02 ARREMATACAO")).FullName;
        File.WriteAllText(Path.Combine(alias, "arquivo.pdf"), "original");
        var manager = new StandardFolderManager(new PhysicalFileSystem());

        Assert.Throws<ApprovalRequiredException>(() => manager.ConsolidateSimilar(
            directory.FullPath, ExplicitApproval.Denied("teste")));

        Assert.True(File.Exists(Path.Combine(alias, "arquivo.pdf")));
        Assert.False(Directory.Exists(directory.PathFor("02 ARREMATAÇÃO E JUDICIAL")));
    }

    [Fact]
    public async Task RenameInOrganizeOnlyMode_DoesNotConsolidateFolderStructure()
    {
        using var directory = new TemporaryDirectory();
        var alias = Directory.CreateDirectory(directory.PathFor("02 ARREMATACAO personalizada")).FullName;
        var preserved = Path.Combine(alias, "manter.pdf");
        await File.WriteAllTextAsync(preserved, "preservar estrutura");
        var original = directory.PathFor("ARQUIVO.TXT");
        await File.WriteAllTextAsync(original, "renomear");
        var fs = new PhysicalFileSystem();
        var service = new RenameService(fs, null!, null!, new PathGuard(),
            new JsonHistoryStore(directory.FullPath), new EmptyFolderCleaner(fs));

        await service.ApplySelectedAsync(
            [new(original, "Arquivo organizado.txt", true)],
            ExplicitApproval.Grant("teste"),
            directory.FullPath,
            Organiza.Domain.Selection.FolderOrganizationMode.OrganizeFilesOnly);

        Assert.True(File.Exists(preserved));
        Assert.True(Directory.Exists(alias));
        Assert.False(Directory.Exists(directory.PathFor("02 ARREMATAÇÃO E JUDICIAL")));
        Assert.True(File.Exists(directory.PathFor("Arquivo organizado.TXT")));
    }

    [Fact]
    public async Task RenameInStandardMode_ConsolidatesEquivalentFolders_AndLogsEveryMove()
    {
        using var directory = new TemporaryDirectory();
        var alias = Directory.CreateDirectory(directory.PathFor("02 ARREMATACAO")).FullName;
        var legacy = Path.Combine(alias, "carta antiga.pdf");
        await File.WriteAllTextAsync(legacy, "conteúdo preservado");
        var source = directory.PathFor("arquivo.txt");
        await File.WriteAllTextAsync(source, "renomear");
        var fs = new PhysicalFileSystem();
        var history = new JsonHistoryStore(directory.FullPath);
        var service = new RenameService(fs, null!, null!, new PathGuard(), history, new EmptyFolderCleaner(fs));

        var result = await service.ApplySelectedAsync(
            [new(source, "Arquivo organizado.txt", true)],
            ExplicitApproval.Grant("teste"), directory.FullPath,
            Organiza.Domain.Selection.FolderOrganizationMode.ApplyStandardStructure);

        var canonicalFile = Path.Combine(directory.FullPath, StandardFolders.AuctionAndJudicial, "carta antiga.pdf");
        Assert.True(File.Exists(canonicalFile));
        Assert.False(Directory.Exists(alias));
        Assert.All(StandardFolders.PropertyTemplate, folder =>
            Assert.True(Directory.Exists(directory.PathFor(folder)), $"Pasta oficial ausente: {folder}"));
        Assert.Equal(0, result.Failed);
        Assert.Contains(await history.ReadAsync(), entry =>
            entry.Operation == "Consolidar pasta padrão" &&
            entry.Source == legacy && entry.Destination == canonicalFile &&
            entry.Status == OperationStatus.Completed);
    }

    [Fact]
    public async Task RenameBatch_UsesIncrementalSuffix_RecordsFailure_AndContinues()
    {
        using var directory = new TemporaryDirectory();
        var invalid = directory.PathFor("invalido.txt");
        var valid = directory.PathFor("origem.txt");
        await File.WriteAllTextAsync(invalid, "permanece");
        await File.WriteAllTextAsync(valid, "renomear");
        await File.WriteAllTextAsync(directory.PathFor("Destino.txt"), "já existente");
        var fs = new PhysicalFileSystem();
        var history = new JsonHistoryStore(directory.FullPath);
        var service = new RenameService(fs, null!, null!, new PathGuard(), history, new EmptyFolderCleaner(fs));

        await service.ApplySelectedAsync(
            [
                new(invalid, "[tag].txt", true),
                new(valid, "Destino.txt", true)
            ],
            ExplicitApproval.Grant("teste"), directory.FullPath,
            Organiza.Domain.Selection.FolderOrganizationMode.OrganizeFilesOnly);

        Assert.True(File.Exists(invalid));
        Assert.True(File.Exists(directory.PathFor("Destino_2.txt")));
        var entries = await history.ReadAsync();
        Assert.Contains(entries, entry => entry.Source == invalid && entry.Status == OperationStatus.Failed);
        Assert.Contains(entries, entry => entry.Destination?.EndsWith("Destino_2.txt", StringComparison.OrdinalIgnoreCase) == true &&
                                          entry.Status == OperationStatus.Completed &&
                                          entry.Details?.Contains("sufixo", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task RenameBatch_AppliesCaseOnlyChangePhysically_AndReportsCompleted()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("ANOTACOES DO PROCESSO.txt");
        await File.WriteAllTextAsync(source, "conteúdo preservado");
        var fs = new PhysicalFileSystem();
        var history = new JsonHistoryStore(directory.FullPath);
        var service = new RenameService(fs, null!, null!, new PathGuard(), history, new EmptyFolderCleaner(fs));

        var result = await service.ApplySelectedAsync(
            [new(source, "Anotacoes do processo.txt", true)],
            ExplicitApproval.Grant("teste"), directory.FullPath,
            Organiza.Domain.Selection.FolderOrganizationMode.OrganizeFilesOnly);

        Assert.Equal(1, result.Completed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal("Anotacoes do processo.txt", Path.GetFileName(Assert.Single(
            Directory.EnumerateFiles(directory.FullPath, "*.txt", SearchOption.TopDirectoryOnly))));
        Assert.Equal("conteúdo preservado", await File.ReadAllTextAsync(
            directory.PathFor("Anotacoes do processo.txt")));
        Assert.Contains(await history.ReadAsync(), entry => entry.Status == OperationStatus.Completed &&
            entry.Details?.Contains("maiúsculas", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task RenameBatch_PrevalidatesLockedFile_LogsExactAlert_AndContinuesFreeItems()
    {
        using var directory = new TemporaryDirectory();
        var lockedPath = directory.PathFor("aberto.txt");
        var freePath = directory.PathFor("livre.txt");
        await File.WriteAllTextAsync(lockedPath, "arquivo aberto");
        await File.WriteAllTextAsync(freePath, "arquivo livre");
        var fs = new PhysicalFileSystem();
        var history = new JsonHistoryStore(directory.FullPath);
        var service = new RenameService(fs, null!, null!, new PathGuard(), history,
            new EmptyFolderCleaner(fs));
        await using var lockedHandle = new FileStream(lockedPath, FileMode.Open, FileAccess.Read,
            FileShare.Read);

        var result = await service.ApplySelectedAsync(
            [
                new(lockedPath, "Documento aberto.txt", true),
                new(freePath, "Documento livre.txt", true)
            ],
            ExplicitApproval.Grant("teste"), directory.FullPath,
            Organiza.Domain.Selection.FolderOrganizationMode.OrganizeFilesOnly);

        Assert.Equal(1, result.Completed);
        Assert.Equal(1, result.Failed);
        var issue = Assert.Single(result.LockedFiles);
        Assert.Equal(lockedPath, issue.Path);
        Assert.Contains("ALERTA: O arquivo [aberto.txt] está aberto ou em uso", issue.Message);
        Assert.Contains(lockedPath, issue.Message);
        Assert.True(File.Exists(lockedPath));
        Assert.True(File.Exists(directory.PathFor("Documento livre.txt")));
        var entries = await history.ReadAsync();
        Assert.Contains(entries, entry => entry.Operation == "Pré-validar bloqueio de arquivo" &&
            entry.Source == lockedPath && entry.Status == OperationStatus.Failed &&
            entry.Details?.Contains(lockedPath, StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task RenameBatch_InStandardMode_MovesFileToSuggestedCanonicalCategory()
    {
        using var directory = new TemporaryDirectory();
        var sourceFolder = Directory.CreateDirectory(directory.PathFor("01")).FullName;
        var source = Path.Combine(sourceFolder, "MATRICULA.PDF");
        await File.WriteAllTextAsync(source, "registro imobiliário");
        var fs = new PhysicalFileSystem();
        var history = new JsonHistoryStore(directory.FullPath);
        var service = new RenameService(fs, null!, null!, new PathGuard(), history, new EmptyFolderCleaner(fs));

        var result = await service.ApplySelectedAsync(
            [new(source, "Matrícula do imóvel.pdf", true, "teste", StandardFolders.Registry)],
            ExplicitApproval.Grant("teste"), directory.FullPath,
            Organiza.Domain.Selection.FolderOrganizationMode.ApplyStandardStructure);

        var destination = Path.Combine(directory.FullPath, StandardFolders.Registry, "Matrícula do imóvel.PDF");
        Assert.Equal(1, result.Completed);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));
        Assert.Contains(await history.ReadAsync(), entry => entry.Destination == destination &&
            entry.Details?.Contains("classificado", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task RenameBatch_InStandardMode_ConsolidatesEquivalentFolderWithoutLosingContent()
    {
        using var directory = new TemporaryDirectory();
        var existing = Directory.CreateDirectory(directory.PathFor("02 ARREMATACAO personalizada")).FullName;
        var preserved = Path.Combine(existing, "documento preservado.pdf");
        await File.WriteAllTextAsync(preserved, "não mover automaticamente");
        var source = directory.PathFor("MATRICULA.PDF");
        await File.WriteAllTextAsync(source, "registro imobiliário");
        var fs = new PhysicalFileSystem();
        var service = new RenameService(fs, null!, null!, new PathGuard(),
            new JsonHistoryStore(directory.FullPath), new EmptyFolderCleaner(fs));

        await service.ApplySelectedAsync(
            [new(source, "Matrícula do imóvel.pdf", true, "teste", StandardFolders.Registry)],
            ExplicitApproval.Grant("teste"), directory.FullPath,
            Organiza.Domain.Selection.FolderOrganizationMode.ApplyStandardStructure);

        var consolidated = Path.Combine(directory.FullPath, StandardFolders.AuctionAndJudicial,
            "documento preservado.pdf");
        Assert.False(Directory.Exists(existing));
        Assert.True(File.Exists(consolidated));
        Assert.Equal("não mover automaticamente", await File.ReadAllTextAsync(consolidated));
    }

    [Fact]
    public async Task RenameBatch_DoesNotReuseBareNumericCategoryFolder()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.PathFor("05"));
        var source = directory.PathFor("pagamento.pdf");
        await File.WriteAllTextAsync(source, "comprovante de IPTU");
        var fs = new PhysicalFileSystem();
        var service = new RenameService(fs, null!, null!, new PathGuard(),
            new JsonHistoryStore(directory.FullPath), new EmptyFolderCleaner(fs));

        await service.ApplySelectedAsync(
            [new(source, "Comprovante de IPTU - ID 058377.pdf", true, "teste", StandardFolders.Financial)],
            ExplicitApproval.Grant("teste"), directory.FullPath,
            Organiza.Domain.Selection.FolderOrganizationMode.ApplyStandardStructure);

        Assert.True(File.Exists(Path.Combine(directory.FullPath, StandardFolders.Financial,
            "Comprovante de IPTU - ID 058377.pdf")));
        Assert.False(Directory.Exists(directory.PathFor("05")));
    }

    [Fact]
    public async Task DuplicateMove_UsesIncrementalSuffix_AndRemovesEmptySourceFolders()
    {
        using var directory = new TemporaryDirectory();
        var firstFolder = Directory.CreateDirectory(directory.PathFor("lote-a")).FullName;
        var secondFolder = Directory.CreateDirectory(directory.PathFor("lote-b")).FullName;
        var first = Path.Combine(firstFolder, "copia.txt");
        var second = Path.Combine(secondFolder, "copia.txt");
        await File.WriteAllTextAsync(first, "primeira");
        await File.WriteAllTextAsync(second, "segunda");
        var duplicates = Directory.CreateDirectory(directory.PathFor(StandardFolders.Duplicates)).FullName;
        await File.WriteAllTextAsync(Path.Combine(duplicates, "copia.txt"), "existente");
        var fs = new PhysicalFileSystem();
        var history = new JsonHistoryStore(directory.FullPath);
        var service = new DuplicateMover(fs, new StandardFolderManager(fs), new PathGuard(), history,
            new EmptyFolderCleaner(fs));

        await service.MoveConfirmedAsync(directory.FullPath, [first, second], ExplicitApproval.Grant("teste"));

        Assert.False(Directory.Exists(firstFolder));
        Assert.False(Directory.Exists(secondFolder));
        Assert.Equal("primeira", await File.ReadAllTextAsync(Path.Combine(duplicates, "copia_2.txt")));
        Assert.Equal("segunda", await File.ReadAllTextAsync(Path.Combine(duplicates, "copia_3.txt")));
        var entries = await history.ReadAsync();
        Assert.Equal(2, entries.Count(entry => entry.Operation == "Mover duplicado" &&
                                               entry.Status == OperationStatus.Completed));
        Assert.Contains(entries, entry => entry.Operation == "Remover pasta vazia" &&
                                          entry.Source == firstFolder);
        Assert.Contains(entries, entry => entry.Operation == "Remover pasta vazia" &&
                                          entry.Source == secondFolder);
    }
}
