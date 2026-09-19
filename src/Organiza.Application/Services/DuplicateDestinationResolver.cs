using Organiza.Application.Abstractions;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed record DuplicateDestinationDecision(
    string FolderName,
    FolderStructureProfile Profile,
    string RuleCode,
    string Explanation);

public sealed class DuplicateDestinationResolver(IFileSystem fileSystem)
{
    public DuplicateDestinationDecision Resolve(string rootPath)
    {
        var directNames = fileSystem.EnumerateDirectories(rootPath)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToArray();
        var profile = FolderStructureDetector.Detect(rootPath, directNames);
        return profile switch
        {
            FolderStructureProfile.LegalProcess => new(
                "12 DUPLICADOS", profile, "DUP-LEGAL-12",
                "Processo jurídico reconhecido: cópias confirmadas pertencem ao módulo 12 DUPLICADOS."),
            FolderStructureProfile.PersonalDocumentsPerson => new(
                "12 Documentos duplicados", profile, "DUP-PESSOA-12",
                "Pessoa física reconhecida: cópias confirmadas pertencem ao módulo 12 Documentos duplicados."),
            _ => new(StandardFolders.Duplicates, profile, "DUP-GERAL-98",
                "Perfil geral ou sem destino contextual específico: usar 98 DUPLICADOS.")
        };
    }
}
